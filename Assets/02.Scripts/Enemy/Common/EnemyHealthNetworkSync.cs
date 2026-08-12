using Fusion;
using UnityEngine;

/// <summary>
/// Enemy Health의 Host 값 복제
/// 비호스트 피해 요청 전달
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class EnemyHealthNetworkSync : NetworkBehaviour, IDamageRouter
{
    // 호스트 기준 현재 체력
    [Networked] private float NetworkedHealth { get; set; }
    // 호스트 기준 사망 상태
    [Networked] private NetworkBool NetworkedIsDead { get; set; }
    // 상어 공격 연출 번호
    [Networked] private int AttackSequence { get; set; }
    // 호스트 기준 AI 상태
    [Networked] private int NetworkedAiState { get; set; }
    [Networked] private NetworkBool NetworkedIsSuspicious { get; set; }
    [Networked] private int DetectionSequence { get; set; }

    private Health health; // 같은 오브젝트의 로컬 체력
    private SharkController shark; // 상어 전용 연출 대상
    private OctopusController octopus; // 문어 전용 연출 대상
    private int renderedAttackSequence; // 마지막으로 재생한 공격 번호
    private int renderedAiState; // 마지막으로 적용한 AI 상태
    private bool subscribed; // 체력 이벤트 구독 여부
    private Rigidbody body; // 프록시 물리 실행 차단 대상

    // 같은 적의 로컬 컴포넌트 확보
    private int renderedDetectionSequence;
    private bool renderedIsSuspicious;

    private void Awake()
    {
        health = GetComponent<Health>();
        shark = GetComponent<SharkController>();
        octopus = GetComponent<OctopusController>();
        body = GetComponent<Rigidbody>();
    }

    // Host 초기값 게시
    // 프록시 초기값 적용
    public override void Spawned()
    {
        Subscribe();
        ConfigureProxyPhysics();

        if (Object.HasStateAuthority)
        {
            // 로컬 체력을 Host 초기값으로 게시
            NetworkedHealth = health.CurrentHealth;
            NetworkedIsDead = health.IsDead;
            NetworkedAiState = GetCurrentAiState();
            NetworkedIsSuspicious = shark != null && shark.IsSuspicious;
            renderedAttackSequence = AttackSequence;
            renderedAiState = NetworkedAiState;
            renderedDetectionSequence = DetectionSequence;
            renderedIsSuspicious = NetworkedIsSuspicious;
        }
        else
        {
            // 스폰 시점의 Host 값 즉시 적용
            ApplyReplicatedState();
            ApplyReplicatedAiState();
            ApplyReplicatedSuspicion();
            renderedAttackSequence = AttackSequence;
            renderedAiState = NetworkedAiState;
            renderedDetectionSequence = DetectionSequence;
            renderedIsSuspicious = NetworkedIsSuspicious;
        }
    }

    // 적 이동 및 충돌 판정은 Host만 실행
    // Proxy Collider 유지 및 Rigidbody만 Kinematic 적용
    private void ConfigureProxyPhysics()
    {
        if (body == null || Object.HasStateAuthority)
            return;

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        body.interpolation = RigidbodyInterpolation.None;
    }

    // 프록시 체력과 연출 갱신
    public override void Render()
    {
        if (Object.HasStateAuthority)
            return;

        // 최신 체력과 사망 상태 적용
        ApplyReplicatedState();

        if (renderedAiState != NetworkedAiState)
        {
            // 변경된 AI 상태만 한 번 적용
            renderedAiState = NetworkedAiState;
            ApplyReplicatedAiState();
        }

        if (renderedIsSuspicious != NetworkedIsSuspicious)
        {
            renderedIsSuspicious = NetworkedIsSuspicious;
            ApplyReplicatedSuspicion();
        }

        if (renderedDetectionSequence != DetectionSequence)
        {
            renderedDetectionSequence = DetectionSequence;
            shark?.PlayReplicatedDetectionIndicator();
        }

        if (renderedAttackSequence == AttackSequence)
            return;

        // 변경된 공격 연출만 한 번 재생
        renderedAttackSequence = AttackSequence;
        shark?.PlayReplicatedAttackAnimation();
    }

    // 이벤트 구독 해제
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Unsubscribe();
    }

    // 비호스트 피해 전달 판정
    public bool RouteDamage(DamageInfo damageInfo)
    {
        if (Object == null || !Object.IsValid || Object.HasStateAuthority)
            return false;

        NetworkObject sourceObject = damageInfo.Source != null
            ? damageInfo.Source.GetComponentInParent<NetworkObject>()
            : null;

        // 공격자 식별자와 피해 정보를 Host에 전달
        RPC_RequestDamage(
            damageInfo.RequestedAmount,
            sourceObject != null ? sourceObject.Id : default,
            (int)damageInfo.Type,
            damageInfo.PlayHitAnimation,
            damageInfo.HasImpactPoint,
            damageInfo.Point,
            damageInfo.Normal);
        return true;
    }

    // 비호스트 피해 요청
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDamage(
        float amount,
        NetworkId sourceId,
        int damageType,
        NetworkBool playHitAnimation,
        NetworkBool hasImpactPoint,
        Vector3 point,
        Vector3 normal)
    {
        GameObject source = null;
        // 공격자 네트워크 오브젝트 복원
        if (sourceId.IsValid && Runner.TryFindObject(sourceId, out NetworkObject sourceObject))
            source = sourceObject.gameObject;

        // 충돌 지점 존재 여부에 맞는 피해 정보 생성
        DamageInfo damage = hasImpactPoint
            ? new DamageInfo(amount, source, point, normal, (DamageType)damageType, playHitAnimation)
            : DamageInfo.WithoutImpact(amount, source, (DamageType)damageType, playHitAnimation);

        // 같은 적의 로컬 Health에 피해 적용
        health.ApplyDamage(damage);
    }

    // 상어 공격 연출 번호 증가
    public void PublishSharkAttack()
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            AttackSequence++;
    }

    // Replicates one-shot detection indicator playback.
    public void PublishSharkDetection()
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            DetectionSequence++;
    }

    public void PublishSharkSuspicion(bool suspicious)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedIsSuspicious = suspicious;
    }

    // 호스트 AI 상태 기록
    public void PublishAiState(int state)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedAiState = state;
    }

    private void Subscribe()
    {
        if (subscribed || health == null)
            return;

        // 로컬 체력 변경 이벤트 연결
        health.OnHealthChanged.AddListener(HandleHealthChanged);
        health.OnDeath.AddListener(HandleDeath);
        health.OnRevived.AddListener(HandleRevived);
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || health == null)
            return;

        // 로컬 체력 변경 이벤트 해제
        health.OnHealthChanged.RemoveListener(HandleHealthChanged);
        health.OnDeath.RemoveListener(HandleDeath);
        health.OnRevived.RemoveListener(HandleRevived);
        subscribed = false;
    }

    private void HandleHealthChanged(float current, float maximum)
    {
        // Host만 현재 체력 게시
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedHealth = current;
    }

    private void HandleDeath()
    {
        // Host만 사망 상태 게시
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedIsDead = true;
    }

    private void HandleRevived()
    {
        // Host만 부활 상태 게시
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedIsDead = false;
    }

    private void ApplyReplicatedState()
    {
        if (health == null)
            return;

        if (!Mathf.Approximately(health.CurrentHealth, NetworkedHealth)
            || health.IsDead != NetworkedIsDead)
        {
            // 값이 다를 때만 로컬 Health 갱신
            health.SyncFrom(NetworkedHealth, NetworkedIsDead);
        }
    }

    // 프록시 AI 상태 반영
    private void ApplyReplicatedSuspicion()
    {
        shark?.ApplyReplicatedSuspicion(NetworkedIsSuspicious);
    }

    private void ApplyReplicatedAiState()
    {
        if (shark != null)
            shark.ApplyReplicatedState((SharkStateType)NetworkedAiState);
        else if (octopus != null)
            octopus.ApplyReplicatedState((OctopusStateType)NetworkedAiState);
    }

    // 현재 AI 상태 조회
    private int GetCurrentAiState()
    {
        if (shark != null)
            return (int)shark.CurrentState;
        if (octopus != null)
            return (int)octopus.CurrentState;
        return 0;
    }

    private void OnDestroy()
    {
        // 로컬 파괴 시 잔여 이벤트 해제
        Unsubscribe();
    }
}
