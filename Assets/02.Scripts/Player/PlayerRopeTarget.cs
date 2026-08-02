using UnityEngine;
using GogoGaga.OptimizedRopesAndCables;

// 회오리에 갇혔는지와 무관하게, 누구나 밧줄에 맞으면 연결되어 당겨질 수 있게 하는 범용 컴포넌트.
// 이전엔 매 프레임 직접 힘을 계산(AddForce)했는데, Unity가 이미 "밧줄처럼 특정 길이까지는
// 자유롭고 그 이상 벌어지면 팽팽하게 당기는" 걸 위한 SpringJoint를 제공해서 그걸로 교체함.
// 훨씬 자연스러운 "당겨지는 손맛"이 남.
[RequireComponent(typeof(Rigidbody))]
public class PlayerRopeTarget : MonoBehaviour
{
    [Header("밧줄 연결 설정 (밸런싱용)")]
    [Tooltip("이 길이까지는 밧줄이 헐렁해서 자유롭게 움직일 수 있음. 이 거리를 넘어서야 팽팽해지며 당겨짐")]
    public float leashLength = 2f;
    [Tooltip("밧줄이 팽팽해졌을 때 당기는 세기 (SpringJoint의 spring 값)")]
    public float ropeSpring = 40f;
    [Tooltip("출렁임을 줄이는 감쇠값 (SpringJoint의 damper 값)")]
    public float ropeDamper = 8f;
    [Tooltip("당기는 사람과 이 거리 이상 멀어지면 밧줄이 끊어짐 (회오리 등 반대쪽 힘에 완전히 밀린 경우)")]
    public float ropeMaxDistance = 15f;

    [Header("줄 시각 효과")]
    [Tooltip("연결이 끊어진 뒤에도 이 시간(초) 동안은 그 자리에 로프가 잠깐 남아있다가 사라짐")]
    public float ropeLingerDuration = 0.5f;

    public bool IsRopeAttached => ropePuller != null;

    // 연결이 끊길 때(이유 상관없이: 거리 초과든, 상대가 다시 발사키를 눌러 해제했든) 알려주는 신호.
    // RopeItem이 이걸 구독해서 "지금 자기가 뭘 붙잡고 있는지" 동기화하는 데 씀
    public System.Action OnDetached;

    private Transform ropePuller;
    private Rope visualRope;
    private Rigidbody rb;
    private SpringJoint joint;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (ropePuller == null) return;

        // 거리 초과 확인은 SpringJoint가 대신 안 해주니 직접 체크
        float dist = Vector3.Distance(transform.position, ropePuller.position);
        if (dist > ropeMaxDistance)
        {
            DetachRope();
        }
    }

    // RopeProjectile이 이 플레이어를 맞혔을 때 호출
    public void AttachRope(Transform puller, Rope incomingVisualRope)
    {
        ropePuller = puller;
        visualRope = incomingVisualRope;

        if (visualRope != null)
            visualRope.SetEndPoint(transform, true);

        Rigidbody pullerRb = puller.GetComponent<Rigidbody>();
        if (pullerRb != null)
        {
            joint = gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = pullerRb;
            joint.autoConfigureConnectedAnchor = true;
            joint.spring = ropeSpring;
            joint.damper = ropeDamper;
            joint.minDistance = 0f;
            joint.maxDistance = leashLength; // 이 길이까지는 자유롭고, 넘으면 SpringJoint가 알아서 팽팽하게 당김
        }
    }

    public void DetachRope()
    {
        if (joint != null)
            Destroy(joint);
        joint = null;

        if (visualRope != null)
            Destroy(visualRope.gameObject, ropeLingerDuration); // 즉시 안 지우고 잠깐 남았다가 사라짐

        visualRope = null;
        ropePuller = null;

        OnDetached?.Invoke();
    }
}