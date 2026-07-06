using UnityEngine;

public class SharkChaseState : ISharkState
{
    private SharkController shark;

    public SharkChaseState(SharkController shark)
    {
        this.shark = shark;
    }

    public void Enter()
    {
        Debug.Log("Shark Chase");
    }

    public void Update()
    {
        
    }

    public void Exit()
    {
        
    }
}