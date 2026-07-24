using UnityEngine;
using UnityEngine.UI;

// 산소바처럼 "현재 값"뿐 아니라 "배고픔 때문에 최대치가 줄어서 잠긴 구간"까지 같이 보여주는 UI.
// 매 프레임 DepletingStat(OxygenStat 등)을 직접 확인해서 두 이미지의 채움 정도를 갱신한다.
public class SegmentedStatBarUI : MonoBehaviour
{
    [Tooltip("확인할 대상 (OxygenStat 등)")]
    public DepletingStat stat;

    [Header("이미지 연결")]
    [Tooltip("현재 값만큼 채워지는 이미지. Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left")]
    public Image fillImage;
    [Tooltip("최대치가 줄어든 만큼(잠긴 구간) 채워지는 이미지. Image Type = Filled, Fill Method = Horizontal, Fill Origin = Right")]
    public Image lockedOverlay;

    void Update()
    {
        if (stat == null) return;

        float baseMax = stat.BaseMaxValue;
        if (baseMax <= 0f) return;

        // 초록색 부분: "원래 최대치" 대비 지금 값이 얼마나 차있는지
        if (fillImage != null)
            fillImage.fillAmount = stat.CurrentValue / baseMax;

        // 사선 무늬 부분: "원래 최대치" 대비 지금 최대치가 얼마나 줄어들어서 잠겼는지
        if (lockedOverlay != null)
            lockedOverlay.fillAmount = 1f - (stat.maxValue / baseMax);
    }
}