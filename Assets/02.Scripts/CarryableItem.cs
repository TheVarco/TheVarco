using UnityEngine;

// 산소통, 자원 등 "손에 들고 다니다가 나중에 쓰거나 내려놓는" 아이템에 붙이는 스크립트.
// 기존 Interactable을 구현해서 PlayerInteractor(E키)로 집을 수 있게 한다.
[RequireComponent(typeof(Collider))]
public class CarryableItem : MonoBehaviour, Interactable
{
    [Header("아이템 정보")]
    public string itemName = "산소통";
    [Tooltip("우클릭(사용)했을 때 소모되어 사라지는 아이템인지")]
    public bool isConsumable = true;

    [Header("손에 들었을 때 위치 보정")]
    public Vector3 holdPositionOffset;
    public Vector3 holdRotationOffset;

    private Rigidbody rb;
    private Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    public string GetInteractionPrompt()
    {
        return $"E : {itemName} 들기";
    }

    public bool CanInteract(GameObject interactor)
    {
        // 핫바에 빈 슬롯(2 또는 3)이 있어야 주울 수 있음
        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        return hotbar != null && hotbar.HasFreeSlot();
    }

    public void Interact(GameObject interactor)
    {
        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        if (hotbar == null)
        {
            Debug.LogWarning("CarryableItem: 상호작용한 오브젝트에 PlayerHotbar가 없음");
            return;
        }
        hotbar.TryAddItem(this);
    }

    // 손에 붙을 때 PlayerCarrier가 호출
    public void OnPickedUp(Transform handSocket)
    {
        if (rb != null) rb.isKinematic = true;   // 물리 영향 끄기 (손에 붙어서 따라다녀야 하므로)
        if (col != null) col.enabled = false;    // 들고 있는 동안은 다시 집히거나 부딪히지 않게

        transform.SetParent(handSocket);
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);
    }

    // 내려놓을 때 PlayerCarrier가 호출
    public void OnDropped()
    {
        transform.SetParent(null);
        if (col != null) col.enabled = true;
        if (rb != null) rb.isKinematic = false;
    }

    // 내려놓지는 않고, 다른 핫바 슬롯으로 바꿨을 때 화면에서만 잠깐 숨기는 용도
    public void SetVisible(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.enabled = visible;
        }
    }

    // 우클릭으로 "사용"했을 때 실제로 일어나는 효과.
    // 산소통이라면 이 함수를 오버라이드해서 산소 회복 로직을 넣게 될 자리 (산소 시스템 만들 때 연결)
    public virtual void OnUse(GameObject user)
    {
        Debug.Log($"[CarryableItem] {itemName} 사용함");
    }
}