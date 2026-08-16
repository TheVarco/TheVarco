using Fusion;
using UnityEngine;

// 슬롯 1은 맨손, 슬롯 2/3은 CarryableItem을 보관한다.
// 네트워크 세션에서는 State Authority가 슬롯 내용을 확정하고, Input Authority는
// 선택 슬롯만 로컬에서 즉시 예측한 뒤 호스트에 게시한다.
public class PlayerHotbar : NetworkBehaviour
{
    [Tooltip("아이템이 손에 위치할 지점 (Player 자식으로 만들어 연결)")]
    public Transform handSocket;
    [Tooltip("좌클릭 - 활성 아이템의 OnPrimaryAction으로 그대로 전달됨")]
    public KeyCode primaryActionKey = KeyCode.Mouse0;
    [Tooltip("우클릭 - 활성 아이템의 OnSecondaryHeld로 그대로 전달됨")]
    public KeyCode secondaryActionKey = KeyCode.Mouse1;
    public KeyCode dropKey = KeyCode.G;
    [Tooltip("무기 아이템에게 조준 방향/위치 기준으로 넘겨줄 Transform (보통 CameraRig)")]
    public Transform aimReference;
    [Tooltip("Item Zone 상태 안내를 표시할 상호작용기 (미지정 시 자동 탐색)")]
    public PlayerInteractor interactor;

    [Header("내려놓기 설정")]
    [Tooltip("몸(Collider)과 안 겹치도록, 내려놓을 때 정면으로 얼마나 떨어뜨릴지")]
    public float dropDistance = 1.2f;
    public float dropHeightOffset = 0f;

    private readonly CarryableItem[] localItemSlots = new CarryableItem[2];
    private int localActiveSlot = 1;

    [Networked] private NetworkId Slot2ItemId { get; set; }
    [Networked] private NetworkId Slot3ItemId { get; set; }
    [Networked] private int NetworkedActiveSlot { get; set; }
    [Networked, OnChangedRender(nameof(OnHotbarRevisionChanged))]
    private int HotbarRevision { get; set; }

    private bool IsNetworkActive => Object != null && Object.IsValid;

    public int ActiveSlot => IsNetworkActive && !Object.HasInputAuthority
        ? NormalizeSlot(NetworkedActiveSlot)
        : NormalizeSlot(localActiveSlot);

    private void Start()
    {
        if (interactor == null)
            interactor = GetComponent<PlayerInteractor>();
        AttachHandSocketToRightHand();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority && (NetworkedActiveSlot < 1 || NetworkedActiveSlot > 3))
        {
            NetworkedActiveSlot = 1;
            HotbarRevision++;
        }

        localActiveSlot = NormalizeSlot(NetworkedActiveSlot);
        RefreshRestoredVisibility();
    }

    public void AttachHandSocketToRightHand()
    {
        if (handSocket == null)
            return;

        Animator anim = GetComponentInChildren<Animator>();
        if (anim == null)
            anim = GetComponentInParent<Animator>();
        if (anim == null)
            return;

        Transform rightHandBone = anim.isHuman
            ? anim.GetBoneTransform(HumanBodyBones.RightHand)
            : null;

        if (rightHandBone == null)
        {
            foreach (Transform candidate in anim.GetComponentsInChildren<Transform>(true))
            {
                string name = candidate.name.ToLowerInvariant();
                if (!name.Contains("righthand")
                    && !name.Contains("hand_r")
                    && !name.Contains("hand.r")
                    && !name.Contains("hand_right")
                    && !name.Contains("right_hand"))
                {
                    continue;
                }

                rightHandBone = candidate;
                break;
            }
        }

        if (rightHandBone == null)
            return;

        handSocket.SetParent(rightHandBone, false);
        handSocket.localPosition = Vector3.zero;
        handSocket.localRotation = Quaternion.identity;

        // Spawned가 Start보다 먼저 Held 배치를 적용한 경우 손 소켓의 새 본 스케일을
        // 기준으로 위치와 월드 스케일 보정을 한 번 더 계산한다.
        GetItemAtSlot(2)?.RefreshCheckpointPresentation();
        GetItemAtSlot(3)?.RefreshCheckpointPresentation();
    }

    private void Update()
    {
        if (IsNetworkActive && !Object.HasInputAuthority)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            SwitchTo(1);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            SwitchTo(2);
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            SwitchTo(3);

        CarryableItem active = GetActiveItem();
        if (active == null)
            return;

        if (Input.GetKeyDown(primaryActionKey))
        {
            bool shouldRemove = active.OnPrimaryAction(gameObject, aimReference);
            if (shouldRemove)
                RemoveActiveItem();
        }

        active.OnPrimaryHeld(gameObject, aimReference, Input.GetKey(primaryActionKey));
        active.OnSecondaryHeld(gameObject, aimReference, Input.GetKey(secondaryActionKey));

        if (Input.GetKeyDown(dropKey))
            DropActiveItem();
    }

    private void OnDisable()
    {
        CarryableItem active = GetActiveItem();
        if (active == null)
            return;

        active.OnPrimaryHeld(gameObject, aimReference, false);
        active.OnSecondaryHeld(gameObject, aimReference, false);
    }

    private void SwitchTo(int slot)
    {
        slot = NormalizeSlot(slot);
        if (slot == ActiveSlot)
            return;

        CarryableItem previous = GetActiveItem();
        if (previous != null)
        {
            previous.OnPrimaryHeld(gameObject, aimReference, false);
            previous.OnSecondaryHeld(gameObject, aimReference, false);
        }

        localActiveSlot = slot;
        RefreshRestoredVisibility();

        if (!IsNetworkActive)
            return;

        if (Object.HasStateAuthority)
            SetAuthorityActiveSlot(slot);
        else
            RPC_RequestActiveSlot(slot);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestActiveSlot(int slot)
    {
        SetAuthorityActiveSlot(slot);
    }

    private void SetAuthorityActiveSlot(int slot)
    {
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        slot = NormalizeSlot(slot);
        NetworkedActiveSlot = slot;
        localActiveSlot = slot;
        CommitHotbarState();
    }

    public void SelectSlotFromAuthority(int slot)
    {
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;
        SetAuthorityActiveSlot(slot);
    }

    public bool TryAddItem(CarryableItem item)
    {
        if (item == null || !HasFreeSlot())
            return false;

        if (IsNetworkActive && item.Object != null && item.Object.IsValid)
        {
            item.RequestPickup(Object.Id);
            return true;
        }

        if (!TryReserveItemFromAuthority(item, out int slot))
            return false;

        if (!item.RestoreHeldFromCheckpoint(this, slot))
        {
            RemoveItemFromAuthority(item);
            return false;
        }

        SwitchTo(slot);
        return true;
    }

    public void RegisterPickedUpItem(CarryableItem item)
    {
        if (item == null)
            return;

        if (!IsNetworkActive && GetSlotOfItem(item) < 0
            && TryReserveItemFromAuthority(item, out int slot))
        {
            localActiveSlot = slot;
        }

        RefreshRestoredVisibility();
    }

    public bool HasFreeSlot()
    {
        return GetItemAtSlot(2) == null || GetItemAtSlot(3) == null;
    }

    public CarryableItem GetActiveItem()
    {
        return ActiveSlot == 1 ? null : GetItemAtSlot(ActiveSlot);
    }

    public CarryableItem GetItemAtSlot(int slotNumber)
    {
        if (slotNumber is < 2 or > 3)
            return null;

        if (!IsNetworkActive)
            return localItemSlots[slotNumber - 2];

        NetworkId id = GetNetworkSlotId(slotNumber);
        if (!id.IsValid || Runner == null || !Runner.TryFindObject(id, out NetworkObject itemObject))
            return null;

        return itemObject.GetComponent<CarryableItem>();
    }

    public int GetSlotOfItem(CarryableItem item)
    {
        if (item == null)
            return -1;

        if (IsNetworkActive && item.Object != null && item.Object.IsValid)
        {
            NetworkId id = item.Object.Id;
            if (Slot2ItemId == id) return 2;
            if (Slot3ItemId == id) return 3;
            return -1;
        }

        if (localItemSlots[0] == item) return 2;
        if (localItemSlots[1] == item) return 3;
        return -1;
    }

    public bool IsItemInActiveSlot(CarryableItem item)
    {
        int slot = GetSlotOfItem(item);
        int selected = IsNetworkActive ? NormalizeSlot(NetworkedActiveSlot) : ActiveSlot;
        return slot >= 2 && slot == selected;
    }

    public bool IsSlotActiveForPresentation(int slot)
    {
        int selected = IsNetworkActive && !Object.HasInputAuthority
            ? NormalizeSlot(NetworkedActiveSlot)
            : ActiveSlot;
        return slot == selected;
    }

    public bool TryReserveItemFromAuthority(CarryableItem item, out int slotNumber)
    {
        slotNumber = GetSlotOfItem(item);
        if (slotNumber >= 2)
            return true;
        if (item == null || IsNetworkActive && !Object.HasStateAuthority)
            return false;

        for (int slot = 2; slot <= 3; slot++)
        {
            if (GetItemAtSlot(slot) != null || IsNetworkActive && GetNetworkSlotId(slot).IsValid)
                continue;

            if (!SetSlotFromAuthority(slot, item))
                return false;

            slotNumber = slot;
            return true;
        }

        return false;
    }

    public bool RestoreItemToExactSlot(int slotNumber, CarryableItem item)
    {
        if (slotNumber is < 2 or > 3 || item == null)
            return false;
        if (IsNetworkActive && !Object.HasStateAuthority)
            return false;

        CarryableItem occupant = GetItemAtSlot(slotNumber);
        if (occupant != null && occupant != item)
            return false;

        int previousSlot = GetSlotOfItem(item);
        if (previousSlot >= 2 && previousSlot != slotNumber)
            return false;

        return SetSlotFromAuthority(slotNumber, item);
    }

    public bool RemoveItemFromAuthority(CarryableItem item)
    {
        int slot = GetSlotOfItem(item);
        if (slot < 2 || IsNetworkActive && !Object.HasStateAuthority)
            return false;

        if (IsNetworkActive)
            SetNetworkSlotId(slot, default);
        else
            localItemSlots[slot - 2] = null;

        CommitHotbarState();
        return true;
    }

    public void ClearItemsForCheckpointRestore()
    {
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        if (IsNetworkActive)
        {
            Slot2ItemId = default;
            Slot3ItemId = default;
        }
        else
        {
            localItemSlots[0] = null;
            localItemSlots[1] = null;
        }

        CommitHotbarState();
    }

    public void RefreshRestoredVisibility()
    {
        GetItemAtSlot(2)?.RefreshHeldVisibility();
        GetItemAtSlot(3)?.RefreshHeldVisibility();
    }

    public void RemoveActiveItem()
    {
        CarryableItem item = GetActiveItem();
        if (item == null)
            return;

        item.OnPrimaryHeld(gameObject, aimReference, false);
        item.OnSecondaryHeld(gameObject, aimReference, false);

        NetworkId requester = IsNetworkActive ? Object.Id : default;
        item.RequestConsume(requester, ActiveSlot);
    }

    public void DropActiveItem()
    {
        CarryableItem item = GetActiveItem();
        if (item == null)
            return;

        item.OnPrimaryHeld(gameObject, aimReference, false);
        item.OnSecondaryHeld(gameObject, aimReference, false);

        NetworkId requester = IsNetworkActive ? Object.Id : default;
        item.RequestRelease(requester, ActiveSlot);
    }

    public Vector3 GetAuthorityDropPosition()
    {
        return transform.position
            + transform.forward * dropDistance
            + Vector3.up * dropHeightOffset;
    }

    private bool SetSlotFromAuthority(int slotNumber, CarryableItem item)
    {
        if (slotNumber is < 2 or > 3 || item == null)
            return false;

        if (IsNetworkActive)
        {
            if (item.Object == null || !item.Object.IsValid)
                return false;
            SetNetworkSlotId(slotNumber, item.Object.Id);
        }
        else
        {
            localItemSlots[slotNumber - 2] = item;
        }

        CommitHotbarState();
        return true;
    }

    private NetworkId GetNetworkSlotId(int slotNumber)
    {
        return slotNumber == 2 ? Slot2ItemId : Slot3ItemId;
    }

    private void SetNetworkSlotId(int slotNumber, NetworkId id)
    {
        if (slotNumber == 2)
            Slot2ItemId = id;
        else
            Slot3ItemId = id;
    }

    private void CommitHotbarState()
    {
        if (IsNetworkActive)
            HotbarRevision++;
        RefreshRestoredVisibility();
    }

    private void OnHotbarRevisionChanged()
    {
        localActiveSlot = NormalizeSlot(NetworkedActiveSlot);
        RefreshRestoredVisibility();
    }

    private static int NormalizeSlot(int slot)
    {
        return slot is >= 1 and <= 3 ? slot : 1;
    }

    public void SpawnProjectile(NetworkPrefabRef prefab, Vector3 position, Vector3 direction)
    {
        if (Object == null)
            return;
        RPC_SpawnProjectile(prefab, position, direction);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SpawnProjectile(NetworkPrefabRef prefab, Vector3 position, Vector3 direction)
    {
        Runner.Spawn(prefab, position, Quaternion.LookRotation(direction), Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile projectile = obj.GetComponent<Projectile>();
                if (projectile != null)
                    projectile.owner = gameObject;

                RopeProjectile rope = obj.GetComponent<RopeProjectile>();
                if (rope != null)
                    rope.InitOwner(Object.Id);
            });
    }
}
