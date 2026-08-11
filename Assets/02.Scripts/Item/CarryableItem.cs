using Fusion;
using UnityEngine;

// 산소통, 자원 등 "손에 들고 다니다가 나중에 쓰거나 내려놓는" 아이템에 붙이는 스크립트.
// 기존 Interactable을 구현해서 PlayerInteractor(E키)로 집을 수 있게 한다.
//
// 네트워크에서는 "누가 들고 있는가"(HolderId)만 복제하고, 손에 붙이는 건 각 머신이
// 자기 화면의 손 위치를 기준으로 직접 처리한다. 호스트 좌표를 복제받아 붙이면
// 정작 아이템을 든 본인이 자기 손보다 늦게 따라오는 아이템을 보게 되기 때문.
[RequireComponent(typeof(Collider))]
public class CarryableItem : NetworkBehaviour, Interactable
{
    [Header("아이템 정보")]
    public string itemName = "산소통";
    [Tooltip("우클릭(사용)했을 때 소모되어 사라지는 아이템인지")]
    public bool isConsumable = true;
    [Tooltip("핫바 UI에 표시될 아이콘 (안 채우면 텍스트만 표시됨)")]
    public Sprite icon;
    [Tooltip("팀원에게 사용할 때 안내 문구에 붙일 동사 (예: 산소통이면 '채워주기', 식량이면 '먹여주기')")]
    public string giveActionName = "사용해주기";

    [Header("손에 들었을 때 위치 보정")]
    public Vector3 holdPositionOffset;
    public Vector3 holdRotationOffset;
    
    protected Rigidbody rb { get; private set; }
    protected Collider col { get; private set; }

    [Networked, OnChangedRender(nameof(OnHolderChanged))]
    private NetworkId HolderId { get; set; } // 유효하지 않으면 바닥에 있는 상태

    [Networked] private Vector3 DroppedPosition { get; set; }

    // 핫바 슬롯을 바꿔서 손에서 잠깐 감출 때. 기본 false = 보임
    [Networked, OnChangedRender(nameof(OnHiddenChanged))]
    private NetworkBool NetworkedHidden { get; set; }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    // 핫바가 "이 사람이 집겠다"고 호스트에 요청할 때 호출
    public void RequestPickup(NetworkId holder)
    {
        if (Object == null) return;
        RPC_RequestPickup(holder);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkId requester)
    {
        if (HolderId.IsValid) return; // 동시에 집으면 먼저 도착한 쪽이 가져간다
        HolderId = requester;
    }

    // State Authority에서만 실행되도록 (문어/성게 부착 해제, 소유자 지정 등)
    // 서영 추가
    protected bool TryAssignHolderFromStateAuthority(NetworkId requester)
    {
        // 호스트만 소유자 변경 허용
        // 이미 점유된 아이템의 중복 획득 차단
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || HolderId.IsValid)
            return false;

        // 요청자를 최종 소유자로 게시
        HolderId = requester;
        return true;
    }

    // 핫바가 내려놓을 때 호출
    public void RequestDrop(Vector3 dropPosition)
    {
        if (Object == null) { OnDropped(dropPosition); return; } // 러너 없는 씬은 그 자리에서 처리
        RPC_RequestDrop(dropPosition);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDrop(Vector3 dropPosition)
    {
        DroppedPosition = dropPosition;
        NetworkedHidden = false; // 바닥에 놓으면 무조건 보이게
        HolderId = default;
    }

    // 소모품을 다 써서 없앨 때 호출
    public void RequestDespawn()
    {
        if (Object == null) { Destroy(gameObject); return; }
        RPC_RequestDespawn();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDespawn()
    {
        Runner.Despawn(Object);
    }

    // 소유자가 바뀌면 모든 머신에서 실행. 각자 자기 화면의 손에 붙이거나 바닥에 내려놓는다
    private void OnHolderChanged()
    {
        if (!Runner.TryFindObject(HolderId, out NetworkObject holder))
        {
            OnDropped(DroppedPosition);
            return;
        }

        PlayerHotbar hotbar = holder.GetComponent<PlayerHotbar>();
        if (hotbar == null || hotbar.handSocket == null) return;

        OnPickedUp(hotbar.handSocket);

        // 슬롯 관리는 들고 있는 본인 머신에서만 (내 인벤토리는 내가 안다)
        if (holder.HasInputAuthority) hotbar.RegisterPickedUpItem(this);
    }

    public virtual string GetInteractionPrompt()
    {
        return $"E : {itemName} 들기";
    }

    public virtual bool CanInteract(GameObject interactor)
    {
        // 핫바에 빈 슬롯(2 또는 3)이 있어야 주울 수 있음
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

    // 손에 붙을 때 PlayerCarrier가 호출
    public virtual void OnPickedUp(Transform handSocket)
    {
        if (rb != null) rb.isKinematic = true;   // 물리 영향 끄기 (손에 붙어서 따라다녀야 하므로)
        if (col != null) col.enabled = false;    // 들고 있는 동안은 다시 집히거나 부딪히지 않게

        transform.SetParent(handSocket);
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);
    }

    // 내려놓을 때 PlayerHotbar가 호출. dropPosition으로 몸에서 떨어진 안전한 위치로 옮긴 뒤 물리를 켬
    // (안 옮기면 손 위치 = 몸 Collider 근처라서, 물리가 켜지자마자 겹쳐서 튕겨나갈 수 있음)
    public virtual void OnDropped(Vector3 dropPosition)
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
        ApplyVisible(visible); // 내 화면엔 즉시 반영
        if (Object == null) return;

        RPC_RequestHidden(!visible); // 나머지 머신에도 전파 (안 하면 상대는 안 든 아이템까지 계속 보임)
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestHidden(NetworkBool hidden) => NetworkedHidden = hidden;

    private void OnHiddenChanged() => ApplyVisible(!NetworkedHidden);

    private void ApplyVisible(bool visible)
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
        // 애니메이터에 없는 파라미터(HasWeapon/Attack)를 호출하면 경고만 나므로 제거하고,
        // 실제로 존재하는 MeleeWeapon 스테이트를 네트워크 경로로 재생한다 (원격에서도 보이게)
        if (user != null)
        {
            PlayerController controller = user.GetComponent<PlayerController>();
            if (controller != null)
                controller.PlayMotionState(Animator.StringToHash("MeleeWeapon"));
        }
        OnUse(user, user); // 기본은 자기 자신을 대상으로 사용
        return isConsumable;
    }
    
    // 좌클릭을 누르고 있는 동안 매 프레임 호출
    // 망치처럼 충전 시간이 필요한 아이템만 적용
    public virtual void OnPrimaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
    }

    // 우클릭을 누르고 있는 동안 매 프레임 호출됨 (조준처럼 "누르고 있는 동안 지속되는" 동작용).
    // 기본 아이템은 우클릭에 반응 안 함. 무기류가 재정의해서 조준 등을 구현.
    public virtual void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
    }
}
