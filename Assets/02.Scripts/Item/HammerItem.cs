using UnityEngine;

// 해머 수리 입력 처리
// 조준 지점에서 가장 가까운 손상 슬롯 검색
// 네트워크 세션에서는 호스트 수리 요청을 주기적으로 갱신
// 로컬 세션에서는 프레임 시간으로 수리 진행
// 수리 대상과 애니메이션과 진행 UI 수명 관리

// 조준한 손상 부위를 일정 시간 동안 수리하는 망치.
// 실제 HP 회복과 부위 손상 감소는 RepairableStructure가 책임진다.
public class HammerItem : CarryableItem
{
    // 수리량과 탐색 거리와 대상 레이어 설정
    [Header("수리 설정")]
    [SerializeField, Min(0.01f)] private float repairAmount = 10f;
    [SerializeField, Min(0.01f)] private float repairRange = 1.5f;
    [SerializeField] private LayerMask repairLayerMask = ~0;

    [Header("수리 진행 UI")]
    [SerializeField] private RepairProgressWorldUI progressUI;

    private static readonly int IsFixingHash = Animator.StringToHash("IsFixing");

    // 현재 수리 대상과 사용자 표시 상태
    private RepairableStructure currentStructure;
    private int currentSlotIndex = -1;
    private Animator userAnimator;
    private GameObject currentUser;
    private bool ownsRuntimeProgressUI;
    private float nextNetworkRepairRefresh;
    private Vector3 currentRepairWorldPoint;
    private Vector3 currentRepairWorldNormal = Vector3.up;

    // 좌클릭 클릭 시 해머 근접 공격 모션 실행
    // 근접 공격 애니메이션만 재생하고 해머 유지
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        // 사용자 애니메이터를 찾아 근접 공격 트리거 실행
        if (user != null)
        {
            Animator anim = user.GetComponentInChildren<Animator>();
            if (anim == null) anim = user.GetComponentInParent<Animator>();
            if (anim == null) anim = user.GetComponent<Animator>();

            if (anim != null)
            {
                anim.SetTrigger("Melee");
                Debug.Log($"[HammerItem] SetTrigger('Melee') 실행됨! (대상: {anim.gameObject.name})");
            }
            else
            {
                Debug.LogWarning("[HammerItem] user에서 Animator를 찾지 못함");
            }
        }
        return false;
    }

    // 지속 입력을 사용하지 않는 기본 공격 경로
    public override void OnPrimaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        // 해머 기본 공격은 누름 이벤트에서 끝나므로 추가 처리 생략
    }

    // 우클릭 홀드 시 수리 모션(IsFixing) 실행 및 구동
    // 수리 입력 해제와 대상 변경과 권위 경로 처리
    public override void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        // 사용자와 입력 상태를 확인한 뒤 수리 시작과 종료 경로 분리
        CacheUserAnimator(user);

        if (!isHeld)
        {
            if (currentStructure != null && currentSlotIndex >= 0)
            {
                currentStructure.RequestNetworkRepair(user, currentSlotIndex, false);
                currentStructure.StopRepair(currentSlotIndex);
            }
            SetFixingAnimation(false);
            if (progressUI != null) progressUI.Hide();
            return;
        }

        // 우클릭 유지 시 수리 모션(IsFixing = true) 발동
        SetFixingAnimation(true);

        if (!TryFindRepairTarget(
                aimReference,
                out RepairableStructure structure,
                out int slotIndex,
                out Vector3 repairWorldPoint,
                out Vector3 repairWorldNormal))
        {
            ClearCurrentTarget();
            // 수리 구조물이 근처에 없더라도 우클릭 동안 모션 유지
            SetFixingAnimation(true);
            return;
        }

        SwitchTargetIfNeeded(structure, slotIndex);
        currentRepairWorldPoint = repairWorldPoint;
        currentRepairWorldNormal = repairWorldNormal;

        if (currentStructure.UsesNetworkAuthority)
        {
            if (Time.unscaledTime >= nextNetworkRepairRefresh)
            {
                currentStructure.RequestNetworkRepair(user, currentSlotIndex, true);
                nextNetworkRepairRefresh = Time.unscaledTime + 0.5f;
            }
            ShowProgress(aimReference);
            return;
        }

        float repairedAmount = currentStructure.AdvanceRepair(
            currentSlotIndex,
            Time.deltaTime,
            repairAmount,
            out bool completedCycle);

        if (completedCycle && repairedAmount <= 0f)
        {
            ClearCurrentTarget();
            SetFixingAnimation(true);
            return;
        }

        if (!currentStructure.CanRepairSlot(currentSlotIndex))
        {
            ClearCurrentTarget();
            SetFixingAnimation(true);
            return;
        }

        ShowProgress(aimReference);
    }

    // 조준 광선으로 가장 가까운 수리 슬롯 검색
    private bool TryFindRepairTarget(
        Transform aimReference,
        out RepairableStructure structure,
        out int slotIndex,
        out Vector3 repairWorldPoint,
        out Vector3 repairWorldNormal)
    {
        // 조준 기준에서 광선을 쏴 RepairableStructure와 슬롯 번호 탐색
        structure = null;
        slotIndex = -1;
        repairWorldPoint = Vector3.zero;
        repairWorldNormal = Vector3.up;

        if (aimReference == null)
            return false;

        if (!Physics.Raycast(
                aimReference.position,
                aimReference.forward,
                out RaycastHit hit,
                repairRange,
                repairLayerMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        structure = hit.collider.GetComponentInParent<RepairableStructure>();
        if (structure == null)
            return false;

        repairWorldPoint = hit.point;
        repairWorldNormal = hit.normal;

        // 손상 슬롯을 먼저 거르지 않는다. 바라본 위치의 가장 가까운 슬롯이 정상이라면 수리 불가다.
        if (!structure.TryFindClosestSlot(hit.point, false, out slotIndex))
            return false;

        return structure.CanRepairSlot(slotIndex);
    }

    // 이전 수리 점유를 해제하고 새 슬롯으로 전환
    private void SwitchTargetIfNeeded(RepairableStructure structure, int slotIndex)
    {
        // 대상이 바뀐 경우 이전 네트워크 점유와 로컬 진행 상태 정리
        if (currentStructure == structure && currentSlotIndex == slotIndex)
            return;

        if (currentStructure != null && currentSlotIndex >= 0)
        {
            currentStructure.RequestNetworkRepair(currentUser, currentSlotIndex, false);
            currentStructure.StopRepair(currentSlotIndex);
        }

        SetFixingAnimation(false);
        currentStructure = structure;
        currentSlotIndex = slotIndex;
        nextNetworkRepairRefresh = 0f;
    }

    // 월드 공간 수리 진행 UI 표시
    private void ShowProgress(Transform viewer)
    {
        // 손상 슬롯에서 월드 위치와 진행률을 받아 UI에 전달
        if (currentStructure == null || currentSlotIndex < 0)
            return;

        if (!currentStructure.TryGetRepairUIData(
                currentSlotIndex,
                out _,
                out _,
                out float progress01))
        {
            return;
        }

        // 씬/프리팹에서 UI가 연결되지 않은 경우에만 런타임 UI를 늦게 생성한다.
        // 망치를 장착했다는 이유만으로 만들지 않고, 실제 수리 가능한 부위를 찾았을 때 생성한다.
        if (progressUI == null)
        {
            progressUI = RepairProgressWorldUI.CreateRuntime();
            ownsRuntimeProgressUI = progressUI != null;
        }

        if (progressUI == null)
            return;

        progressUI.Show(
            currentRepairWorldPoint,
            currentRepairWorldNormal,
            progress01,
            viewer);
    }

    // 현재 사용자 애니메이터 참조 갱신
    private void CacheUserAnimator(GameObject user)
    {
        // 사용자가 바뀔 때만 기존 애니메이션을 끄고 새 참조 검색
        if (user == currentUser)
            return;

        SetFixingAnimation(false);
        currentUser = user;
        userAnimator = null;

        if (currentUser == null)
            return;

        userAnimator = currentUser.GetComponent<Animator>();
        if (userAnimator == null)
            userAnimator = currentUser.GetComponentInChildren<Animator>();
    }

    // 수리 애니메이션 상태 적용
    private void SetFixingAnimation(bool isFixing)
    {
        // 캐시된 애니메이터에 수리 상태 불 값 적용
        if (userAnimator != null)
            userAnimator.SetBool(IsFixingHash, isFixing);
    }

    // 현재 수리 요청과 UI 상태 정리
    private void ClearCurrentTarget()
    {
        // 수리 점유 해제 후 대상 번호와 애니메이션과 UI 초기화
        if (currentStructure != null && currentSlotIndex >= 0)
        {
            currentStructure.RequestNetworkRepair(currentUser, currentSlotIndex, false);
            currentStructure.StopRepair(currentSlotIndex);
        }

        currentStructure = null;
        currentSlotIndex = -1;
        SetFixingAnimation(false);

        if (progressUI != null)
            progressUI.Hide();
    }

    // 아이템 비활성화 시 수리 점유 해제
    private void OnDisable()
    {
        // 비활성화 중 수리 상태가 남지 않게 현재 대상 정리
        ClearCurrentTarget();
    }

    // 런타임 생성 UI만 제거
    private void OnDestroy()
    {
        // 이 해머가 직접 만든 진행 UI만 함께 파괴
        if (ownsRuntimeProgressUI && progressUI != null)
            Destroy(progressUI.gameObject);
    }

    // 에디터에서 수리 거리 표시
    private void OnDrawGizmosSelected()
    {
        // Scene 화면에 해머 수리 가능 거리 선 표시
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * repairRange);
    }
}
