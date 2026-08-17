using UnityEngine;
using UnityEngine.UI;

// 산소바처럼 "현재 값"뿐 아니라 "배고픔 때문에 최대치가 줄어서 잠긴 구간"까지 같이 보여주는 UI.
// 매 프레임 DepletingStat(OxygenStat 등)을 직접 확인해서 두 이미지의 채움 정도를 갱신한다.
public class SegmentedStatBarUI : MonoBehaviour
{
    [Tooltip("확인할 대상 (OxygenStat 등)")]
    public DepletingStat stat;

    [Header("이미지 연결")]
    [Tooltip("현재 값만큼 채워지는 이미지")]
    public Image fillImage;
    [Tooltip("최대치가 줄어든 만큼(잠긴 구간) 채워지는 이미지")]
    public Image lockedOverlay;
    [Tooltip("배고픔 발생 시 활성화할 물고기(식량) 이미지")]
    public Image fishImage;

    private RectTransform fillRect;
    private RectTransform lockedRect;

    void Awake()
    {
        if (fillImage != null) fillRect = fillImage.rectTransform;
        if (lockedOverlay != null) lockedRect = lockedOverlay.rectTransform;
        if (fishImage == null)
        {
            Transform fishObj = transform.Find("FishImage");
            if (fishObj == null) fishObj = transform.Find("Fish");
            if (fishObj != null) fishImage = fishObj.GetComponent<Image>();
        }

        if (fillRect != null)
            HatchedStatBarStyle.Ensure(gameObject, fillRect, lockedRect);
    }

    void Update()
    {
        if (stat == null) return;

        float baseMax = stat.BaseMaxValue;
        if (baseMax <= 0f) return;

        // 1. 초록색 부분: "원래 최대치" 대비 지금 값이 얼마나 차있는지 (Image Type을 강제 변경하지 않고 지원)
        if (fillImage != null)
        {
            float fillRatio = Mathf.Clamp01(stat.CurrentValue / baseMax);
            if (fillImage.type == Image.Type.Filled)
            {
                fillImage.fillAmount = fillRatio;
            }
            else if (fillRect != null)
            {
                fillRect.anchorMin = new Vector2(0f, fillRect.anchorMin.y);
                fillRect.anchorMax = new Vector2(fillRatio, fillRect.anchorMax.y);
                fillRect.offsetMin = new Vector2(0f, fillRect.offsetMin.y);
                fillRect.offsetMax = new Vector2(0f, fillRect.offsetMax.y);
            }
        }

        // 2. 사선 무늬 부분: "원래 최대치" 대비 지금 최대치가 얼마나 줄어들어서 잠겼는지
        bool isHungry = stat.maxValue < baseMax - 0.1f;
        if (lockedOverlay != null)
        {
            float lockedRatio = Mathf.Clamp01(1f - (stat.maxValue / baseMax));
            if (lockedOverlay.type == Image.Type.Filled)
            {
                lockedOverlay.fillAmount = lockedRatio;
            }
            else if (lockedRect != null)
            {
                lockedRect.anchorMin = new Vector2(1f - lockedRatio, lockedRect.anchorMin.y);
                lockedRect.anchorMax = new Vector2(1f, lockedRect.anchorMax.y);
                lockedRect.offsetMin = new Vector2(0f, lockedRect.offsetMin.y);
                lockedRect.offsetMax = new Vector2(0f, lockedRect.offsetMax.y);
            }
        }

        // 3. 배고픔 발생 시 FishImage 활성화 / 배부르면 비활성화
        if (fishImage != null)
        {
            if (fishImage.gameObject.activeSelf != isHungry)
            {
                fishImage.gameObject.SetActive(isHungry);
            }
        }
    }
}
