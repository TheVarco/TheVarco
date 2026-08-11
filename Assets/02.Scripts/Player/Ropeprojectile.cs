using Fusion;
using UnityEngine;
using GogoGaga.OptimizedRopesAndCables;

// RopeItem이 던진 밧줄. 맞으면 대상의 PlayerRopeTarget에 "누가 당기는지"를 기록하고 사라진다.
//
// 총알(Projectile)과 같은 구조: 호스트가 소유하고 비행/판정까지 담당한다.
// 던진 사람 클라이언트에서 판정하면 상대가 remote time의 kinematic 프록시라 빗나간다.
// 로프 비주얼은 복제하지 않고 각 머신이 자기 화면의 손 ↔ 이 투사체를 잇는다.
[RequireComponent(typeof(Rigidbody))]
public class RopeProjectile : NetworkBehaviour
{
    [Header("투사체 수치 (밸런싱용)")]
    public float speed = 15f;
    [Tooltip("아무것도 못 맞히면 이 시간(초) 뒤에 사라짐 - 짧을수록 빗나갔을 때 로프가 빨리 정리됨")]
    public float lifeTime = 2.5f;
    [Tooltip("판정 굵기. 0이면 얇은 선으로 훑어서 대상 옆구리로 스칠 때 놓침")]
    public float hitRadius = 0.3f;

    // 던진 사람. OwnerId로 해석해서 채우고, 자기 밧줄에 자기가 맞는 것을 거르는 데 씀
    private GameObject owner;

    // 모든 머신이 "누가 던졌나"를 알아야 로프 시작점(손)을 잡을 수 있다
    [Networked] public NetworkId OwnerId { get; private set; }

    private Rigidbody rb;
    private Rope visualRope;
    private float despawnTime;
    private bool hasHit; // 한 발이 두 번 처리되지 않게

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // 빠른 속도로 얇은 대상을 그냥 통과해버리는 터널링 현상 방지
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    // 호스트가 Runner.Spawn의 onBeforeSpawned에서 주입 (스폰 전에 넣어야 모든 머신이 첫 프레임부터 앎)
    public void InitOwner(NetworkId ownerId) => OwnerId = ownerId;

    public override void Spawned()
    {
        // 던진 사람 손 → 이 투사체. 각 머신이 자기 화면 기준으로 직접 만든다
        if (Runner.TryFindObject(OwnerId, out NetworkObject ownerObject))
        {
            owner = ownerObject.gameObject;
            AttachVisualRope(ownerObject.transform);
        }

        if (!Object.HasStateAuthority) return; // 프록시는 복제된 위치만 따라가면 됨

        rb.linearVelocity = transform.forward * speed;
        despawnTime = Runner.SimulationTime + lifeTime;
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => DestroyVisualRope();
    void OnDestroy() => DestroyVisualRope();

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        // 틱 사이 이동 구간을 직접 훑는다. 트리거에만 의존하면 한 스텝이 대상을 통째로 건너뛴다.
        // QueryTriggerInteraction.Ignore가 없으면 잠수함 걷기존 같은 트리거 볼륨에 밧줄이 죽는다
        Vector3 step = rb.linearVelocity * Runner.DeltaTime;

        if (!hasHit && step.sqrMagnitude > 0f
            && Physics.SphereCast(transform.position, hitRadius, step.normalized, out RaycastHit hit,
                                  step.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            HandleHit(hit.collider);
        }

        // 던진 사람 콜라이더에 걸려 HandleHit가 그냥 빠져나온 경우에도 수명 체크는 돌아야 한다
        if (!hasHit && Runner.SimulationTime >= despawnTime)
            Runner.Despawn(Object);
    }

    private void HandleHit(Collider other)
    {
        if (hasHit) return;

        // 자기를 던진 사람은 무시. 콜라이더가 자식에 있어도 걸러지도록 루트로 비교
        if (owner != null && other.transform.root == owner.transform.root) return;

        hasHit = true;

        // 연결 상태는 대상이 들고 있는다. 투사체는 전달만 하고 사라짐
        PlayerRopeTarget target = other.GetComponentInParent<PlayerRopeTarget>();
        if (target != null) target.SetPuller(OwnerId);

        Runner.Despawn(Object);
    }

    private void AttachVisualRope(Transform ownerTransform)
    {
        // 로프 비주얼 프리팹은 던진 사람에게서 빌려온다. 투사체에도 슬롯을 두면
        // 둘이 어긋났을 때 조용히 로프가 안 그려짐
        PlayerRopeTarget ownerRope = ownerTransform.GetComponent<PlayerRopeTarget>();
        if (ownerRope != null)
            visualRope = PlayerRopeTarget.SpawnVisualRope(ownerRope.ropeVisualPrefab, ownerTransform, transform);
    }

    private void DestroyVisualRope()
    {
        if (visualRope != null) Destroy(visualRope.gameObject);
        visualRope = null;
    }
}
