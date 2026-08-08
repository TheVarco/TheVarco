using Fusion;
using UnityEngine;

// 체력(Health)이 0이 되면 "기절" 상태로 전환하는 스크립트.
// 죽어서 게임이 끝나는 게 아니라, 조작 불가 + 몸이 떠오르는 상태가 되고
// 동료가 PlayerReviver로 부활시켜줄 때까지 대기한다.
[RequireComponent(typeof(Health))]
public class PlayerDownedState : NetworkBehaviour
{
    [Header("기절 중 비활성화할 컴포넌트들 (조작 불가)")]
    [Tooltip("PlayerController, MeleeAttack, RangedAttack, PlayerGrabber, PlayerInteractor 등을 여기에 연결")]
    public MonoBehaviour[] disableWhileDowned;

    [Header("기절 중 물리 (수중이라 위로 떠오름)")]
    public float floatUpForce = 2f;

    [Header("부활 시 회복 비율")]
    [Range(0f, 1f)] public float reviveHealthRatio = 0.3f;
    [Range(0f, 1f)] public float reviveOxygenRatio = 0.5f;

    [Tooltip("산소 스탯 연결 (없으면 산소는 회복 안 함)")]
    public OxygenStat oxygen;

    public bool IsDowned { get; private set; }

    public event System.Action OnDowned;
    public event System.Action OnRevived;

    private Health health;
    private Rigidbody rb;

    void Awake()
    {
        health = GetComponent<Health>();
        rb = GetComponent<Rigidbody>();
        health.OnDeath.AddListener(EnterDownedState);
        // 사망이 OnDeath로 모든 머신에 퍼지는 것과 대칭. 부활도 모든 머신에서 상태가 풀려야 한다
        health.OnRevived.AddListener(ExitDownedState);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return; // 물리는 시뮬레이션 권한 있는 쪽(호스트)만 적용
        ApplyFloatUp();
    }

    // 러너가 없는 씬(팀원 테스트 씬 등)에서는 FixedUpdateNetwork가 호출되지 않으므로 여기서 대신 처리
    void FixedUpdate()
    {
        if (Object != null) return;
        ApplyFloatUp();
    }

    private void ApplyFloatUp()
    {
        if (IsDowned && rb != null)
        {
            // 익사 등으로 기절하면 시체가 가라앉지 않고 위로 떠오르게
            rb.AddForce(Vector3.up * floatUpForce, ForceMode.Acceleration);
        }
    }

    private void EnterDownedState()
    {
        IsDowned = true;

        foreach (MonoBehaviour comp in disableWhileDowned)
        {
            if (comp != null) comp.enabled = false;
        }

        if (rb != null) rb.useGravity = false;

        OnDowned?.Invoke();
    }

    // PlayerReviver가 채널링을 다 채웠을 때 호출. 부활시키는 쪽이 State Authority가 아닐 수 있어서
    // RPC로 State Authority(호스트)에 요청만 하고, 실제 처리는 호스트에서 Revive()가 실행됨
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRevive()
    {
        Revive();
    }

    // 권한자(호스트)에서만 실행된다. 실제 상태 해제는 Health.OnRevived를 통해 모든 머신에서 일어남
    public void Revive()
    {
        if (!IsDowned) return;

        health.ReviveWithRatio(reviveHealthRatio);

        if (oxygen != null)
            oxygen.SetValueRatio(reviveOxygenRatio); // 산소도 네트워크화돼서 알아서 전파됨
    }

    // Health.OnRevived로 모든 머신에서 실행됨 (호스트는 직접, 나머지는 SyncFrom을 통해)
    private void ExitDownedState()
    {
        if (!IsDowned) return;

        IsDowned = false;

        foreach (MonoBehaviour comp in disableWhileDowned)
        {
            if (comp != null) comp.enabled = true;
        }

        OnRevived?.Invoke();
    }
}