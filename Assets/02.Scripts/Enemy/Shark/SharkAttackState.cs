using UnityEngine;

public class SharkAttackState : ISharkState
{
    // 물기 판정(히트박스 콜라이더)이 켜져 있는 시간
    private const float BiteDuration = 0.3f;

    private SharkController shark;

    private float biteTimer; // 현재 물기 판정이 켜져 있는 남은 시간
    private bool isBiting;

    // 다음 물기가 가능한 시각. Enter에서 리셋하지 않아, Attack을 재진입해도
    // 쿨다운이 초기화되지 않는다 (Chase⇄Attack 진동으로 인한 쿨다운 우회 방지).
    private float nextBiteTime;

    public SharkAttackState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        isBiting = false;
    }

    public void Update()
    {
        // 타겟을 놓치면 순찰로 복귀
        if (!shark.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Patrol);
            return;
        }

        Vector3 direction = shark.Target.position - shark.transform.position;

        // 사거리를 벗어나면 다시 추격
        if (direction.magnitude > shark.AttackRange)
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        // 무는 동안에도 플레이어를 향해 바라본다
        shark.RotateToDirection(direction);

        if (isBiting)
            UpdateBite();
        else
            UpdateCooldown();
    }

    // 물기 판정이 켜져 있는 동안: 시간이 다 되면 판정을 끈다
    private void UpdateBite()
    {
        biteTimer -= Time.deltaTime;
        if (biteTimer <= 0f)
        {
            shark.AttackHitbox.EndBite();
            isBiting = false;
        }
    }

    // 쿨다운 대기: 다음 물기 가능 시각이 되면 새 물기 판정을 켠다
    private void UpdateCooldown()
    {
        if (Time.time < nextBiteTime)
            return;

        shark.AttackHitbox.BeginBite(shark.AttackDamage, shark.gameObject);
        isBiting = true;
        biteTimer = BiteDuration;
        nextBiteTime = Time.time + shark.AttackCooldown; // 다음 물기 시각 고정
    }

    public void Exit()
    {
        // 피격/사망 등으로 빠져나갈 때 판정이 켜진 채 남지 않도록 정리
        if (isBiting)
        {
            shark.AttackHitbox.EndBite();
            isBiting = false;
        }
    }
}
