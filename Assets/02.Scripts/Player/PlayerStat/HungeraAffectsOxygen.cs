using UnityEngine;

// 배고픔(DepletingStat)이 낮을수록 산소(DepletingStat)의 소모 속도를 빠르게 만드는 연결 스크립트.
// 배고픔 자체는 체력에 직접 영향을 안 주고, 산소 소모 속도라는 "다른 스탯"에만 영향을 준다.
public class HungerAffectsOxygen : MonoBehaviour
{
    [Tooltip("배고픔 수치")]
    public HungerStat hunger;
    [Tooltip("영향을 받을 산소 수치")]
    public OxygenStat oxygen;
    [Tooltip("배고픔이 완전히 0일 때, 산소 소모 속도가 기본값의 몇 배가 되는지")]
    public float maxDrainMultiplier = 2f;

    private float baseOxygenDepletionRate;

    void Awake()
    {
        // 산소의 "원래" 소모 속도를 기억해뒀다가, 그걸 기준으로 배율을 곱함
        baseOxygenDepletionRate = oxygen.depletionRatePerSecond;
    }

    void Update()
    {
        float hungerRatio = hunger.CurrentValue / hunger.maxValue; // 1 = 배부름, 0 = 배고픔
        float multiplier = Mathf.Lerp(maxDrainMultiplier, 1f, hungerRatio);
        oxygen.depletionRatePerSecond = baseOxygenDepletionRate * multiplier;
    }
}