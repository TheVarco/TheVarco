using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 잠수함 내부에서 내려놓은 CarryableItem을 일정한 슬롯에 고정하는 보관 구역.
/// 슬롯 점유의 최종 결정은 아이템의 State Authority가 수행하고, 각 피어는
/// CarryableItem에 복제된 Zone/슬롯 번호로 동일한 로컬 부모 관계를 구성한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SubmarineItemZone : MonoBehaviour
{
    private static readonly HashSet<SubmarineItemZone> ActiveZones = new HashSet<SubmarineItemZone>();

    [Header("슬롯 설정")]
    [SerializeField] private BoxCollider slotBounds;
    [SerializeField, Min(1)] private int slotCount = 7;
    [SerializeField, Min(0f)] private float endPadding = 0.1f;
    [SerializeField] private Vector3 defaultItemRotation = new Vector3(0f, 90f, 0f);

    [Header("잠수함 내부 판정")]
    [SerializeField] private PlayerWalkZone walkZone;

    private readonly Dictionary<int, CarryableItem> occupants = new Dictionary<int, CarryableItem>();
    private readonly List<Collider> hullColliders = new List<Collider>();
    private NetworkObject submarineObject;

    public int SlotCount => Mathf.Max(1, slotCount);
    public Vector3 DefaultItemRotation => defaultItemRotation;
    public NetworkObject SubmarineObject => submarineObject;
    public NetworkRunner NetworkRunner => submarineObject != null ? submarineObject.Runner : null;
    public NetworkId SubmarineId => submarineObject != null && submarineObject.IsValid
        ? submarineObject.Id
        : default;

    private void Awake()
    {
        submarineObject = GetComponentInParent<NetworkObject>();
        if (walkZone == null)
            walkZone = GetComponentInParent<SubmarineController>()
                ?.GetComponentInChildren<PlayerWalkZone>(true);
        if (slotBounds == null)
            slotBounds = GetComponentInChildren<BoxCollider>(true);

        CacheHullColliders();
    }

    private void OnEnable() => ActiveZones.Add(this);

    private void OnDisable()
    {
        ActiveZones.Remove(this);
        occupants.Clear();
    }

    public bool ContainsPlayer(PlayerController player)
    {
        return player != null && walkZone != null && walkZone.ContainsPlayer(player);
    }

    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        if (slotBounds == null || !slotBounds.enabled)
            return false;

        Vector3 closest = slotBounds.ClosestPoint(worldPoint);
        return (closest - worldPoint).sqrMagnitude <= 0.0001f;
    }

    public bool TryGetFirstFreeSlot(out int slotIndex)
    {
        CleanupOccupants();
        for (int i = 0; i < SlotCount; i++)
        {
            if (!occupants.ContainsKey(i))
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>State Authority 또는 비네트워크 로컬 실행에서 슬롯을 선점한다.</summary>
    public bool TryReserve(CarryableItem item, out int slotIndex)
    {
        slotIndex = -1;
        if (item == null || !TryGetFirstFreeSlot(out slotIndex))
            return false;

        occupants[slotIndex] = item;
        return true;
    }

    /// <summary>체크포인트가 저장된 정확한 칸을 복원할 때 사용한다.</summary>
    public bool TryReserveExact(CarryableItem item, int slotIndex)
    {
        CleanupOccupants();
        if (item == null || slotIndex < 0 || slotIndex >= SlotCount)
            return false;

        if (occupants.TryGetValue(slotIndex, out CarryableItem occupant)
            && occupant != null && occupant != item)
        {
            return false;
        }

        Release(item);
        occupants[slotIndex] = item;
        return true;
    }

    /// <summary>복원 트랜잭션이 저장된 점유표를 다시 구성하기 전에 로컬 캐시를 비운다.</summary>
    public void ClearForCheckpointRestore()
    {
        occupants.Clear();
    }

    public CarryableItem GetOccupant(int slotIndex)
    {
        CleanupOccupants();
        return occupants.TryGetValue(slotIndex, out CarryableItem item) ? item : null;
    }

    /// <summary>복제 상태를 받은 피어가 로컬 점유표를 맞출 때 사용한다.</summary>
    public void RegisterStoredItem(CarryableItem item, int slotIndex)
    {
        if (item == null || slotIndex < 0 || slotIndex >= SlotCount)
            return;

        // 같은 아이템이 이전 슬롯에 남아 있으면 제거한다.
        Release(item);
        occupants[slotIndex] = item;
    }

    public void Release(CarryableItem item)
    {
        if (item == null || occupants.Count == 0)
            return;

        int found = -1;
        foreach (KeyValuePair<int, CarryableItem> pair in occupants)
        {
            if (pair.Value == item)
            {
                found = pair.Key;
                break;
            }
        }

        if (found >= 0)
            occupants.Remove(found);
    }

    public Vector3 GetSlotLocalPosition(int slotIndex)
    {
        if (slotBounds == null)
            return Vector3.zero;

        int count = SlotCount;
        slotIndex = Mathf.Clamp(slotIndex, 0, count - 1);

        Bounds localBounds = new Bounds(slotBounds.center, slotBounds.size);
        float halfLength = Mathf.Max(0f, localBounds.extents.z - endPadding);
        float t = count <= 1 ? 0.5f : slotIndex / (float)(count - 1);
        Vector3 pointInCollider = localBounds.center;
        pointInCollider.z = Mathf.Lerp(localBounds.center.z + halfLength,
            localBounds.center.z - halfLength, t);

        Vector3 worldPoint = slotBounds.transform.TransformPoint(pointInCollider);
        return transform.InverseTransformPoint(worldPoint);
    }

    public Vector3 GetSlotWorldPosition(int slotIndex)
    {
        return transform.TransformPoint(GetSlotLocalPosition(slotIndex));
    }

    public IReadOnlyList<Collider> HullColliders => hullColliders;

    private void CacheHullColliders()
    {
        hullColliders.Clear();
        SubmarineController submarine = GetComponentInParent<SubmarineController>();
        if (submarine == null)
            return;

        foreach (Collider candidate in submarine.GetComponentsInChildren<Collider>(true))
        {
            if (candidate == null || candidate == slotBounds || candidate.isTrigger)
                continue;
            hullColliders.Add(candidate);
        }
    }

    private void CleanupOccupants()
    {
        if (occupants.Count == 0)
            return;

        var invalid = new List<int>();
        foreach (KeyValuePair<int, CarryableItem> pair in occupants)
        {
            if (pair.Value == null || !pair.Value.IsStoredIn(this) || pair.Key >= SlotCount)
                invalid.Add(pair.Key);
        }

        foreach (int key in invalid)
            occupants.Remove(key);
    }

    public static SubmarineItemZone FindForPlayer(PlayerController player)
    {
        if (player == null)
            return null;

        NetworkRunner playerRunner = player.Object != null && player.Object.IsValid ? player.Runner : null;
        foreach (SubmarineItemZone zone in ActiveZones)
        {
            if (zone != null
                && (playerRunner == null || zone.NetworkRunner == playerRunner)
                && zone.ContainsPlayer(player))
                return zone;
        }
        return null;
    }

    public static SubmarineItemZone FindBySubmarineId(NetworkId id, NetworkRunner runner = null)
    {
        if (!id.IsValid)
            return null;

        foreach (SubmarineItemZone zone in ActiveZones)
        {
            if (zone != null
                && (runner == null || zone.NetworkRunner == runner)
                && zone.SubmarineId.IsValid
                && zone.SubmarineId == id)
                return zone;
        }
        return null;
    }

    public static SubmarineItemZone FindContainingPoint(Vector3 worldPoint, NetworkRunner runner = null)
    {
        foreach (SubmarineItemZone zone in ActiveZones)
        {
            if (zone != null
                && (runner == null || zone.NetworkRunner == runner)
                && zone.ContainsWorldPoint(worldPoint))
                return zone;
        }
        return null;
    }

    private void OnValidate()
    {
        slotCount = Mathf.Max(1, slotCount);
        endPadding = Mathf.Max(0f, endPadding);
        if (slotBounds == null)
            slotBounds = GetComponentInChildren<BoxCollider>(true);
        if (walkZone == null)
            walkZone = GetComponentInParent<SubmarineController>()
                ?.GetComponentInChildren<PlayerWalkZone>(true);
    }

    private void OnDrawGizmosSelected()
    {
        if (slotBounds == null)
            return;

        Gizmos.color = new Color(1f, 0.82f, 0.12f, 0.8f);
        for (int i = 0; i < SlotCount; i++)
        {
            Vector3 position = GetSlotWorldPosition(i);
            Gizmos.DrawWireSphere(position, 0.09f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(position + Vector3.up * 0.12f, (i + 1).ToString());
#endif
            if (i > 0)
                Gizmos.DrawLine(GetSlotWorldPosition(i - 1), position);
        }
    }
}
