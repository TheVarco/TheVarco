using UnityEngine;

public class SharkPatrolState : ISharkState
{
    // 목적지를 뽑을 때 시도하는 방향 후보 수 (가장 멀리 열린 방향을 선택)
    private const int DirectionAttempts = 4;

    // 이만큼이라도 목적지에 가까워지면 진전으로 간주해 정체 타이머를 초기화
    private const float ProgressEpsilon = 0.05f;

    private SharkController shark;
    private Vector3 patrolPoint;

    // 정체 감지용: 지금까지 목적지에 가장 가까웠던 거리와 정체 지속 시간
    private float closestDistance;
    private float stuckTimer;

    // 기즈모 디버그용: 현재 정찰 목적지
    public Vector3 PatrolPoint => patrolPoint;

    public SharkPatrolState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        SetRandomPatrolPoint();
    }

    public void Update()
    {
        if (shark.Targeting.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        Vector3 direction = patrolPoint - shark.transform.position;
        float distance = direction.magnitude;

        // 목적지(벽 앞 열린 지점)에 도착하면 Idle로 복귀해 다음 목적지를 다시 뽑는다.
        if (distance <= shark.PatrolArriveDistance)
        {
            shark.ChangeState(SharkStateType.Idle);
            return;
        }

        // 목적지에 계속 가까워지지 못하면(움직이는 장애물 등에 막히면) 정체로 보고 Idle로 복귀한다.
        if (distance < closestDistance - ProgressEpsilon)
        {
            closestDistance = distance;
            stuckTimer = 0f;
        }
        else
        {
            stuckTimer += Time.deltaTime;

            if (stuckTimer >= shark.PatrolStuckTime)
            {
                shark.ChangeState(SharkStateType.Idle);
                return;
            }
        }

        shark.Navigator.RotateToDirection(direction);
        shark.Navigator.MoveForward(shark.MoveSpeed);
    }

    public void Exit()
    {

    }

    /// <summary>
    /// 여러 방향으로 검사하여 벽 앞 열린 공간 중 가장 멀리 갈 수 있는 지점을 목적지로 삼는다.
    /// 지점까지 직선 경로가 비어 있음이 보장되므로 정찰 이동 중 벽에 부딪히지 않는다.
    /// </summary>
    private void SetRandomPatrolPoint()
    {
        Vector3 origin = shark.transform.position;

        Vector3 bestPoint = origin;
        float bestDistanceSqr = -1f;

        for (int i = 0; i < DirectionAttempts; i++)
        {
            Vector3 direction = Random.onUnitSphere;

            Vector3 candidate = shark.Navigator.GetOpenPointInDirection(origin, direction, shark.PatrolRadius);
            float candidateDistanceSqr = (candidate - origin).sqrMagnitude;

            if (candidateDistanceSqr > bestDistanceSqr)
            {
                bestDistanceSqr = candidateDistanceSqr;
                bestPoint = candidate;
            }
        }

        patrolPoint = bestPoint;

        closestDistance = Mathf.Sqrt(bestDistanceSqr);
        stuckTimer = 0f;
    }
}
