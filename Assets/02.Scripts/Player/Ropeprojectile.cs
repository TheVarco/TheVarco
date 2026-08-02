using UnityEngine;
using GogoGaga.OptimizedRopesAndCables;

// RopeItem이 던진 밧줄. 맞으면 대상(트랩 여부 상관없이 누구든)의 PlayerRopeTarget에 연결됨.
[RequireComponent(typeof(Rigidbody))]
public class RopeProjectile : MonoBehaviour
{
    [Header("투사체 수치 (밸런싱용)")]
    public float speed = 15f;
    [Tooltip("아무것도 못 맞히면 이 시간(초) 뒤에 사라짐 - 짧을수록 빗나갔을 때 로프가 빨리 정리됨")]
    public float lifeTime = 2.5f;

    [HideInInspector] public GameObject owner;
    [HideInInspector] public Rope visualRope;      // RopeItem이 만들어준 로프 비주얼
    [HideInInspector] public RopeItem sourceItem;  // 이 투사체를 던진 RopeItem (연결 성공 시 알려주기 위함)

    private Rigidbody rb;
    private bool handedOffRope = false; // 로프 비주얼을 맞은 대상 쪽으로 넘겨줬는지

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void Start()
    {
        rb.linearVelocity = transform.forward * speed;
        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        // 자기를 던진 사람은 무시 (자기 자신에게 밧줄이 맞는 것 방지)
        if (owner != null && other.transform.root == owner.transform.root) return;

        PlayerRopeTarget target = other.GetComponentInParent<PlayerRopeTarget>();
        if (target != null)
        {
            target.AttachRope(owner != null ? owner.transform : transform, visualRope);
            handedOffRope = true;

            // 던진 사람(RopeItem)한테 "지금 이 대상이랑 연결됐다"고 알려줘서,
            // 다시 좌클릭했을 때 "새로 던지기"가 아니라 "연결 해제"로 동작하게 함
            if (sourceItem != null)
                sourceItem.SetCurrentTarget(target);
        }

        Destroy(gameObject); // 투사체 자체는 사라짐 (로프 비주얼은 넘겨받은 쪽이 계속 관리)
    }

    void OnDestroy()
    {
        // 아무도 못 맞히고 벽에 부딪히거나 수명이 다한 경우, 로프 비주얼도 같이 정리
        if (!handedOffRope && visualRope != null)
        {
            Destroy(visualRope.gameObject);
        }
    }
}