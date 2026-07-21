using UnityEngine;

// IInteractable을 구현하는 가장 단순한 예시: 채집 가능한 자원 하나.
// 동굴에 흩어놓을 산호 조각, 고철 부품 등에 그대로 붙여서 쓰면 된다.
[RequireComponent(typeof(Collider))]
public class ResourceItem : MonoBehaviour, Interactable
{
    [Tooltip("PlayerInventory에서 이 자원을 구분할 ID (예: \"scrap\", \"oxygen_cell\")")]
    public string resourceId = "scrap";
    public int amount = 1;

    [Tooltip("채집 후 오브젝트를 파괴할지, 그냥 비활성화만 할지")]
    public bool destroyOnCollect = true;

    private bool collected = false;

    public string GetInteractionPrompt()
    {
        return $"F : {resourceId} 채집하기";
    }

    public bool CanInteract(GameObject interactor)
    {
        return !collected;
    }

    public void Interact(GameObject interactor)
    {
        if (collected) return;

        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            Debug.LogWarning("상호작용한 오브젝트에 PlayerInventory가 없음");
            return;
        }

        inventory.AddResource(resourceId, amount);
        collected = true;

        if (destroyOnCollect)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}