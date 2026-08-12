using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
// 풀에서 재사용되며 첫 유효 충돌에 고정 피해를 주는 낙하 바위
public sealed class FallingRock : NetworkBehaviour
{
    [Header("Smoke Effect")]
    [SerializeField] private GameObject smokeObject; // 연기 이펙트 루트 게임오브젝트
    [SerializeField] private ParticleSystem smokeParticle; // 충돌 시 재생할 연기 파티클 컴포넌트

    private RockSpawner owner; // 바위 생성 및 회수 Spawner
    private Rigidbody body; // 낙하와 충돌을 담당하는 물리 본체
    private Collider[] rockColliders; // 풀 반환 시 함께 끌 바위 충돌 판정
    private MeshRenderer[] rockMeshRenderers; // 바위 본체 렌더러 (연기 파티클 렌더러와 분리)
    private Coroutine smokeRoutine; // 연기 파티클 재생 및 대기 코루틴
    private float impactDamage; // 현재 낙하 차례에 적용할 고정 피해량
    private float despawnTime; // 충돌하지 않은 바위를 자동 회수할 절대시간
    private bool isLaunched; // 현재 풀 밖에서 낙하 중인지 나타내는 값
    private bool hasImpacted; // 같은 낙하 차례의 중복 충돌 처리를 막는 값
    private int renderedImpactSequence; // 마지막으로 재생한 충돌 번호

    // 생성한 스포너 식별자
    [Networked] private NetworkId OwnerId { get; set; }
    // 호스트 기준 충돌 상태
    [Networked] private NetworkBool NetworkedImpacted { get; set; }
    // 호스트 기준 충돌 위치
    [Networked] private Vector3 NetworkedImpactPoint { get; set; }
    // 호스트 기준 충돌 방향
    [Networked] private Vector3 NetworkedImpactNormal { get; set; }
    // 충돌 연출 번호
    [Networked] private int ImpactSequence { get; set; }
    // 최대 생존 타이머
    [Networked] private TickTimer LifetimeTimer { get; set; }
    // 충돌 연출 종료 타이머
    [Networked] private TickTimer DespawnTimer { get; set; }

    public bool IsLaunched => isLaunched; // 현재 낙하 중인지 확인하는 읽기 전용 상태
    public bool HasImpacted => hasImpacted; // 현재 차례에 유효 충돌이 발생했는지 확인하는 상태

    // 프리팹의 물리와 표시 컴포넌트 참조를 최초 확보
    private void Awake()
    {
        CacheComponents();
        ConfigureRigidbody();
        ConfigureColliders();
        DeactivateSmoke();
    }

    // 풀 생성 시 소유 Spawner 연결 및 컴포넌트 참조 보강
    internal void Initialize(RockSpawner rockOwner)
    {
        owner = rockOwner;
        CacheComponents();
        ConfigureRigidbody();
        ConfigureColliders();
        DeactivateSmoke();
    }

    // 네트워크 생성 초기값 기록
    internal void InitializeNetwork(RockSpawner rockOwner, float damage, float maxLifetime)
    {
        // 생성 스포너와 피해량 저장
        owner = rockOwner;
        impactDamage = Mathf.Max(0f, damage);
        // 프록시에서 찾을 스포너 식별자 저장
        OwnerId = rockOwner != null && rockOwner.Object != null && rockOwner.Object.IsValid
            ? rockOwner.Object.Id
            : default;
        // 새 낙하의 충돌 기록 초기화
        NetworkedImpacted = false;
        NetworkedImpactPoint = default;
        NetworkedImpactNormal = Vector3.up;
        ImpactSequence = 0;
        // 미충돌 상태의 최대 생존시간 설정
        LifetimeTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, maxLifetime));
        DespawnTimer = TickTimer.None;
    }

    // 권위 물리와 프록시 표시 초기화
    public override void Spawned()
    {
        // 풀 사용 이력을 제거한 표시 상태 준비
        CacheComponents();
        ConfigureRigidbody();
        ConfigureColliders();
        StopSmokeRoutine();
        DeactivateSmoke();

        // 복제된 충돌 기록을 로컬 상태에 반영
        isLaunched = true;
        hasImpacted = NetworkedImpacted;
        renderedImpactSequence = ImpactSequence;
        SetMeshRenderersEnabled(!NetworkedImpacted);

        if (Object.HasStateAuthority)
        {
            // 권위자만 동적 물리와 충돌 활성화
            SetCollidersEnabled(!NetworkedImpacted);
            if (!NetworkedImpacted)
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }
        else
        {
            // 프록시의 독립 물리와 충돌 차단
            SetCollidersEnabled(false);
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            if (NetworkedImpacted)
                // 늦은 참가자의 충돌 연출 복원
                PlayReplicatedImpact();
        }
    }

    // 권위 수명과 연출 종료 판정
    public override void FixedUpdateNetwork()
    {
        // 프록시의 수명 판정 차단
        if (!Object.HasStateAuthority)
            return;

        if (!NetworkedImpacted && LifetimeTimer.Expired(Runner))
        {
            // 충돌하지 않은 바위의 수명 종료
            Runner.Despawn(Object);
            return;
        }

        if (NetworkedImpacted && DespawnTimer.Expired(Runner))
            // 충돌 연출이 끝난 바위 제거
            Runner.Despawn(Object);
    }

    // 프록시 충돌 연출 갱신
    public override void Render()
    {
        // 권위자와 이미 적용한 충돌 번호 제외
        if (Object.HasStateAuthority || renderedImpactSequence == ImpactSequence)
            return;

        // 새 충돌 번호 저장
        renderedImpactSequence = ImpactSequence;
        if (NetworkedImpacted)
            PlayReplicatedImpact();
    }

    // 네트워크 제거 상태 정리
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 생성 스포너의 활성 목록에서 제거
        owner?.NotifyNetworkRockDespawned(this);
        // 남은 파티클과 로컬 상태 정리
        StopSmokeRoutine();
        DeactivateSmoke();
        isLaunched = false;
        hasImpacted = false;
    }

    // 풀에서 대여된 바위의 위치와 물리 및 충돌 기록을 초기화해 낙하 시작
    internal void Launch(Vector3 position, Quaternion rotation, float damage, float maxLifetime)
    {
        StopSmokeRoutine();
        DeactivateSmoke();
        CacheComponents();
        ConfigureColliders();

        impactDamage = Mathf.Max(0f, damage);
        hasImpacted = false;
        isLaunched = true;
        despawnTime = Time.time + Mathf.Max(0.1f, maxLifetime); // 현재시간에 최대 수명을 더해 자동 회수 시점 계산

        transform.SetPositionAndRotation(position, rotation);
        SetMeshRenderersEnabled(true);
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
        StopSmokeRoutine();
        DeactivateSmoke();
        CacheComponents();

        isLaunched = false;
        hasImpacted = false;
        despawnTime = 0f;

        // 충돌 처리 후 Kinematic 본체의 속도 쓰기 제외
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.detectCollisions = false;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.Sleep();

        SetCollidersEnabled(false);
        SetMeshRenderersEnabled(false);
        transform.SetParent(poolParent, false);
        gameObject.SetActive(false);
    }

    // 최대 생존시간이 지나도 충돌하지 않은 바위를 효과 없이 풀로 반환
    private void Update()
    {
        // 네트워크 낙석은 TickTimer 사용
        if (Object != null && Object.IsValid)
            return;

        if (isLaunched && !hasImpacted && Time.time >= despawnTime)
            ReturnToPool();
    }

    // 첫 유효 충돌의 피해와 먼지 효과를 처리하고 연기 파티클 재생 후 풀로 반환
    private void OnCollisionEnter(Collision collision)
    {
        // 프록시의 독립 충돌 처리 차단
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

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

    // 단일 유효 충돌의 Health 피해 물리 정지 연기 Particle 재생
    private void HandleImpact(Collider hitCollider, Vector3 point, Vector3 normal)
    {
        if (!isLaunched || hasImpacted || hitCollider == null)
            return;

        FallingRock otherRock = hitCollider.GetComponentInParent<FallingRock>(); // 직접 호출에서도 낙석끼리의 충돌 처리 제외
        if (otherRock != null)
            return;

        hasImpacted = true;

        if (Object != null && Object.IsValid)
        {
            // 권위 충돌 결과 게시
            NetworkedImpacted = true;
            NetworkedImpactPoint = point;
            NetworkedImpactNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            // 같은 상태의 새 충돌 연출 구분
            ImpactSequence++;
        }

        Health health = hitCollider.GetComponentInParent<Health>(); // 다중 Collider를 부모 Health 하나로 통합
        if (health != null && !health.IsDead && impactDamage > 0f)
        {
            GameObject source = owner != null ? owner.gameObject : gameObject; // 풀 반환 후 유지되는 Spawner 우선 피해 출처
            health.ApplyDamage(new DamageInfo(
                impactDamage,
                source,
                point,
                normal,
                DamageType.Environmental));
        }

        if (owner != null)
            owner.PlayImpactDust(point, normal);

        // 충돌 즉시 바위의 물리 및 충돌 비활성화 및 바위 메쉬 숨김
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.detectCollisions = false;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.Sleep();

        SetCollidersEnabled(false);
        SetMeshRenderersEnabled(false);

        // 네트워크 낙석은 Particle 시간 대기 후 State Authority가 Despawn
        if (Object != null && Object.IsValid)
        {
            // 모든 피어에서 동일한 연기 연출 재생
            PlaySmokeVisualOnly();
            // 연기 종료 시점을 네트워크 틱으로 저장
            float effectLifetime = smokeParticle != null
                ? CalculateParticleLifetime(smokeParticle)
                : 0.1f;
            DespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, effectLifetime));
            return;
        }

        // 로컬 낙석은 연기 파티클 재생 후 풀로 반환
        if (smokeParticle != null || smokeObject != null)
        {
            StopSmokeRoutine();
            smokeRoutine = StartCoroutine(PlaySmokeAndReturn());
        }
        else
        {
            ReturnToPool();
        }
    }

    // Smoke 활성화 및 Particle 재생 후 풀 반환
    private IEnumerator PlaySmokeAndReturn()
    {
        // Smoke GameObject 활성화
        if (smokeObject != null)
        {
            smokeObject.SetActive(true);
        }
        else if (smokeParticle != null)
        {
            smokeParticle.gameObject.SetActive(true);
        }

        // Smoke Particle 컴포넌트 재생
        if (smokeParticle != null)
        {
            smokeParticle.Clear(true);
            smokeParticle.Play(true);

            // 첫 프레임 입자 방출 대기
            yield return null;

            float maxLifetime = CalculateParticleLifetime(smokeParticle);
            float elapsed = Time.deltaTime;

            // 파티클이 재생 중이거나 생존 파티클이 있는 동안 대기 (안전 제한시간 여유 1초 부여)
            while (smokeParticle != null &&
                   (smokeParticle.isPlaying || smokeParticle.IsAlive(true)) &&
                   elapsed < maxLifetime + 1.0f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // Particle 종료 시 비활성화
        DeactivateSmoke();
        smokeRoutine = null;

        // 풀 반환
        ReturnToPool();
    }

    // 소유 Spawner 존재 시 전용 풀 반환 및 부재 시 자체 비활성화
    private void ReturnToPool()
    {
        if (!isLaunched)
            return;

        isLaunched = false;

        if (Object != null && Object.IsValid)
        {
            // 네트워크 바위는 풀 대신 Despawn 사용
            if (Object.HasStateAuthority)
                Runner.Despawn(Object);
            return;
        }

        if (owner != null)
        {
            owner.ReturnRock(this);
            return;
        }

        PrepareForPool(null);
    }

    // 복제 충돌 연출 적용
    private void PlayReplicatedImpact()
    {
        // 중복 충돌 처리를 막는 로컬 상태 설정
        hasImpacted = true;
        // 복제 위치에서 물리와 메쉬 정지
        StopBodyAfterImpact();
        // 먼지 생성을 위한 스포너 복원
        ResolveNetworkOwner();
        owner?.PlayImpactDust(NetworkedImpactPoint, NetworkedImpactNormal);
        PlaySmokeVisualOnly();
    }

    // 생성 스포너 복원
    private void ResolveNetworkOwner()
    {
        // 이미 찾은 스포너와 잘못된 식별자 제외
        if (owner != null || !OwnerId.IsValid || Runner == null)
            return;

        if (Runner.TryFindObject(OwnerId, out NetworkObject ownerObject))
            owner = ownerObject.GetComponent<RockSpawner>();
    }

    // 프록시 바위 물리 정지
    private void StopBodyAfterImpact()
    {
        // Rigidbody 누락 상태 보호
        if (body == null)
            return;

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        // 프록시 물리와 충돌 완전 정지
        body.detectCollisions = false;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.Sleep();
        SetCollidersEnabled(false);
        SetMeshRenderersEnabled(false);
    }

    // 충돌 연기 연출 재생
    private void PlaySmokeVisualOnly()
    {
        // 비활성 연기 오브젝트 활성화
        if (smokeObject != null)
            smokeObject.SetActive(true);
        else if (smokeParticle != null)
            smokeParticle.gameObject.SetActive(true);

        if (smokeParticle == null)
            return;

        // 이전 입자를 제거한 뒤 새 연기 재생
        smokeParticle.Clear(true);
        smokeParticle.Play(true);
    }

    // Rigidbody 자식 Collider MeshRenderer Smoke ParticleSystem 및 GameObject 참조 확보
    private void CacheComponents()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        if (smokeParticle == null || smokeObject == null)
        {
            Transform smokeTransform = transform.Find("Smoke");
            if (smokeTransform != null)
            {
                smokeObject = smokeTransform.gameObject;
                smokeParticle = smokeTransform.GetComponentInChildren<ParticleSystem>(true);
            }

            if (smokeParticle == null)
                smokeParticle = GetComponentInChildren<ParticleSystem>(true);

            if (smokeObject == null && smokeParticle != null)
                smokeObject = smokeParticle.gameObject;
        }

        if (rockColliders == null || rockColliders.Length == 0)
        {
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            if (smokeObject != null)
            {
                List<Collider> filtered = new List<Collider>();
                Transform smokeTransform = smokeObject.transform;
                foreach (Collider rockCollider in allColliders)
                {
                    if (rockCollider != null && !rockCollider.transform.IsChildOf(smokeTransform))
                        filtered.Add(rockCollider);
                }
                rockColliders = filtered.ToArray();
            }
            else
            {
                rockColliders = allColliders;
            }
        }

        if (rockMeshRenderers == null || rockMeshRenderers.Length == 0)
        {
            MeshRenderer[] allRenderers = GetComponentsInChildren<MeshRenderer>(true);
            if (smokeObject != null)
            {
                List<MeshRenderer> filtered = new List<MeshRenderer>();
                Transform smokeTransform = smokeObject.transform;
                foreach (MeshRenderer rockRenderer in allRenderers)
                {
                    if (rockRenderer != null && !rockRenderer.transform.IsChildOf(smokeTransform))
                        filtered.Add(rockRenderer);
                }
                rockMeshRenderers = filtered.ToArray();
            }
            else
            {
                rockMeshRenderers = allRenderers;
            }
        }
    }

    // 낙하 시 사용할 Rigidbody 보간 설정
    private void ConfigureRigidbody()
    {
        if (body == null)
            return;

        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // 동적 Rigidbody와의 호환성을 위해 모든 자식 MeshCollider를 Convex로 설정
    private void ConfigureColliders()
    {
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider meshCollider in meshColliders)
        {
            if (meshCollider != null && !meshCollider.convex)
            {
                meshCollider.convex = true;
            }
        }
    }

    // 모든 바위 Collider 상태 전환 및 Smoke Collider 제외
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

    // 모든 바위 MeshRenderer 상태 전환 및 연기 Particle Renderer 제외
    private void SetMeshRenderersEnabled(bool enabled)
    {
        if (rockMeshRenderers == null)
            return;

        foreach (MeshRenderer rockRenderer in rockMeshRenderers)
        {
            if (rockRenderer != null)
                rockRenderer.enabled = enabled;
        }
    }

    // Smoke Particle 컴포넌트와 오브젝트 정지 및 비활성화
    private void DeactivateSmoke()
    {
        if (smokeParticle != null)
        {
            smokeParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (smokeObject != null)
        {
            smokeObject.SetActive(false);
        }
        else if (smokeParticle != null)
        {
            smokeParticle.gameObject.SetActive(false);
        }
    }

    // 실행 중인 Smoke 코루틴 안전 중단
    private void StopSmokeRoutine()
    {
        if (smokeRoutine == null)
            return;

        StopCoroutine(smokeRoutine);
        smokeRoutine = null;
    }

    // 오브젝트 비활성화 시 코루틴과 파티클 정리
    private void OnDisable()
    {
        StopSmokeRoutine();
        DeactivateSmoke();
    }

    // 자식까지 포함한 모든 파티클의 최대 생존시간 계산
    private static float CalculateParticleLifetime(ParticleSystem rootEffect)
    {
        if (rootEffect == null)
            return 0.1f;

        float longestLifetime = 0.1f;
        ParticleSystem[] systems = rootEffect.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem system in systems)
        {
            if (system == null)
                continue;

            ParticleSystem.MainModule main = system.main;
            float delay = GetMaxFromMinMaxCurve(main.startDelay);
            float duration = main.duration;
            float lifetime = GetMaxFromMinMaxCurve(main.startLifetime);
            float systemLifetime = delay + duration + lifetime;
            longestLifetime = Mathf.Max(longestLifetime, systemLifetime);
        }

        return longestLifetime;
    }

    // ParticleSystem MinMaxCurve 모드별 최댓값 추출
    private static float GetMaxFromMinMaxCurve(ParticleSystem.MinMaxCurve minMaxCurve)
    {
        switch (minMaxCurve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return minMaxCurve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return minMaxCurve.constantMax;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                return minMaxCurve.curveMultiplier;
            default:
                return Mathf.Max(minMaxCurve.constantMax, minMaxCurve.curveMultiplier);
        }
    }
}
