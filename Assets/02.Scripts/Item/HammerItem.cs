using UnityEngine;

// 좌클릭으로 정면 부채꼴 범위 안의 RepairableStructure를 때려서 수리하는 망치.
// MeleeAttack이랑 판정 방식(범위+각도)은 같고, 데미지 대신 회복을 준다는 점만 다름.
public class HammerItem : CarryableItem
{
    [Header("수리 설정 (밸런싱용)")]
    public float repairAmount = 10f;
    public float repairRange = 1.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 수리 (예: 90이면 좌우 45도씩)")]
    public float repairAngle = 90f;
    public float repairCooldown = 0.5f;

    private float cooldownTimer = 0f;

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    // 좌클릭 = 타격. 쿨타임 중이면 아무 일도 안 하고, 망치는 소모품이 아니라 사라지지도 않음
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        if (cooldownTimer > 0f) return false;

        PerformRepair();
        cooldownTimer = repairCooldown;
        return false;
    }

    // 카메라(aimReference)가 아니라 망치 자신의 위치/방향을 기준으로 판정함.
    // 망치는 들고 있는 동안 handSocket(Player 자식)에 붙어서 몸 방향을 그대로 따라가니까,
    // 자기 transform을 쓰는 게 오히려 더 정확하고, Gizmo도 이 자리에서 그대로 그릴 수 있음
    private void PerformRepair()
    {
        Collider[] candidates = Physics.OverlapSphere(transform.position, repairRange);
        Debug.Log($"[HammerItem] 범위 안 오브젝트 {candidates.Length}개 발견");

        bool repairedAny = false;

        foreach (Collider col in candidates)
        {
            Vector3 toTarget = col.transform.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > repairAngle * 0.5f) continue;

            RepairableStructure structure = col.GetComponentInParent<RepairableStructure>();
            if (structure != null)
            {
                structure.Repair(repairAmount);
                repairedAny = true;
                Debug.Log($"[HammerItem] {col.gameObject.name} 수리함 (+{repairAmount})");
            }
        }

        if (!repairedAny)
            Debug.Log("[HammerItem] 각도/범위 안에 RepairableStructure가 없음");
    }

    // 디버그용: 씬 뷰에서 수리 범위(부채꼴)를 눈으로 확인하기 위함.
    // 손에 들려있는 동안(Play 모드에서 이 오브젝트를 선택하면) 실제 판정 위치에 그대로 그려짐
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, repairRange);

        Quaternion leftEdge = Quaternion.AngleAxis(-repairAngle * 0.5f, Vector3.up);
        Quaternion rightEdge = Quaternion.AngleAxis(repairAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(transform.position, transform.position + leftEdge * transform.forward * repairRange);
        Gizmos.DrawLine(transform.position, transform.position + rightEdge * transform.forward * repairRange);
    }
}