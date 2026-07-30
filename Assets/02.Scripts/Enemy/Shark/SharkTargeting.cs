using UnityEngine;

public class SharkTargeting : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData;

    [Header("Detection")]
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private LayerMask obstacleLayer;

    private Transform target;
    private float damageTargetLockUntil;
    private float nextRetargetTime;

    public Transform Target => target;
    public event System.Action<Transform> OnTargetDetected;

    /// <summary>
    /// 잠수함처럼 큰 대상의 중심점 대신 관찰자와 가장 가까운 Collider 표면점을 반환한다.
    /// 추격 및 공격 상태가 이 좌표를 사용하면 상어가 선체 중심으로 파고들지 않고 외벽을 기준으로 움직인다.
    /// </summary>
    /// <param name="observerPosition">대상 표면점을 계산할 상어 또는 공격 히트박스의 위치.</param>
    public Vector3 GetTargetPoint(Vector3 observerPosition)
    {
        if (target == null)
            return observerPosition;

        // 탐지용 자식 Collider를 우선 사용하고, 없을 때만 자식 계층에서 대체 Collider를 찾는다.
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider == null)
            targetCollider = target.GetComponentInChildren<Collider>();

        // Collider를 사용할 수 없다면 일반 캐릭터도 계속 추적할 수 있도록 Transform 위치로 대체한다.
        return targetCollider != null && targetCollider.enabled
            ? targetCollider.ClosestPoint(observerPosition)
            : target.position;
    }

    private float ProximityDetectRadius => enemyData.ProximityDetectRadius;
    private float ForwardDetectRadius => enemyData.ForwardDetectRadius;
    private float LoseTargetRadius => enemyData.loseTargetRadius;
    private float DamageTargetLockDuration => enemyData.damageTargetLockDuration;
    private float RetargetInterval => enemyData.retargetInterval;
    private float TargetSwitchDistanceMargin => enemyData.targetSwitchDistanceMargin;
    private float ViewAngle => enemyData.viewAngle;

    /// <summary>
    /// 공격 대상을 상어의 타깃으로 지정할 수 있는지 확인하고 최초 공격자 우선 시간을 설정한다.
    /// </summary>
    /// <param name="source">상어에게 피해를 준 공격 주체.</param>
    public bool TrySetDamageTarget(GameObject source)
    {
        if (source == null)
            return false;

        Transform attacker = source.transform.root;

        if (attacker == transform.root)
            return false;

        int attackerLayerMask = 1 << attacker.gameObject.layer;
        if ((targetLayer.value & attackerLayerMask) == 0)
            return false;

        // 잠금 중에는 후속 공격자가 최초 공격자를 덮어쓰지 않는다.
        if (Time.time >= damageTargetLockUntil)
        {
            SetTarget(attacker);
            damageTargetLockUntil = Time.time + DamageTargetLockDuration;
            nextRetargetTime = damageTargetLockUntil;
        }

        return true;
    }

    /// <summary>
    /// 가까운 대상은 방향과 관계없이 / 먼 대상은 전방 시야각 안에서 탐색
    /// 이미 발견한 대상은 추적 가능 조건을 만족하는 동안 계속 유지한다.
    /// </summary>
    public bool TryFindTarget()
    {
        if (target != null)
        {
            if (CanContinueTracking(target))
                return true;

            ClearTarget();
        }

        float searchRadius = Mathf.Max(ForwardDetectRadius, ProximityDetectRadius);
        Collider[] targetsInRadius = Physics.OverlapSphere(transform.position, searchRadius, targetLayer);

        Transform nearestTarget = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider targetCollider in targetsInRadius)
        {
            Transform candidate = targetCollider.transform;

            // 죽은 대상은 후보에서 제외 (추격용 탐색과 동일하게 처리해 Patrol⇄Chase 무한 전환 방지)
            Damageable candidateDamageable = candidate.GetComponentInParent<Damageable>();
            if (candidateDamageable != null && candidateDamageable.IsDead)
                continue;

            // 대상 중심이 아니라 실제 Collider 표면까지의 거리로 탐지 범위와 시야각을 계산한다.
            // 큰 잠수함은 중심이 멀어도 외벽이 탐지 범위 안에 들어올 수 있기 때문이다.
            Vector3 targetPoint = targetCollider.ClosestPoint(transform.position);
            Vector3 offsetToTarget = targetPoint - transform.position;
            float distanceToTarget = offsetToTarget.magnitude;

            if (distanceToTarget <= Mathf.Epsilon)
                continue;

            Vector3 directionToTarget = offsetToTarget / distanceToTarget;
            bool isWithinProximity = distanceToTarget <= ProximityDetectRadius;
            bool isWithinForward =
                distanceToTarget <= ForwardDetectRadius &&
                Vector3.Angle(transform.forward, directionToTarget) <= ViewAngle * 0.5f;

            if (!isWithinProximity && !isWithinForward)
                continue;

            if (IsTargetBlocked(directionToTarget, distanceToTarget))
                continue;

            if (distanceToTarget < nearestDistance)
            {
                nearestDistance = distanceToTarget;
                nearestTarget = candidate;
            }
        }

        SetTarget(nearestTarget);

        return target != null;
    }

    /// <summary>
    /// 추격 중인 타깃 갱신
    /// 최초 공격자 잠금 시간에는 현재 타깃을 우선하고
    /// 잠금 이후에는 일정 주기로 가장 가까운 타깃을 찾는다.
    /// </summary>
    public bool TryUpdateChaseTarget()
    {
        if (Time.time < damageTargetLockUntil)
        {
            if (target != null && CanContinueTracking(target))
                return true;

            damageTargetLockUntil = 0f;
            nextRetargetTime = 0f;
            ClearTarget();
        }

        bool currentTargetIsValid = target != null && CanContinueTracking(target);

        if (currentTargetIsValid && Time.time < nextRetargetTime)
            return true;

        nextRetargetTime = Time.time + RetargetInterval;

        if (!TryFindNearestTrackableTarget(out Transform nearestTarget, out float nearestDistance))
        {
            if (currentTargetIsValid)
                return true;

            ClearTarget();
            return false;
        }

        if (currentTargetIsValid && nearestTarget != target)
        {
            // 현재 대상도 동일하게 표면 거리로 비교해야 새 후보와의 거리 기준이 일관된다.
            float currentDistance = Vector3.Distance(
                transform.position,
                GetTargetPoint(transform.position));

            if (nearestDistance + TargetSwitchDistanceMargin >= currentDistance)
                return true;
        }

        SetTarget(nearestTarget);
        return true;
    }

    /// <summary>
    /// 추적 타깃 지정
    /// </summary>
    /// <param name="newTarget">새로 지정할 타깃.</param>
    private void SetTarget(Transform newTarget)
    {
        bool discoveredNewTarget = target == null && newTarget != null;
        target = newTarget;

        if (discoveredNewTarget)
            OnTargetDetected?.Invoke(newTarget);
    }

    /// <summary>
    /// 현재 추적 중인 타깃 해제
    /// </summary>
    private void ClearTarget()
    {
        target = null;
    }

    /// <summary>
    /// 추격 해제 거리 안에서 살아 있고 장애물에 가려지지 않은 가장 가까운 타깃을 찾는다.
    /// </summary>
    /// <param name="nearestTarget">조건을 만족하는 가장 가까운 타깃.</param>
    /// <param name="nearestDistance">상어와 가장 가까운 타깃 사이의 거리.</param>
    private bool TryFindNearestTrackableTarget(out Transform nearestTarget, out float nearestDistance)
    {
        nearestTarget = null;
        nearestDistance = float.MaxValue;

        Collider[] candidates = Physics.OverlapSphere(
            transform.position,
            LoseTargetRadius,
            targetLayer
        );

        foreach (Collider candidateCollider in candidates)
        {
            // attachedRigidbody의 루트 Transform을 사용하면 잠수함 중심을 타깃으로 저장하게 된다.
            // 탐지된 자식 Collider 자체를 보존해야 이후 ClosestPoint로 외벽 좌표를 계속 구할 수 있다.
            Transform candidate = candidateCollider.transform;

            Damageable candidateDamageable = candidate.GetComponentInParent<Damageable>();
            if (candidateDamageable != null && candidateDamageable.IsDead)
                continue;

            // 재타깃 후보 역시 중심점이 아닌 Collider 표면까지의 실제 거리를 사용한다.
            Vector3 targetPoint = candidateCollider.ClosestPoint(transform.position);
            Vector3 offsetToCandidate = targetPoint - transform.position;
            float distanceToCandidate = offsetToCandidate.magnitude;

            if (distanceToCandidate <= Mathf.Epsilon)
                continue;

            Vector3 directionToCandidate = offsetToCandidate / distanceToCandidate;
            if (IsTargetBlocked(directionToCandidate, distanceToCandidate))
                continue;

            if (distanceToCandidate >= nearestDistance)
                continue;

            nearestTarget = candidate;
            nearestDistance = distanceToCandidate;
        }

        return nearestTarget != null;
    }

    /// <summary>
    /// 지정한 타깃이 살아 있고 추격 해제 거리 안에 있으며 장애물에 가려지지 않았는지 확인한다.
    /// </summary>
    /// <param name="trackedTarget">현재 추적 중인 타깃.</param>
    private bool CanContinueTracking(Transform trackedTarget)
    {
        Damageable trackedDamageable = trackedTarget.GetComponentInParent<Damageable>();
        if (trackedDamageable != null && trackedDamageable.IsDead)
            return false;

        // 추격 유지 여부도 잠수함 중심이 아닌 가장 가까운 선체 표면을 기준으로 판단한다.
        Vector3 targetPoint = GetTargetPoint(transform.position);
        Vector3 offsetToTarget = targetPoint - transform.position;
        float distanceToTarget = offsetToTarget.magnitude;

        if (distanceToTarget > LoseTargetRadius)
            return false;

        if (distanceToTarget <= Mathf.Epsilon)
            return true;

        Vector3 directionToTarget = offsetToTarget / distanceToTarget;
        return !IsTargetBlocked(directionToTarget, distanceToTarget);
    }

    /// <summary>
    /// 상어와 타깃 사이에 장애물 레이어가 존재하는지 Raycast로 확인한다.
    /// </summary>
    /// <param name="directionToTarget">상어에서 타깃을 향하는 정규화된 방향.</param>
    /// <param name="distanceToTarget">상어와 타깃 사이의 거리.</param>
    private bool IsTargetBlocked(Vector3 directionToTarget, float distanceToTarget)
    {
        return Physics.Raycast(
            transform.position,
            directionToTarget,
            distanceToTarget,
            obstacleLayer
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (enemyData == null)
            return;

        float radius = ForwardDetectRadius;
        float halfAngle = ViewAngle * 0.5f;

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, radius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, ProximityDetectRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, LoseTargetRadius);

        Vector3 leftBoundary = Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0f, halfAngle, 0f) * transform.forward;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + leftBoundary * radius);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary * radius);

        const int segments = 20;
        Vector3 previousPoint = transform.position + leftBoundary * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -halfAngle + (ViewAngle * i / segments);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Vector3 currentPoint = transform.position + direction * radius;

            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }
}
