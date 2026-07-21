using UnityEngine;

// 체력(Health)이 0이 되면 "기절" 상태로 전환하는 스크립트.
// 죽어서 게임이 끝나는 게 아니라, 조작 불가 + 몸이 떠오르는 상태가 되고
// 동료가 PlayerReviver로 부활시켜줄 때까지 대기한다.
[RequireComponent(typeof(Health))]
public class PlayerDownedState : MonoBehaviour
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

    private Health health;
    private Rigidbody rb;

    void Awake()
    {
        health = GetComponent<Health>();
        rb = GetComponent<Rigidbody>();
        health.OnDeath.AddListener(EnterDownedState);
    }

    void FixedUpdate()
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
    }

    // PlayerReviver가 채널링을 다 채웠을 때 호출
    public void Revive()
    {
        if (!IsDowned) return;

        IsDowned = false;

        foreach (MonoBehaviour comp in disableWhileDowned)
        {
            if (comp != null) comp.enabled = true;
        }

        health.ReviveWithRatio(reviveHealthRatio);

        if (oxygen != null)
            oxygen.SetValueRatio(reviveOxygenRatio);
    }
}