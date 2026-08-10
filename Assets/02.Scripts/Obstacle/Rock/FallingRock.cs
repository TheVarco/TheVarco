using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
// 풀에서 재사용되며 첫 유효 충돌에 고정 피해를 주는 낙하 바위
public sealed class FallingRock : MonoBehaviour
{
    [Header("Smoke Effect")]
    [SerializeField] private GameObject smokeObject; // 연기 이펙트 루트 게임오브젝트
    [SerializeField] private ParticleSystem smokeParticle; // 충돌 시 재생할 연기 파티클 컴포넌트

    private RockSpawner owner; // 이 바위를 생성하고 다시 회수할 Spawner
    private Rigidbody body; // 낙하와 충돌을 담당하는 물리 본체
    private Collider[] rockColliders; // 풀 반환 시 함께 끌 바위 충돌 판정
    private MeshRenderer[] rockMeshRenderers; // 바위 본체 렌더러 (연기 파티클 렌더러와 분리)
    private Coroutine smokeRoutine; // 연기 파티클 재생 및 대기 코루틴
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
        ConfigureColliders();
        DeactivateSmoke();
    }

    // 풀 생성 시 소유 Spawner를 연결하고 컴포넌트 참조 보강
    internal void Initialize(RockSpawner rockOwner)
    {
        owner = rockOwner;
        CacheComponents();
        ConfigureRigidbody();
        ConfigureColliders();
        DeactivateSmoke();
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

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
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
        if (isLaunched && !hasImpacted && Time.time >= despawnTime)
            ReturnToPool();
    }

    // 첫 유효 충돌의 피해와 먼지 효과를 처리하고 연기 파티클 재생 후 풀로 반환
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

    // 유효 충돌을 한 번만 잠그고 Health 피해, 물리 정지, 연기 파티클 재생 처리
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

        // 연기 파티클 재생 후 완료되면 풀로 반환
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

    // Smoke 객체를 활성화하고 파티클 컴포넌트를 재생한 뒤, 완료되면 비활성화하고 풀로 반환
    private IEnumerator PlaySmokeAndReturn()
    {
        // 1. Smoke 게임오브젝트 활성화
        if (smokeObject != null)
        {
            smokeObject.SetActive(true);
        }
        else if (smokeParticle != null)
        {
            smokeParticle.gameObject.SetActive(true);
        }

        // 2. Smoke 파티클 컴포넌트 재생
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

        // 3. 파티클 종료 시 비활성화
        DeactivateSmoke();
        smokeRoutine = null;

        // 4. 풀로 반환
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

    // Rigidbody, 자식 Collider, MeshRenderer 및 Smoke ParticleSystem/GameObject 참조 확보
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

    // 모든 바위 Collider를 상태에 맞춰 전환 (Smoke 객체의 Collider는 제외)
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

    // 모든 바위 MeshRenderer를 상태에 맞춰 전환 (연기 파티클 렌더러는 영향받지 않음)
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

    // Smoke 파티클 컴포넌트 및 오브젝트 정지 및 비활성화
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

    // ParticleSystem.MinMaxCurve에서 모드별 최댓값 추출
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
