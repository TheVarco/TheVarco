using System.Collections.Generic;
using UnityEngine;

// 지금 단계에서는 UI 없이 콘솔/디버그로 확인 가능한 최소 인벤토리.
// 나중에 잠수함 수리, 산소 보급과 연결될 자리.
public class PlayerInventory : MonoBehaviour
{
    private Dictionary<string, int> resources = new Dictionary<string, int>();

    public System.Action<string, int> OnResourceChanged;

    public void AddResource(string resourceId, int amount)
    {
        if (!resources.ContainsKey(resourceId))
            resources[resourceId] = 0;

        resources[resourceId] += amount;
        OnResourceChanged?.Invoke(resourceId, resources[resourceId]);

        Debug.Log($"[Inventory] {resourceId} +{amount} (총 {resources[resourceId]}개)");
    }

    public int GetResourceCount(string resourceId)
    {
        return resources.TryGetValue(resourceId, out int count) ? count : 0;
    }

    public bool TrySpendResource(string resourceId, int amount)
    {
        if (GetResourceCount(resourceId) < amount) return false;

        resources[resourceId] -= amount;
        OnResourceChanged?.Invoke(resourceId, resources[resourceId]);
        return true;
    }
}