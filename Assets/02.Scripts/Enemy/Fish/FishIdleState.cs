using UnityEngine;

public sealed class FishIdleState : IFishState
{
    private readonly FishController fish;
    private float elapsed;
    private float waitTime;

    public FishIdleState(FishController fish)
    {
        this.fish = fish;
    }

    public void Enter()
    {
        elapsed = 0f;

        float minimum = Mathf.Min(fish.IdleWaitMin, fish.IdleWaitMax);
        float maximum = Mathf.Max(fish.IdleWaitMin, fish.IdleWaitMax);
        waitTime = Random.Range(minimum, maximum);
    }

    public void Update()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= waitTime)
            fish.ChangeState(FishStateType.Patrol);
    }

    public void Exit() { }
}
