using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(FishController))]
[RequireComponent(typeof(NetworkTransform))]
public sealed class FishItem : FoodItem
{
    private FishController fishController;
    private NetworkTransform networkTransform;

    protected override void Awake()
    {
        base.Awake();
        fishController = GetComponent<FishController>();
        networkTransform = GetComponent<NetworkTransform>();
    }

    public override void OnPickedUp(Transform handSocket)
    {
        SetTransformReplicationEnabled(false);
        fishController?.SuspendForPickup();
        base.OnPickedUp(handSocket);
    }

    public override void OnDropped(Vector3 dropPosition)
    {
        base.OnDropped(dropPosition);
        fishController?.ResumeAfterDrop(dropPosition);
        SetTransformReplicationEnabled(true);
    }

    private void SetTransformReplicationEnabled(bool enabled)
    {
        if (networkTransform != null)
            networkTransform.enabled = enabled;
    }
}
