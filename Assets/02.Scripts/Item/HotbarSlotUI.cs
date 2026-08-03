using UnityEngine;
using UnityEngine.UI;

// 핫바 슬롯 하나(1, 2, 3번 중 하나)를 화면에 표시하는 UI.
// 활성화 여부에 따라 배경색을 바꾸고, 들어있는 아이템 이름을 텍스트로 보여줌.
public class HotbarSlotUI : MonoBehaviour
{
    public Image background;
    public Text label;
    [Tooltip("아이템 아이콘을 표시할 Image (배경과는 별도로, 슬롯 안쪽에 작게 배치)")]
    public Image icon;

    public Color activeColor = new Color(1f, 1f, 1f, 0.9f);
    public Color inactiveColor = new Color(1f, 1f, 1f, 0.4f);

    private Sprite originalBackgroundSprite;
    private bool hasOriginalSpriteBeenSaved;

    void Awake()
    {
        AutoBindComponents();
        SaveOriginalSprite();
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

        SaveOriginalSprite();
    }

    public void SetState(bool isActive, string itemLabel, Sprite itemIcon)
    {
        if (background == null)
            AutoBindComponents();

        SaveOriginalSprite();

        if (background != null)
        {
            background.color = isActive ? activeColor : inactiveColor;

            // Slot 자체 Image 컴포넌트의 소스 이미지(Source Image / sprite)를 직접 변경
            if (itemIcon != null)
            {
                background.sprite = itemIcon;
            }
            else
            {
                background.sprite = originalBackgroundSprite;
            }
        }

        if (label != null)
            label.text = itemLabel;

        if (icon != null)
        {
            icon.sprite = itemIcon;
            icon.enabled = itemIcon != null; // 아이콘이 없는 아이템(또는 빈 슬롯)이면 아예 안 보이게
            if (itemIcon != null)
            {
                icon.color = Color.white;
                icon.preserveAspect = true;
            }
        }
    }
}