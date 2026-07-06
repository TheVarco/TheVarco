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
        
    }

    public void Update()
    {
        if (shark.TryFindTarget())
        {
            shark.ChangeState(SharkStateType.Chase);
        }
    }

    public void Exit()
    {
        
    }
}
