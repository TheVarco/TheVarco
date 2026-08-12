using Fusion;
using UnityEngine;

// 핫바: 슬롯 1은 항상 맨손(비어있음), 슬롯 2/3은 실제 아이템(무기, 산소통 등)을 하나씩 담음.
// 아이템을 주우면 빈 슬롯(2 또는 3)에 들어가고, 숫자키로 슬롯을 바꾸면 그 슬롯의 아이템만 손에 보임.
// PlayerCarrier가 하던 역할(들기/사용/내려놓기)을 이제 여기서 다 처리하므로 PlayerCarrier는 안 써도 됨.
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

    [Header("내려놓기 설정")]
    [Tooltip("몸(Collider)과 안 겹치도록, 내려놓을 때 정면으로 얼마나 떨어뜨릴지")]
    public float dropDistance = 1.2f;
    public float dropHeightOffset = 0f;

    // 인덱스 0 = 슬롯 2, 인덱스 1 = 슬롯 3 (슬롯 1은 항상 빈 맨손이라 배열에 안 넣음)
    private CarryableItem[] itemSlots = new CarryableItem[2];

    public int ActiveSlot { get; private set; } = 1;

    void Start()
    {
        AttachHandSocketToRightHand();
    }

    public void AttachHandSocketToRightHand()
    {
        if (handSocket == null) return;

        Animator anim = GetComponentInChildren<Animator>();
        if (anim == null) anim = GetComponentInParent<Animator>();

        if (anim != null)
        {
            Transform rightHandBone = null;
            if (anim.isHuman)
            {
                rightHandBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
            }

            if (rightHandBone == null)
            {
                Transform[] allChildren = anim.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allChildren)
                {
                    string n = t.name.ToLower();
                    if (n.Contains("righthand") || n.Contains("hand_r") || n.Contains("hand.r") || n.Contains("hand_right") || n.Contains("right_hand"))
                    {
                        rightHandBone = t;
                        break;
                    }
                }
            }

            if (rightHandBone != null)
            {
                handSocket.SetParent(rightHandBone, false);
                handSocket.localPosition = Vector3.zero;
                handSocket.localRotation = Quaternion.identity;
                Debug.Log($"[PlayerHotbar] handSocket가 캐릭터 오른손 뼈({rightHandBone.name})에 자동으로 부착되었습니다.");
            }
        }
    }

    void Update()
    {
        // 내 캐릭터가 아니면(원격 플레이어) 로컬 입력을 읽지 않음. 비네트워크 씬에선 Object가 null이라 그대로 동작
        if (Object != null && !Object.HasInputAuthority) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(1);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(2);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(3);

        CarryableItem active = GetActiveItem();
        if (active == null) return; // 맨손(슬롯1)이면 위임할 아이템이 없음 - 잡기/근접공격은 PlayerGrabber/MeleeAttack이 별도로 처리

        // 좌클릭: 아이템에게 "너 알아서 해"라고 넘겨줌. 일반 아이템은 사용, 무기는 발사 등 아이템이 직접 결정
        if (Input.GetKeyDown(primaryActionKey))
        {
            bool shouldRemove = active.OnPrimaryAction(gameObject, aimReference);
            if (shouldRemove) RemoveActiveItem();
        }

        // 서영 추가 : 아이템이 좌클릭 유지와 해제 상태를 매 프레임 받을 수 있게 전달
        active.OnPrimaryHeld(gameObject, aimReference, Input.GetKey(primaryActionKey));

        // 우클릭: "지금 눌려있는 상태"를 매 프레임 그대로 전달 (조준처럼 지속되는 동작용)
        active.OnSecondaryHeld(gameObject, aimReference, Input.GetKey(secondaryActionKey));

        // 예전엔 R키로 아이템 종류와 상관없이 OnUse를 강제 실행했지만,
        // 좌클릭이 이미 아이템별 사용을 처리하는데다 총처럼 isConsumable인 무기가 R로 사라지는 버그가 있어서 제거함.
        // (R은 이제 부활 키로 쓰임)
        if (Input.GetKeyDown(dropKey))
        {
            DropActiveItem();
        }
    }

    // 서영 추가 (잠수함 조종)
    private void OnDisable()
    {
        CarryableItem active = GetActiveItem();
        if (active != null)
        {
            active.OnPrimaryHeld(gameObject, aimReference, false); // 서영 추가
            active.OnSecondaryHeld(gameObject, aimReference, false);
        }
    }

    private void SwitchTo(int slot)
    {
        if (slot == ActiveSlot) return;

        CarryableItem previous = GetActiveItem(); // 서영 추가 : 기존 아이템 사용 중에 전환 시 종료처리
        if (previous != null)
        {
            previous.OnPrimaryHeld(gameObject, aimReference, false); // 서영 추가
            previous.OnSecondaryHeld(gameObject, aimReference, false);
            previous.SetVisible(false); // 지금 보고 있던 아이템 숨김
        }

        ActiveSlot = slot;
        GetActiveItem()?.SetVisible(true);  // 새로 활성화된 슬롯의 아이템 표시
    }

    // 새 아이템을 주웠을 때 빈 슬롯(2 또는 3)에 넣는다. 슬롯이 꽉 차 있으면 false 반환
    public bool TryAddItem(CarryableItem item)
    {
        if (item == null || !HasFreeSlot()) return false;

        // 네트워크 세션에서는 호스트가 소유자를 정한다. 그 결과가 돌아오면
        // CarryableItem.OnHolderChanged가 각 머신에서 손에 붙이고,
        // 내 것이면 RegisterPickedUpItem으로 슬롯에 들어온다
        if (item.Object != null && Object != null)
        {
            item.RequestPickup(Object.Id);
            return true;
        }

        return AddToSlot(item, true);
    }

    // 아이템이 이미 손에 붙은 뒤 슬롯에만 등록할 때 (네트워크 경로에서 CarryableItem이 호출)
    public void RegisterPickedUpItem(CarryableItem item) => AddToSlot(item, false);

    private bool AddToSlot(CarryableItem item, bool attachToHand)
    {
        foreach (CarryableItem slot in itemSlots)
            if (slot == item) return true; // 이미 들고 있음 (중복 등록 방지)

        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null) continue;

            itemSlots[i] = item;
            if (attachToHand) item.OnPickedUp(handSocket);

            // 아이템을 줍는 즉시 해당 아이템이 들어간 슬롯으로 자동 전환
            SwitchTo(IndexToSlotNumber(i));
            return true;
        }
        return false; // 핫바가 꽉 참
    }

    public bool HasFreeSlot()
    {
        foreach (var slot in itemSlots)
            if (slot == null) return true;
        return false;
    }

    // 지금 활성화된 슬롯에 들어있는 아이템 (슬롯 1이면 항상 null)
    public CarryableItem GetActiveItem()
    {
        if (ActiveSlot == 1) return null;
        return itemSlots[ActiveSlot - 2];
    }

    // 활성/비활성 상관없이 특정 슬롯 번호(1~3)에 뭐가 들어있는지 조회 (핫바 UI 표시용)
    public CarryableItem GetItemAtSlot(int slotNumber)
    {
        if (slotNumber == 1) return null;
        int index = slotNumber - 2;
        return (index >= 0 && index < itemSlots.Length) ? itemSlots[index] : null;
    }

    // 슬롯 데이터만 비움 (오브젝트는 그대로 둠) - 내려놓기처럼 "세상에 남겨야 하는" 경우용
    private void ClearActiveSlot()
    {
        if (ActiveSlot == 1) return;
        itemSlots[ActiveSlot - 2] = null;
    }

    // 팀원에게 주는 등 핫바 밖에서 아이템을 "다 써서 없애야" 할 때 호출 (PlayerItemGiver 등에서 사용)
    public void RemoveActiveItem()
    {
        if (ActiveSlot == 1) return;

        CarryableItem item = itemSlots[ActiveSlot - 2];
        ClearActiveSlot();

        if (item != null)
        {
            item.OnPrimaryHeld(gameObject, aimReference, false); // 서영 추가
            item.OnSecondaryHeld(gameObject, aimReference, false);
            item.RequestDespawn(); // 소모품은 실제로 없애야 손에 남아있지 않음 (네트워크면 호스트가 despawn)
        }
    }

    public void DropActiveItem()
    {
        CarryableItem item = GetActiveItem();
        if (item == null) return;

        item.OnPrimaryHeld(gameObject, aimReference, false); // 서영 추가
        item.OnSecondaryHeld(gameObject, aimReference, false);

        // Player 정면 기준으로 몸에서 떨어진 위치를 계산해서, 물리가 켜지자마자 겹쳐서 튕기는 것 방지
        Vector3 dropPosition = transform.position
            + transform.forward * dropDistance
            + Vector3.up * dropHeightOffset;

        item.RequestDrop(dropPosition); // 네트워크면 호스트가 소유자를 풀고, 각 머신이 그 위치에 내려놓음
        ClearActiveSlot();              // 파괴하지 않고 슬롯 데이터만 비움
    }

    // 클라이언트는 네트워크 오브젝트를 직접 스폰할 수 없어서 호스트에 요청한다.
    // 무기 아이템(RangedWeaponItem 등)이 발사할 때 호출.
    public void SpawnProjectile(NetworkPrefabRef prefab, Vector3 position, Vector3 direction)
    {
        if (Object == null) return; // 러너 없는 씬이면 호출부가 로컬 Instantiate로 폴백
        RPC_SpawnProjectile(prefab, position, direction);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SpawnProjectile(NetworkPrefabRef prefab, Vector3 position, Vector3 direction)
    {
        // owner는 스폰 "전에" 넣어야 한다. 나중에 넣으면 총구가 몸에 겹친 순간
        // OnTriggerEnter가 먼저 터져서 자기 총에 맞을 수 있음
        Runner.Spawn(prefab, position, Quaternion.LookRotation(direction), Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile projectile = obj.GetComponent<Projectile>();
                if (projectile != null) projectile.owner = gameObject;

                // 밧줄은 모든 머신이 던진 사람을 알아야 로프 시작점(손)을 잡는다
                RopeProjectile rope = obj.GetComponent<RopeProjectile>();
                if (rope != null) rope.InitOwner(Object.Id);
            });
    }

    private int IndexToSlotNumber(int index) => index + 2; // 0->2번 슬롯, 1->3번 슬롯
}
