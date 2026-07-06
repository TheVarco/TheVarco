using UnityEngine;

public class SharkIdleState : ISharkState
{
    private SharkController shark;

    public SharkIdleState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        Debug.Log("Shark Idle");
    }

    public void Update()
    {
        if (shark.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
        }
        else
        {
            shark.ChangeState(SharkStateType.Patrol);
        }
    }

    public void Exit()
    {
        
    }
}
