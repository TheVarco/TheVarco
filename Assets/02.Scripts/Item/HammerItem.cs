using UnityEngine;

// 조준한 손상 부위를 일정 시간 동안 수리하는 망치.
// 실제 HP 회복과 부위 손상 감소는 RepairableStructure가 책임진다.
public class HammerItem : CarryableItem
{
    [Header("수리 설정")]
    [SerializeField, Min(0.01f)] private float repairAmount = 10f;
    [SerializeField, Min(0.01f)] private float repairRange = 1.5f;
    [SerializeField] private LayerMask repairLayerMask = ~0;

    [Header("수리 진행 UI")]
    [SerializeField] private RepairProgressWorldUI progressUI;

    private static readonly int IsFixingHash = Animator.StringToHash("IsFixing");

    private RepairableStructure currentStructure;
    private int currentSlotIndex = -1;
    private Animator userAnimator;
    private GameObject currentUser;
    private bool ownsRuntimeProgressUI;

    // 좌클릭 클릭 시 해머 근접 공격 모션 실행
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
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

    public override void OnPrimaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
    }

    // 우클릭 홀드 시 수리 모션(IsFixing) 실행 및 구동
    public override void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        CacheUserAnimator(user);

        if (!isHeld)
        {
            if (currentStructure != null && currentSlotIndex >= 0)
            {
                currentStructure.StopRepair(currentSlotIndex);
            }
            SetFixingAnimation(false);
            if (progressUI != null) progressUI.Hide();
            return;
        }

        // 우클릭 유지 시 수리 모션(IsFixing = true) 발동
        SetFixingAnimation(true);

        if (!TryFindRepairTarget(aimReference, out RepairableStructure structure, out int slotIndex))
        {
            ClearCurrentTarget();
            // 수리 구조물이 근처에 없더라도 우클릭 동안 모션 유지
            SetFixingAnimation(true);
            return;
        }

        SwitchTargetIfNeeded(structure, slotIndex);

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

    private bool TryFindRepairTarget(
        Transform aimReference,
        out RepairableStructure structure,
        out int slotIndex)
    {
        structure = null;
        slotIndex = -1;

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

        // 손상 슬롯을 먼저 거르지 않는다. 바라본 위치의 가장 가까운 슬롯이 정상이라면 수리 불가다.
        if (!structure.TryFindClosestSlot(hit.point, false, out slotIndex))
            return false;

        return structure.CanRepairSlot(slotIndex);
    }

    private void SwitchTargetIfNeeded(RepairableStructure structure, int slotIndex)
    {
        if (currentStructure == structure && currentSlotIndex == slotIndex)
            return;

        if (currentStructure != null && currentSlotIndex >= 0)
            currentStructure.StopRepair(currentSlotIndex);

        SetFixingAnimation(false);
        currentStructure = structure;
        currentSlotIndex = slotIndex;
    }

    private void ShowProgress(Transform viewer)
    {
        if (currentStructure == null || currentSlotIndex < 0)
            return;

        if (!currentStructure.TryGetRepairUIData(
                currentSlotIndex,
                out Vector3 worldPosition,
                out Vector3 worldNormal,
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
            worldPosition,
            worldNormal,
            progress01,
            viewer);
    }

    private void CacheUserAnimator(GameObject user)
    {
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

    private void SetFixingAnimation(bool isFixing)
    {
        if (userAnimator != null)
            userAnimator.SetBool(IsFixingHash, isFixing);
    }

    private void ClearCurrentTarget()
    {
        if (currentStructure != null && currentSlotIndex >= 0)
            currentStructure.StopRepair(currentSlotIndex);

        currentStructure = null;
        currentSlotIndex = -1;
        SetFixingAnimation(false);

        if (progressUI != null)
            progressUI.Hide();
    }

    private void OnDisable()
    {
        ClearCurrentTarget();
    }

    private void OnDestroy()
    {
        if (ownsRuntimeProgressUI && progressUI != null)
            Destroy(progressUI.gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * repairRange);
    }
}
