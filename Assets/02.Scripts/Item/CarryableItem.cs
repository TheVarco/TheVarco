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
    [Tooltip("핫바 UI에 표시될 아이콘 (안 채우면 텍스트만 표시됨)")]
    public Sprite icon;

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

    // 내려놓을 때 PlayerHotbar가 호출. dropPosition으로 몸에서 떨어진 안전한 위치로 옮긴 뒤 물리를 켬
    // (안 옮기면 손 위치 = 몸 Collider 근처라서, 물리가 켜지자마자 겹쳐서 튕겨나갈 수 있음)
    public void OnDropped(Vector3 dropPosition)
    {
        transform.SetParent(null);
        transform.position = dropPosition;

        if (col != null) col.enabled = true;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true; // 프리팹 설정에 상관없이 내려놓으면 확실히 중력 받아 떨어지게 함
        }
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

    // "사용"했을 때 실제로 일어나는 효과.
    // user = 사용한 사람(아이템을 든 사람), target = 효과가 실제로 적용될 대상.
    // 자기 자신에게 쓰면 user == target, 팀원에게 주면 target이 그 팀원이 됨.
    // 산소통이라면 이 함수를 오버라이드해서 target의 산소를 채우는 로직을 넣게 됨
    public virtual void OnUse(GameObject user, GameObject target)
    {
        Debug.Log($"[CarryableItem] {itemName} 사용함 (대상: {target.name})");
    }

    // 좌클릭했을 때 실행. 기본 아이템(산소통 등)은 "자기 자신에게 사용"이 곧 좌클릭 동작임.
    // 무기류는 이 함수를 재정의해서 발사 등 완전히 다른 동작으로 바꿈.
    // 반환값(true/false)은 "이 행동 이후 아이템을 핫바에서 제거(소모)할지"를 PlayerHotbar에게 알려주는 용도.
    public virtual bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        OnUse(user, user); // 기본은 자기 자신을 대상으로 사용
        return isConsumable;
    }

    // 우클릭을 누르고 있는 동안 매 프레임 호출됨 (조준처럼 "누르고 있는 동안 지속되는" 동작용).
    // 기본 아이템은 우클릭에 반응 안 함. 무기류가 재정의해서 조준 등을 구현.
    public virtual void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
    }
}