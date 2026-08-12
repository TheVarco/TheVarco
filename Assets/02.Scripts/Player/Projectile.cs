using Fusion;
using UnityEngine;

// 발사된 후 앞으로 날아가다가, 뭔가에 부딪히면 데미지를 주고 사라지는 투사체.
// 프리팹의 Collider는 반드시 Is Trigger 체크 필요.
//
// 네트워크에서는 호스트가 총알을 소유하고 판정까지 담당한다.
// 쏜 사람 클라이언트에서 판정하면 상대가 remote time의 kinematic 프록시라
// "눈엔 맞았는데 데미지가 안 들어가는" 문제가 생긴다.
[RequireComponent(typeof(Rigidbody))]
public class Projectile : NetworkBehaviour
{
    [Header("투사체 수치 (밸런싱용)")]
    public float speed = 20f;
    public float damage = 15f;
    [Tooltip("아무것도 안 맞고 이 시간(초)이 지나면 자동으로 사라짐 (허공으로 날아가다 안 없어지는 것 방지)")]
    public float lifeTime = 5f;
    [Tooltip("판정 굵기. 0이면 얇은 선으로 훑어서 대상 옆구리로 스칠 때 놓침")]
    public float hitRadius = 0.1f;

    // 쏜 사람 (자기 자신에게 맞지 않게 구분하고, TakeDamage에 누가 쐈는지 전달하는 용도)
    [HideInInspector] public GameObject owner;

    private Rigidbody rb;
    private float despawnTime;
    private bool hasHit; // 한 발이 두 번 처리되지 않게

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // 빠른 속도로 얇은 대상을 그냥 통과해버리는 터널링 현상 방지
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    // 러너 없는 씬(팀원 테스트 씬 등)에서 Instantiate로 만들어졌을 때의 경로
    void Start()
    {
        if (Object != null) return;

        InitFlight();
        Destroy(gameObject, lifeTime);
    }

    public override void Spawned()
    {
        if (!Object.HasStateAuthority) return; // 프록시는 복제된 위치만 따라가면 됨

        InitFlight();
        despawnTime = Runner.SimulationTime + lifeTime;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        // 틱 사이 이동 구간을 직접 훑는다.
        // 트리거에만 의존하면 한 스텝(속도 20이면 약 0.33m)이 대상을 통째로 건너뛰어
        // "띄엄띄엄 맞는" 터널링이 생김. NetworkRigidbody3D가 매 틱 위치를 덮어써서
        // Rigidbody의 연속 충돌 판정도 기대할 수 없다.
        Vector3 step = rb.linearVelocity * Runner.DeltaTime;

        // QueryTriggerInteraction.Ignore가 없으면 잠수함 걷기존 같은 트리거 볼륨에 총알이 죽는다
        if (!hasHit && step.sqrMagnitude > 0f
            && Physics.SphereCast(transform.position, hitRadius, step.normalized, out RaycastHit hit,
                                  step.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            HandleHit(hit.collider);
        }

        // 쏜 사람 콜라이더에 걸려 HandleHit가 그냥 빠져나온 경우에도 수명 체크는 돌아야 한다
        if (!hasHit && Runner.SimulationTime >= despawnTime)
            Runner.Despawn(Object);
    }

    private void InitFlight()
    {
        rb.linearVelocity = transform.forward * speed;
    }

    void OnTriggerEnter(Collider other)
    {
        if (Object != null) return; // 네트워크 경로는 위의 스윕이 담당
        HandleHit(other);
    }

    private void HandleHit(Collider other)
    {
        if (hasHit) return;

        // 자기를 쏜 사람은 무시. Collider가 owner의 자식 오브젝트에 있어도 걸러지도록
        // 정확히 같은 오브젝트가 아니라 "최상위 루트가 같은지"로 비교함
        if (owner != null && other.transform.root == owner.transform.root) return;

        hasHit = true;

        Damageable target = other.GetComponentInParent<Damageable>();
        if (target != null && !target.IsDead)
        {
            target.TakeDamage(damage, owner);
        }

        // 데미지를 줬든 안 줬든(벽에 맞았든) 부딪히면 사라짐
        if (Object != null) Runner.Despawn(Object);
        else Destroy(gameObject);
    }
}