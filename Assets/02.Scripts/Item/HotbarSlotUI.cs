using UnityEngine;
using UnityEngine.UI;

// 핫바 슬롯 하나(1, 2, 3번 중 하나)를 화면에 표시하는 UI.
// 활성화 여부에 따라 배경색을 바꾸고, 들어있는 아이템 이름을 텍스트로 보여주며
// 아이템 아이콘을 기존 슬롯 배경 위에 오버레이하여 표시함.
public class HotbarSlotUI : MonoBehaviour
{
    public Image background;
    public Text label;
    [Tooltip("아이템 아이콘을 표시할 Image (배경 슬롯 위에 별도의 자식 오브젝트로 표시)")]
    public Image icon;

    public Color activeColor = new Color(1f, 1f, 1f, 0.9f);
    public Color inactiveColor = new Color(1f, 1f, 1f, 0.4f);

    [Header("아이콘 레이아웃 설정")]
    [Tooltip("슬롯 내부 여백 패딩 비율 (0.15이면 사방 15% 여백)")]
    public float iconPaddingRatio = 0.15f;

    private Sprite originalBackgroundSprite;
    private bool hasOriginalSpriteBeenSaved;

    void Awake()
    {
        AutoBindComponents();
    }

    private void SaveOriginalSprite()
    {
        if (!hasOriginalSpriteBeenSaved && background != null)
        {
            originalBackgroundSprite = background.sprite;
            hasOriginalSpriteBeenSaved = true;
        }
    }

    public void AutoBindComponents()
    {
        if (background == null)
            background = GetComponent<Image>();

        if (label == null)
            label = GetComponentInChildren<Text>();

        SaveOriginalSprite();

        if (icon == null)
        {
            Transform iconChild = transform.Find("Icon");
            if (iconChild == null) iconChild = transform.Find("ItemIcon");
            if (iconChild == null) iconChild = transform.Find("Image");

            if (iconChild != null)
            {
                icon = iconChild.GetComponent<Image>();
            }

            if (icon == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                foreach (Image img in images)
                {
                    if (img != background)
                    {
                        icon = img;
                        break;
                    }
                }
            }
        }
    }

    // 아이콘 Image 오브젝트가 없을 경우 기존 슬롯 위에 자동으로 자식 오브젝트 생성
    private void EnsureIconComponent()
    {
        if (icon != null) return;

        AutoBindComponents();
        if (icon != null) return;

        GameObject iconObj = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObj.transform.SetParent(transform, false);

        // 텍스트(label)가 있으면 텍스트 뒤(배경 위)에 아이콘이 오도록 순서 조정
        if (label != null)
        {
            int labelIndex = label.transform.GetSiblingIndex();
            iconObj.transform.SetSiblingIndex(labelIndex);
        }
        else
        {
            iconObj.transform.SetAsLastSibling();
        }

        RectTransform rect = iconObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(iconPaddingRatio, iconPaddingRatio);
        rect.anchorMax = new Vector2(1f - iconPaddingRatio, 1f - iconPaddingRatio);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        icon = iconObj.GetComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.enabled = false;
    }

    public void SetState(bool isActive, string itemLabel, Sprite itemIcon)
    {
        if (background == null)
            AutoBindComponents();

        SaveOriginalSprite();

        // 1. 슬롯 배경은 기존 스프라이트를 유지하고 활성화 색상만 적용
        if (background != null)
        {
            background.color = isActive ? activeColor : inactiveColor;
            if (hasOriginalSpriteBeenSaved && background.sprite != originalBackgroundSprite)
            {
                background.sprite = originalBackgroundSprite;
            }
        }

        // 2. 라벨 텍스트 갱신
        if (label != null)
            label.text = itemLabel;

        // 3. 아이콘을 기존 슬롯 위에 표시 (아이콘이 없으면 숨김)
        if (itemIcon != null)
        {
            EnsureIconComponent();
            if (icon != null)
            {
                icon.sprite = itemIcon;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.enabled = true;
            }
        }
        else
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }
    }
}