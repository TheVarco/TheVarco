using Fusion;
using UnityEngine;
using UnityEngine.Events;

// 산소, 배고픔처럼 "시간이 지나면 자동으로 줄어드는 수치"의 공용 부품.
// 이 자체는 "무엇이 줄어드는지"만 관리하고, 0이 됐을 때 실제로 뭘 할지는
// 이 컴포넌트를 참조하는 다른 스크립트(OxygenDrowning 등)가 결정한다.
//
// 값은 State Authority(호스트)가 소유한다. 자동 감소도 호스트에서만 돌고,
// 다른 머신은 복제된 값을 받아 쓴다. 아이템 사용처럼 클라이언트가 값을 바꿔야 할 때는
// RPC로 호스트에 요청한다. (러너가 없는 씬에서는 예전처럼 전부 로컬에서 처리)
public class DepletingStat : NetworkBehaviour
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

    [Networked, OnChangedRender(nameof(OnNetworkedValueChanged))]
    private float NetworkedValue { get; set; }

    // 러너가 없는 씬(팀원 테스트 씬 등)에서는 권한 개념이 없으므로 로컬에서 전부 처리한다
    protected bool HasAuthority => Object == null || Object.HasStateAuthority;

    void Awake()
    {
        BaseMaxValue = maxValue; // 시작할 때 값을 "원래 최대치"로 고정 기억
        CurrentValue = maxValue;
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority) NetworkedValue = CurrentValue;
    }

    protected virtual void Update()
    {
        if (!HasAuthority) return; // 자동 감소는 권한자만 (안 그러면 머신마다 따로 닳음)
        Deplete(depletionRatePerSecond * Time.deltaTime);
    }

    public void Deplete(float amount)
    {
        if (!HasAuthority) { RPC_RequestDelta(-amount); return; }
        ApplyAuthoritative(CurrentValue - amount);
    }

    public void Refill(float amount)
    {
        if (!HasAuthority) { RPC_RequestDelta(amount); return; }
        ApplyAuthoritative(CurrentValue + amount);
    }

    // 부활처럼 "최대치의 몇 %로 딱 맞춰야" 할 때 쓰는 함수
    public void SetValueRatio(float ratio)
    {
        if (!HasAuthority) { RPC_RequestRatio(ratio); return; }
        ApplyAuthoritative(maxValue * Mathf.Clamp01(ratio));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDelta(float delta) => ApplyAuthoritative(CurrentValue + delta);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRatio(float ratio) => ApplyAuthoritative(maxValue * Mathf.Clamp01(ratio));

    // 권한자 경로: 값을 바꾸고 네트워크에 실어보낸다
    private void ApplyAuthoritative(float value)
    {
        ApplyLocal(value);
        if (Object != null) NetworkedValue = CurrentValue;
    }

    // 복제된 값을 받았을 때. 여기서 다시 네트워크에 쓰면 안 됨
    private void OnNetworkedValueChanged()
    {
        if (Object.HasStateAuthority) return;
        ApplyLocal(NetworkedValue);
    }

    private void ApplyLocal(float value)
    {
        bool wasDepleted = IsDepleted;
        CurrentValue = Mathf.Clamp(value, 0f, maxValue);

        OnValueChanged?.Invoke(CurrentValue, maxValue);

        if (!wasDepleted && IsDepleted) OnDepleted?.Invoke();
        else if (wasDepleted && !IsDepleted) OnReplenished?.Invoke();
    }

    // 배고픔 등 다른 스탯이 "이 수치의 최대치 자체"를 줄이거나 늘릴 때 쓰는 함수.
    // 배고픔에서 결정론적으로 계산되는 값이라 각 머신이 같은 결과를 내므로 네트워크로 보내지 않는다.
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
