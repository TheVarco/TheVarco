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
        // Debug.Log($"BeginBite at {Time.time}");
            
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

        // target.TakeDamage(damage, owner);
        Health targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth != null)
        {
            Vector3 sourcePosition = owner != null
                ? owner.transform.position
                : transform.position;
            Collider damageSurface = FindClosestDamageSurface(targetHealth, sourcePosition);
            Collider pointSource = damageSurface != null ? damageSurface : other;
            Vector3 hitPoint = pointSource.ClosestPoint(sourcePosition);
            Vector3 hitNormal = sourcePosition - hitPoint;

            if (hitNormal.sqrMagnitude <= 0.0001f)
                hitNormal = sourcePosition - pointSource.bounds.center;

            targetHealth.ApplyDamage(new DamageInfo(damage, owner, hitPoint, hitNormal, DamageType.Bite));
        }
        else
        {
            target.TakeDamage(damage, owner);
        }

        hasHit = true;
    }

    // 상어는 먼저 Shark Target Volume(Trigger)과 충돌 > 안 그러면 잠수함에 붙은 메쉬 콜라이더가 너무 많아서 계속 공격 & 데칼 안입혀짐..
    // Health를 사용하는 실제 MeshCollider들 중 상어와 가장 가까운 표면을 찾아 피격 위치로 사용
    private static Collider FindClosestDamageSurface(Health targetHealth, Vector3 sourcePosition)
    {
        Collider closestCollider = null;
        float closestSqrDistance = float.PositiveInfinity;
        Collider[] candidates = targetHealth.GetComponentsInChildren<Collider>(true);

        // 실제 데칼이 붙을 수 있는 MeshCollider만 검사
        foreach (Collider candidate in candidates)
        {
            if (candidate == null || !candidate.enabled || candidate.isTrigger)
                continue;

            Health owningHealth = candidate.GetComponentInParent<Health>();
            if (owningHealth != targetHealth)
                continue;

            // 선택된 실제 선체 Collider의 표면 좌표를 구한다.
            // 이 위치가 DamageInfo의 피격 위치가 되며
            // RepairableStructure는 이 좌표를 기준으로 가장 가까운 손상 슬롯을 선택한다.
            Vector3 surfacePoint = candidate.ClosestPoint(sourcePosition);
            float sqrDistance = (surfacePoint - sourcePosition).sqrMagnitude;

            if (sqrDistance >= closestSqrDistance)
                continue;

            closestSqrDistance = sqrDistance;
            closestCollider = candidate;
        }

        return closestCollider;
    }
}
