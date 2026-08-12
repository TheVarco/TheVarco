using UnityEngine;

/// <summary>
/// 문어 대기 및 타깃 탐색 상태
/// </summary>
public class OctopusIdleState : IOctopusState
{
    private readonly OctopusController octopus; // 문어 상태 컨텍스트
    private float timer;                        // 현재 대기 시간
    private float waitTime;                     // 이번 대기 목표 시간

    public OctopusIdleState(OctopusController octopus)
    {
        this.octopus = octopus;
    }

    /// <summary>
    /// 무작위 대기 시간 설정
    /// </summary>
    public void Enter()
    {
        timer = 0f;
        waitTime = Random.Range(octopus.IdleWaitMin, octopus.IdleWaitMax);
    }

    /// <summary>
    /// 타깃 발견 또는 대기 종료 기준 상태 전환
    /// </summary>
    public void Update()
    {
        if (octopus.Targeting.TryFindTarget())
        {
            octopus.ChangeState(OctopusStateType.Chase);
            return;
        }

        timer += Time.deltaTime;
        if (timer >= waitTime)
            octopus.ChangeState(OctopusStateType.Patrol);
    }

    public void Exit() { }
}
