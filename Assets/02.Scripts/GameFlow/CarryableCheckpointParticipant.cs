using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Varco.GameFlow
{
    // Carryable 오브젝트, 플레이어 핫바, 잠수함 Item Zone, 생물 부착 관계를
    // 하나의 스냅샷 그래프로 캡처하고 복원한다.
    [DisallowMultipleComponent]
    public sealed class CarryableCheckpointParticipant : CheckpointParticipantBehaviour,
        ICheckpointRestoreValidator,
        ICheckpointRestoreStatus
    {
        private const int FirstItemSlot = 2;
        private const int LastItemSlot = 3;

        private IGameFlowNetworkBridge bridge;
        private readonly List<CarryableItem> items = new();
        private readonly List<PlayerHotbar> hotbars = new();
        private readonly List<SubmarineItemZone> zones = new();

        public override int RestoreOrder => 40;
        public bool CheckpointRestoreSucceeded { get; private set; } = true;
        public string CheckpointRestoreError { get; private set; }

        public void Initialize(IGameFlowNetworkBridge networkBridge)
        {
            bridge = networkBridge;
        }

        public override object CaptureCheckpointState()
        {
            var session = new CarryableSessionState();
            if (!TryBuildPlayerContexts(out Dictionary<string, PlayerContext> players, out string playerError))
            {
                session.CaptureError = playerError;
                return session;
            }

            session.PlayerKeys = new string[players.Count];
            players.Keys.CopyTo(session.PlayerKeys, 0);
            Array.Sort(session.PlayerKeys, StringComparer.Ordinal);

            RefreshItems();
            RefreshZones();

            var heldItems = new Dictionary<CarryableItem, HeldLocation>();
            foreach (PlayerContext player in players.Values)
            {
                for (int slot = FirstItemSlot; slot <= LastItemSlot; slot++)
                {
                    CarryableItem item = player.Hotbar.GetItemAtSlot(slot);
                    if (item == null)
                        continue;

                    if (!heldItems.TryAdd(item, new HeldLocation(player.Key, slot)))
                    {
                        session.CaptureError =
                            $"Carryable '{item.name}' is registered in more than one hotbar slot.";
                        return session;
                    }
                }
            }

            var itemStates = new List<CarryableState>(items.Count);
            var itemKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (CarryableItem item in items)
            {
                string itemKey = item.CheckpointSessionKey;
                if (string.IsNullOrWhiteSpace(itemKey) || !itemKeys.Add(itemKey))
                {
                    session.CaptureError = $"Duplicate or empty Carryable checkpoint key: {itemKey}";
                    return session;
                }

                var itemState = new CarryableState
                {
                    ItemKey = itemKey,
                    Mode = item.PlacementMode,
                    Position = item.transform.position,
                    Rotation = item.transform.rotation,
                    HotbarSlot = -1,
                    StoredSlot = -1
                };

                bool isHeld = itemState.Mode == CarryablePlacementMode.Held;
                if (heldItems.TryGetValue(item, out HeldLocation heldLocation))
                {
                    if (!isHeld)
                    {
                        session.CaptureError =
                            $"Carryable '{itemKey}' is in a hotbar but its mode is {itemState.Mode}.";
                        return session;
                    }

                    itemState.PlayerKey = heldLocation.PlayerKey;
                    itemState.HotbarSlot = heldLocation.Slot;
                }
                else if (isHeld)
                {
                    session.CaptureError =
                        $"Held Carryable '{itemKey}' is not registered in its owner's hotbar.";
                    return session;
                }

                if (itemState.Mode == CarryablePlacementMode.Stored)
                {
                    SubmarineItemZone zone = ResolveStoredZone(item);
                    if (zone == null)
                    {
                        session.CaptureError =
                            $"Stored Carryable '{itemKey}' has no matching Item Zone.";
                        return session;
                    }

                    itemState.ZoneKey = GetZoneKey(zone);
                    itemState.StoredSlot = item.StoredSlotIndex;
                }

                if (item is FishItem fish)
                {
                    itemState.HasFishLifecycle = true;
                    itemState.FishCollected = fish.IsCollected;
                }

                if (item is HarvestableCreature harvestable)
                {
                    itemState.HasHarvestableLifecycle = true;
                    itemState.CreaturePhase = harvestable.Phase;
                    itemState.AttachmentSlotType = harvestable.AttachmentSlot;

                    if (harvestable.Phase == HarvestableCreature.CreaturePhase.Attached)
                    {
                        PlayerContext attachedPlayer = FindPlayerForAttachment(players, harvestable.AttachedSlot);
                        if (attachedPlayer == null)
                        {
                            session.CaptureError =
                                $"Attached creature '{itemKey}' has no player in the fixed roster.";
                            return session;
                        }

                        itemState.AttachedPlayerKey = attachedPlayer.Key;
                    }
                }

                itemStates.Add(itemState);
            }

            itemStates.Sort((left, right) => string.CompareOrdinal(left.ItemKey, right.ItemKey));
            session.Items = itemStates.ToArray();
            return session;
        }

        public bool ValidateCheckpointState(object state, out string error)
        {
            error = null;
            if (state is not CarryableSessionState session)
            {
                error = "Snapshot type does not match CarryableSessionState.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(session.CaptureError))
            {
                error = session.CaptureError;
                return false;
            }

            if (!TryBuildPlayerContexts(out Dictionary<string, PlayerContext> players, out error))
                return false;
            if (!ValidateFixedPlayerRoster(session.PlayerKeys, players, out error))
                return false;

            if (!TryBuildCurrentItemMap(out Dictionary<string, CarryableItem> currentItems, out error))
                return false;
            if (!TryBuildZoneMap(out Dictionary<string, SubmarineItemZone> currentZones, out error))
                return false;

            CarryableState[] capturedItems = session.Items ?? Array.Empty<CarryableState>();
            if (capturedItems.Length != currentItems.Count)
            {
                error =
                    $"Carryable roster changed (captured {capturedItems.Length}, current {currentItems.Count}).";
                return false;
            }

            var capturedKeys = new HashSet<string>(StringComparer.Ordinal);
            var occupiedHotbarSlots = new HashSet<string>(StringComparer.Ordinal);
            var occupiedZoneSlots = new HashSet<string>(StringComparer.Ordinal);
            var occupiedAttachmentSlots = new HashSet<string>(StringComparer.Ordinal);

            foreach (CarryableState itemState in capturedItems)
            {
                if (itemState == null
                    || string.IsNullOrWhiteSpace(itemState.ItemKey)
                    || !capturedKeys.Add(itemState.ItemKey))
                {
                    error = $"Duplicate or empty captured Carryable key: {itemState?.ItemKey}";
                    return false;
                }

                if (!currentItems.TryGetValue(itemState.ItemKey, out CarryableItem item))
                {
                    error = $"Captured Carryable is missing from the session: {itemState.ItemKey}.";
                    return false;
                }

                if (!Enum.IsDefined(typeof(CarryablePlacementMode), itemState.Mode))
                {
                    error = $"Carryable '{itemState.ItemKey}' has an invalid placement mode.";
                    return false;
                }

                bool isFish = item is FishItem;
                bool isHarvestable = item is HarvestableCreature;
                if (itemState.HasFishLifecycle != isFish
                    || itemState.HasHarvestableLifecycle != isHarvestable)
                {
                    error = $"Carryable lifecycle type changed for '{itemState.ItemKey}'.";
                    return false;
                }

                switch (itemState.Mode)
                {
                    case CarryablePlacementMode.Held:
                        if (!players.ContainsKey(itemState.PlayerKey)
                            || itemState.HotbarSlot is < FirstItemSlot or > LastItemSlot)
                        {
                            error = $"Held Carryable '{itemState.ItemKey}' has an invalid owner or slot.";
                            return false;
                        }

                        if (!occupiedHotbarSlots.Add(
                                $"{itemState.PlayerKey}:{itemState.HotbarSlot}"))
                        {
                            error =
                                $"More than one Carryable targets player {itemState.PlayerKey} "
                                + $"hotbar slot {itemState.HotbarSlot}.";
                            return false;
                        }
                        break;

                    case CarryablePlacementMode.Stored:
                        if (string.IsNullOrWhiteSpace(itemState.ZoneKey)
                            || !currentZones.TryGetValue(itemState.ZoneKey, out SubmarineItemZone zone)
                            || itemState.StoredSlot < 0
                            || itemState.StoredSlot >= zone.SlotCount)
                        {
                            error = $"Stored Carryable '{itemState.ItemKey}' has an invalid zone or slot.";
                            return false;
                        }

                        if (!occupiedZoneSlots.Add($"{itemState.ZoneKey}:{itemState.StoredSlot}"))
                        {
                            error =
                                $"More than one Carryable targets Item Zone slot "
                                + $"{itemState.ZoneKey}:{itemState.StoredSlot}.";
                            return false;
                        }
                        break;

                    case CarryablePlacementMode.CreatureAttached:
                        if (!isHarvestable
                            || itemState.CreaturePhase != HarvestableCreature.CreaturePhase.Attached
                            || !players.TryGetValue(itemState.AttachedPlayerKey, out PlayerContext player)
                            || player.AttachmentSlot == null
                            || player.AttachmentSlot.GetAnchor(itemState.AttachmentSlotType) == null)
                        {
                            error =
                                $"Attached creature '{itemState.ItemKey}' has an invalid player or anchor.";
                            return false;
                        }

                        if (((HarvestableCreature)item).AttachmentSlot
                            != itemState.AttachmentSlotType)
                        {
                            error = $"Attachment slot type changed for '{itemState.ItemKey}'.";
                            return false;
                        }

                        if (!occupiedAttachmentSlots.Add(
                                $"{itemState.AttachedPlayerKey}:{itemState.AttachmentSlotType}"))
                        {
                            error =
                                $"More than one creature targets the same attachment slot on "
                                + $"player {itemState.AttachedPlayerKey}.";
                            return false;
                        }
                        break;
                }

                if (isHarvestable)
                {
                    bool phaseIsAttached = itemState.CreaturePhase
                        == HarvestableCreature.CreaturePhase.Attached;
                    if (phaseIsAttached
                        != (itemState.Mode == CarryablePlacementMode.CreatureAttached))
                    {
                        error =
                            $"Creature placement and lifecycle phase disagree for '{itemState.ItemKey}'.";
                        return false;
                    }
                }

                if (isFish
                    && !itemState.FishCollected
                    && itemState.Mode != CarryablePlacementMode.WorldInitial)
                {
                    error = $"Wild fish '{itemState.ItemKey}' is not in its initial world mode.";
                    return false;
                }
            }

            return true;
        }

        public override void PrepareForCheckpointRestore()
        {
            RefreshItems();
            foreach (CarryableItem item in items)
                item?.PrepareForCheckpointRestore();

            RefreshHotbars();
            foreach (PlayerHotbar hotbar in hotbars)
                hotbar?.ClearItemsForCheckpointRestore();

            RefreshZones();
            foreach (SubmarineItemZone zone in zones)
                zone?.ClearForCheckpointRestore();
        }

        public override void RestoreCheckpointState(object state)
        {
            CheckpointRestoreSucceeded = false;
            CheckpointRestoreError = null;

            if (!ValidateCheckpointState(state, out string validationError))
            {
                CheckpointRestoreError = validationError;
                return;
            }

            var session = (CarryableSessionState)state;
            if (!TryBuildPlayerContexts(
                    out Dictionary<string, PlayerContext> players,
                    out string playerError))
            {
                CheckpointRestoreError = playerError;
                return;
            }

            if (!TryBuildCurrentItemMap(
                    out Dictionary<string, CarryableItem> currentItems,
                    out string itemError))
            {
                CheckpointRestoreError = itemError;
                return;
            }

            if (!TryBuildZoneMap(
                    out Dictionary<string, SubmarineItemZone> currentZones,
                    out string zoneError))
            {
                CheckpointRestoreError = zoneError;
                return;
            }

            foreach (CarryableState itemState in session.Items)
            {
                CarryableItem item = currentItems[itemState.ItemKey];

                bool restored = itemState.Mode switch
                {
                    CarryablePlacementMode.WorldInitial => item.RestoreWorldFromCheckpoint(
                        CarryablePlacementMode.WorldInitial,
                        itemState.Position,
                        itemState.Rotation),
                    CarryablePlacementMode.WorldDropped => item.RestoreWorldFromCheckpoint(
                        CarryablePlacementMode.WorldDropped,
                        itemState.Position,
                        itemState.Rotation),
                    CarryablePlacementMode.Held => item.RestoreHeldFromCheckpoint(
                        players[itemState.PlayerKey].Hotbar,
                        itemState.HotbarSlot),
                    CarryablePlacementMode.Stored => item.RestoreStoredFromCheckpoint(
                        currentZones[itemState.ZoneKey],
                        itemState.StoredSlot),
                    CarryablePlacementMode.Consumed => item.RestoreConsumedFromCheckpoint(),
                    CarryablePlacementMode.CreatureAttached => item.RestoreCreatureAttachedFromCheckpoint(
                        players[itemState.AttachedPlayerKey].AttachmentSlot),
                    _ => false
                };

                if (!restored)
                {
                    CheckpointRestoreError =
                        $"Carryable '{itemState.ItemKey}' rejected its checkpoint placement "
                        + $"({itemState.Mode}).";
                    return;
                }

                // World 배치가 호출하는 일반 드롭 처리는 생물을 Collectible로 만들 수 있다.
                // 따라서 배치를 먼저 끝낸 뒤 체크포인트 생명주기를 최종 상태로 덮어쓴다.
                if (itemState.Mode == CarryablePlacementMode.CreatureAttached)
                    continue;

                if (item is FishItem fish)
                    fish.RestoreCheckpointCollectedState(itemState.FishCollected);

                if (item is HarvestableCreature harvestable)
                    harvestable.RestoreCheckpointPhase(itemState.CreaturePhase, null);
            }

            CheckpointRestoreSucceeded = true;
        }

        public override void CompleteCheckpointRestore()
        {
            if (!CheckpointRestoreSucceeded)
                return;

            RefreshItems();
            foreach (CarryableItem item in items)
                item?.RefreshCheckpointPresentation();

            RefreshHotbars();
            foreach (PlayerHotbar hotbar in hotbars)
                hotbar?.RefreshRestoredVisibility();
        }

        private bool TryBuildPlayerContexts(
            out Dictionary<string, PlayerContext> result,
            out string error)
        {
            result = new Dictionary<string, PlayerContext>(StringComparer.Ordinal);
            error = null;
            if (bridge == null || bridge.Players == null)
            {
                error = "GameFlow network bridge or player roster is unavailable.";
                return false;
            }

            RefreshHotbars();
            var assignedHotbars = new HashSet<PlayerHotbar>();
            foreach (IPlayerCheckpointParticipant participant in bridge.Players)
            {
                if (participant == null || string.IsNullOrWhiteSpace(participant.PlayerKey))
                {
                    error = "Fixed player roster contains a null player or empty key.";
                    return false;
                }

                if (result.ContainsKey(participant.PlayerKey))
                {
                    error = $"Duplicate player key in fixed roster: {participant.PlayerKey}.";
                    return false;
                }

                PlayerHotbar hotbar = ResolvePlayerHotbar(participant);
                if (hotbar == null || !assignedHotbars.Add(hotbar))
                {
                    error = $"Player {participant.PlayerKey} has no unique PlayerHotbar.";
                    return false;
                }

                AttachmentSlot attachmentSlot = hotbar.GetComponent<AttachmentSlot>()
                    ?? hotbar.GetComponentInChildren<AttachmentSlot>(true)
                    ?? hotbar.GetComponentInParent<AttachmentSlot>();
                result.Add(
                    participant.PlayerKey,
                    new PlayerContext(participant.PlayerKey, hotbar, attachmentSlot));
            }

            return true;
        }

        private PlayerHotbar ResolvePlayerHotbar(IPlayerCheckpointParticipant participant)
        {
            if (participant is Component component)
            {
                PlayerHotbar componentHotbar = component.GetComponent<PlayerHotbar>()
                    ?? component.GetComponentInChildren<PlayerHotbar>(true)
                    ?? component.GetComponentInParent<PlayerHotbar>();
                if (componentHotbar != null)
                    return componentHotbar;
            }

            // NetworkObject가 없는 로컬 어댑터는 플레이어 GameObject InstanceID를 키로 쓴다.
            foreach (PlayerHotbar hotbar in hotbars)
            {
                if (hotbar != null
                    && hotbar.gameObject.GetInstanceID().ToString() == participant.PlayerKey)
                {
                    return hotbar;
                }
            }

            return null;
        }

        private static bool ValidateFixedPlayerRoster(
            string[] capturedPlayerKeys,
            Dictionary<string, PlayerContext> currentPlayers,
            out string error)
        {
            error = null;
            capturedPlayerKeys ??= Array.Empty<string>();
            if (capturedPlayerKeys.Length != currentPlayers.Count)
            {
                error =
                    $"Player roster changed (captured {capturedPlayerKeys.Length}, "
                    + $"current {currentPlayers.Count}).";
                return false;
            }

            var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in capturedPlayerKeys)
            {
                if (string.IsNullOrWhiteSpace(key)
                    || !uniqueKeys.Add(key)
                    || !currentPlayers.ContainsKey(key))
                {
                    error = $"Captured player roster does not match current fixed roster: {key}.";
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildCurrentItemMap(
            out Dictionary<string, CarryableItem> result,
            out string error)
        {
            RefreshItems();
            result = new Dictionary<string, CarryableItem>(StringComparer.Ordinal);
            error = null;
            foreach (CarryableItem item in items)
            {
                string key = item.CheckpointSessionKey;
                if (string.IsNullOrWhiteSpace(key) || !result.TryAdd(key, item))
                {
                    error = $"Duplicate or empty current Carryable key: {key}.";
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildZoneMap(
            out Dictionary<string, SubmarineItemZone> result,
            out string error)
        {
            RefreshZones();
            result = new Dictionary<string, SubmarineItemZone>(StringComparer.Ordinal);
            error = null;
            foreach (SubmarineItemZone zone in zones)
            {
                string key = GetZoneKey(zone);
                if (string.IsNullOrWhiteSpace(key) || !result.TryAdd(key, zone))
                {
                    error = $"Duplicate or empty Item Zone key: {key}.";
                    return false;
                }
            }

            return true;
        }

        private SubmarineItemZone ResolveStoredZone(CarryableItem item)
        {
            if (item.StoredSubmarineNetworkId.IsValid)
            {
                foreach (SubmarineItemZone zone in zones)
                {
                    if (zone != null
                        && zone.SubmarineId.IsValid
                        && zone.SubmarineId == item.StoredSubmarineNetworkId)
                    {
                        return zone;
                    }
                }
            }

            foreach (SubmarineItemZone zone in zones)
            {
                if (zone != null && item.IsStoredIn(zone))
                    return zone;
            }

            return null;
        }

        private static PlayerContext FindPlayerForAttachment(
            Dictionary<string, PlayerContext> players,
            AttachmentSlot attachedSlot)
        {
            if (attachedSlot == null)
                return null;

            foreach (PlayerContext player in players.Values)
            {
                if (player.AttachmentSlot == attachedSlot
                    || player.Hotbar.transform.root == attachedSlot.transform.root)
                {
                    return player;
                }
            }

            return null;
        }

        private static string GetZoneKey(SubmarineItemZone zone)
        {
            if (zone == null)
                return null;
            if (zone.SubmarineId.IsValid)
                return $"network:{zone.SubmarineId}";
            return $"scene:{BuildHierarchyKey(zone.transform)}";
        }

        private static string BuildHierarchyKey(Transform target)
        {
            if (target == null)
                return "missing";

            string path = $"{target.name}[{target.GetSiblingIndex()}]";
            Transform parent = target.parent;
            while (parent != null)
            {
                path = $"{parent.name}[{parent.GetSiblingIndex()}]/{path}";
                parent = parent.parent;
            }

            return $"{target.gameObject.scene.handle}:{path}";
        }

        private void RefreshItems()
        {
            items.Clear();
            items.AddRange(UnityEngine.Object.FindObjectsByType<CarryableItem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
        }

        private void RefreshHotbars()
        {
            hotbars.Clear();
            hotbars.AddRange(UnityEngine.Object.FindObjectsByType<PlayerHotbar>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
        }

        private void RefreshZones()
        {
            zones.Clear();
            zones.AddRange(UnityEngine.Object.FindObjectsByType<SubmarineItemZone>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
        }

        private sealed class PlayerContext
        {
            public PlayerContext(string key, PlayerHotbar hotbar, AttachmentSlot attachmentSlot)
            {
                Key = key;
                Hotbar = hotbar;
                AttachmentSlot = attachmentSlot;
            }

            public string Key { get; }
            public PlayerHotbar Hotbar { get; }
            public AttachmentSlot AttachmentSlot { get; }
        }

        private readonly struct HeldLocation
        {
            public HeldLocation(string playerKey, int slot)
            {
                PlayerKey = playerKey;
                Slot = slot;
            }

            public string PlayerKey { get; }
            public int Slot { get; }
        }

        [Serializable]
        private sealed class CarryableSessionState
        {
            public string CaptureError;
            public string[] PlayerKeys = Array.Empty<string>();
            public CarryableState[] Items = Array.Empty<CarryableState>();
        }

        [Serializable]
        private sealed class CarryableState
        {
            public string ItemKey;
            public CarryablePlacementMode Mode;
            public Vector3 Position;
            public Quaternion Rotation;
            public string PlayerKey;
            public int HotbarSlot;
            public string ZoneKey;
            public int StoredSlot;
            public bool HasFishLifecycle;
            public bool FishCollected;
            public bool HasHarvestableLifecycle;
            public HarvestableCreature.CreaturePhase CreaturePhase;
            public string AttachedPlayerKey;
            public AttachmentSlotType AttachmentSlotType;
        }
    }
}
