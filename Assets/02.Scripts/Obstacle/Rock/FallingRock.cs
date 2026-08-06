using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
// 풀에서 재사용되며 첫 유효 충돌에 고정 피해를 주는 낙하 바위
public sealed class FallingRock : MonoBehaviour
{
    private RockSpawner owner; // 이 바위를 생성하고 다시 회수할 Spawner
    private Rigidbody body; // 낙하와 충돌을 담당하는 물리 본체
    private Collider[] rockColliders; // 풀 반환 시 함께 끌 전체 충돌 판정
    private Renderer[] rockRenderers; // 풀 반환 시 함께 숨길 전체 렌더러
    private float impactDamage; // 현재 낙하 차례에 적용할 고정 피해량
    private float despawnTime; // 충돌하지 않은 바위를 자동 회수할 절대시간
    private bool isLaunched; // 현재 풀 밖에서 낙하 중인지 나타내는 값
    private bool hasImpacted; // 같은 낙하 차례의 중복 충돌 처리를 막는 값

    public bool IsLaunched => isLaunched; // 현재 낙하 중인지 확인하는 읽기 전용 상태
    public bool HasImpacted => hasImpacted; // 현재 차례에 유효 충돌이 발생했는지 확인하는 상태

    // 프리팹의 물리와 표시 컴포넌트 참조를 최초 확보
    private void Awake()
    {
        CacheComponents();
        ConfigureRigidbody();
    }

    // 풀 생성 시 소유 Spawner를 연결하고 컴포넌트 참조 보강
    internal void Initialize(RockSpawner rockOwner)
    {
        owner = rockOwner;
        CacheComponents();
        ConfigureRigidbody();
    }

    // 풀에서 대여된 바위의 위치와 물리 및 충돌 기록을 초기화해 낙하 시작
    internal void Launch(Vector3 position, Quaternion rotation, float damage, float maxLifetime)
    {
        CacheComponents();

        impactDamage = Mathf.Max(0f, damage);
        hasImpacted = false;
        isLaunched = true;
        despawnTime = Time.time + Mathf.Max(0.1f, maxLifetime); // 현재시간에 최대 수명을 더해 자동 회수 시점 계산

        transform.SetPositionAndRotation(position, rotation);
        SetRenderersEnabled(true);
        SetCollidersEnabled(true);
        gameObject.SetActive(true);

        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.useGravity = true;
        body.detectCollisions = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    // 바위의 속도와 판정을 모두 끄고 다음 낙하를 위해 풀 부모 아래 보관
    internal void PrepareForPool(Transform poolParent)
    {
        CacheComponents();

        isLaunched = false;
        hasImpacted = false;
        despawnTime = 0f;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.detectCollisions = false;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.Sleep();

        SetCollidersEnabled(false);
        SetRenderersEnabled(false);
        transform.SetParent(poolParent, false);
        gameObject.SetActive(false);
    }

    // 최대 생존시간이 지나도 충돌하지 않은 바위를 효과 없이 풀로 반환
    private void Update()
    {
        if (isLaunched && Time.time >= despawnTime)
            ReturnToPool();
    }

    // 첫 유효 충돌의 피해와 먼지 효과를 처리하고 즉시 풀로 반환
    private void OnCollisionEnter(Collision collision)
    {
        if (!isLaunched || hasImpacted || collision == null || collision.collider == null)
            return;

        FallingRock otherRock = collision.collider.GetComponentInParent<FallingRock>(); // 상대 콜라이더가 속한 낙석 확인
        if (otherRock != null)
            return;

        Vector3 point = transform.position; // 접촉점이 없을 때 사용할 안전한 대체 위치
        Vector3 normal = Vector3.up; // 접촉점이 없을 때 사용할 안전한 대체 표면 방향

        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0); // 첫 접촉점을 피해와 파티클의 대표 지점으로 사용
            point = contact.point;
            normal = contact.normal;
        }

        HandleImpact(collision.collider, point, normal);
    }

    // 유효 충돌을 한 번만 잠그고 Health 피해와 표면 먼지 처리
    private void HandleImpact(Collider hitCollider, Vector3 point, Vector3 normal)
    {
        if (!isLaunched || hasImpacted || hitCollider == null)
            return;

        FallingRock otherRock = hitCollider.GetComponentInParent<FallingRock>(); // 직접 호출에서도 낙석끼리의 충돌 처리 제외
        if (otherRock != null)
            return;

        hasImpacted = true;

        Health health = hitCollider.GetComponentInParent<Health>(); // 다중 콜라이더를 부모 Health 하나로 통합
        if (health != null && !health.IsDead && impactDamage > 0f)
        {
            GameObject source = owner != null ? owner.gameObject : gameObject; // 풀 반환 후에도 유지되는 Spawner를 우선 피해 출처로 사용
            health.ApplyDamage(new DamageInfo(
                impactDamage,
                source,
                point,
                normal,
                DamageType.Environmental));
        }

        if (owner != null)
            owner.PlayImpactDust(point, normal);

        ReturnToPool();
    }

    // 소유 Spawner가 남아 있으면 전용 풀로 반환하고 없으면 자체 비활성화
    private void ReturnToPool()
    {
        if (!isLaunched)
            return;

        isLaunched = false;

        if (owner != null)
        {
            owner.ReturnRock(this);
            return;
        }

        PrepareForPool(null);
    }

    // Rigidbody와 전체 자식 Collider 및 Renderer 참조를 필요할 때 복구
    private void CacheComponents()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        if (rockColliders == null || rockColliders.Length == 0)
            rockColliders = GetComponentsInChildren<Collider>(true);

        if (rockRenderers == null || rockRenderers.Length == 0)
            rockRenderers = GetComponentsInChildren<Renderer>(true);
    }

    // 낙하 시 사용할 Rigidbody 보간 설정
    private void ConfigureRigidbody()
    {
        if (body == null)
            return;

        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // 모든 자식 Collider를 풀 상태에 맞춰 함께 전환
    private void SetCollidersEnabled(bool enabled)
    {
        if (rockColliders == null)
            return;

        foreach (Collider rockCollider in rockColliders)
        {
            if (rockCollider != null)
                rockCollider.enabled = enabled;
        }
    }

    // 모든 자식 Renderer를 풀 상태에 맞춰 함께 전환
    private void SetRenderersEnabled(bool enabled)
    {
        if (rockRenderers == null)
            return;

        foreach (Renderer rockRenderer in rockRenderers)
        {
            if (rockRenderer != null)
                rockRenderer.enabled = enabled;
        }
    }
}
