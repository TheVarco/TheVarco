using UnityEngine;

/// <summary>
/// 상어의 위치 이동과 방향 회전을 담당한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SharkNavigator : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData;

    [Header("Obstacle")]
    [SerializeField] private LayerMask obstacleLayer;
    
    private Rigidbody sharkRigidbody;

    private void Awake()
    {
        sharkRigidbody = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// 지정한 방향으로 상어를 주어진 속도만큼 이동시킨다.
    /// Rigidbody.MovePosition을 사용하므로 벽에 막히며 관통하지 않는다.
    /// </summary>
    /// <param name="direction">상어가 이동할 방향.</param>
    /// <param name="speed">초당 이동 속도.</param>
    public void MoveToDirection(Vector3 direction, float speed)
    {
        if (direction.sqrMagnitude <= 0.001f || speed <= 0f)
            return;

        Vector3 nextPosition = sharkRigidbody.position + direction.normalized * speed * Time.deltaTime;
        sharkRigidbody.MovePosition(nextPosition);
    }

    /// <summary>
    /// 지정한 방향을 바라보도록 EnemyData의 회전 속도에 맞춰 부드럽게 회전시킨다.
    /// </summary>
    /// <param name="direction">상어가 바라볼 방향.</param>
    public void RotateToDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);

        Quaternion nextRotation = Quaternion.Slerp(
            sharkRigidbody.rotation,
            targetRotation,
            enemyData.rotateSpeed * Time.deltaTime
        );

        sharkRigidbody.MoveRotation(nextRotation);
    }

    /// <summary>
    /// 지정한 방향으로 벽에 막히기 직전까지의 도달 가능한 지점을 반환한다.
    /// SphereCast로 상어 몸통 굵기를 감안해 검사하므로, 좁은 통로 안쪽이나 벽 너머에는 지점이 잡히지 않는다.
    /// 반환된 지점까지는 직선 경로가 비어 있음이 보장된다.
    /// </summary>
    /// <param name="origin">탐색을 시작할 기준 위치(보통 상어의 현재 위치).</param>
    /// <param name="direction">지점을 찾을 방향.</param>
    /// <param name="maxDistance">기준 위치에서 지점까지 허용할 최대 거리.</param>
    public Vector3 GetOpenPointInDirection(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return origin;

        direction = direction.normalized;
        float reachableDistance = maxDistance;

        if (Physics.SphereCast(origin, enemyData.patrolProbeRadius, direction,
                out RaycastHit hit, maxDistance, obstacleLayer))
        {
            reachableDistance = Mathf.Max(0f, hit.distance - enemyData.patrolWallBuffer);
        }

        return origin + direction * reachableDistance;
    }
}
