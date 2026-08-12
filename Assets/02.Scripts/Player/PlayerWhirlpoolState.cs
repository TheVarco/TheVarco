using Fusion;
using UnityEngine;

// Player에 붙어서 회오리에 끌리는 힘과 "지금 갇혀있는지"를 관리.
// 조작을 꺼버리지 않고, 당기는 힘 자체가 세서 혼자 못 빠져나가는 물리적인 방식만 씀.
//
// 힘을 회오리 쪽이 아니라 플레이어 쪽에서 거는 이유는 FixedUpdateNetwork에 들어가야 하기 때문.
// Unity FixedUpdate에서 걸면 클라이언트 예측이 되감길 때 회오리 힘만 재계산되지 않아 위치가 벌어진다.
[RequireComponent(typeof(Rigidbody))]
public class PlayerWhirlpoolState : NetworkBehaviour
{
    [Header("구조된 직후 설정")]
    [Tooltip("구조되자마자 다시 붙잡히지 않도록 면역이 되는 시간(초)")]
    public float rescueImmunityDuration = 2f;

    [Tooltip("갇힘이 풀리는 거리 배수. 안쪽 반경 × 이 값 밖으로 나가야 풀린다 (경계선에서 깜빡이는 것 방지)")]
    public float releaseRadiusMultiplier = 1.4f;

    [Header("밧줄 구조 설정")]
    [Tooltip("밧줄에 묶여 있는 동안 이 사람이 받는 회오리 힘에 곱해지는 값 (회오리 자체가 약해지는 게 아님). " +
             "구조자와 갇힌 사람의 스탯이 같아서, 이게 없으면 밧줄만으로는 수학적으로 못 끌어냄")]
    [Range(0f, 1f)] public float ropedPullMultiplier = 0.2f;

    // 남의 화면에도 "갇힘!"이 떠야 해서 복제한다. 러너 없는 씬에서는 아래 로컬 값을 씀
    [Networked, OnChangedRender(nameof(HandleTrappedChanged))]
    private NetworkBool NetworkedTrapped { get; set; }
    private bool localTrapped;

    public bool IsTrapped => Object != null ? (bool)NetworkedTrapped : localTrapped;

    // 표시용 컴포넌트가 구독한다. [Networked] 값을 시뮬레이션 루프 밖(Update/LateUpdate)에서
    // 읽으면 보간 버퍼의 어느 스냅샷이 잡히는지가 프레임마다 달라져서 값이 튄다.
    // 바뀐 순간에 한 번만 알려주는 식으로 가야 안 깜빡임
    public event System.Action<bool> TrappedChanged;

    private float immunityTimer;
    private Rigidbody rb;
    private PlayerRopeTarget ropeTarget;

    // 갇힘 판정은 호스트(또는 러너 없는 씬의 본인)만 내린다
    private bool HasAuthority => Object == null || Object.HasStateAuthority;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ropeTarget = GetComponent<PlayerRopeTarget>();
    }

    // 게임 도중 합류한 사람도 이미 갇혀있는 사람을 볼 수 있게
    public override void Spawned() => TrappedChanged?.Invoke(IsTrapped);

    // 프록시는 Physics Addon이 강제로 kinematic이라 힘이 안 먹는다.
    // 갇힘 판정도 그 사람을 실제로 시뮬레이션하는 쪽(호스트 / 본인 클라이언트)이 내리게 둔다
    public override void FixedUpdateNetwork()
    {
        if (Object.IsProxy) return;
        ApplyWhirlpools(Runner.DeltaTime);
    }

    // 러너 없는 씬(팀원 테스트 씬) 폴백
    void FixedUpdate()
    {
        if (Object != null) return;
        ApplyWhirlpools(Time.fixedDeltaTime);
    }

    private void ApplyWhirlpools(float deltaTime)
    {
        bool rescued = IsRescuedFromOutside();
        bool insideTrapLine = false;    // 들어가는 선 (innerRadius)
        bool insideReleaseLine = false; // 나오는 선 (innerRadius × releaseRadiusMultiplier)
        float strongestDamping = 0f;

        foreach (Whirlpool whirlpool in Whirlpool.Active)
        {
            Vector3 toCenter = whirlpool.transform.position - rb.position;
            float dist = toCenter.magnitude;
            if (dist > whirlpool.outerRadius) continue;

            // 정중앙에 정확히 멈추면 방향 벡터가 0이라 당기는 힘만 건너뛴다.
            // 여기서 continue하면 아래 갇힘 판정까지 건너뛰어 "회오리 밖"으로 취급되고,
            // 감쇠가 플레이어를 정확히 중앙에 세우기 때문에 그 상태로 눌러앉는다
            if (dist > 0.001f)
            {
                float pull = whirlpool.GetPullForce(dist);
                if (rescued) pull *= ropedPullMultiplier;
                rb.AddForce(toCenter.normalized * pull, ForceMode.Acceleration);
            }

            // 루프 안에서 걸면 겹친 회오리 수만큼 중복 적용된다. 가장 센 값만 기억해뒀다가 밖에서 한 번
            strongestDamping = Mathf.Max(strongestDamping, whirlpool.pullDamping);

            if (dist <= whirlpool.innerRadius) insideTrapLine = true;
            if (dist <= whirlpool.innerRadius * releaseRadiusMultiplier) insideReleaseLine = true;
        }

        // 감쇠가 없으면 중심을 관통해 진동한다. 갇힌 사람이 튕겨다니면서
        // 판정선을 계속 넘나들어 "갇힘!" 문구가 깜빡임
        if (strongestDamping > 0f)
            rb.AddForce(-rb.linearVelocity * strongestDamping, ForceMode.Acceleration);

        // 힘까지는 예측을 위해 본인 클라이언트도 걸지만,
        // 갇힘 표시는 [Networked] 값이라 State Authority만 쓸 수 있다
        if (!HasAuthority) return;

        if (immunityTimer > 0f) immunityTimer -= deltaTime;

        if (!IsTrapped && insideTrapLine && immunityTimer <= 0f)
        {
            SetTrapped(true);
        }
        // 안쪽 반경보다 여유 있게 나가야 풀린다. 두 선이 같으면 경계에서 흔들릴 때마다
        // 풀림 → 면역 2초 → 그동안 한가운데 있어도 안 잡힘, 이 구멍이 생김
        else if (IsTrapped && !insideReleaseLine)
        {
            SetTrapped(false);
            immunityTimer = rescueImmunityDuration;
        }
    }

    // 밖에 있는 사람이 잡아줄 때만 회오리 힘이 약해진다.
    // 둘 다 갇힌 채로 서로 묶으면 공짜 탈출이 되므로 구조자가 갇혀있는지 확인
    private bool IsRescuedFromOutside()
    {
        if (ropeTarget == null || !ropeTarget.IsRopeAttached) return false;

        Transform puller = ropeTarget.RopePuller;
        if (puller == null) return false;

        PlayerWhirlpoolState pullerState = puller.GetComponent<PlayerWhirlpoolState>();
        return pullerState == null || !pullerState.IsTrapped; // 사람이 아닌 게 당기면(잠수함 등) 통과
    }

    private void SetTrapped(bool value)
    {
        if (Object != null) { NetworkedTrapped = value; return; } // OnChangedRender가 알려줌
        localTrapped = value;
        TrappedChanged?.Invoke(value);
    }

    private void HandleTrappedChanged() => TrappedChanged?.Invoke(IsTrapped);
}
