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

    // 수리는 클릭 순간이 아니라 OnPrimaryHeld에서 진행한다.
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        return false;
    }

    public override void OnPrimaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        CacheUserAnimator(user);

        if (!TryFindRepairTarget(aimReference, out RepairableStructure structure, out int slotIndex))
        {
            ClearCurrentTarget();
            return;
        }

        SwitchTargetIfNeeded(structure, slotIndex);

        if (!isHeld)
        {
            currentStructure.StopRepair(currentSlotIndex);
            SetFixingAnimation(false);
            ShowProgress(aimReference);
            return;
        }

        SetFixingAnimation(true);
        float repairedAmount = currentStructure.AdvanceRepair(
            currentSlotIndex,
            Time.deltaTime,
            repairAmount,
            out bool completedCycle);

        if (completedCycle && repairedAmount <= 0f)
        {
            ClearCurrentTarget();
            return;
        }

        if (!currentStructure.CanRepairSlot(currentSlotIndex))
        {
            ClearCurrentTarget();
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
