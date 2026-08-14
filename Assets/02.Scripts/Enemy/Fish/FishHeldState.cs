public sealed class FishHeldState : IFishState
{
    private readonly FishController fish;

    public FishHeldState(FishController fish)
    {
        this.fish = fish;
    }

    public void Enter()
    {
        fish.Navigator.StopMovement();
    }

    public void Update() { }
    public void Exit() { }
}
