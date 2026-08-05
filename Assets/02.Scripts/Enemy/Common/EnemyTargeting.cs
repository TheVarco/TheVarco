using UnityEngine;

/// <summary>
/// 움직이는 적 오브젝트의 탐지, 추적, 재타깃 선정.
/// IEnemyTargetFilter 기준 적별 타깃 허용 조건 적용.
/// </summary>
public class EnemyTargeting : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData; // 공통 탐지 설정값.

    [Header("Detection")]
    [SerializeField] private LayerMask targetLayer;   // 탐지 대상 레이어.
    [SerializeField] private LayerMask obstacleLayer; // 시야를 막는 레이어.

    private Transform target;                   // 현재 추적 대상.
    private float damageTargetLockUntil;        // 최초 공격자 우선 종료 시각.
    private float nextRetargetTime;             // 다음 재탐색 시각.
    private IEnemyTargetFilter targetFilter;    // 적별 타깃 허용 조건.

    public Transform Target => target;
    public event System.Action<Transform> OnTargetDetected;

    private void Awake()
    {
        targetFilter = GetComponent<IEnemyTargetFilter>();
    }

    /// <summary>
    /// 관찰자와 가장 가까운 대상 Collider 표면점 반환.
    /// </summary>
    /// <param name="observerPosition">대상 표면점을 계산할 적 또는 공격 히트박스의 위치.</param>
    public Vector3 GetTargetPoint(Vector3 observerPosition)
    {
        if (target == null)
            return observerPosition;

        // 대상 Collider 기준.
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider == null)
            targetCollider = target.GetComponentInChildren<Collider>();

        // Collider가 없으면 Transform 위치 기준.
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
    /// 피해를 준 공격자를 우선 타깃으로 설정.
    /// </summary>
    /// <param name="source">적에게 피해를 준 공격 주체.</param>
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

        if (!PassesTargetFilter(attacker))
            return false;

        // 최초 공격자 잠금 시간 기준.
        if (Time.time >= damageTargetLockUntil)
        {
            SetTarget(attacker);
            damageTargetLockUntil = Time.time + DamageTargetLockDuration;
            nextRetargetTime = damageTargetLockUntil;
        }

        return true;
    }

    /// <summary>
    /// 탐지 범위 내 최근접 추적 가능 타깃 탐색.
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

            if (!PassesTargetFilter(candidate))
                continue;

            // 사망 대상 후보 제외.
            Damageable candidateDamageable = candidate.GetComponentInParent<Damageable>();
            if (candidateDamageable != null && candidateDamageable.IsDead)
                continue;

            // Collider 표면 거리 기준 탐지 범위 및 시야각 계산.
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
    /// 추격 타깃 유지 또는 갱신.
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
            // Collider 표면 거리 기준 후보 비교.
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
    /// 추적 타깃 지정.
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
    /// 현재 추적 타깃 해제.
    /// </summary>
    public void ClearTarget()
    {
        target = null;
    }

    /// <summary>
    /// 추격 범위 내 최근접 유효 타깃 탐색.
    /// </summary>
    /// <param name="nearestTarget">조건을 만족하는 가장 가까운 타깃.</param>
    /// <param name="nearestDistance">적과 가장 가까운 타깃 사이의 거리.</param>
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
            // 탐지된 자식 Collider 기준 타깃 저장.
            Transform candidate = candidateCollider.transform;

            if (!PassesTargetFilter(candidate))
                continue;

            Damageable candidateDamageable = candidate.GetComponentInParent<Damageable>();
            if (candidateDamageable != null && candidateDamageable.IsDead)
                continue;

            // Collider 표면 거리 기준 재타깃 후보 비교.
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
    /// 지정 타깃의 추적 유지 조건 확인.
    /// </summary>
    /// <param name="trackedTarget">현재 추적 중인 타깃.</param>
    private bool CanContinueTracking(Transform trackedTarget)
    {
        if (!PassesTargetFilter(trackedTarget))
            return false;

        Damageable trackedDamageable = trackedTarget.GetComponentInParent<Damageable>();
        if (trackedDamageable != null && trackedDamageable.IsDead)
            return false;

        // 최근접 Collider 표면 기준 추격 유지 판정.
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
    /// 적별 필터 기준 타깃 후보 검사.
    /// </summary>
    private bool PassesTargetFilter(Transform candidate)
    {
        if (targetFilter == null)
            targetFilter = GetComponent<IEnemyTargetFilter>();

        return candidate != null && (targetFilter == null || targetFilter.CanTarget(candidate));
    }

    /// <summary>
    /// 적과 타깃 사이 장애물 Raycast 확인.
    /// </summary>
    /// <param name="directionToTarget">적에서 타깃을 향하는 정규화된 방향.</param>
    /// <param name="distanceToTarget">적과 타깃 사이의 거리.</param>
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
