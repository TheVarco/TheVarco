using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(FishController))]
[RequireComponent(typeof(NetworkTransform))]
public sealed class FishItem : FoodItem
{
    private FishController fishController;
    private bool localCollected;

    [Networked, OnChangedRender(nameof(OnCollectedStateChanged))]
    private NetworkBool NetworkedCollected { get; set; }

    public bool IsCollected => Object != null && Object.IsValid
        ? NetworkedCollected
        : localCollected;

    protected override void Awake()
    {
        base.Awake();
        fishController = GetComponent<FishController>();
    }

    public override void Spawned()
    {
        base.Spawned();
        ApplyCollectedState(NetworkedCollected);
    }

    protected override void OnAuthorityPickupConfirmed()
    {
        SetCollectedFromAuthority(true);
        base.OnAuthorityPickupConfirmed();
    }

    protected override void OnInitialWorld(Vector3 position, Quaternion rotation)
    {
        base.OnInitialWorld(position, rotation);

        // PlacementRevision과 수집 플래그의 Render 콜백 순서는 보장되지 않는다.
        // 월드 자세가 적용된 시점에도 HomePosition을 갱신해 프록시가 복원 전
        // 순찰 중심으로 되돌아가지 않게 한다.
        if (IsCollected)
            fishController?.EnterCollectedPassiveState();
        else
            fishController?.RestoreWildCheckpointState(position);
    }

    public override void OnPickedUp(Transform handSocket)
    {
        localCollected = true;
        SetTransformReplicationEnabled(false);
        fishController?.SuspendForPickup();
        base.OnPickedUp(handSocket);
    }

    public override void OnDropped(Vector3 dropPosition)
    {
        // WorldDropped는 반드시 한 번 획득된 뒤의 상태다. 야생 복원은 별도
        // RestoreCheckpointCollectedState(false) 경로만 사용한다.
        localCollected = true;
        base.OnDropped(dropPosition);
        fishController?.EnterCollectedPassiveState();
        SetTransformReplicationEnabled(true);
    }

    public override void OnStored(SubmarineItemZone zone, int slotIndex)
    {
        localCollected = true;
        SetTransformReplicationEnabled(false);
        fishController?.SuspendForPickup();
        base.OnStored(zone, slotIndex);
    }

    /// <summary>
    /// 체크포인트 당시 채집 여부를 복원한다. false는 유일하게 물고기 AI를
    /// 다시 Wild 상태로 돌리는 경로이며, 일반 드롭은 항상 Passive다.
    /// </summary>
    public void RestoreCheckpointCollectedState(bool collected)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        SetCollectedFromAuthority(collected);
        ApplyCollectedState(collected);
    }

    private void SetCollectedFromAuthority(bool collected)
    {
        localCollected = collected;
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedCollected = collected;
    }

    private void OnCollectedStateChanged()
    {
        ApplyCollectedState(NetworkedCollected);
    }

    private void ApplyCollectedState(bool collected)
    {
        localCollected = collected;
        if (fishController == null)
            return;

        if (collected)
        {
            // 손/Zone 상태는 OnPickedUp/OnStored가 Held 물리를 적용한다. 이미 Held인
            // 경우에는 Passive로 덮어쓰지 않는다.
            if (fishController.CurrentState != FishStateType.Held)
                fishController.EnterCollectedPassiveState();
            return;
        }

        fishController.RestoreWildCheckpointState(transform.position);
    }
}
