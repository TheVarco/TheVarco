using System.Collections.Generic;
using Fusion;
using UnityEngine;
using GogoGaga.OptimizedRopesAndCables;

// 회오리에 갇혔는지와 무관하게, 누구나 밧줄에 맞으면 연결되어 당겨질 수 있게 하는 범용 컴포넌트.
// SpringJoint를 써서 "특정 길이까지는 헐렁하고 그 이상 벌어지면 팽팽하게 당기는" 밧줄 느낌을 낸다.
//
// 네트워크에서는 "누가 나를 당기고 있는가"(PullerId)만 복제하고, 조인트와 로프 비주얼은
// 각 머신이 그 값을 보고 스스로 만든다. 조인트를 던진 사람 머신에만 만들면,
// 모든 플레이어를 시뮬레이션하는 호스트에는 힘이 안 걸려서 실제로는 아무도 안 끌려간다.
[RequireComponent(typeof(Rigidbody))]
public class PlayerRopeTarget : NetworkBehaviour
{
    [Header("밧줄 연결 설정 (밸런싱용)")]
    [Tooltip("이 길이까지는 밧줄이 헐렁해서 자유롭게 움직일 수 있음. 이 거리를 넘어서야 팽팽해지며 당겨짐")]
    public float leashLength = 2f;
    [Tooltip("밧줄이 팽팽해졌을 때 당기는 세기 (SpringJoint의 spring 값)")]
    public float ropeSpring = 40f;
    [Tooltip("출렁임을 줄이는 감쇠값. 2×√ropeSpring(현재 약 12.6)보다 낮으면 당긴 뒤 상대를 지나쳐 튕겨나간다")]
    public float ropeDamper = 15f;
    [Tooltip("밧줄이 자기 길이보다 이만큼 더 늘어나면 끊어짐. 절대 거리가 아니라 '늘어난 정도' 기준 — " +
             "밧줄 길이는 던진 거리마다 다르므로, 절대 거리로 재면 멀리서 맞힌 긴 밧줄이 당기기도 전에 끊긴다")]
    public float ropeBreakStretch = 6f;

    [Header("줄 시각 효과")]
    [Tooltip("입체적인 로프를 그려주는 프리팹 (GogoGaga Rope 컴포넌트가 붙은 것)")]
    public GameObject ropeVisualPrefab;
    [Tooltip("연결이 끊어진 뒤에도 이 시간(초) 동안은 로프가 잠깐 남아있다가 사라짐")]
    public float ropeLingerDuration = 0.5f;

    // 나를 당기고 있는 사람. 유효하지 않으면 연결 안 된 상태
    [Networked, OnChangedRender(nameof(OnPullerChanged))]
    private NetworkId PullerId { get; set; }

    // 붙는 순간의 거리가 곧 밧줄의 자연 길이. 머신마다 따로 재면 예측이 어긋나서 복제한다
    [Networked] private float AttachedLeash { get; set; }

    // "내가 지금 누굴 묶고 있나"를 던진 쪽에서 찾아야 해서 활성 대상을 모아둔다
    private static readonly List<PlayerRopeTarget> Active = new List<PlayerRopeTarget>();

    public static PlayerRopeTarget FindPulledBy(NetworkId pullerId)
    {
        if (!pullerId.IsValid) return null;

        foreach (PlayerRopeTarget target in Active)
            if (target != null && target.PullerId == pullerId) return target;

        return null;
    }

    public bool IsRopeAttached => ropePuller != null;

    // 당기는 쪽. PlayerWhirlpoolState가 "구조자도 같이 갇혀있는지" 확인하는 데 씀
    public Transform RopePuller => ropePuller;

    private Transform ropePuller;
    private Rope visualRope;
    private Rigidbody rb;
    private SpringJoint joint;

    void Awake() => rb = GetComponent<Rigidbody>();

    public override void Spawned()
    {
        Active.Add(this);
        OnPullerChanged(); // 도중 합류한 사람도 이미 걸려 있는 밧줄을 보게
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Active.Remove(this);
        ClearLocalRope(true); // 끝점 Transform이 이미 파괴돼서, 지연 파괴하면 그동안 예외가 남
    }

    // 거리 초과로 끊는 판정은 호스트만. 각자 판단하면 머신마다 다른 시점에 끊긴다
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !PullerId.IsValid) return;

        // 렌더 콜백이 채우는 ropePuller가 아니라 복제값을 직접 해석한다.
        // ropePuller는 롤백되지 않는 렌더 파생값이라 리시뮬레이션에서 틱마다 결과가 달라질 수 있음
        if (!Runner.TryFindObject(PullerId, out NetworkObject puller))
        {
            PullerId = default; // 당기던 사람이 나갔다. 안 지우면 조인트가 월드에 고정된 채 남는다
            return;
        }

        // 늘어난 정도로 판정. 13m짜리 밧줄은 19m에서, 3m짜리는 9m에서 끊긴다
        if (Vector3.Distance(transform.position, puller.transform.position) > AttachedLeash + ropeBreakStretch)
            PullerId = default;
    }

    // 투사체가 명중했을 때 호스트가 호출
    public void SetPuller(NetworkId puller)
    {
        if (PullerId.IsValid) return; // 이미 걸린 밧줄이 있으면 먼저 온 쪽이 이긴다

        // 길이를 leashLength로 고정하면 13m에서 붙는 순간 장력이 40×11=440이 되어
        // 갇힌 사람뿐 아니라 구조자까지 서로에게 확 딸려간다. 던진 거리가 곧 밧줄 길이
        AttachedLeash = Runner.TryFindObject(puller, out NetworkObject pullerObject)
            ? Mathf.Max(leashLength, Vector3.Distance(transform.position, pullerObject.transform.position))
            : leashLength;

        PullerId = puller;
    }

    // 던진 사람이 다시 눌러 해제할 때. 클라이언트는 [Networked] 값을 직접 못 바꾸니 호스트에 요청
    public void RequestDetach()
    {
        if (Object != null) RPC_RequestDetach();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDetach() => PullerId = default;

    // PullerId가 바뀌면 모든 머신에서 실행. 각자 자기 화면에 로프를 만들고 조인트를 건다
    private void OnPullerChanged()
    {
        ClearLocalRope();

        if (!Runner.TryFindObject(PullerId, out NetworkObject puller)) return;

        ropePuller = puller.transform;
        visualRope = SpawnVisualRope(ropeVisualPrefab, puller.transform, transform);

        // 조인트를 kinematic 프록시에 붙여도 connectedBody 쪽에는 힘이 전달된다.
        // 당기는 사람이 내 캐릭터면 조인트가 있어야 반작용을 느껴 호스트와 예측이 맞는다
        // (없으면 줄이 팽팽할 때 구조자 화면이 떨림). 양쪽 다 프록시일 때만 건너뛴다
        if (Object.IsProxy && puller.IsProxy) return;

        Rigidbody pullerRb = puller.GetComponent<Rigidbody>();
        if (pullerRb == null) return;

        joint = gameObject.AddComponent<SpringJoint>();
        joint.connectedBody = pullerRb;

        // 자동 연결점은 "붙는 순간의 위치"를 구조자 로컬 좌표에 박아버려서, 구조자 몸에서
        // 뻗어나온 긴 막대기처럼 동작한다 (회전하면 상대를 휘두르고, 다가가면 밀어냄).
        // 양쪽 몸 중심을 직접 이어야 가까워지면 그냥 느슨해지는 진짜 밧줄이 된다
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = Vector3.zero;
        joint.spring = ropeSpring;
        joint.damper = ropeDamper;
        joint.minDistance = 0f;
        joint.maxDistance = AttachedLeash; // 이 길이까지는 헐렁하고, 넘어간 만큼만 팽팽하게 당김
    }

    // 로프 비주얼 만들기. RopeProjectile도 같은 방식으로 그려야 해서 여기 모아둠
    // (구현이 두 벌이면 언젠가 어긋난다)
    public static Rope SpawnVisualRope(GameObject prefab, Transform ownerRoot, Transform end)
    {
        if (prefab == null || ownerRoot == null || end == null) return null;

        // 손 뼈를 다시 찾지 않고 핫바가 이미 오른손에 붙여둔 소켓을 그대로 쓴다
        PlayerHotbar hotbar = ownerRoot.GetComponent<PlayerHotbar>();
        Transform start = (hotbar != null && hotbar.handSocket != null) ? hotbar.handSocket : ownerRoot;

        // 비활성으로 만들어서 Awake 전에 시작점/끝점을 채운다 (첫 프레임에 엉뚱한 자리에 그려지는 것 방지)
        GameObject visualObj = UnityEngine.Object.Instantiate(prefab);
        visualObj.SetActive(false);

        Rope rope = visualObj.GetComponent<Rope>();
        if (rope != null)
        {
            rope.SetStartPoint(start, false);
            rope.SetEndPoint(end, false);
        }

        visualObj.SetActive(true);
        return rope;
    }

    // immediate = despawn 경로. 끝점 Transform이 이미 파괴된 상태라 지연 파괴하면
    // 그 시간 동안 로프가 매 프레임 파괴된 참조를 읽어서 예외가 쏟아진다
    private void ClearLocalRope(bool immediate = false)
    {
        if (joint != null) Destroy(joint);
        joint = null;

        if (visualRope != null)
            Destroy(visualRope.gameObject, immediate ? 0f : ropeLingerDuration); // 평소엔 잠깐 남았다가 사라짐
        visualRope = null;

        ropePuller = null;
    }
}
