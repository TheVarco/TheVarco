using UnityEngine;

// PlayerHotbar의 상태(지금 몇 번 슬롯인지, 각 슬롯에 뭐가 들었는지)를 매 프레임 확인해서
// 3개의 HotbarSlotUI에 반영하는 관리자 스크립트.
public class HotbarUI : MonoBehaviour
{
    public PlayerHotbar hotbar;

    [Tooltip("반드시 슬롯 1, 2, 3 순서대로 3개 연결")]
    public HotbarSlotUI[] slotUIs = new HotbarSlotUI[3];

    void Update()
    {
        if (hotbar == null) return;

        for (int i = 0; i < slotUIs.Length; i++)
        {
            int slotNumber = i + 1;
            if (slotUIs[i] == null) continue;

            bool isActive = hotbar.ActiveSlot == slotNumber;
            CarryableItem item = slotNumber == 1 ? null : hotbar.GetItemAtSlot(slotNumber);

            string labelText = slotNumber == 1 ? "맨손" : (item != null ? item.itemName : "비어있음");
            Sprite iconSprite = item != null ? item.icon : null;

            slotUIs[i].SetState(isActive, labelText, iconSprite);
        }
    }
}