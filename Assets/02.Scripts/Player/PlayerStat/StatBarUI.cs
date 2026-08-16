using UnityEngine;
using UnityEngine.UI;

// 체력바, 산소바처럼 "현재값/최대값을 막대로 보여주는" 모든 UI가 공용으로 쓰는 스크립트.
// Health.OnHealthChanged나 OxygenStat.OnValueChanged 같은 (현재값, 최대값) 신호를
// UpdateBar 함수에 직접 연결하면 체력바와 동일하게 동작한다.
public class StatBarUI : MonoBehaviour
{
    [Tooltip("배경/채움/손잡이가 기본 세팅된 Unity Slider")]
    public Slider slider;

    [Tooltip("슬라이더 대신 채움 Image를 직접 참조할 경우")]
    public Image fillImage;

    [Header("배고픔 알림 아이콘 (선택 사항)")]
    [Tooltip("배고픔이 생기면(최대치 감소 또는 배고픔 수치 감소 시) 활성화할 물고기(식량) 이미지")]
    public Image fishImage;

    [HideInInspector] public HungerStat hungerStat;
    [HideInInspector] public OxygenStat oxygenStat;

    private RectTransform fillRect;

    void Awake()
    {
        AutoBind();
        ApplyHatchedStyle();
    }

    private void ApplyHatchedStyle()
    {
        RectTransform targetFill = slider != null && slider.fillRect != null
            ? slider.fillRect
            : fillRect;

        if (targetFill != null)
            HatchedStatBarStyle.Ensure(gameObject, targetFill);
    }

    public void AutoBind()
    {
        if (slider == null)
        {
            slider = GetComponent<Slider>();
            if (slider == null)
                slider = GetComponentInChildren<Slider>();
        }

        if (fillRect == null)
        {
            if (slider != null && slider.fillRect != null)
            {
                fillRect = slider.fillRect;
            }
            else
            {
                Transform fillArea = transform.Find("Fill Area");
                if (fillArea != null)
                {
                    Transform fillObj = fillArea.Find("Fill");
                    if (fillObj != null) fillRect = fillObj as RectTransform;
                }

                if (fillRect == null)
                {
                    Transform fillObj = transform.Find("Fill");
                    if (fillObj != null) fillRect = fillObj as RectTransform;
                }

                if (fillRect == null && fillImage != null)
                {
                    fillRect = fillImage.rectTransform;
                }
            }
        }

        if (fillImage == null && fillRect != null)
        {
            fillImage = fillRect.GetComponent<Image>();
        }

        if (fishImage == null)
        {
            Transform fishObj = transform.Find("FishImage");
            if (fishObj == null) fishObj = transform.Find("Fish");
            if (fishObj != null)
            {
                fishImage = fishObj.GetComponent<Image>();
            }
        }
    }

    void Update()
    {
        UpdateFishImage();
    }

    private void UpdateFishImage()
    {
        if (fishImage == null) return;

        if (hungerStat == null && oxygenStat == null)
        {
            hungerStat = FindFirstObjectByType<HungerStat>();
            oxygenStat = FindFirstObjectByType<OxygenStat>();
            if (hungerStat == null && oxygenStat == null) return;
        }

        bool isHungry = false;
        if (hungerStat != null)
        {
            isHungry = hungerStat.CurrentValue < hungerStat.BaseMaxValue - 0.1f;
        }
        else if (oxygenStat != null)
        {
            isHungry = oxygenStat.maxValue < oxygenStat.BaseMaxValue - 0.1f;
        }

        if (fishImage.gameObject.activeSelf != isHungry)
        {
            fishImage.gameObject.SetActive(isHungry);
        }
    }

    public void UpdateBar(float current, float max)
    {
        if (slider == null && fillRect == null)
            AutoBind();

        if (max <= 0f) return;

        // 1. Slider 컴포넌트가 있으면 Slider 방식으로 갱신 (체력바와 동일)
        if (slider != null)
        {
            slider.maxValue = max;
            slider.value = current;
            return;
        }

        // 2. Image Type을 강제로 변경하지 않고 Sliced/Simple/Filled 모두 지원
        float ratio = Mathf.Clamp01(current / max);

        if (fillImage != null && fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = ratio;
        }
        else if (fillRect != null)
        {
            // Slider의 내부 동작과 동일하게 RectTransform의 anchorMax.x를 조절하여 Sliced 형태 그대로 너비 조절
            fillRect.anchorMin = new Vector2(0f, fillRect.anchorMin.y);
            fillRect.anchorMax = new Vector2(ratio, fillRect.anchorMax.y);
            fillRect.offsetMin = new Vector2(0f, fillRect.offsetMin.y);
            fillRect.offsetMax = new Vector2(0f, fillRect.offsetMax.y);
        }
    }
}
