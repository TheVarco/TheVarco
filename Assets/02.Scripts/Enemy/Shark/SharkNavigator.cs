using UnityEngine;

/// <summary>
/// 상어의 이동과 회전을 Rigidbody 기준으로 실행한다.
/// 상태 스크립트는 이동 방향만 결정하고 실제 물리 이동은 이 컴포넌트가 담당한다.
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

        // 빠르게 움직이는 동적 Rigidbody가 얇은 선체를 건너뛰지 않도록
        // 연속 충돌 검사를 사용한다. 실제 충돌 해결은 Unity 물리 엔진이 담당한다.
        if (!sharkRigidbody.isKinematic)
            sharkRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        sharkRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>
    /// 지정한 방향과 속도를 Rigidbody의 선형 속도로 적용한다.
    /// 위치를 직접 이동시키지 않으므로 CapsuleCollider와 선체 Collider의 충돌 결과가 물리에 반영된다.
    /// </summary>
    public void MoveToDirection(Vector3 direction, float speed)
    {
        if (direction.sqrMagnitude <= 0.001f || speed <= 0f)
        {
            StopMovement();
            return;
        }

        sharkRigidbody.linearVelocity = direction.normalized * speed;
    }

    /// <summary>
    /// 이전 상태에서 남은 이동 및 회전 속도를 제거한다.
    /// 추격 상태의 속도가 공격, 피격, 사망 상태까지 이어지는 것을 방지한다.
    /// </summary>
    public void StopMovement()
    {
        if (sharkRigidbody == null)
            return;

        sharkRigidbody.linearVelocity = Vector3.zero;
        sharkRigidbody.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 지정한 방향을 바라보도록 물리 프레임 기준으로 부드럽게 회전한다.
    /// </summary>
    public void RotateToDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        Quaternion nextRotation = Quaternion.Slerp(
            sharkRigidbody.rotation,
            targetRotation,
            enemyData.rotateSpeed * Time.fixedDeltaTime);

        sharkRigidbody.MoveRotation(nextRotation);
    }

    /// <summary>
    /// 지정한 방향에서 순찰 가능한 가장 먼 지점을 반환한다.
    /// 순찰 경로 탐색은 기존처럼 SphereCast로 장애물을 확인한다.
    /// </summary>
    public Vector3 GetOpenPointInDirection(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return origin;

        direction = direction.normalized;
        float reachableDistance = maxDistance;

        if (Physics.SphereCast(origin, enemyData.patrolProbeRadius, direction, out RaycastHit hit, maxDistance, obstacleLayer))
        {
            reachableDistance = Mathf.Max(0f, hit.distance - enemyData.patrolWallBuffer);
        }

        return origin + direction * reachableDistance;
    }
}
