using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ItemIconMapping
{
    [Tooltip("아이템 이름 또는 키워드 (예: Hammer, Gun, Rope, 산소통, 총 등)")]
    public string itemName;
    [Tooltip("해당 아이템 슬롯에 표시할 아이콘 Sprite")]
    public Sprite iconSprite;
}

// PlayerHotbar의 상태(지금 몇 번 슬롯인지, 각 슬롯에 뭐가 들었는지)를 매 프레임 확인해서
// 3개의 HotbarSlotUI에 반영하는 관리자 스크립트.
public class HotbarUI : MonoBehaviour
{
    public PlayerHotbar hotbar;

    [Tooltip("반드시 슬롯 1, 2, 3 순서대로 3개 연결")]
    public HotbarSlotUI[] slotUIs = new HotbarSlotUI[3];

    [Header("인스펙터 아이콘 직접 등록")]
    [Tooltip("총 아이콘 스프라이트")]
    public Sprite gunIcon;
    [Tooltip("해머/망치 아이콘 스프라이트")]
    public Sprite hammerIcon;
    [Tooltip("밧줄/로프 아이콘 스프라이트")]
    public Sprite ropeIcon;
    [Tooltip("산소통 아이콘 스프라이트")]
    public Sprite oxygenIcon;
    [Tooltip("식량 아이콘 스프라이트")]
    public Sprite foodIcon;

    [Header("커스텀 아이템 아이콘 매핑 목록")]
    [Tooltip("아이템 이름별 아이콘 스프라이트 수동 매핑")]
    public List<ItemIconMapping> customIconMappings = new List<ItemIconMapping>();

    private Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();

    void Awake()
    {
        AutoBindComponents();
    }

    private void AutoBindComponents()
    {
        if (hotbar == null)
        {
            hotbar = FindFirstObjectByType<PlayerHotbar>();
        }

        if (slotUIs == null || slotUIs.Length == 0 || slotUIs[0] == null)
        {
            slotUIs = GetComponentsInChildren<HotbarSlotUI>();
        }
    }

    void Update()
    {
        if (hotbar == null || slotUIs == null || slotUIs.Length == 0)
        {
            AutoBindComponents();
            if (hotbar == null) return;
        }

        for (int i = 0; i < slotUIs.Length; i++)
        {
            int slotNumber = i + 1;
            if (slotUIs[i] == null) continue;

            bool isActive = hotbar.ActiveSlot == slotNumber;
            CarryableItem item = slotNumber == 1 ? null : hotbar.GetItemAtSlot(slotNumber);

            string labelText = GetEnglishItemName(item, slotNumber);
            Sprite iconSprite = item != null ? GetItemIcon(item) : null;

            slotUIs[i].SetState(isActive, labelText, iconSprite);
        }
    }

    private string GetEnglishItemName(CarryableItem item, int slotNumber)
    {
        if (slotNumber == 1) return "Bare Hands";
        if (item == null) return "Empty";

        string rawName = item.itemName;
        if (string.IsNullOrEmpty(rawName)) return "Empty";

        string lower = rawName.ToLower();
        if (rawName.Contains("해머") || rawName.Contains("망치") || lower.Contains("hammer"))
            return "Hammer";
        if (rawName.Contains("총") || lower.Contains("gun"))
            return "Gun";
        if (rawName.Contains("산소") || lower.Contains("oxygen"))
            return "Oxygen Tank";
        if (rawName.Contains("밧줄") || rawName.Contains("로프") || lower.Contains("rope"))
            return "Rope";
        if (rawName.Contains("식량") || rawName.Contains("음식") || lower.Contains("food"))
            return "Food";

        return rawName;
    }

    private Sprite GetItemIcon(CarryableItem item)
    {
        if (item == null) return null;

        // 1. CarryableItem 자체 인스펙터에 직접 등록된 아이콘이 있으면 사용
        if (item.icon != null) return item.icon;

        string itemName = item.itemName;
        if (string.IsNullOrEmpty(itemName)) return null;

        // 2. 이미 로드된 적이 있으면 캐시에서 바로 반환
        if (iconCache.TryGetValue(itemName, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Sprite loaded = null;

        // 3. HotbarUI 인스펙터 커스텀 매핑 목록에서 찾기
        if (customIconMappings != null)
        {
            foreach (var mapping in customIconMappings)
            {
                if (!string.IsNullOrEmpty(mapping.itemName) && mapping.iconSprite != null)
                {
                    if (itemName.Equals(mapping.itemName, System.StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains(mapping.itemName))
                    {
                        loaded = mapping.iconSprite;
                        break;
                    }
                }
            }
        }

        // 4. HotbarUI 인스펙터의 기본 아이콘 변수 체크
        if (loaded == null)
        {
            string lower = itemName.ToLower();
            if ((itemName.Contains("총") || lower.Contains("gun")) && gunIcon != null)
                loaded = gunIcon;
            else if ((itemName.Contains("해머") || itemName.Contains("망치") || lower.Contains("hammer")) && hammerIcon != null)
                loaded = hammerIcon;
            else if ((itemName.Contains("밧줄") || itemName.Contains("로프") || lower.Contains("rope")) && ropeIcon != null)
                loaded = ropeIcon;
            else if ((itemName.Contains("산소") || lower.Contains("oxygen")) && oxygenIcon != null)
                loaded = oxygenIcon;
            else if ((itemName.Contains("식량") || itemName.Contains("음식") || lower.Contains("food")) && foodIcon != null)
                loaded = foodIcon;
        }

        // 5. Resources/Icon/ 폴더에서 자동 스프라이트 찾기
        if (loaded == null)
        {
            loaded = Resources.Load<Sprite>($"Icon/{itemName}Icon");
            if (loaded == null) loaded = Resources.Load<Sprite>($"Icon/{itemName}");

            if (loaded == null)
            {
                if (itemName.Contains("총") || itemName.Contains("Gun"))
                    loaded = Resources.Load<Sprite>("Icon/GunIcon");
                else if (itemName.Contains("해머") || itemName.Contains("망치") || itemName.Contains("Hammer"))
                    loaded = Resources.Load<Sprite>("Icon/HammerIcon2");
                else if (itemName.Contains("밧줄") || itemName.Contains("로프") || itemName.Contains("Rope"))
                    loaded = Resources.Load<Sprite>("Icon/RopeIcon");
            }
        }

        // 6. Texture2D 백업 변환
        if (loaded == null)
        {
            Texture2D tex = Resources.Load<Texture2D>($"Icon/{itemName}Icon");
            if (tex == null) tex = Resources.Load<Texture2D>($"Icon/{itemName}");
            if (tex == null)
            {
                if (itemName.Contains("총") || itemName.Contains("Gun")) tex = Resources.Load<Texture2D>("Icon/GunIcon");
                else if (itemName.Contains("해머") || itemName.Contains("망치") || itemName.Contains("Hammer")) tex = Resources.Load<Texture2D>("Icon/HammerIcon2");
                else if (itemName.Contains("밧줄") || itemName.Contains("로프") || itemName.Contains("Rope")) tex = Resources.Load<Texture2D>("Icon/RopeIcon");
            }

            if (tex != null)
            {
                loaded = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }

        if (loaded != null)
        {
            iconCache[itemName] = loaded;
            item.icon = loaded;
        }

        return loaded;
    }
}