using UnityEngine;

/// <summary>
/// 움직이는 적 오브젝트의 Rigidbody 이동, 회전, 장애물 탐색.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyNavigator : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData; // 공통 이동 설정값.

    [Header("Obstacle")]
    [SerializeField] private LayerMask obstacleLayer; // 이동을 막는 레이어.

    private Rigidbody body; // 이동 대상 Rigidbody.

    private void Awake()
    {
        body = GetComponent<Rigidbody>();

        // 동적 적의 연속 충돌 검사 기준.
        if (!body.isKinematic)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>
    /// 지정 방향 및 속도 기준 Rigidbody 이동.
    /// </summary>
    public void MoveToDirection(Vector3 direction, float speed)
    {
        if (direction.sqrMagnitude <= 0.001f || speed <= 0f)
        {
            StopMovement();
            return;
        }

        body.linearVelocity = direction.normalized * speed;
    }

    /// <summary>
    /// Rigidbody가 현재 바라보는 방향으로 이동.
    /// 회전 중인 적이 목표 방향으로 옆미끄러지는 것을 방지한다.
    /// </summary>
    public void MoveForward(float speed)
    {
        MoveToDirection(body.rotation * Vector3.forward, speed);
    }

    /// <summary>
    /// Rigidbody 이동 및 회전 속도 제거.
    /// </summary>
    public void StopMovement()
    {
        if (body == null || body.isKinematic)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 지정 방향 기준 Rigidbody 회전.
    /// </summary>
    public void RotateToDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        Quaternion nextRotation = Quaternion.Slerp(
            body.rotation,
            targetRotation,
            enemyData.rotateSpeed * Time.fixedDeltaTime);

        body.MoveRotation(nextRotation);
    }

    /// <summary>
    /// 지정 방향의 최장 이동 가능 지점 반환.
    /// </summary>
    public Vector3 GetOpenPointInDirection(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return origin;

        direction = direction.normalized;
        float reachableDistance = maxDistance;

        if (Physics.SphereCast(
                origin,
                enemyData.patrolProbeRadius,
                direction,
                out RaycastHit hit,
                maxDistance,
                obstacleLayer,
                QueryTriggerInteraction.Collide))
        {
            reachableDistance = Mathf.Max(0f, hit.distance - enemyData.patrolWallBuffer);
        }

        return origin + direction * reachableDistance;
    }

    /// <summary>
    /// 지정 방향의 가까운 이동 경로가 벽 또는 AI 전용 Trigger 경계에 막혔는지 반환.
    /// </summary>
    public bool IsPathBlocked(Vector3 origin, Vector3 direction, float distance)
    {
        if (direction.sqrMagnitude <= 0.001f || distance <= 0f)
            return false;

        return Physics.SphereCast(
            origin,
            enemyData.patrolProbeRadius,
            direction.normalized,
            out _,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Collide);
    }
}
