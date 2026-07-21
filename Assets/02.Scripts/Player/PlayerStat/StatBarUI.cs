using UnityEngine;
using UnityEngine.UI;

// 체력바, 산소바처럼 "현재값/최대값을 막대로 보여주는" 모든 UI가 공용으로 쓰는 스크립트.
// Health.OnHealthChanged나 OxygenStat.OnValueChanged 같은 (현재값, 최대값) 신호를
// Inspector에서 이 UpdateBar 함수에 직접 연결하면 끝난다 (별도 스크립트 필요 없음).
public class StatBarUI : MonoBehaviour
{
    [Tooltip("배경/채움/손잡이가 기본 세팅된 Unity Slider")]
    public Slider slider;

    public void UpdateBar(float current, float max)
    {
        if (slider == null || max <= 0f) return;

        // Slider 자체의 범위를 매번 맞춰주면, fillAmount를 직접 계산할 필요 없이
        // Slider가 알아서 "현재/최대" 비율만큼 채워줌
        slider.maxValue = max;
        slider.value = current;
    }
}