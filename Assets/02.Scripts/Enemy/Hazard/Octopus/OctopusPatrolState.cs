using UnityEngine;

/// <summary>
/// 장애물 회피 기반 문어 무작위 순찰 상태
/// </summary>
public class OctopusPatrolState : IOctopusState
{
    private const int DirectionAttempts = 4;       // 순찰 방향 탐색 횟수
    private const float ProgressEpsilon = 0.05f;   // 이동 진척 판정 오차

    private readonly OctopusController octopus; // 문어 상태 컨텍스트
    private Vector3 patrolPoint;                // 현재 순찰 목적지
    private float closestDistance;              // 목적지에 가장 가까웠던 거리
    private float stuckTimer;                   // 이동 정체 시간

    public OctopusPatrolState(OctopusController octopus)
    {
        this.octopus = octopus;
    }

    /// <summary>
    /// 무작위 순찰 목적지 설정
    /// </summary>
    public void Enter()
    {
        SetRandomPatrolPoint();
    }

    /// <summary>
    /// 타깃 탐색 및 순찰 이동
    /// </summary>
    public void Update()
    {
        if (octopus.Targeting.TryFindTarget())
        {
            octopus.ChangeState(OctopusStateType.Chase);
            return;
        }

        Vector3 direction = patrolPoint - octopus.transform.position;
        float distance = direction.magnitude;

        if (distance <= octopus.PatrolArriveDistance)
        {
            octopus.ChangeState(OctopusStateType.Idle);
            return;
        }

        if (distance < closestDistance - ProgressEpsilon)
        {
            closestDistance = distance;
            stuckTimer = 0f;
        }
        else
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= octopus.PatrolStuckTime)
            {
                octopus.ChangeState(OctopusStateType.Idle);
                return;
            }
        }

        octopus.Navigator.RotateToDirection(direction);
        octopus.Navigator.MoveToDirection(direction, octopus.MoveSpeed);
    }

    public void Exit() { }

    /// <summary>
    /// 이동 가능 방향 기준 순찰 목적지 선택
    /// </summary>
    private void SetRandomPatrolPoint()
    {
        Vector3 origin = octopus.transform.position;
        Vector3 bestPoint = origin;
        float bestDistanceSqr = 0f;

        for (int i = 0; i < DirectionAttempts; i++)
        {
            Vector3 candidate = octopus.Navigator.GetOpenPointInDirection(
                origin,
                Random.onUnitSphere,
                octopus.PatrolRadius);

            float distanceSqr = (candidate - origin).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestPoint = candidate;
            }
        }

        patrolPoint = bestPoint;
        closestDistance = Mathf.Sqrt(bestDistanceSqr);
        stuckTimer = 0f;
    }
}
