using UnityEngine;

public class SharkHitState : ISharkState
{
    private SharkController shark;
    
    public SharkHitState(SharkController shark)
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
