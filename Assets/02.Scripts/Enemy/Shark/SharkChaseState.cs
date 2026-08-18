using UnityEngine;

public class SharkChaseState : ISharkState
{
    // TODO : 나중에 밸런싱을 자주 해야되는 상황이 오면 SO로 분리해서 인스펙터에서 관리하도록 바꾸기
    // 추격 시 이속 증가
    private const float ChaseSpeedBonus = 2f;
    private const float SuspiciousDuration = 3f;

    private SharkController shark;
    private bool isSuspicious;
    private float suspiciousTimer;

    public SharkChaseState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        // Debug.Log("Shark Chase");
        isSuspicious = false;
        suspiciousTimer = 0f;

        if (shark.Targeting.Target == null)
            BeginSuspiciousWait();
    }

    public void Update()
    {
        if (isSuspicious)
        {
            UpdateSuspiciousWait();
            return;
        }

        if (!shark.Targeting.TryUpdateChaseTarget())
        {
            BeginSuspiciousWait();
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
        float chaseSpeed = shark.MoveSpeed + ChaseSpeedBonus;
        float boundaryLookAhead = chaseSpeed * Time.fixedDeltaTime + shark.Data.patrolWallBuffer;
        if (shark.Navigator.IsPathBlocked(
                shark.transform.position,
                direction,
                boundaryLookAhead))
        {
            // Z6 출구처럼 통과 가능한 Trigger도 상어에게는 경계로 취급한다.
            shark.Targeting.ClearTarget();
            shark.ChangeState(SharkStateType.Patrol);
            return;
        }

        shark.Navigator.RotateToDirection(direction);
        shark.Navigator.MoveForward(chaseSpeed);
    }

    public void Exit()
    {
        EndSuspiciousWait();
    }

    private void BeginSuspiciousWait()
    {
        if (isSuspicious)
            return;

        isSuspicious = true;
        suspiciousTimer = SuspiciousDuration;
        shark.Navigator.StopMovement();
        shark.SetSuspicious(true);
    }

    private void UpdateSuspiciousWait()
    {
        if (shark.Targeting.TryFindTarget())
        {
            EndSuspiciousWait();
            return;
        }

        suspiciousTimer -= Time.deltaTime;
        if (suspiciousTimer > 0f)
            return;

        EndSuspiciousWait();
        shark.ChangeState(SharkStateType.Patrol);
    }

    private void EndSuspiciousWait()
    {
        if (!isSuspicious)
            return;

        isSuspicious = false;
        suspiciousTimer = 0f;
        shark.SetSuspicious(false);
    }
}
