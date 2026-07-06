using UnityEngine;

public class SharkPatrolState : ISharkState
{
    private SharkController shark;
    
    public SharkPatrolState(SharkController shark)
    {
        this.shark = shark;
    }
    
    public void Enter()
    {
        throw new System.NotImplementedException();
    }

    public void Update()
    {
        throw new System.NotImplementedException();
    }

    public void Exit()
    {
        throw new System.NotImplementedException();
    }
}
