using UnityEngine;

// 핫바: 슬롯 1은 항상 맨손(비어있음), 슬롯 2/3은 실제 아이템(무기, 산소통 등)을 하나씩 담음.
// 아이템을 주우면 빈 슬롯(2 또는 3)에 들어가고, 숫자키로 슬롯을 바꾸면 그 슬롯의 아이템만 손에 보임.
// PlayerCarrier가 하던 역할(들기/사용/내려놓기)을 이제 여기서 다 처리하므로 PlayerCarrier는 안 써도 됨.
public class PlayerHotbar : MonoBehaviour
{
    [Tooltip("아이템이 손에 위치할 지점 (Player 자식으로 만들어 연결)")]
    public Transform handSocket;
    [Tooltip("좌클릭 - 활성 아이템의 OnPrimaryAction으로 그대로 전달됨")]
    public KeyCode primaryActionKey = KeyCode.Mouse0;
    [Tooltip("우클릭 - 활성 아이템의 OnSecondaryHeld로 그대로 전달됨")]
    public KeyCode secondaryActionKey = KeyCode.Mouse1;
    [Tooltip("R키 - 어떤 아이템이든 무조건 OnUse를 직접 실행하는 백업용 키")]
    public KeyCode useKey = KeyCode.R;
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

    void Update()
    {
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

        // R키: 아이템 종류와 상관없이 강제로 자기 자신에게 OnUse 실행 (백업용)
        if (Input.GetKeyDown(useKey))
        {
            active.OnUse(gameObject, gameObject);
            if (active.isConsumable) RemoveActiveItem();
        }
        else if (Input.GetKeyDown(dropKey))
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
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] == null)
            {
                itemSlots[i] = item;
                item.OnPickedUp(handSocket);
                item.SetVisible(IndexToSlotNumber(i) == ActiveSlot); // 지금 보고 있는 슬롯이 아니면 바로 숨김
                return true;
            }
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
            Destroy(item.gameObject); // 소모품은 실제로 파괴해야 손에 남아있지 않음
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

        item.OnDropped(dropPosition); // 다시 세상에 존재하는 오브젝트로 되돌림 (안전한 위치 + 물리 켜기)
        ClearActiveSlot();            // 파괴하지 않고 슬롯 데이터만 비움
    }

    private int IndexToSlotNumber(int index) => index + 2; // 0->2번 슬롯, 1->3번 슬롯
}
