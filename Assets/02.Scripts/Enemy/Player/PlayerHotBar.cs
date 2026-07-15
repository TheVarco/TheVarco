using UnityEngine;

// 핫바: 슬롯 1은 항상 맨손(비어있음), 슬롯 2/3은 실제 아이템(무기, 산소통 등)을 하나씩 담음.
// 아이템을 주우면 빈 슬롯(2 또는 3)에 들어가고, 숫자키로 슬롯을 바꾸면 그 슬롯의 아이템만 손에 보임.
// PlayerCarrier가 하던 역할(들기/사용/내려놓기)을 이제 여기서 다 처리하므로 PlayerCarrier는 안 써도 됨.
public class PlayerHotbar : MonoBehaviour
{
    [Tooltip("아이템이 손에 위치할 지점 (Player 자식으로 만들어 연결)")]
    public Transform handSocket;
    public KeyCode useKey = KeyCode.R;
    public KeyCode dropKey = KeyCode.G;

    // 인덱스 0 = 슬롯 2, 인덱스 1 = 슬롯 3 (슬롯 1은 항상 빈 맨손이라 배열에 안 넣음)
    private CarryableItem[] itemSlots = new CarryableItem[2];

    public int ActiveSlot { get; private set; } = 1;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(1);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(2);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(3);

        CarryableItem active = GetActiveItem();
        if (active == null) return;

        if (Input.GetKeyDown(useKey))
        {
            active.OnUse(gameObject);
            if (active.isConsumable) RemoveActiveItem();
        }
        else if (Input.GetKeyDown(dropKey))
        {
            DropActiveItem();
        }
    }

    private void SwitchTo(int slot)
    {
        if (slot == ActiveSlot) return;

        GetActiveItem()?.SetVisible(false); // 지금 보고 있던 아이템 숨김
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

    private void RemoveActiveItem()
    {
        if (ActiveSlot == 1) return;
        itemSlots[ActiveSlot - 2] = null;
    }

    public void DropActiveItem()
    {
        CarryableItem item = GetActiveItem();
        if (item == null) return;

        item.OnDropped();
        RemoveActiveItem();
    }

    private int IndexToSlotNumber(int index) => index + 2; // 0->2번 슬롯, 1->3번 슬롯
}