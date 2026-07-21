using UnityEngine;

// 플레이어의 근접 공격. 앞쪽 일정 범위 안의 Damageable을 모두 때린다.
public class MeleeAttack : MonoBehaviour
{
    [Header("공격 수치 (밸런싱용 - 여기서 직접 조절)")]
    [Tooltip("한 번 때릴 때 주는 데미지")]
    public float damage = 10f;
    [Tooltip("공격이 닿는 범위(반지름)")]
    public float attackRange = 1.5f;
    [Tooltip("공격 후 다시 공격 가능해지기까지 걸리는 시간(초)")]
    public float attackCooldown = 0.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 타격 (예: 60이면 좌우 30도씩, 총 60도 부채꼴)")]
    public float attackAngle = 60f;

    [Header("참조")]
    [Tooltip("공격 판정의 중심이 될 위치 (보통 Player 앞쪽에 빈 오브젝트를 만들어 연결)")]
    public Transform attackPoint;
    [Tooltip("공격 대상이 되는 레이어 (몬스터 전용 레이어를 만들어 지정 권장)")]
    public LayerMask targetLayer = ~0;

    public KeyCode attackKey = KeyCode.Mouse1;

    [Tooltip("맨손(슬롯 1)일 때만 근접 공격 가능하게 하려면 연결. 안 하면 항상 가능")]
    public PlayerHotbar hotbar;

    private float cooldownTimer = 0f;

    void Update()
    {
        // 무기를 들고 있으면 우클릭은 조준 용도로 넘어가므로, 여기서는 무시함
        bool bareHanded = hotbar == null || hotbar.ActiveSlot == 1;
        if (!bareHanded) return;

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(attackKey) && cooldownTimer <= 0f)
        {
            PerformAttack();
            cooldownTimer = attackCooldown;
        }
    }

    private void PerformAttack()
    {
        if (attackPoint == null)
        {
            Debug.LogWarning("MeleeAttack: attackPoint가 연결되어 있지 않음");
            return;
        }

        Collider[] hits = Physics.OverlapSphere(attackPoint.position, attackRange, targetLayer);

        foreach (Collider hit in hits)
        {
            // 자기 자신은 때리지 않도록 제외 (자식 오브젝트에 Collider가 있어도 안전하게 걸러지도록 root로 비교)
            if (hit.transform.root == transform.root) continue;

            // 정면 기준 각도 체크: 부채꼴 범위 밖(등 뒤 등)에 있으면 무시
            Vector3 directionToTarget = (hit.transform.position - attackPoint.position).normalized;
            float angle = Vector3.Angle(attackPoint.forward, directionToTarget);
            if (angle > attackAngle * 0.5f) continue;

            Damageable target = hit.GetComponentInParent<Damageable>();
            if (target != null && !target.IsDead)
            {
                target.TakeDamage(damage, gameObject);
            }
        }
    }

    // 씬 뷰에서 공격 범위를 눈으로 확인하기 위한 디버그용
    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);

        // 부채꼴의 좌우 경계선을 시각적으로 표시 (씬 뷰에서 각도 확인용)
        Gizmos.color = Color.yellow;
        Quaternion leftEdge = Quaternion.AngleAxis(-attackAngle * 0.5f, Vector3.up);
        Quaternion rightEdge = Quaternion.AngleAxis(attackAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(attackPoint.position, attackPoint.position + leftEdge * attackPoint.forward * attackRange);
        Gizmos.DrawLine(attackPoint.position, attackPoint.position + rightEdge * attackPoint.forward * attackRange);
    }
}