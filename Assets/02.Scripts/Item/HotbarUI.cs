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
    [Tooltip("맨손 / 빈 손 아이콘 스프라이트 (1번 슬롯용)")]
    public Sprite bareHandsIcon;
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
    [Tooltip("문어/오징어 아이콘 스프라이트")]
    public Sprite octopusIcon;
    [Tooltip("성게 아이콘 스프라이트")]
    public Sprite urchinIcon;

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
            Sprite iconSprite = null;

            if (slotNumber == 1)
            {
                iconSprite = GetBareHandsIcon();
            }
            else if (item != null)
            {
                iconSprite = GetItemIcon(item);
            }

            slotUIs[i].SetState(isActive, labelText, iconSprite);
        }
    }

    private Sprite GetBareHandsIcon()
    {
        // 1. 인스펙터에 직접 등록된 맨손 아이콘이 있으면 최우선 사용
        if (bareHandsIcon != null) return bareHandsIcon;

        // 2. 캐시 확인
        if (iconCache.TryGetValue("BareHands", out Sprite cached) && cached != null)
        {
            return cached;
        }

        Sprite loaded = null;

        // 3. 커스텀 매핑 목록에서 맨손/빈손/Hand 매핑 확인
        if (customIconMappings != null)
        {
            foreach (var mapping in customIconMappings)
            {
                if (!string.IsNullOrEmpty(mapping.itemName) && mapping.iconSprite != null)
                {
                    string name = mapping.itemName.ToLower();
                    if (name.Contains("손") || name.Contains("hand") || name.Contains("bare") || name.Contains("empty"))
                    {
                        loaded = mapping.iconSprite;
                        break;
                    }
                }
            }
        }

        // 4. Resources/Icon 폴더에서 탐색
        if (loaded == null)
        {
            loaded = Resources.Load<Sprite>("Icon/BareHandsIcon");
            if (loaded == null) loaded = Resources.Load<Sprite>("Icon/HandIcon");
            if (loaded == null) loaded = Resources.Load<Sprite>("Icon/BareHands");
            if (loaded == null) loaded = Resources.Load<Sprite>("Icon/Hand");
            if (loaded == null) loaded = Resources.Load<Sprite>("Icon/foot");
        }

        if (loaded != null)
        {
            iconCache["BareHands"] = loaded;
        }

        return loaded;
    }

    private string GetEnglishItemName(CarryableItem item, int slotNumber)
    {
        string name = "Empty";

        if (slotNumber == 1)
        {
            name = "Bare";
        }
        else if (item != null)
        {
            // 컴포넌트 타입으로 직접 생물 판별
            if (item is HarvestableCreature creature)
            {
                if (creature.GetComponent<OctopusController>() != null || creature.itemName.ToLower().Contains("squid") || creature.itemName.ToLower().Contains("octopus"))
                    name = "Octopus";
                else if (creature.GetComponent<UrchinController>() != null || creature.itemName.ToLower().Contains("urchin"))
                    name = "Sea Urchin";
            }
            else
            {
                string rawName = item.itemName;
                if (!string.IsNullOrEmpty(rawName))
                {
                    string lower = rawName.ToLower();
                    if (rawName.Contains("해머") || rawName.Contains("망치") || lower.Contains("hammer"))
                        name = "Hammer";
                    else if (rawName.Contains("총") || lower.Contains("gun"))
                        name = "Gun";
                    else if (rawName.Contains("산소") || lower.Contains("oxygen"))
                        name = "Oxygen Tank";
                    else if (rawName.Contains("밧줄") || rawName.Contains("로프") || lower.Contains("rope"))
                        name = "Rope";
                    else if (rawName.Contains("문어") || rawName.Contains("오징어") || lower.Contains("octopus") || lower.Contains("squid"))
                        name = "Octopus";
                    else if (rawName.Contains("성게") || lower.Contains("urchin"))
                        name = "Sea Urchin";
                    else if (rawName.Contains("식량") || rawName.Contains("음식") || lower.Contains("food") || lower.Contains("fish"))
                        name = "Food";
                    else
                        name = rawName;
                }
            }
        }

        return name;
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
            else if ((itemName.Contains("문어") || itemName.Contains("오징어") || lower.Contains("octopus") || lower.Contains("squid") || item.GetComponent<OctopusController>() != null) && octopusIcon != null)
                loaded = octopusIcon;
            else if ((itemName.Contains("성게") || lower.Contains("urchin") || item.GetComponent<UrchinController>() != null) && urchinIcon != null)
                loaded = urchinIcon;
        }

        // 5. Resources/Icon/ 폴더에서 자동 스프라이트 찾기
        if (loaded == null)
        {
            loaded = Resources.Load<Sprite>($"Icon/{itemName}Icon");
            if (loaded == null) loaded = Resources.Load<Sprite>($"Icon/{itemName}");

            if (loaded == null)
            {
                string lower = itemName.ToLower();
                if (itemName.Contains("총") || itemName.Contains("Gun"))
                    loaded = Resources.Load<Sprite>("Icon/GunIcon");
                else if (itemName.Contains("해머") || itemName.Contains("망치") || itemName.Contains("Hammer"))
                    loaded = Resources.Load<Sprite>("Icon/HammerIcon2");
                else if (itemName.Contains("밧줄") || itemName.Contains("로프") || itemName.Contains("Rope"))
                    loaded = Resources.Load<Sprite>("Icon/RopeIcon");
                else if (itemName.Contains("산소") || itemName.Contains("Oxygen"))
                    loaded = Resources.Load<Sprite>("Icon/Oxygen");
                else if (itemName.Contains("문어") || itemName.Contains("오징어") || lower.Contains("octopus") || lower.Contains("squid"))
                    loaded = Resources.Load<Sprite>("Icon/Octopus") ?? Resources.Load<Sprite>("Icon/Squid") ?? Resources.Load<Sprite>("Icon/SquidIcon");
                else if (itemName.Contains("성게") || lower.Contains("urchin"))
                    loaded = Resources.Load<Sprite>("Icon/Urchin") ?? Resources.Load<Sprite>("Icon/SeaUrchin") ?? Resources.Load<Sprite>("Icon/UrchinIcon");
            }
        }

        // 6. Texture2D 백업 변환
        if (loaded == null)
        {
            Texture2D tex = Resources.Load<Texture2D>($"Icon/{itemName}Icon");
            if (tex == null) tex = Resources.Load<Texture2D>($"Icon/{itemName}");
            if (tex == null)
            {
                string lower = itemName.ToLower();
                if (itemName.Contains("총") || itemName.Contains("Gun")) tex = Resources.Load<Texture2D>("Icon/GunIcon");
                else if (itemName.Contains("해머") || itemName.Contains("망치") || itemName.Contains("Hammer")) tex = Resources.Load<Texture2D>("Icon/HammerIcon2");
                else if (itemName.Contains("밧줄") || itemName.Contains("로프") || itemName.Contains("Rope")) tex = Resources.Load<Texture2D>("Icon/RopeIcon");
                else if (itemName.Contains("산소") || itemName.Contains("Oxygen")) tex = Resources.Load<Texture2D>("Icon/Oxygen");
                else if (itemName.Contains("문어") || itemName.Contains("오징어") || lower.Contains("octopus") || lower.Contains("squid"))
                    tex = Resources.Load<Texture2D>("Icon/Octopus") ?? Resources.Load<Texture2D>("Icon/Squid");
                else if (itemName.Contains("성게") || lower.Contains("urchin"))
                    tex = Resources.Load<Texture2D>("Icon/Urchin") ?? Resources.Load<Texture2D>("Icon/SeaUrchin");
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