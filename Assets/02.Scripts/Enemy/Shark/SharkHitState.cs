using UnityEngine;

public class SharkHitState : ISharkState
{
    // 피격 시 잠깐 멈칫하는 경직 시간
    private const float HitStunDuration = 0.4f;

    private SharkController shark;
    private float stunTimer;

    public SharkHitState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        // TODO : 피격 애니메이션 재생
        stunTimer = HitStunDuration;
    }

    public void Update()
    {
        // 경직 동안은 아무 행동도 하지 않고 잠깐 멈춤
        stunTimer -= Time.deltaTime;
        if (stunTimer <= 0f)
        {
            // 경직이 끝나면 다시 추격 시도
            // (플레이어가 시야에 없으면 Chase가 알아서 Patrol로 되돌림)
            shark.ChangeState(SharkStateType.Chase);
        }
    }

    public void Exit()
    {

    }
}
