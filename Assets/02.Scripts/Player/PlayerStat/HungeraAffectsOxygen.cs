using UnityEngine;

// 배고픔(HungerStat)이 낮을수록 산소(OxygenStat)의 "최대치 자체"를 줄이는 연결 스크립트.
// (예전 버전은 소모 "속도"를 빠르게 하는 방식이었는데, PEAK처럼 최대치 자체가 줄어드는 방식으로 변경)
public class HungerAffectsOxygen : MonoBehaviour
{
    [Tooltip("배고픔 수치")]
    public HungerStat hunger;
    [Tooltip("영향을 받을 산소 수치")]
    public OxygenStat oxygen;

    [Tooltip("배고픔이 완전히 0일 때, 산소 최대치가 최소 어디까지 줄어들 수 있는지 (0으로 두면 완전히 못 채우게 됨)")]
    public float minOxygenMax = 40f;

    void Update()
    {
        float hungerRatio = hunger.CurrentValue / hunger.BaseMaxValue; // 1 = 배부름, 0 = 완전히 배고픔
        float reducedMax = Mathf.Lerp(minOxygenMax, oxygen.BaseMaxValue, hungerRatio);

        oxygen.SetMaxValue(reducedMax);
    }
}