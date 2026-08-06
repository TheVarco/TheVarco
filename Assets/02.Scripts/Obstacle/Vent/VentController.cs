using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 분출구 상태
public enum VentState
{
    Inactive, // 파티클과 물리 판정 정지
    Warning, // 약한 예고 파티클 재생
    Active // 본 분출과 접촉 판정 실행
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
// 개별 분출구 제어
public sealed class VentController : MonoBehaviour, IPatternTarget
{
    [Header("Effect Volume")]
    [SerializeField] private Collider effectCollider; // 기체가 영향을 주는 트리거 범위
    [SerializeField] private LayerMask affectedLayers = ~0; // 힘과 피해를 받을 레이어

    [Header("Motion")]
    [SerializeField, Min(0f)] private float initialVelocityChange = 5f; // 첫 접촉 순간에 더하는 속도
    [SerializeField, Min(0f)] private float continuousAcceleration = 12f; // 체류 중 매 물리 프레임에 주는 가속도

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damagePerEruption = 10f; // 한 번의 분출에서 대상이 받는 피해

    [Header("Visuals")]
    [SerializeField] private ParticleSystem[] warningParticleSystems; // 예고 상태에서 재생할 파티클
    [SerializeField] private ParticleSystem[] activeParticleSystems; // 분출 상태에서 재생할 파티클

    [Header("Automatic Alone Pattern")]
    [SerializeField, Min(0f)] private float aloneStartDelay; // 자동 Alone 시작 전 대기 시간
    [SerializeField, Min(0f)] private float aloneWarningDuration = 1f; // 자동 Alone 예고 시간
    [SerializeField, Min(0.01f)] private float aloneActiveDuration = 2.5f; // 자동 Alone 분출 시간
    [SerializeField, Min(0f)] private float aloneRecoveryDuration = 1.5f; // 자동 Alone 다음 반복 전 휴식 시간

    private readonly HashSet<int> impulsedTargets = new HashSet<int>(); // 현재 분출에서 초기 충격을 받은 대상
    private readonly HashSet<int> damagedTargets = new HashSet<int>(); // 현재 분출에서 피해를 받은 대상
    private readonly HashSet<int> acceleratedThisFixedStep = new HashSet<int>(); // 현재 물리 프레임에서 가속한 대상
    private ObstaclePatternBase patternOwner; // 현재 분출구를 제어하는 외부 패턴
    private Coroutine aloneRoutine; // 자동 Alone 반복 코루틴
    private bool hasStarted; // Start 호출 완료 여부

    public VentState CurrentState { get; private set; } = VentState.Inactive; // 현재 분출 상태
    public Vector3 EruptionDirection => transform.up; // 오브젝트 위쪽을 기준으로 한 분출 방향
    public bool IsHazardActive => CurrentState == VentState.Active; // 물리 판정 활성 여부
    public bool IsPatternControlled => patternOwner != null; // 외부 패턴 제어 여부

    Object IPatternTarget.PatternTargetObject => this; // 공용 패턴의 Unity 생명주기 검사 대상

    // 공용 패턴에서 분출구 제어권을 요청
    bool IPatternTarget.ClaimPatternControl(ObstaclePatternBase owner)
    {
        return ClaimPatternControl(owner);
    }

    // 공용 패턴에서 분출구 제어권을 반환
    void IPatternTarget.ReleasePatternControl(ObstaclePatternBase owner)
    {
        ReleasePatternControl(owner);
    }

    // 공용 Warning 명령을 분출구 예고 상태로 변환
    void IPatternTarget.EnterPatternWarning()
    {
        SetState(VentState.Warning);
    }

    // 공용 Active 명령을 실제 분출 상태로 변환
    void IPatternTarget.EnterPatternActive()
    {
        SetState(VentState.Active);
    }

    // 공용 Inactive 명령을 분출 정지 상태로 변환
    void IPatternTarget.EnterPatternInactive()
    {
        SetState(VentState.Inactive);
    }

    // 공용 Reset 명령으로 파티클과 접촉 기록 완전 초기화
    void IPatternTarget.ResetPatternTarget()
    {
        ResetVent();
    }

    // 컴포넌트 추가 시 같은 오브젝트의 콜라이더 자동 연결
    private void Reset()
    {
        effectCollider = GetComponent<Collider>();
        ConfigureEffectCollider();
    }

    // 실행 시 참조 복구와 초기 상태 설정
    private void Awake()
    {
        if (effectCollider == null)
            effectCollider = GetComponent<Collider>();

        ConfigureEffectCollider();
        ResetVent();
    }

    // 모든 Awake 이후 외부 패턴 미등록 여부 확인
    private void Start()
    {
        hasStarted = true;
        TryStartAlonePattern();
    }

    // 재활성화된 미등록 분출구의 자동 Alone 재시작
    private void OnEnable()
    {
        if (hasStarted)
            TryStartAlonePattern();
    }

    private void FixedUpdate()
    {
        // 다중 콜라이더 대상도 물리 프레임당 한 번만 가속하도록 기록 초기화
        acceleratedThisFixedStep.Clear();
    }

    // 외부 패턴에 제어권을 넘기고 자동 Alone 중지
    internal bool ClaimPatternControl(ObstaclePatternBase owner)
    {
        if (owner == null)
            return false;

        // 다른 패턴이 먼저 확보한 분출구의 중복 제어 차단
        if (patternOwner != null && patternOwner != owner)
        {
            Debug.LogWarning($"{name} is already controlled by {patternOwner.name}.", this);
            return false;
        }

        patternOwner = owner;
        StopAlonePattern();
        ResetVent();
        return true;
    }

    // 외부 패턴 제어 해제 후 자동 Alone 복구
    internal void ReleasePatternControl(ObstaclePatternBase owner)
    {
        if (patternOwner != owner)
            return;

        patternOwner = null;
        ResetVent();
        TryStartAlonePattern();
    }

    // 실행 가능 조건을 만족할 때만 자동 Alone 코루틴 생성
    private void TryStartAlonePattern()
    {
        if (!hasStarted || !isActiveAndEnabled || patternOwner != null || aloneRoutine != null)
            return;

        aloneRoutine = StartCoroutine(RunAlonePattern());
    }

    // 실행 중인 자동 Alone 코루틴 안전 종료
    private void StopAlonePattern()
    {
        if (aloneRoutine == null)
            return;

        StopCoroutine(aloneRoutine);
        aloneRoutine = null;
    }

    // 시작 지연 후 예고 분출 휴식을 순서대로 반복
    private IEnumerator RunAlonePattern()
    {
        ResetVent();
        // 분출구별 시작 시점 차이를 만드는 최초 지연
        yield return WaitForDuration(aloneStartDelay);

        while (patternOwner == null && isActiveAndEnabled)
        {
            // 피해 없이 접근 가능한 예고 구간
            SetState(VentState.Warning);
            yield return WaitForDuration(aloneWarningDuration);
            if (patternOwner != null || !isActiveAndEnabled) yield break;

            // 파티클 힘 피해가 모두 활성화되는 분출 구간
            SetState(VentState.Active);
            yield return WaitForDuration(aloneActiveDuration);
            if (patternOwner != null || !isActiveAndEnabled) yield break;

            // 다음 반복 전 모든 판정을 끄는 휴식 구간
            SetState(VentState.Inactive);
            yield return WaitForDuration(aloneRecoveryDuration);
        }

        aloneRoutine = null;
    }

    // 양수 시간만 대기해 불필요한 한 프레임 지연 방지
    private static IEnumerator WaitForDuration(float duration)
    {
        if (duration > 0f)
            yield return new WaitForSeconds(duration);
    }

    // 상태에 맞춰 파티클과 물리 판정 전환
    public void SetState(VentState nextState)
    {
        if (CurrentState == nextState)
        {
            if (nextState == VentState.Active)
                BeginEruption();

            return;
        }

        CurrentState = nextState;

        switch (nextState)
        {
            case VentState.Inactive: // 모든 분출 판정 정지
                SetEffectVolumeEnabled(false);
                StopParticles(warningParticleSystems, ParticleSystemStopBehavior.StopEmitting);
                StopParticles(activeParticleSystems, ParticleSystemStopBehavior.StopEmitting);
                break;

            case VentState.Warning: // 예고 파티클만 재생
                SetEffectVolumeEnabled(false);
                StopParticles(activeParticleSystems, ParticleSystemStopBehavior.StopEmitting);
                PlayParticles(warningParticleSystems);
                break;

            case VentState.Active: // 접촉 기록 초기화 후 본 분출 시작
                BeginEruption();
                StopParticles(warningParticleSystems, ParticleSystemStopBehavior.StopEmitting);
                PlayParticles(activeParticleSystems);
                SetEffectVolumeEnabled(true);
                break;
        }
    }

    // 상태 파티클 접촉 기록 완전 초기화
    public void ResetVent()
    {
        CurrentState = VentState.Inactive;
        SetEffectVolumeEnabled(false);
        ClearContactHistory();
        StopParticles(warningParticleSystems, ParticleSystemStopBehavior.StopEmittingAndClear);
        StopParticles(activeParticleSystems, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // 새로운 분출마다 최초 충격과 피해 허용
    private void BeginEruption()
    {
        ClearContactHistory();
    }

    // 대상별 중복 처리 기록 제거
    private void ClearContactHistory()
    {
        impulsedTargets.Clear();
        damagedTargets.Clear();
        acceleratedThisFixedStep.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAffect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryAffect(other);
    }

    // 활성 분출에 닿은 대상의 이동과 피해 처리
    private void TryAffect(Collider other)
    {
        if (CurrentState != VentState.Active || other == null)
            return;

        // 대상 레이어 비트를 마스크와 비교해 제외 대상 조기 반환
        if ((affectedLayers.value & (1 << other.gameObject.layer)) == 0)
            return;

        IExternalMotionReceiver externalReceiver = other.GetComponentInParent<IExternalMotionReceiver>(); // 키네마틱 이동 수신 대상
        Rigidbody attachedBody = other.attachedRigidbody; // 콜라이더에 연결된 물리 본체

        int motionTargetId = 0; // 다중 콜라이더를 하나의 이동 대상으로 묶는 식별값
        // 키네마틱 대상은 전용 수신 인터페이스로 이동 전달
        if (externalReceiver != null)
        {
            motionTargetId = externalReceiver.ExternalMotionReceiverId;
            ApplyMotion(externalReceiver, motionTargetId);
        }
        // 동적 Rigidbody 대상은 Unity 힘으로 이동 적용
        else if (attachedBody != null && !attachedBody.isKinematic)
        {
            motionTargetId = attachedBody.GetInstanceID();
            ApplyMotion(attachedBody, motionTargetId);
        }

        Health health = other.GetComponentInParent<Health>(); // 부모 단위 체력 탐색
        // 같은 분출에서 체력 대상별 피해 한 번만 허용
        if (health != null && !health.IsDead && damagedTargets.Add(health.GetInstanceID()))
        {
            Vector3 hitPoint = other.ClosestPoint(transform.position); // 분출구와 가장 가까운 피격 지점
            health.ApplyDamage(new DamageInfo(
                damagePerEruption,
                gameObject,
                hitPoint,
                -EruptionDirection,
                DamageType.Environmental));
        }
    }

    // 키네마틱 대상에 최초 충격과 지속 가속 적용
    private void ApplyMotion(IExternalMotionReceiver receiver, int targetId)
    {
        // HashSet 추가 성공 시 현재 분출의 첫 접촉
        if (impulsedTargets.Add(targetId))
            receiver.ApplyExternalImpulse(EruptionDirection * initialVelocityChange);

        // 다중 콜라이더가 겹쳐도 물리 프레임당 한 번만 가속
        if (acceleratedThisFixedStep.Add(targetId))
            receiver.ApplyExternalAcceleration(EruptionDirection * continuousAcceleration, Time.fixedDeltaTime);
    }

    // 동적 Rigidbody에 최초 충격과 지속 가속 적용
    private void ApplyMotion(Rigidbody body, int targetId)
    {
        if (impulsedTargets.Add(targetId))
            body.AddForce(EruptionDirection * initialVelocityChange, ForceMode.VelocityChange);

        if (acceleratedThisFixedStep.Add(targetId))
            body.AddForce(EruptionDirection * continuousAcceleration, ForceMode.Acceleration);
    }

    // 물리 범위 콜라이더를 트리거로 강제 설정
    private void ConfigureEffectCollider()
    {
        if (effectCollider != null)
            effectCollider.isTrigger = true;
    }

    // 활성 분출에서만 물리 범위 사용
    private void SetEffectVolumeEnabled(bool enabled)
    {
        if (effectCollider != null)
            effectCollider.enabled = enabled;
    }

    // 등록된 파티클 전체 재생
    private static void PlayParticles(ParticleSystem[] systems)
    {
        if (systems == null)
            return;

        foreach (ParticleSystem system in systems) // 배열에 등록된 개별 파티클
        {
            if (system != null && !system.isPlaying)
                system.Play(true);
        }
    }

    // 등록된 파티클 전체 정지
    private static void StopParticles(ParticleSystem[] systems, ParticleSystemStopBehavior stopBehavior)
    {
        if (systems == null)
            return;

        foreach (ParticleSystem system in systems) // 배열에 등록된 개별 파티클
        {
            if (system != null)
                system.Stop(true, stopBehavior);
        }
    }

    // 비활성화 시 코루틴과 분출 상태 정리
    private void OnDisable()
    {
        StopAlonePattern();
        ResetVent();
    }

    // Inspector 입력값 유효 범위 제한
    private void OnValidate()
    {
        aloneStartDelay = Mathf.Max(0f, aloneStartDelay);
        aloneWarningDuration = Mathf.Max(0f, aloneWarningDuration);
        aloneActiveDuration = Mathf.Max(0.01f, aloneActiveDuration);
        aloneRecoveryDuration = Mathf.Max(0f, aloneRecoveryDuration);
    }

    // 선택한 분출구의 방향 표시
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CurrentState == VentState.Active
            ? new Color(1f, 0.25f, 0.1f, 0.9f)
            : new Color(0.2f, 0.8f, 1f, 0.75f);
        Gizmos.DrawLine(transform.position, transform.position + EruptionDirection * 3f);
        Gizmos.DrawSphere(transform.position + EruptionDirection * 3f, 0.12f);
    }
}
