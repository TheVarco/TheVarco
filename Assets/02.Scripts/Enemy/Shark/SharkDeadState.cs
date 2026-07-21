using UnityEngine;

public class SharkDeadState : ISharkState
{
    // 사망 후 오브젝트가 제거되기까지의 시간 (사망 연출 여유)
    private const float DestroyDelay = 3f;

    private SharkController shark;

    public SharkDeadState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        shark.PlayDieAnimation();

        // 공격 판정이 켜진 채로 죽지 않도록 정리
        if (shark.AttackHitbox != null)
            shark.AttackHitbox.EndBite();

        // 일정 시간 뒤 오브젝트 제거
        Object.Destroy(shark.gameObject, DestroyDelay);
    }

    public void Update()
    {
        
    }

    public void Exit()
    {

    }
}
