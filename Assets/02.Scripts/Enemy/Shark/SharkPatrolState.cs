using UnityEngine;

public class SharkPatrolState : ISharkState
{
    private SharkController shark;
    private Vector3 patrolPoint;
    
    public SharkPatrolState(SharkController shark)
    {
        this.shark = shark;
    }
    
    public void Enter()
    {
        Debug.Log("Shark Patrol");
        SetRandomPatrolPoint();
    }

    public void Update()
    {
        if (shark.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
            return;
        }

        Vector3 direction = patrolPoint - shark.transform.position;

        if (direction.magnitude <= shark.PatrolArriveDistance)
        {
            shark.ChangeState(SharkStateType.Idle);
            return;
        }

        shark.RotateToDirection(direction);
        shark.MoveToDirection(direction);
    }

    public void Exit()
    {
        
    }
    
    private void SetRandomPatrolPoint()
    {
        Vector3 randomOffset = Random.insideUnitSphere * shark.PatrolRadius;

        patrolPoint = shark.SpawnPosition + randomOffset;
    }
}
