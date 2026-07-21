using UnityEngine;

public class SharkIdleState : ISharkState
{
    private SharkController shark;

    // Idle 대기상태
    private float idleTimer;

    // 1초~5초 랜덤 대기시간 저장
    private float idleWaitTime;

    public SharkIdleState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        shark.PlayIdleAnimation();
        
        // Debug.Log("Shark Idle");
        idleTimer = 0f;
        idleWaitTime = Random.Range(shark.IdleWaitMin, shark.IdleWaitMax);
    }

    public void Update()
    {
        // 대기 중에도 플레이어를 발견하면 즉시 추적으로 전환
        if (shark.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        idleTimer += Time.deltaTime;

        if (idleTimer >= idleWaitTime)
        {
            shark.ChangeState(SharkStateType.Patrol);
        }
    }

    public void Exit()
    {

    }
}
