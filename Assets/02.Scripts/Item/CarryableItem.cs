using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum CarryablePlacementMode
{
    WorldInitial,
    WorldDropped,
    Held,
    Stored,
    Consumed,
    CreatureAttached
}

// 손, 월드, Item Zone, 소모 상태를 하나의 권위 상태로 관리하는 공용 아이템.
[RequireComponent(typeof(Collider))]
public class CarryableItem : NetworkBehaviour, Interactable
{
    [Header("아이템 정보")]
    public string itemName = "산소통";
    [Tooltip("사용했을 때 소모 상태로 전환되는 아이템인지")]
    public bool isConsumable = true;
    [Tooltip("핫바 UI에 표시될 아이콘")]
    public Sprite icon;
    public string giveActionName = "사용해주기";

    [Header("손에 들었을 때 위치 보정")]
    public Vector3 holdPositionOffset;
    public Vector3 holdRotationOffset;
    [SerializeField, Min(0.01f)]
    [Tooltip("손에 들었을 때 월드 크기 배율 (1 = 월드에서의 원래 크기)")]
    private float heldScaleMultiplier = 0.35f;

    [Header("Item Zone 보관 보정")]
    public Vector3 storagePositionOffset;
    public Vector3 storageRotationOffset;
    [Tooltip("동굴 배치 도구가 잠수함 내부 시작 아이템으로 만든 인스턴스에만 사용")]
    public bool startStoredInItemZone;

    protected Rigidbody rb { get; private set; }
    protected Collider col { get; private set; }

    private NetworkTransform networkTransform;
    private RigidbodyInterpolation initialInterpolation;
    private RigidbodyConstraints initialConstraints;
    private CollisionDetectionMode initialCollisionDetection;
    private float initialAngularDamping;
    private bool initialIsKinematic;
    private bool initialUseGravity;
    private bool initialColliderTrigger;
    private LayerMask initialColliderExcludeLayers;
    private Vector3 defaultWorldScale;
    private int playerLayerMask;
    private string localCheckpointSessionKey;

    private SubmarineItemZone appliedStorageZone;
    private PlayerHotbar localHolderHotbar;
    private SubmarineItemZone localStoredZone;
    private int localStoredSlot = -1;
    private CarryablePlacementMode localPlacementMode = CarryablePlacementMode.WorldInitial;
    private Vector3 localWorldPosition;
    private Quaternion localWorldRotation;

    [Networked] private CarryablePlacementMode NetworkedPlacementMode { get; set; }
    [Networked] private NetworkId HolderId { get; set; }
    [Networked] private NetworkId StoredSubmarineId { get; set; }
    [Networked] private int StoredSlotPlusOne { get; set; }
    [Networked] private Vector3 PlacementWorldPosition { get; set; }
    [Networked] private Quaternion PlacementWorldRotation { get; set; }
    [Networked, OnChangedRender(nameof(OnPlacementRevisionChanged))]
    private int PlacementRevision { get; set; }

    private int lastAppliedRevision = int.MinValue;
    private bool placementResolutionPending;
    private bool initialStoragePending;

    private bool IsNetworkActive => Object != null && Object.IsValid;
    private bool HasPlacementAuthority => !IsNetworkActive || Object.HasStateAuthority;

    public CarryablePlacementMode PlacementMode => IsNetworkActive
        ? NetworkedPlacementMode
        : localPlacementMode;
    public NetworkId HolderNetworkId => IsNetworkActive ? HolderId : default;
    public NetworkId StoredSubmarineNetworkId => IsNetworkActive ? StoredSubmarineId : default;
    public bool IsConsumed => PlacementMode == CarryablePlacementMode.Consumed;
    public bool IsStored => PlacementMode == CarryablePlacementMode.Stored;
    public virtual bool CanUseOnTeammate => isConsumable;
    public int StoredSlotIndex => IsNetworkActive ? StoredSlotPlusOne - 1 : localStoredSlot;
    public bool IsSonarDetectable => PlacementMode is
        CarryablePlacementMode.WorldInitial or CarryablePlacementMode.WorldDropped;

    public string CheckpointSessionKey
    {
        get
        {
            if (IsNetworkActive)
                return $"network-item:{Object.Id}";

            return localCheckpointSessionKey;
        }
    }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        networkTransform = GetComponent<NetworkTransform>();
        defaultWorldScale = transform.lossyScale;
        localWorldPosition = transform.position;
        localWorldRotation = transform.rotation;
        localCheckpointSessionKey = BuildLocalCheckpointSessionKey();

        int playerLayer = LayerMask.NameToLayer("Player");
        playerLayerMask = playerLayer >= 0 ? 1 << playerLayer : 0;

        if (rb != null)
        {
            initialInterpolation = rb.interpolation;
            initialConstraints = rb.constraints;
            initialCollisionDetection = rb.collisionDetectionMode;
            initialAngularDamping = rb.angularDamping;
            initialIsKinematic = rb.isKinematic;
            initialUseGravity = rb.useGravity;
        }

        if (col != null)
        {
            initialColliderTrigger = col.isTrigger;
            initialColliderExcludeLayers = col.excludeLayers;
        }
    }

    protected virtual void Start()
    {
        if (!IsNetworkActive)
        {
            if (startStoredInItemZone)
                TryStoreInitialItem();
            else
                ApplyPlacementState(true);
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority && PlacementRevision == 0)
        {
            NetworkedPlacementMode = CarryablePlacementMode.WorldInitial;
            PlacementWorldPosition = transform.position;
            PlacementWorldRotation = transform.rotation;
            PlacementRevision = 1;
        }

        if (Object.HasStateAuthority
            && startStoredInItemZone
            && PlacementMode == CarryablePlacementMode.WorldInitial)
        {
            initialStoragePending = true;
            TryStoreInitialItem();
        }

        ApplyPlacementState(true);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !initialStoragePending)
            return;

        if (PlacementMode != CarryablePlacementMode.WorldInitial)
        {
            initialStoragePending = false;
            return;
        }

        TryStoreInitialItem();
    }

    public override void Render()
    {
        if (placementResolutionPending)
            ApplyPlacementState(true);
    }

    public bool IsEquippedBy(NetworkObject holder)
    {
        if (holder == null || !holder.IsValid
            || PlacementMode != CarryablePlacementMode.Held
            || !HolderId.IsValid || HolderId != holder.Id)
        {
            return false;
        }

        PlayerHotbar hotbar = holder.GetComponent<PlayerHotbar>();
        return hotbar != null && hotbar.IsItemInActiveSlot(this);
    }

    public void RequestPickup(NetworkId holder)
    {
        if (!IsNetworkActive)
            return;
        RPC_RequestPickup(holder);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestPickup(NetworkId requester, RpcInfo info = default)
    {
        if (PlacementMode is CarryablePlacementMode.Held
            or CarryablePlacementMode.Consumed
            or CarryablePlacementMode.CreatureAttached)
        {
            return;
        }

        if (!TryResolveRequester(
                requester,
                info,
                out NetworkObject requesterObject,
                out PlayerHotbar hotbar))
        {
            return;
        }

        // 클라이언트 UI 판정만 신뢰하지 않고, Host가 생물 단계와 거리까지 다시 확인한다.
        if (!CanInteract(requesterObject.gameObject)
            || !IsWithinAuthorityInteractionRange(requesterObject))
        {
            return;
        }

        if (!hotbar.TryReserveItemFromAuthority(this, out int slot))
        {
            RPC_RequestRejected(requester, 0);
            return;
        }

        ReleaseCurrentStorageReservation();
        OnAuthorityPickupConfirmed();
        if (!CommitPlacement(
                CarryablePlacementMode.Held,
                requester,
                default,
                -1,
                transform.position,
                transform.rotation))
        {
            hotbar.RemoveItemFromAuthority(this);
            return;
        }

        hotbar.RefreshRestoredVisibility();
        hotbar.SelectSlotFromAuthority(slot);
    }

    protected virtual void OnAuthorityPickupConfirmed() { }

    protected bool TryAssignHolderFromStateAuthority(NetworkId requester)
    {
        if (!HasPlacementAuthority
            || PlacementMode is CarryablePlacementMode.Held or CarryablePlacementMode.Consumed)
        {
            return false;
        }

        if (!IsNetworkActive)
            return false;
        if (!Runner.TryFindObject(requester, out NetworkObject requesterObject))
            return false;

        if (!CanInteract(requesterObject.gameObject)
            || !IsWithinAuthorityInteractionRange(requesterObject))
        {
            return false;
        }

        PlayerHotbar hotbar = requesterObject.GetComponent<PlayerHotbar>();
        if (hotbar == null || !hotbar.TryReserveItemFromAuthority(this, out _))
            return false;

        ReleaseCurrentStorageReservation();
        OnAuthorityPickupConfirmed();
        bool committed = CommitPlacement(
            CarryablePlacementMode.Held,
            requester,
            default,
            -1,
            transform.position,
            transform.rotation);
        if (committed)
            hotbar.SelectSlotFromAuthority(hotbar.GetSlotOfItem(this));
        return committed;
    }

    public void RequestRelease(NetworkId requester, int requestedSlot)
    {
        if (!IsNetworkActive)
        {
            ReleaseLocal(requestedSlot);
            return;
        }

        RPC_RequestRelease(requester, requestedSlot);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestRelease(NetworkId requester, int requestedSlot, RpcInfo info = default)
    {
        if (PlacementMode != CarryablePlacementMode.Held
            || HolderId != requester
            || !TryResolveRequester(requester, info, out NetworkObject requesterObject, out PlayerHotbar hotbar)
            || hotbar.GetSlotOfItem(this) != requestedSlot)
        {
            return;
        }

        PlayerController player = requesterObject.GetComponent<PlayerController>();
        SubmarineItemZone zone = SubmarineItemZone.FindForPlayer(player);
        if (zone != null)
        {
            if (!zone.TryReserve(this, out int slotIndex))
            {
                RPC_RequestRejected(requester, 1);
                return;
            }

            if (!hotbar.RemoveItemFromAuthority(this))
            {
                zone.Release(this);
                return;
            }

            CommitPlacement(
                CarryablePlacementMode.Stored,
                default,
                zone.SubmarineId,
                slotIndex,
                transform.position,
                transform.rotation);
            return;
        }

        if (!hotbar.RemoveItemFromAuthority(this))
            return;

        Vector3 dropPosition = hotbar.GetAuthorityDropPosition();
        CommitPlacement(
            CarryablePlacementMode.WorldDropped,
            default,
            default,
            -1,
            dropPosition,
            transform.rotation);
    }

    public void RequestConsume(NetworkId requester, int requestedSlot)
    {
        if (!IsNetworkActive)
        {
            if (localHolderHotbar != null)
                localHolderHotbar.RemoveItemFromAuthority(this);
            CommitPlacement(
                CarryablePlacementMode.Consumed,
                default,
                default,
                -1,
                transform.position,
                transform.rotation);
            return;
        }

        RPC_RequestConsume(requester, requestedSlot);
    }

    // 기존 외부 호출부 호환용. 실제로 Despawn하지 않고 소프트 소비한다.
    public void RequestDespawn()
    {
        int slot = -1;
        if (IsNetworkActive && HolderId.IsValid
            && Runner.TryFindObject(HolderId, out NetworkObject holderObject))
        {
            slot = holderObject.GetComponent<PlayerHotbar>()?.GetSlotOfItem(this) ?? -1;
        }
        else if (localHolderHotbar != null)
        {
            slot = localHolderHotbar.GetSlotOfItem(this);
        }

        RequestConsume(IsNetworkActive ? HolderId : default, slot);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestConsume(NetworkId requester, int requestedSlot, RpcInfo info = default)
    {
        if (PlacementMode != CarryablePlacementMode.Held
            || HolderId != requester
            || !TryResolveRequester(requester, info, out _, out PlayerHotbar hotbar)
            || hotbar.GetSlotOfItem(this) != requestedSlot)
        {
            return;
        }

        if (!hotbar.RemoveItemFromAuthority(this))
            return;

        ReleaseCurrentStorageReservation();
        CommitPlacement(
            CarryablePlacementMode.Consumed,
            default,
            default,
            -1,
            transform.position,
            transform.rotation);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_RequestRejected(NetworkId requester, int reason)
    {
        if (!requester.IsValid
            || !Runner.TryFindObject(requester, out NetworkObject requesterObject)
            || !requesterObject.HasInputAuthority)
        {
            return;
        }

        string message = reason == 1
            ? "Item Zone이 가득 찼습니다"
            : "핫바가 가득 찼습니다";
        requesterObject.GetComponent<PlayerInteractor>()?.ShowTemporaryPrompt(message);
    }

    private bool TryResolveRequester(
        NetworkId requester,
        RpcInfo info,
        out NetworkObject requesterObject,
        out PlayerHotbar hotbar)
    {
        requesterObject = null;
        hotbar = null;
        if (!requester.IsValid || !Runner.TryFindObject(requester, out requesterObject))
            return false;
        if (info.Source != PlayerRef.None && requesterObject.InputAuthority != info.Source)
            return false;

        hotbar = requesterObject.GetComponent<PlayerHotbar>();
        return hotbar != null;
    }

    protected bool IsWithinAuthorityInteractionRange(NetworkObject requesterObject)
    {
        if (requesterObject == null || col == null || !col.enabled)
            return false;

        PlayerInteractor requesterInteractor = requesterObject.GetComponent<PlayerInteractor>();
        if (requesterInteractor == null)
            return false;

        Transform reference = requesterInteractor.lookReference != null
            ? requesterInteractor.lookReference
            : requesterObject.transform;
        Vector3 closestPoint = col.ClosestPoint(reference.position);
        float allowedRange = Mathf.Max(0f, requesterInteractor.interactRange) + 0.5f;
        return (closestPoint - reference.position).sqrMagnitude
            <= allowedRange * allowedRange;
    }

    private void ReleaseLocal(int requestedSlot)
    {
        PlayerHotbar hotbar = localHolderHotbar;
        if (hotbar == null || hotbar.GetSlotOfItem(this) != requestedSlot)
            return;

        PlayerController player = hotbar.GetComponent<PlayerController>();
        SubmarineItemZone zone = SubmarineItemZone.FindForPlayer(player);
        if (zone != null)
        {
            if (!zone.TryReserve(this, out int slotIndex))
            {
                hotbar.interactor?.ShowTemporaryPrompt("Item Zone이 가득 찼습니다");
                return;
            }

            hotbar.RemoveItemFromAuthority(this);
            CommitPlacement(
                CarryablePlacementMode.Stored,
                default,
                default,
                slotIndex,
                transform.position,
                transform.rotation,
                zone);
            return;
        }

        hotbar.RemoveItemFromAuthority(this);
        CommitPlacement(
            CarryablePlacementMode.WorldDropped,
            default,
            default,
            -1,
            hotbar.GetAuthorityDropPosition(),
            transform.rotation);
    }

    private void OnPlacementRevisionChanged()
    {
        ApplyPlacementState(false);
    }

    private void ApplyPlacementState(bool force)
    {
        int revision = IsNetworkActive ? PlacementRevision : 0;
        if (!force && !placementResolutionPending && lastAppliedRevision == revision)
            return;

        lastAppliedRevision = revision;
        placementResolutionPending = false;

        switch (PlacementMode)
        {
            case CarryablePlacementMode.WorldInitial:
                OnInitialWorld(CurrentPlacementWorldPosition, CurrentPlacementWorldRotation);
                break;

            case CarryablePlacementMode.WorldDropped:
                OnDropped(CurrentPlacementWorldPosition);
                transform.rotation = CurrentPlacementWorldRotation;
                RestoreDefaultWorldScale();
                break;

            case CarryablePlacementMode.Held:
                if (!TryResolveHolderHotbar(out PlayerHotbar holderHotbar)
                    || holderHotbar.handSocket == null)
                {
                    ApplyPendingPresentation();
                    return;
                }

                localHolderHotbar = holderHotbar;
                OnPickedUp(holderHotbar.handSocket);
                holderHotbar.RefreshRestoredVisibility();
                break;

            case CarryablePlacementMode.Stored:
                SubmarineItemZone zone = ResolveStoredZone();
                if (zone == null || StoredSlotIndex < 0)
                {
                    ApplyPendingPresentation();
                    return;
                }

                OnStored(zone, StoredSlotIndex);
                break;

            case CarryablePlacementMode.Consumed:
                OnConsumed();
                break;

            case CarryablePlacementMode.CreatureAttached:
                ApplyCreatureAttachedPresentation();
                break;
        }
    }

    private Vector3 CurrentPlacementWorldPosition => IsNetworkActive
        ? PlacementWorldPosition
        : localWorldPosition;
    private Quaternion CurrentPlacementWorldRotation => IsNetworkActive
        ? PlacementWorldRotation
        : localWorldRotation;

    private bool TryResolveHolderHotbar(out PlayerHotbar hotbar)
    {
        hotbar = localHolderHotbar;
        if (!IsNetworkActive)
            return hotbar != null;

        if (!HolderId.IsValid || !Runner.TryFindObject(HolderId, out NetworkObject holderObject))
            return false;

        hotbar = holderObject.GetComponent<PlayerHotbar>();
        return hotbar != null && hotbar.GetSlotOfItem(this) >= 2;
    }

    private void ApplyPendingPresentation()
    {
        placementResolutionPending = true;
        ClearStoredPresentation();
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();
        if (col != null)
            col.enabled = false;
        ApplyVisible(false);
    }

    protected virtual void OnInitialWorld(Vector3 position, Quaternion rotation)
    {
        ClearStoredPresentation();
        localHolderHotbar = null;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        RestoreDefaultWorldScale();

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = initialColliderTrigger;
            col.excludeLayers = initialColliderExcludeLayers;
        }

        if (rb != null)
        {
            StopVelocityIfDynamic();
            bool proxy = IsNetworkActive && !Object.HasStateAuthority;
            bool targetKinematic = proxy || initialIsKinematic;
            CollisionDetectionMode targetCollision = proxy
                && initialCollisionDetection == CollisionDetectionMode.ContinuousDynamic
                    ? CollisionDetectionMode.ContinuousSpeculative
                    : initialCollisionDetection;
            if (targetKinematic)
                rb.collisionDetectionMode = targetCollision;
            rb.isKinematic = targetKinematic;
            rb.useGravity = initialUseGravity;
            rb.constraints = initialConstraints;
            rb.angularDamping = initialAngularDamping;
            rb.interpolation = proxy ? RigidbodyInterpolation.None : initialInterpolation;
            rb.collisionDetectionMode = targetCollision;
        }

        SetTransformReplicationEnabled(true);
        ApplyVisible(true);
    }

    public virtual void OnPickedUp(Transform handSocket)
    {
        ClearStoredPresentation();
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();

        if (col != null)
        {
            col.enabled = false;
            col.isTrigger = initialColliderTrigger;
            col.excludeLayers = initialColliderExcludeLayers;
        }

        transform.SetParent(handSocket, false);
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);
        ApplyHeldWorldScale();
        RefreshHeldVisibility();
    }

    public virtual void OnDropped(Vector3 dropPosition)
    {
        ClearStoredPresentation();
        localHolderHotbar = null;
        transform.SetParent(null, true);
        transform.position = dropPosition;
        RestoreDefaultWorldScale();

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = false;
            col.excludeLayers = initialColliderExcludeLayers | playerLayerMask;
        }

        if (rb != null)
        {
            StopVelocityIfDynamic();
            bool proxy = IsNetworkActive && !Object.HasStateAuthority;
            rb.isKinematic = proxy;
            rb.useGravity = false;
            rb.linearDamping = 1f;
            rb.angularDamping = initialAngularDamping;
            rb.constraints = initialConstraints;
            rb.interpolation = proxy ? RigidbodyInterpolation.None : initialInterpolation;
            rb.collisionDetectionMode = proxy
                ? CollisionDetectionMode.ContinuousSpeculative
                : CollisionDetectionMode.ContinuousDynamic;
        }

        SetTransformReplicationEnabled(true);
        ApplyVisible(true);
    }

    public virtual void OnStored(SubmarineItemZone zone, int slotIndex)
    {
        if (zone == null || slotIndex < 0)
            return;

        if (appliedStorageZone != zone)
            ClearStoredPresentation();

        localHolderHotbar = null;
        appliedStorageZone = zone;
        zone.RegisterStoredItem(this, slotIndex);
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = true;
            col.excludeLayers = initialColliderExcludeLayers | playerLayerMask;
        }

        transform.SetParent(zone.transform, false);
        transform.localPosition = zone.GetSlotLocalPosition(slotIndex) + storagePositionOffset;
        transform.localRotation = Quaternion.Euler(zone.DefaultItemRotation + storageRotationOffset);
        RestoreDefaultWorldScale();
        ApplyVisible(true);
    }

    protected virtual void OnConsumed()
    {
        ClearStoredPresentation();
        localHolderHotbar = null;
        transform.SetParent(null, true);
        RestoreDefaultWorldScale();
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();
        if (col != null)
            col.enabled = false;
        ApplyVisible(false);
    }

    protected virtual void ApplyCreatureAttachedPresentation()
    {
        ClearStoredPresentation();
        localHolderHotbar = null;
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();
        if (col != null)
        {
            col.enabled = true;
            col.excludeLayers = initialColliderExcludeLayers;
        }
        RestoreDefaultWorldScale();
        ApplyVisible(true);
    }

    public void RefreshHeldVisibility()
    {
        if (PlacementMode != CarryablePlacementMode.Held)
            return;

        bool visible = TryResolveHolderHotbar(out PlayerHotbar hotbar)
            && hotbar.IsSlotActiveForPresentation(hotbar.GetSlotOfItem(this));
        ApplyVisible(visible);
    }

    public void SetVisible(bool visible)
    {
        ApplyVisible(visible);
    }

    private void ApplyVisible(bool visible)
    {
        foreach (Renderer itemRenderer in GetComponentsInChildren<Renderer>(true))
            itemRenderer.enabled = visible;
    }

    protected void StopBodyForAttachment()
    {
        if (rb == null)
            return;

        StopVelocityIfDynamic();
        // ContinuousDynamic은 Kinematic Rigidbody에서 지원되지 않는다.
        // 먼저 안전한 모드로 되돌려 상태 전환 경고를 막는다.
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;
    }

    private void StopVelocityIfDynamic()
    {
        if (rb == null || rb.isKinematic)
            return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    protected void SetTransformReplicationEnabled(bool enabled)
    {
        if (networkTransform != null)
            networkTransform.enabled = enabled;
    }

    private bool CommitPlacement(
        CarryablePlacementMode mode,
        NetworkId holder,
        NetworkId submarine,
        int storedSlot,
        Vector3 worldPosition,
        Quaternion worldRotation,
        SubmarineItemZone localZone = null)
    {
        if (!HasPlacementAuthority)
            return false;

        SubmarineItemZone previousZone = ResolveStoredZone();
        if (previousZone != null
            && (mode != CarryablePlacementMode.Stored
                || previousZone != localZone && (!submarine.IsValid || previousZone.SubmarineId != submarine)
                || StoredSlotIndex != storedSlot))
        {
            previousZone.Release(this);
        }

        if (IsNetworkActive)
        {
            NetworkedPlacementMode = mode;
            HolderId = holder;
            StoredSubmarineId = mode == CarryablePlacementMode.Stored ? submarine : default;
            StoredSlotPlusOne = mode == CarryablePlacementMode.Stored ? storedSlot + 1 : 0;
            PlacementWorldPosition = worldPosition;
            PlacementWorldRotation = worldRotation;
            PlacementRevision++;
        }
        else
        {
            localPlacementMode = mode;
            localStoredZone = mode == CarryablePlacementMode.Stored ? localZone : null;
            localStoredSlot = mode == CarryablePlacementMode.Stored ? storedSlot : -1;
            localWorldPosition = worldPosition;
            localWorldRotation = worldRotation;
            if (mode != CarryablePlacementMode.Held)
                localHolderHotbar = null;
        }

        ApplyPlacementState(true);
        return true;
    }

    protected bool CommitCreatureAttachedPlacementFromAuthority()
    {
        return CommitPlacement(
            CarryablePlacementMode.CreatureAttached,
            default,
            default,
            -1,
            transform.position,
            transform.rotation);
    }

    protected bool CommitWorldDroppedPlacementFromAuthority(Vector3 position, Quaternion rotation)
    {
        return CommitPlacement(
            CarryablePlacementMode.WorldDropped,
            default,
            default,
            -1,
            position,
            rotation);
    }

    private void ReleaseCurrentStorageReservation()
    {
        ResolveStoredZone()?.Release(this);
    }

    private SubmarineItemZone ResolveStoredZone()
    {
        if (IsNetworkActive)
            return SubmarineItemZone.FindBySubmarineId(StoredSubmarineId, Runner);
        return localStoredZone;
    }

    public bool IsStoredIn(SubmarineItemZone zone)
    {
        if (zone == null || PlacementMode != CarryablePlacementMode.Stored)
            return false;
        if (IsNetworkActive)
            return zone.SubmarineId.IsValid && zone.SubmarineId == StoredSubmarineId;
        return localStoredZone == zone;
    }

    private void ClearStoredPresentation()
    {
        appliedStorageZone?.Release(this);
        appliedStorageZone = null;
    }

    private void TryStoreInitialItem()
    {
        if (!HasPlacementAuthority || PlacementMode != CarryablePlacementMode.WorldInitial)
            return;

        NetworkRunner runner = IsNetworkActive ? Runner : null;
        SubmarineItemZone zone = SubmarineItemZone.FindContainingPoint(transform.position, runner);
        if (zone == null
            || IsNetworkActive && !zone.SubmarineId.IsValid
            || !zone.TryReserve(this, out int slotIndex))
        {
            return;
        }

        CommitPlacement(
            CarryablePlacementMode.Stored,
            default,
            IsNetworkActive ? zone.SubmarineId : default,
            slotIndex,
            transform.position,
            transform.rotation,
            zone);
        initialStoragePending = false;
    }

    protected void RestoreDefaultWorldScale()
    {
        ApplyWorldScale(defaultWorldScale);
    }

    private void ApplyHeldWorldScale()
    {
        ApplyWorldScale(defaultWorldScale * Mathf.Max(0.01f, heldScaleMultiplier));
    }

    private void ApplyWorldScale(Vector3 targetWorldScale)
    {
        transform.localScale = Vector3.one;
        Vector3 inheritedWorldScale = transform.lossyScale;
        transform.localScale = new Vector3(
            DivideScale(targetWorldScale.x, inheritedWorldScale.x),
            DivideScale(targetWorldScale.y, inheritedWorldScale.y),
            DivideScale(targetWorldScale.z, inheritedWorldScale.z));
    }

    protected void RestoreInitialColliderCollisionMask()
    {
        if (col != null)
            col.excludeLayers = initialColliderExcludeLayers;
    }

    private string BuildLocalCheckpointSessionKey()
    {
        string path = $"{gameObject.name}[{transform.GetSiblingIndex()}]";
        Transform current = transform.parent;
        while (current != null)
        {
            path = $"{current.name}[{current.GetSiblingIndex()}]/{path}";
            current = current.parent;
        }
        return $"scene-item:{gameObject.scene.handle}:{path}";
    }

    private static float DivideScale(float desiredWorldScale, float inheritedWorldScale)
    {
        return Mathf.Abs(inheritedWorldScale) > 0.0001f
            ? desiredWorldScale / inheritedWorldScale
            : desiredWorldScale;
    }

    public virtual void PrepareForCheckpointRestore()
    {
        ClearStoredPresentation();
        placementResolutionPending = false;
        SetTransformReplicationEnabled(false);
        StopBodyForAttachment();
        if (col != null)
            col.enabled = false;
        transform.SetParent(null, true);
    }

    public bool RestoreWorldFromCheckpoint(
        CarryablePlacementMode mode,
        Vector3 position,
        Quaternion rotation)
    {
        if (mode is not (CarryablePlacementMode.WorldInitial or CarryablePlacementMode.WorldDropped))
            return false;
        ReleaseCurrentStorageReservation();
        return CommitPlacement(mode, default, default, -1, position, rotation);
    }

    public bool RestoreHeldFromCheckpoint(PlayerHotbar hotbar, int slotNumber)
    {
        if (hotbar == null || !HasPlacementAuthority
            || !hotbar.RestoreItemToExactSlot(slotNumber, this))
        {
            return false;
        }

        localHolderHotbar = hotbar;
        NetworkId holder = hotbar.Object != null && hotbar.Object.IsValid
            ? hotbar.Object.Id
            : default;
        return CommitPlacement(
            CarryablePlacementMode.Held,
            holder,
            default,
            -1,
            transform.position,
            transform.rotation);
    }

    public bool RestoreStoredFromCheckpoint(SubmarineItemZone zone, int slotIndex)
    {
        if (zone == null || !HasPlacementAuthority || !zone.TryReserveExact(this, slotIndex))
            return false;

        return CommitPlacement(
            CarryablePlacementMode.Stored,
            default,
            IsNetworkActive ? zone.SubmarineId : default,
            slotIndex,
            transform.position,
            transform.rotation,
            zone);
    }

    public bool RestoreConsumedFromCheckpoint()
    {
        ReleaseCurrentStorageReservation();
        return CommitPlacement(
            CarryablePlacementMode.Consumed,
            default,
            default,
            -1,
            transform.position,
            transform.rotation);
    }

    public virtual bool RestoreCreatureAttachedFromCheckpoint(AttachmentSlot slot)
    {
        return false;
    }

    public void RefreshCheckpointPresentation()
    {
        ApplyPlacementState(true);
    }

    public virtual string GetInteractionPrompt()
    {
        return $"E : {itemName} 들기";
    }

    public virtual bool CanInteract(GameObject interactor)
    {
        if (PlacementMode is CarryablePlacementMode.Held
            or CarryablePlacementMode.Consumed
            or CarryablePlacementMode.CreatureAttached)
        {
            return false;
        }

        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        return hotbar != null && hotbar.HasFreeSlot();
    }

    public virtual void Interact(GameObject interactor)
    {
        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        if (hotbar == null)
        {
            Debug.LogWarning("CarryableItem: 상호작용한 오브젝트에 PlayerHotbar가 없음");
            return;
        }
        hotbar.TryAddItem(this);
    }

    public virtual void OnUse(GameObject user, GameObject target)
    {
        Debug.Log($"[CarryableItem] {itemName} 사용함 (대상: {target.name})");
    }

    public virtual bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        if (user != null)
        {
            PlayerController controller = user.GetComponent<PlayerController>();
            if (controller != null)
                controller.PlayMotionState(Animator.StringToHash("MeleeWeapon"));
        }
        OnUse(user, user);
        return isConsumable;
    }

    public virtual void OnPrimaryHeld(GameObject user, Transform aimReference, bool isHeld) { }
    public virtual void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld) { }
}
