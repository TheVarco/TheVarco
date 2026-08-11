using UnityEngine;

// 몬스터의 공격 판정용 히트박스
// 평소엔 콜라이더가 꺼져 있고 공격 시작될 때만 켜짐
// 켜져 있는 동안 플레이어에 실제로 닿으면 한 번만 데미지를 줌
[RequireComponent(typeof(Collider))]
public class EnemyAttackHitbox : MonoBehaviour
{
    [SerializeField] private LayerMask targetLayer;

    private int damage;
    private GameObject owner; // 데미지 출처 (상어 본체)
    private bool hasHit; // 1회 데미지 (중복 데미지 방지용)

    private Collider hitboxCollider;

    void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        hitboxCollider.isTrigger = true;
        hitboxCollider.enabled = false; // 평소엔 판정 꺼둠
    }

    // 공격 시작
    public void BeginBite(int damage, GameObject owner)
    {
        this.damage = damage;
        this.owner = owner;
        hasHit = false;
        hitboxCollider.enabled = true;
    }

    // 공격 종료
    public void EndBite()
    {
        hitboxCollider.enabled = false;
    }
    
    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    // 히트박스가 이미 겹쳐있을 때 Event 놓치는 거 방지용
    void OnTriggerStay(Collider other)
    {
        TryHit(other);
    }

    void TryHit(Collider other)
    {
        if (hasHit)
            return;

        // 대상 레이어(플레이어)가 아니면 무시
        if ((targetLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        Damageable target = other.GetComponentInParent<Damageable>();
        if (target == null || target.IsDead)
            return;

        Health targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth != null)
        {
            // 입 히트박스 중심 기준 표면 탐색
            Vector3 sourcePosition = hitboxCollider != null
                ? hitboxCollider.bounds.center
                : transform.position;

            DamageInfo damageInfo = TryFindDamageImpact(
                targetHealth,
                other,
                sourcePosition,
                out Vector3 hitPoint,
                out Vector3 hitNormal)
                ? new DamageInfo(damage, owner, hitPoint, hitNormal, DamageType.Bite)
                : DamageInfo.WithoutImpact(damage, owner, DamageType.Bite);

            targetHealth.ApplyDamage(damageInfo);
        }
        else
        {
            target.TakeDamage(damage, owner);
        }

        hasHit = true;
    }

    // Health 소유 선체에 입 중심 Raycast
    // 검사 실패 시 전용 primitive Trigger를 근사 표면으로 사용
    private static bool TryFindDamageImpact(
        Health targetHealth,
        Collider contactedCollider,
        Vector3 sourcePosition,
        out Vector3 hitPoint,
        out Vector3 hitNormal)
    {
        hitPoint = default;
        hitNormal = default;

        float closestDistance = float.PositiveInfinity;
        bool foundSurface = false;
        Collider[] candidates = targetHealth.GetComponentsInChildren<Collider>(true);

        // 데칼용 실제 선체 우선 검사
        foreach (Collider candidate in candidates)
        {
            if (candidate == null || !candidate.enabled || candidate.isTrigger)
                continue;

            Health owningHealth = candidate.GetComponentInParent<Health>();
            if (owningHealth != targetHealth)
                continue;

            if (!PhysicsSurfaceQuery.TryRaycastTowards(
                    candidate,
                    sourcePosition,
                    candidate.bounds.center,
                    out RaycastHit surfaceHit) ||
                surfaceHit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = surfaceHit.distance;
            hitPoint = surfaceHit.point;
            hitNormal = surfaceHit.normal;
            foundSurface = true;
        }

        if (foundSurface)
            return true;

        bool isSubmarine = targetHealth.GetComponentInParent<SubmarineController>() != null;

        // 플레이어 접촉 캡슐을 유효한 근사 표면으로 사용
        // 잠수함은 전용 대상 영역만 근사 표면으로 사용
        // 무관한 감지 영역의 데칼 좌표 사용 방지
        if (TryGetCapsuleFallback(
                contactedCollider,
                targetHealth,
                isSubmarine,
                sourcePosition,
                out hitPoint,
                out hitNormal))
        {
            return true;
        }

        if (isSubmarine)
        {
            foreach (Collider candidate in candidates)
            {
                if (candidate == contactedCollider ||
                    !TryGetCapsuleFallback(
                    candidate,
                    targetHealth,
                    true,
                    sourcePosition,
                    out hitPoint,
                    out hitNormal))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool TryGetCapsuleFallback(
        Collider candidate,
        Health targetHealth,
        bool requireSubmarineTargetVolume,
        Vector3 sourcePosition,
        out Vector3 hitPoint,
        out Vector3 hitNormal)
    {
        hitPoint = default;
        hitNormal = default;

        return candidate != null &&
               candidate.enabled &&
               candidate.gameObject.activeInHierarchy &&
               candidate is CapsuleCollider &&
               (!requireSubmarineTargetVolume || candidate.name == "Shark Target Volume") &&
               candidate.GetComponentInParent<Health>() == targetHealth &&
               PhysicsSurfaceQuery.TryDirectionalClosestPoint(
                   candidate,
                   sourcePosition,
                   candidate.bounds.center,
                   out hitPoint,
                   out hitNormal);
    }
}
