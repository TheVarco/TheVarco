using UnityEngine;

public class SharkAttackState : ISharkState
{
    private SharkController shark;

    // 다음 물기가 가능한 시각. Enter에서 리셋하지 않아, Attack을 재진입해도
    // 쿨다운이 초기화되지 않는다 (Chase⇄Attack 진동으로 인한 쿨다운 우회 방지).
    private float nextBiteTime;

    public SharkAttackState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        
    }

    public void Update()
    {
        // 타겟을 놓치면 순찰로 복귀
        if (!shark.Targeting.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        Vector3 targetPoint = shark.Targeting.GetTargetPoint(shark.AttackHitbox.transform.position);
        Vector3 direction = targetPoint - shark.transform.position;
        
        float attackDistance = Vector3.Distance(
            shark.AttackHitbox.transform.position,
            targetPoint
        );

        // 사거리를 벗어나면 다시 추격
        if (attackDistance > shark.AttackRange)
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        // 무는 동안에도 플레이어를 향해 바라본다
        shark.Navigator.RotateToDirection(direction);

        UpdateCooldown();
    }

    // 쿨다운 대기: 다음 물기 가능 시각이 되면 새 물기 판정을 켠다
    private void UpdateCooldown()
    {
        if (Time.time < nextBiteTime)
            return;

        shark.PlayAttackAnimation();
        
        nextBiteTime = Time.time + shark.AttackCooldown; // 다음 물기 시각 고정
    }

    public void Exit()
    {
        shark.EndAttackHitbox();
    }
}
