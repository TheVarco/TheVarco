using UnityEngine;

public class SharkChaseState : ISharkState
{
    // TODO : 나중에 밸런싱을 자주 해야되는 상황이 오면 SO로 분리해서 인스펙터에서 관리하도록 바꾸기
    // 추격 시 이속 증가
    private const float ChaseSpeedBonus = 3f;

    private SharkController shark;

    public SharkChaseState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        // Debug.Log("Shark Chase");
    }

    public void Update()
    {
        if (!shark.Targeting.TryUpdateChaseTarget())
        {
            shark.ChangeState(SharkStateType.Patrol);
            return;
        }

        Vector3 targetPoint = shark.Targeting.GetTargetPoint(shark.AttackHitbox.transform.position);
        Vector3 direction = targetPoint - shark.transform.position;
        
        float attackDistance = Vector3.Distance(
            shark.AttackHitbox.transform.position,
            targetPoint
        );

        // 공격 사거리 안에 들어오면 공격 상태로 전환
        if (attackDistance <= shark.AttackRange)
        {
            shark.ChangeState(SharkStateType.Attack);
            return;
        }

        // 사거리 밖이면 기본 속도 + 추격 보너스로 접근
        shark.Navigator.RotateToDirection(direction);
        shark.Navigator.MoveToDirection(direction, shark.MoveSpeed + ChaseSpeedBonus);
    }

    public void Exit()
    {

    }
}
