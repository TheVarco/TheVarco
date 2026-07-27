using UnityEngine;
using UnityEngine.Events;

// 산소, 배고픔처럼 "시간이 지나면 자동으로 줄어드는 수치"의 공용 부품.
// 이 자체는 "무엇이 줄어드는지"만 관리하고, 0이 됐을 때 실제로 뭘 할지는
// 이 컴포넌트를 참조하는 다른 스크립트(OxygenDrowning 등)가 결정한다.
public class DepletingStat : MonoBehaviour
{
    [Header("공용 수치 (밸런싱용)")]
    public float maxValue = 100f;
    [Tooltip("초당 자동으로 줄어드는 양. 다른 스탯이 이 값을 실시간으로 바꿀 수도 있음 (예: 배고픔이 산소 소모를 가속)")]
    public float depletionRatePerSecond = 1f;

    public float CurrentValue { get; private set; }
    public bool IsDepleted => CurrentValue <= 0f;

    // 배고픔 등으로 maxValue 자체가 줄어들어도, "원래 최대치가 뭐였는지"는 따로 기억해둠
    // (UI에서 "전체 대비 얼마나 잠겼는지"를 그리려면 이 원래값이 필요함)
    public float BaseMaxValue { get; private set; }

    [Header("이벤트 (UI, 사망 연동 등에서 구독)")]
    public UnityEvent<float, float> OnValueChanged; // (현재값, 최대값)
    public UnityEvent OnDepleted;      // 0에 막 도달한 "그 순간" 한 번만
    public UnityEvent OnReplenished;   // 0이었다가 다시 채워진 "그 순간" 한 번만

    void Awake()
    {
        BaseMaxValue = maxValue; // 시작할 때 값을 "원래 최대치"로 고정 기억
        CurrentValue = maxValue;
    }

    protected virtual void Update()
    {
        Deplete(depletionRatePerSecond * Time.deltaTime);
    }

    public void Deplete(float amount)
    {
        bool wasDepleted = IsDepleted;
        CurrentValue = Mathf.Max(0f, CurrentValue - amount);
        OnValueChanged?.Invoke(CurrentValue, maxValue);

        if (!wasDepleted && IsDepleted)
            OnDepleted?.Invoke();
    }

    public void Refill(float amount)
    {
        bool wasDepleted = IsDepleted;
        CurrentValue = Mathf.Min(maxValue, CurrentValue + amount);
        OnValueChanged?.Invoke(CurrentValue, maxValue);

        if (wasDepleted && !IsDepleted)
            OnReplenished?.Invoke();
    }

    // 부활처럼 "최대치의 몇 %로 딱 맞춰야" 할 때 쓰는 함수
    public void SetValueRatio(float ratio)
    {
        bool wasDepleted = IsDepleted;
        CurrentValue = maxValue * Mathf.Clamp01(ratio);
        OnValueChanged?.Invoke(CurrentValue, maxValue);

        if (wasDepleted && !IsDepleted)
            OnReplenished?.Invoke();
    }

    // 배고픔 등 다른 스탯이 "이 수치의 최대치 자체"를 줄이거나 늘릴 때 쓰는 함수.
    // BaseMaxValue(원래 최대치)를 넘거나 0 밑으로는 못 내려가게 막고,
    // 줄어든 최대치보다 지금 값이 더 크면(그릇보다 물이 많으면) 같이 깎아줌
    public void SetMaxValue(float newMax)
    {
        maxValue = Mathf.Clamp(newMax, 0f, BaseMaxValue);

        if (CurrentValue > maxValue)
        {
            CurrentValue = maxValue;
        }

        OnValueChanged?.Invoke(CurrentValue, maxValue);
    }
}