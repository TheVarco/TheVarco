using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;

// 잠수함 이동 상태 동기화
// 호스트가 합성 조종 입력으로 이동과 회전 계산
// 전진과 수직과 회전 속도를 네트워크 상태로 유지
// 충돌 외력과 피해 판정을 호스트 물리 틱에서 처리
// 네트워크가 없는 씬은 기존 로컬 물리 흐름 유지

[System.Serializable]
public struct SubmarineMotionState
{
    public float ForwardVelocity;
    public float VerticalVelocity;
    public float YawVelocity;
    public Vector3 ExternalWorldVelocity;
}

[RequireComponent(typeof(Rigidbody))]
// 운전자 입력 기반 전진 후진 상승 하강 좌우 회전 처리
// 속도를 즉시 바꾸지 않고 누적·감속하여 무거운 관성 적용
public class SubmarineController : NetworkBehaviour, IExternalMotionReceiver
{
    private struct CollisionContact
    {
        public Collider Collider;
        public Vector3 Point;
        public Vector3 Normal;
        public bool HasImpactPoint;
    }

    private const int MaxCollisionContacts = 3;
    private const float MovementEpsilon = 0.0001f;
    private const float ContactDirectionEpsilon = 0.0001f;
    private const float RotationDepthTolerance = 0.00001f;

    [Header("Forward / Reverse")]
    // 선체 로컬 forward 방향 속도와 가속 및 자연 감속 설정
    [SerializeField] private float maxForwardSpeed = 8f;
    [SerializeField] private float maxReverseSpeed = 4f;
    [SerializeField] private float forwardAcceleration = 2.5f;
    [SerializeField] private float forwardCoastDeceleration = 0.75f;

    [Header("Vertical")]
    // 월드 Y축 기준 상승 하강 속도와 가속 및 자연 감속 설정
    [SerializeField] private float maxVerticalSpeed = 3f;
    [SerializeField] private float verticalAcceleration = 2f;
    [SerializeField] private float verticalCoastDeceleration = 0.65f;

    [Header("Yaw")]
    // 월드 Y축 기준 좌우 회전 속도와 가속 및 자연 감속 설정
    [SerializeField] private float maxYawSpeed = 30f;
    [SerializeField] private float yawAcceleration = 18f;
    [SerializeField] private float yawCoastDeceleration = 7f;

    [Header("Hull Collision Probe")]
    [Tooltip("잠수함 이동을 막고 충돌 피해를 줄 환경 레이어")]
    [SerializeField] private LayerMask collisionMask = 65; // Default + Obstacle
    [Tooltip("잠수함 로컬 좌표 기준 캡슐 중심")]
    [SerializeField] private Vector3 collisionProbeCenter = Vector3.zero;
    [Tooltip("선체를 감싸는 캡슐 반지름")]
    [SerializeField, Min(0.01f)] private float collisionProbeRadius = 1.25f;
    [Tooltip("로컬 Z축 방향 캡슐 전체 길이")]
    [SerializeField, Min(0.02f)] private float collisionProbeHeight = 5.5f;
    [SerializeField, Min(0f)] private float collisionSkinWidth = 0.05f;

    [Header("Collision Damage")]
    [SerializeField, Min(0f)] private float minimumDamageSpeed = 3f;
    [SerializeField, Min(0f)] private float damagePerExcessSpeed = 5f;
    [SerializeField, Min(0f)] private float maximumCollisionDamage = 30f;
    [SerializeField, Min(0f)] private float collisionDamageCooldown = 0.5f;

    // 열수구 외부 이동 설정
    [Header("External Motion")]
    [Tooltip("External impulses and acceleration are scaled before being added to the submarine.")]
    [SerializeField, Min(0f)] private float externalMotionMultiplier = 0.35f; // 외부 힘을 잠수함 이동에 반영할 비율
    [SerializeField, Min(0f)] private float externalVelocityDamping = 2f; // 외부 속도가 초당 감소하는 양
    [SerializeField, Min(0f)] private float maximumExternalSpeed = 6f; // 누적 가능한 외부 속도 상한
    [Tooltip("Only this fraction of vent-created velocity contributes to wall collision damage.")]
    [SerializeField, Range(0f, 1f)] private float externalCollisionDamageMultiplier = 0.25f; // 외부 속도의 충돌 피해 반영 비율

    [Header("Seat Manager")]
    [SerializeField] private SubmarineSeatManager seatManager;

    [Header("Bulb Emission")]
    [Tooltip("Object_6 전구 메시 전체에 적용할 발광 머티리얼")]
    [SerializeField] private Material bulbEmissionMaterial;
    [SerializeField] private string bulbObjectName = "Object_6";

    private AudioSource submarineHumSource;

    // 현재 조종 상태와 이동 상태를 읽을 때 사용하는 값들
    public bool HasDriver => seatManager != null && seatManager.HasDriver;
    public SubmarinePlayMode CurrentPlayMode => seatManager != null
        ? seatManager.CurrentPlayMode
        : SubmarinePlayMode.Solo;
    public float CurrentSpeed => CurrentWorldVelocity.magnitude;
    public float CurrentYawSpeed => Mathf.Abs(yawVelocity);
    public int ExternalMotionReceiverId => GetInstanceID(); // 다중 콜라이더를 잠수함 하나로 식별
    
    // 하차하는 플레이어에게 전달할 수 있도록 현재 이동 속도를 월드 벡터로 변환
    // 조종 속도와 외부 속도 분리
    public Vector3 DrivenWorldVelocity =>
        transform.forward * forwardVelocity +
        Vector3.up * verticalVelocity +
        drivenSlideWorldVelocity; // 조종 입력과 벽면을 따라 남은 접선 속도
    public Vector3 ExternalWorldVelocity => externalWorldVelocity; // 열수구 등 외부 힘으로 만든 속도
    public Vector3 CurrentWorldVelocity => DrivenWorldVelocity + externalWorldVelocity; // 실제 이동에 사용하는 합산 속도

    // 조이스틱 스크립트에서 읽을 수 있는 현재 입력값
    public float ThrottleInput => seatManager != null ? seatManager.ThrottleInput : 0f;
    public float SteeringInput => seatManager != null ? seatManager.SteeringInput : 0f;
    public float VerticalInput => seatManager != null ? seatManager.VerticalInput : 0f;

    /// <summary>
    /// 체크포인트에서 저장하지 않는 이동 속도 초기화
    /// 조종 입력 결과와 외부 힘 결과 제거
    /// 충돌 피해 재사용 대기 기록 제거
    /// </summary>
    public void ResetMotionState()
    {
        // 기본 구조체를 복원 경로에 전달해 모든 이동값 초기화
        RestoreMotionState(default);
    }

    // 현재 잠수함 이동 결과를 체크포인트용 값으로 복사
    public SubmarineMotionState CaptureMotionState()
    {
        // 조종 속도 세 축과 외부 힘 속도를 하나의 구조체로 묶어 반환
        return new SubmarineMotionState
        {
            ForwardVelocity = forwardVelocity,
            VerticalVelocity = verticalVelocity,
            YawVelocity = yawVelocity,
            ExternalWorldVelocity = externalWorldVelocity
        };
    }

    // 저장된 이동 상태를 권위 잠수함에 안전한 범위로 복원
    public void RestoreMotionState(SubmarineMotionState state)
    {
        // 네트워크 세션에서는 호스트만 이동 상태 변경 가능
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        // 저장값을 현재 잠수함 설정의 최대 속도 범위로 제한
        forwardVelocity = Mathf.Clamp(state.ForwardVelocity, -maxReverseSpeed, maxForwardSpeed);
        verticalVelocity = Mathf.Clamp(state.VerticalVelocity, -maxVerticalSpeed, maxVerticalSpeed);
        yawVelocity = Mathf.Clamp(state.YawVelocity, -maxYawSpeed, maxYawSpeed);
        externalWorldVelocity = Vector3.ClampMagnitude(state.ExternalWorldVelocity, maximumExternalSpeed);
        drivenSlideWorldVelocity = Vector3.zero;
        // 복원 전 충돌 피해 재사용 대기 기록 제거
        lastDamageTimeByCollider.Clear();

        // 키네마틱 잠수함 이동
        // 동적 바디 속도 초기화
        if (body != null && !body.isKinematic)
        {
            // 동적 Rigidbody에 남은 Unity 물리 속도도 함께 제거
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private Rigidbody body;
    private Health health;
    private NetworkRigidbody3D networkBody;
    private CapsuleCollider rotationProbeCollider;
    private Vector3 rotationProbeLocalPosition;
    private Quaternion rotationProbeLocalRotation;

    private readonly RaycastHit[] castHits = new RaycastHit[32];
    private readonly Collider[] overlapHits = new Collider[32];
    private readonly Collider[] currentOverlapHits = new Collider[32];
    private readonly CollisionContact[] collisionContacts = new CollisionContact[MaxCollisionContacts];
    private readonly Dictionary<int, float> lastDamageTimeByCollider = new Dictionary<int, float>();
    
    // 매 FixedUpdate마다 가속/감속되는 실제 내부 속도값들
    [Networked] private float NetworkedForwardVelocity { get; set; }
    [Networked] private float NetworkedVerticalVelocity { get; set; }
    [Networked] private float NetworkedYawVelocity { get; set; }
    [Networked] private Vector3 NetworkedExternalWorldVelocity { get; set; }
    [Networked] private Vector3 NetworkedDrivenSlideWorldVelocity { get; set; }

    // 네트워크 스폰 전 로컬 테스트 씬에서 사용하는 동일 상태 저장소
    private float localForwardVelocity;
    private float localVerticalVelocity;
    private float localYawVelocity;
    private Vector3 localExternalWorldVelocity;
    private Vector3 localDrivenSlideWorldVelocity;

    // FixedUpdateNetwork 기반 Checkpoint 복원
    // 권위 Tick의 Rigidbody 자세와 NetworkRigidbody 상태 동시 적용
    private bool checkpointGameplayEnabled = true;
    private bool hasPendingCheckpointTeleport;
    private Vector3 pendingCheckpointPosition;
    private Quaternion pendingCheckpointRotation;
    private ulong checkpointTeleportRequestSequence;
    private ulong checkpointTeleportCompletedSequence;

    public bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

    // Coordinator용 Checkpoint 동기화 상태
    // Sequence 기반 개별 복원 요청 완료 대기
    internal bool IsCheckpointTeleportPending => hasPendingCheckpointTeleport;
    internal ulong CheckpointTeleportRequestSequence => checkpointTeleportRequestSequence;
    internal ulong CheckpointTeleportCompletedSequence => checkpointTeleportCompletedSequence;

    private float forwardVelocity
    {
        get => IsNetworkActive ? NetworkedForwardVelocity : localForwardVelocity;
        set
        {
            if (IsNetworkActive) NetworkedForwardVelocity = value;
            else localForwardVelocity = value;
        }
    }

    private float verticalVelocity
    {
        get => IsNetworkActive ? NetworkedVerticalVelocity : localVerticalVelocity;
        set
        {
            if (IsNetworkActive) NetworkedVerticalVelocity = value;
            else localVerticalVelocity = value;
        }
    }

    private float yawVelocity
    {
        get => IsNetworkActive ? NetworkedYawVelocity : localYawVelocity;
        set
        {
            if (IsNetworkActive) NetworkedYawVelocity = value;
            else localYawVelocity = value;
        }
    }

    private Vector3 externalWorldVelocity
    {
        get => IsNetworkActive ? NetworkedExternalWorldVelocity : localExternalWorldVelocity;
        set
        {
            if (IsNetworkActive) NetworkedExternalWorldVelocity = value;
            else localExternalWorldVelocity = value;
        }
    }

    private Vector3 drivenSlideWorldVelocity
    {
        get => IsNetworkActive ? NetworkedDrivenSlideWorldVelocity : localDrivenSlideWorldVelocity;
        set
        {
            if (IsNetworkActive) NetworkedDrivenSlideWorldVelocity = value;
            else localDrivenSlideWorldVelocity = value;
        }
    }

    // Rigidbody와 체력과 좌석 관리자와 충돌체 준비
    private void Awake()
    {
        // 필수 컴포넌트를 수집하고 네트워크와 로컬 이동 상태 초기화
        body = GetComponent<Rigidbody>();
        health = GetComponent<Health>();
        networkBody = GetComponent<NetworkRigidbody3D>();
        if (seatManager == null)
            seatManager = GetComponent<SubmarineSeatManager>();

        if (seatManager == null)
            Debug.LogError("SubmarineController: SubmarineSeatManager를 찾지 못했습니다.", this);

        body.useGravity = false;
        body.isKinematic = true; // Rigidbody 키네마틱 사용
        body.interpolation = RigidbodyInterpolation.Interpolate;
        CacheRotationProbe();
        ApplyBulbEmissionMaterial();
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library != null)
            submarineHumSource = VarcoAudio.EnsureLoop(
                transform, "Submarine Hum Audio", library.submarineHum, true, 0.08f, 3f, 55f);
    }

    private void ApplyBulbEmissionMaterial()
    {
        if (bulbEmissionMaterial == null || string.IsNullOrEmpty(bulbObjectName))
            return;

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            Renderer childRenderer = childRenderers[i];
            if (childRenderer == null || childRenderer.gameObject.name != bulbObjectName)
                continue;

            Material[] materials = childRenderer.sharedMaterials;
            if (materials.Length == 0)
                materials = new Material[1];

            materials[0] = bulbEmissionMaterial;
            childRenderer.sharedMaterials = materials;
        }
    }

    private void Update()
    {
        if (submarineHumSource == null)
            return;

        if (!submarineHumSource.isPlaying)
            submarineHumSource.Play();

        float inputAmount = Mathf.Max(
            Mathf.Abs(ThrottleInput),
            Mathf.Max(Mathf.Abs(SteeringInput), Mathf.Abs(VerticalInput)));
        float targetVolume = HasDriver ? Mathf.Lerp(0.18f, 0.46f, inputAmount) : 0.08f;
        submarineHumSource.volume = Mathf.MoveTowards(
            submarineHumSource.volume,
            targetVolume,
            Time.deltaTime * 0.7f);
    }

    private void CacheRotationProbe()
    {
        CapsuleCollider[] candidates = GetComponentsInChildren<CapsuleCollider>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            CapsuleCollider candidate = candidates[i];
            if (candidate != null && candidate.name == "Shark Target Volume")
            {
                rotationProbeCollider = candidate;
                break;
            }
        }

        if (rotationProbeCollider == null)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                CapsuleCollider candidate = candidates[i];
                if (candidate == null || candidate.direction != 2)
                    continue;

                if (Mathf.Approximately(candidate.radius, collisionProbeRadius)
                    && Mathf.Approximately(candidate.height, collisionProbeHeight))
                {
                    rotationProbeCollider = candidate;
                    break;
                }
            }
        }

        if (rotationProbeCollider == null)
            return;

        rotationProbeLocalPosition = Quaternion.Inverse(transform.rotation)
            * (rotationProbeCollider.transform.position - transform.position);
        rotationProbeLocalRotation = Quaternion.Inverse(transform.rotation) * rotationProbeCollider.transform.rotation;
    }

    // 네트워크가 없는 씬의 Unity 물리 틱 이동
    private void FixedUpdate()
    {
        if (!checkpointGameplayEnabled)
            return;

        // Runner가 없을 때만 고정 시간으로 잠수함 이동 계산
        // 로컬 테스트 물리 실행
        if (!IsNetworkActive)
            SimulateMovement(Time.fixedDeltaTime);
    }

    // 호스트 권위 Fusion 틱 이동
    public override void FixedUpdateNetwork()
    {
        // State Authority 전용 합성 입력과 충돌 및 이동 상태 변경
        if (!Object.HasStateAuthority)
            return;

        // 일반 이동 전 Checkpoint 자세 적용
        // Teleport Tick의 복원 속도 이동 방지
        if (hasPendingCheckpointTeleport)
        {
            ApplyPendingCheckpointTeleport();
            return;
        }

        if (!checkpointGameplayEnabled)
            return;

        SimulateMovement(Runner.DeltaTime);
    }

    // 네트워크 물리 틱에서 적용할 체크포인트 위치 예약
    internal bool QueueCheckpointTeleport(Vector3 position, Quaternion rotation)
    {
        if (!IsNetworkActive
            || networkBody == null
            || !Object.HasStateAuthority
            || !Object.IsInSimulation)
        {
            return false;
        }

        pendingCheckpointPosition = position;
        pendingCheckpointRotation = rotation;
        checkpointTeleportRequestSequence++;
        hasPendingCheckpointTeleport = true;
        return true;
    }

    internal bool HasCompletedCheckpointTeleport(ulong requestSequence)
    {
        return requestSequence == 0
            || checkpointTeleportCompletedSequence >= requestSequence;
    }

    internal bool TryGetCurrentFusionTick(out int tickRaw)
    {
        if (!IsNetworkActive)
        {
            tickRaw = 0;
            return false;
        }

        tickRaw = Runner.Tick.Raw;
        return true;
    }

    internal void SetCheckpointGameplayEnabled(bool enabled)
    {
        if (checkpointGameplayEnabled == enabled)
            return;

        checkpointGameplayEnabled = enabled;
        if (enabled)
            return;

        // 컴포넌트 비활성화 시 기존 정리 동작 유지
        // 예약 Teleport용 FixedUpdateNetwork 실행 유지
        lastDamageTimeByCollider.Clear();
        if (!IsNetworkActive || Object.HasStateAuthority)
            externalWorldVelocity = Vector3.zero;
    }

    private void ApplyPendingCheckpointTeleport()
    {
        if (!hasPendingCheckpointTeleport || networkBody == null || !Object.IsInSimulation)
            return;

        networkBody.Teleport(pendingCheckpointPosition, pendingCheckpointRotation);
        hasPendingCheckpointTeleport = false;
        checkpointTeleportCompletedSequence = checkpointTeleportRequestSequence;
    }

    // 입력 가속과 외력과 충돌을 합쳐 다음 잠수함 자세 계산
    private void SimulateMovement(float dt)
    {
        // 좌석 입력으로 목표 속도를 만들고 가감속 제한 적용

        // 전진과 후진은 서로 다른 최대 속도를 사용할 수 있음
        float targetForwardSpeed = ThrottleInput >= 0f
            ? ThrottleInput * maxForwardSpeed
            : ThrottleInput * maxReverseSpeed;

        // 입력 중에는 가속도를
        // 입력이 없을 때는 자연 감속도 적용
        forwardVelocity = MoveVelocity(
            forwardVelocity,
            targetForwardSpeed,
            forwardAcceleration,
            forwardCoastDeceleration,
            Mathf.Abs(ThrottleInput) > 0.01f,
            dt);

        float targetVerticalSpeed = VerticalInput * maxVerticalSpeed;
        verticalVelocity = MoveVelocity(
            verticalVelocity,
            targetVerticalSpeed,
            verticalAcceleration,
            verticalCoastDeceleration,
            Mathf.Abs(VerticalInput) > 0.01f,
            dt);

        float targetYawSpeed = SteeringInput * maxYawSpeed;
        yawVelocity = MoveVelocity(
            yawVelocity,
            targetYawSpeed,
            yawAcceleration,
            yawCoastDeceleration,
            Mathf.Abs(SteeringInput) > 0.01f,
            dt);

        // 벽 충돌 시 전진 속도의 월드 측면 접선 전환
        // Tick 사이 접선 속도 유지
        // 접촉과 입력 해제 시 일반 조종 속도와 동일한 감속
        drivenSlideWorldVelocity = Vector3.MoveTowards(
            drivenSlideWorldVelocity,
            Vector3.zero,
            forwardCoastDeceleration * dt);

        // 누적된 속도를 물리 프레임의 위치/회전 변화량으로 변환
        Vector3 drivenVelocityBeforeCollision = DrivenWorldVelocity; // 충돌 전 조종 속도
        Vector3 externalVelocityBeforeCollision = externalWorldVelocity; // 충돌 전 외부 속도
        Vector3 worldVelocityBeforeCollision = drivenVelocityBeforeCollision + externalVelocityBeforeCollision; // 이동 검사에 사용할 전체 속도
        Vector3 displacement = worldVelocityBeforeCollision * dt;
        Quaternion yawDelta = Quaternion.AngleAxis(yawVelocity * dt, Vector3.up);

        Vector3 resolvedDisplacement = ResolveDisplacement(
            body.position,
            body.rotation,
            displacement,
            out int contactCount);

        if (contactCount > 0)
        {
            CollisionContact strongestImpact = default;
            float strongestNormalSpeed = 0f;

            for (int i = 0; i < contactCount; i++)
            {
                CollisionContact contact = collisionContacts[i];
                Vector3 normal = contact.Normal.normalized;

                // 표면 안쪽 속도만 충돌 피해 적용
                float drivenNormalSpeed = Mathf.Max(
                    0f,
                    -Vector3.Dot(drivenVelocityBeforeCollision, normal));
                float externalNormalSpeed = Mathf.Max(
                    0f,
                    -Vector3.Dot(externalVelocityBeforeCollision, normal));
                float totalNormalSpeed = Mathf.Max(
                    0f,
                    -Vector3.Dot(worldVelocityBeforeCollision, normal));
                float inwardContribution = drivenNormalSpeed + externalNormalSpeed;
                float damageContribution = drivenNormalSpeed
                    + externalNormalSpeed * externalCollisionDamageMultiplier;
                float normalSpeed = inwardContribution > MovementEpsilon
                    ? totalNormalSpeed * (damageContribution / inwardContribution)
                    : 0f;

                if (normalSpeed > strongestNormalSpeed)
                {
                    strongestImpact = contact;
                    strongestNormalSpeed = normalSpeed;
                }
            }

            // 모서리에서도 이동 Tick당 단일 충돌 피해 유지
            // 모든 접촉면의 속도 Clipping 적용
            if (strongestNormalSpeed > 0f)
            {
                ApplyCollisionDamage(
                    strongestImpact.Collider,
                    strongestImpact.Point,
                    strongestImpact.Normal.normalized,
                    strongestNormalSpeed,
                    strongestImpact.HasImpactPoint);
            }

            Vector3 drivenSlidingVelocity = ClipVelocityAgainstContacts(
                drivenVelocityBeforeCollision,
                contactCount);
            externalWorldVelocity = ClipVelocityAgainstContacts(
                externalVelocityBeforeCollision,
                contactCount);
            forwardVelocity = Vector3.Dot(drivenSlidingVelocity, transform.forward);
            verticalVelocity = Vector3.Dot(drivenSlidingVelocity, Vector3.up);
            drivenSlideWorldVelocity = drivenSlidingVelocity
                - transform.forward * forwardVelocity
                - Vector3.up * verticalVelocity;
        }

        Vector3 nextPosition = body.position + resolvedDisplacement;
        Quaternion nextRotation = yawDelta * body.rotation;

        if (WouldIntroduceOrDeepenRotationOverlap(nextPosition, body.rotation, nextRotation))
        {
            nextRotation = body.rotation;
            yawVelocity = 0f;
        }

        // 키네마틱 Rigidbody는 Transform을 직접 변경하지 않고 Move 계열 API로 이동
        body.MovePosition(nextPosition);
        body.MoveRotation(nextRotation);

        // 물리 프레임마다 외부 속도를 영으로 이동시켜 자연 감쇠
        externalWorldVelocity = Vector3.MoveTowards(
            externalWorldVelocity,
            Vector3.zero,
            externalVelocityDamping * dt);
    }

    // 외부 순간 속도에 잠수함 반영 비율을 곱해 누적
    public void ApplyExternalImpulse(Vector3 velocityChange)
    {
        // 즉시 속도 변화량을 외부 월드 속도에 추가
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        AddExternalVelocity(velocityChange * externalMotionMultiplier);
    }

    // 외부 가속도에 시간과 잠수함 반영 비율을 곱해 속도로 누적
    public void ApplyExternalAcceleration(Vector3 acceleration, float deltaTime)
    {
        // 가속도와 시간을 곱한 속도 변화량을 외력에 추가
        if (deltaTime <= 0f || (IsNetworkActive && !Object.HasStateAuthority))
            return;

        AddExternalVelocity(acceleration * externalMotionMultiplier * deltaTime);
    }

    // 외부 속도 합산 후 최대 속도 제한
    private void AddExternalVelocity(Vector3 velocityDelta)
    {
        // 유효한 벡터만 받아 최대 외부 속도 범위로 제한
        externalWorldVelocity = Vector3.ClampMagnitude(
            externalWorldVelocity + velocityDelta,
            maximumExternalSpeed);
    }

    /// <summary>
    /// 이동 경로의 충돌을 검사하고 벽면을 따라 미끄러질 수 있는 실제 이동량 계산
    /// </summary>
    private Vector3 ResolveDisplacement(
        Vector3 startPosition,
        Quaternion rotation,
        Vector3 displacement,
        out int contactCount)
    {
        // 이동 캡슐 캐스트 결과로 충돌 없는 변위와 접촉면 계산
        contactCount = 0;

        Vector3 resolved = Vector3.zero;
        Vector3 remaining = displacement;
        Vector3 castPosition = startPosition;

        // 최대 세 접촉면의 안쪽 성분만 제거해 벽과 모서리를 따라 이동
        for (int iteration = 0; iteration < MaxCollisionContacts; iteration++)
        {
            float distance = remaining.magnitude;
            if (distance <= MovementEpsilon)
                break;

            Vector3 direction = remaining / distance;
            if (TryGetBlockingOverlap(
                    castPosition,
                    rotation,
                    direction,
                    out Collider overlapCollider,
                    out Vector3 overlapNormal))
            {
                collisionContacts[contactCount++] = new CollisionContact
                {
                    Collider = overlapCollider,
                    Point = Vector3.zero,
                    Normal = overlapNormal,
                    HasImpactPoint = false
                };
                remaining = ClipVelocityAgainstContacts(remaining, contactCount);
                continue;
            }

            if (!TryCapsuleCast(castPosition, rotation, direction, distance + collisionSkinWidth, out RaycastHit hit))
            {
                resolved += remaining;
                break;
            }

            collisionContacts[contactCount++] = new CollisionContact
            {
                Collider = hit.collider,
                Point = hit.point,
                Normal = hit.normal,
                // 초기 겹침 Shape Cast 지점의 Decal 좌표 사용 제외
                // Unity의 0이 아닌 Vector 반환도 신뢰 대상 제외
                HasImpactPoint = HasReliableImpactPoint(hit.distance)
            };

            float travelDistance = Mathf.Max(0f, hit.distance - collisionSkinWidth);
            Vector3 travel = direction * Mathf.Min(travelDistance, distance);
            resolved += travel;
            castPosition += travel;

            Vector3 unconsumed = remaining - travel;
            remaining = ClipVelocityAgainstContacts(unconsumed, contactCount);
        }

        return resolved;
    }

    private Vector3 ClipVelocityAgainstContacts(Vector3 velocity, int contactCount)
    {
        Vector3 clipped = velocity;

        // 후속 평면 처리로 이전 평면 안쪽 성분 재발 가능
        // 모든 반공간 유지를 위한 소규모 접촉 목록 재검사
        for (int pass = 0; pass < MaxCollisionContacts; pass++)
        {
            bool changed = false;
            for (int i = 0; i < contactCount; i++)
            {
                Vector3 next = RemoveInwardComponent(
                    clipped,
                    collisionContacts[i].Normal);
                bool removedInwardComponent = (next - clipped).sqrMagnitude > 0f;
                clipped = next;
                changed |= removedInwardComponent;
            }

            if (!changed)
                break;
        }

        return clipped;
    }

    internal static Vector3 RemoveInwardComponent(
        Vector3 velocity,
        Vector3 outwardNormal)
    {
        float normalLengthSquared = outwardNormal.sqrMagnitude;
        if (normalLengthSquared <= Mathf.Epsilon)
            return velocity;

        Vector3 normalizedNormal = outwardNormal / Mathf.Sqrt(normalLengthSquared);
        float normalComponent = Vector3.Dot(velocity, normalizedNormal);
        return normalComponent < 0f
            ? velocity - normalizedNormal * normalComponent
            : velocity;
    }

    internal static bool HasReliableImpactPoint(float hitDistance)
    {
        return !float.IsNaN(hitDistance) &&
               !float.IsInfinity(hitDistance) &&
               hitDistance > MovementEpsilon;
    }

    private bool TryGetBlockingOverlap(
        Vector3 position,
        Quaternion rotation,
        Vector3 direction,
        out Collider blocker,
        out Vector3 normal)
    {
        blocker = null;
        normal = Vector3.zero;

        if (rotationProbeCollider == null)
            return false;

        GetWorldCapsule(position, rotation, out Vector3 pointA, out Vector3 pointB, out float radius);
        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            radius,
            currentOverlapHits,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        GetRotationProbePose(
            position,
            rotation,
            out Vector3 probePosition,
            out Quaternion probeRotation);

        float strongestInwardComponent = -ContactDirectionEpsilon;
        float selectedDepth = 0f;

        for (int i = 0; i < overlapCount; i++)
        {
            Collider candidate = currentOverlapHits[i];
            if (IsOwnCollider(candidate)
                || !TryGetPenetration(
                    candidate,
                    probePosition,
                    probeRotation,
                    out Vector3 separationDirection,
                    out float depth))
            {
                continue;
            }

            float inwardComponent = Vector3.Dot(direction, separationDirection);
            if (inwardComponent >= -ContactDirectionEpsilon)
                continue;

            bool isStronger = inwardComponent < strongestInwardComponent;
            bool isSameDirectionButDeeper = Mathf.Abs(inwardComponent - strongestInwardComponent)
                <= ContactDirectionEpsilon
                && depth > selectedDepth;
            if (!isStronger && !isSameDirectionButDeeper)
                continue;

            strongestInwardComponent = inwardComponent;
            selectedDepth = depth;
            blocker = candidate;
            normal = separationDirection;
        }

        return blocker != null;
    }

    /// <summary>
    /// 잠수함의 캡슐 형태로 이동 방향을 검사하고 자체 Collider를 제외한 가장 가까운 충돌 반환
    /// </summary>
    private bool TryCapsuleCast(
        Vector3 position,
        Quaternion rotation,
        Vector3 direction,
        float distance,
        out RaycastHit closestHit)
    {
        // 잠수함 캡슐을 이동 방향으로 투사해 가장 가까운 외부 충돌 선택
        GetWorldCapsule(position, rotation, out Vector3 pointA, out Vector3 pointB, out float radius);

        int hitCount = Physics.CapsuleCastNonAlloc(
            pointA,
            pointB,
            radius,
            direction,
            castHits,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        closestHit = default;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = castHits[i];
            if (IsOwnCollider(candidate.collider) || candidate.distance >= closestDistance)
                continue;

            if (candidate.distance <= MovementEpsilon && rotationProbeCollider != null)
            {
                GetRotationProbePose(
                    position,
                    rotation,
                    out Vector3 probePosition,
                    out Quaternion probeRotation);

                if (TryGetPenetration(
                        candidate.collider,
                        probePosition,
                        probeRotation,
                        out Vector3 separationDirection,
                        out _))
                {
                    // 초기 겹침 Shape Cast의 반대 방향 Normal 보정
                    // ComputePenetration 기반 실제 탈출 Normal 적용
                    // 안쪽과 바깥쪽 이동 구분
                    candidate.normal = separationDirection;
                }
            }

            // 표면 시작 Cast의 평행 및 이탈 이동에서 거리 0 Hit 가능
            // 거리 0 Hit로 인한 잠수함 고정 방지
            if (candidate.distance <= collisionSkinWidth + MovementEpsilon
                && Vector3.Dot(direction, candidate.normal) >= -ContactDirectionEpsilon)
            {
                continue;
            }

            closestDistance = candidate.distance;
            closestHit = candidate;
        }

        return closestHit.collider != null;
    }

    /// <summary>
    /// 새 겹침 또는 침투 증가 시에만 Yaw 차단
    /// 기존 겹침 유지 및 감소 회전을 탈출 방향으로 허용
    /// </summary>
    private bool WouldIntroduceOrDeepenRotationOverlap(
        Vector3 position,
        Quaternion currentRotation,
        Quaternion proposedRotation)
    {
        if (Quaternion.Angle(currentRotation, proposedRotation) <= 0.001f)
            return false;

        GetWorldCapsule(
            position,
            proposedRotation,
            out Vector3 proposedPointA,
            out Vector3 proposedPointB,
            out float proposedRadius);

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            proposedPointA,
            proposedPointB,
            proposedRadius,
            overlapHits,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        GetWorldCapsule(
            position,
            currentRotation,
            out Vector3 currentPointA,
            out Vector3 currentPointB,
            out float currentRadius);

        int currentOverlapCount = Physics.OverlapCapsuleNonAlloc(
            currentPointA,
            currentPointB,
            currentRadius,
            currentOverlapHits,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        if (rotationProbeCollider == null)
        {
            for (int i = 0; i < overlapCount; i++)
            {
                Collider candidate = overlapHits[i];
                if (!IsOwnCollider(candidate)
                    && !ContainsCollider(currentOverlapHits, currentOverlapCount, candidate))
                    return true;
            }

            return false;
        }

        GetRotationProbePose(position, currentRotation, out Vector3 currentProbePosition, out Quaternion currentProbeRotation);
        GetRotationProbePose(position, proposedRotation, out Vector3 proposedProbePosition, out Quaternion proposedProbeRotation);

        for (int i = 0; i < overlapCount; i++)
        {
            Collider candidate = overlapHits[i];
            if (IsOwnCollider(candidate))
                continue;

            bool currentlyOverlapsCandidate = ContainsCollider(
                currentOverlapHits,
                currentOverlapCount,
                candidate);
            float currentDepth = GetPenetrationDepth(
                candidate,
                currentProbePosition,
                currentProbeRotation);
            float proposedDepth = GetPenetrationDepth(
                candidate,
                proposedProbePosition,
                proposedProbeRotation);

            if (proposedDepth <= 0f)
            {
                if (!currentlyOverlapsCandidate)
                    return true;

                continue;
            }

            if (proposedDepth > currentDepth + RotationDepthTolerance)
                return true;
        }

        return false;
    }

    private static bool ContainsCollider(Collider[] colliders, int count, Collider target)
    {
        for (int i = 0; i < count; i++)
        {
            if (colliders[i] == target)
                return true;
        }

        return false;
    }

    private void GetRotationProbePose(
        Vector3 rootPosition,
        Quaternion rootRotation,
        out Vector3 probePosition,
        out Quaternion probeRotation)
    {
        probePosition = rootPosition + rootRotation * rotationProbeLocalPosition;
        probeRotation = rootRotation * rotationProbeLocalRotation;
    }

    private float GetPenetrationDepth(
        Collider other,
        Vector3 probePosition,
        Quaternion probeRotation)
    {
        return TryGetPenetration(
            other,
            probePosition,
            probeRotation,
            out _,
            out float distance)
            ? distance
            : 0f;
    }

    private bool TryGetPenetration(
        Collider other,
        Vector3 probePosition,
        Quaternion probeRotation,
        out Vector3 direction,
        out float distance)
    {
        direction = Vector3.zero;
        distance = 0f;

        return other != null
            && rotationProbeCollider != null
            && Physics.ComputePenetration(
                rotationProbeCollider,
                probePosition,
                probeRotation,
                other,
                other.transform.position,
                other.transform.rotation,
                out direction,
                out distance);
    }

    /// <summary>
    /// 잠수함 위치 회전 스케일 기반 월드 공간 Capsule 양 끝점과 반지름 계산
    /// </summary>
    private void GetWorldCapsule(
        Vector3 position,
        Quaternion rotation,
        out Vector3 pointA,
        out Vector3 pointB,
        out float radius)
    {
        // Transform 스케일과 회전을 반영해 월드 캡슐 치수 계산
        Vector3 scale = transform.lossyScale;
        float radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        float lengthScale = Mathf.Abs(scale.z);

        radius = collisionProbeRadius * radialScale;
        float halfLineLength = Mathf.Max(0f, collisionProbeHeight * lengthScale * 0.5f - radius);
        Vector3 center = position + rotation * Vector3.Scale(collisionProbeCenter, scale);
        Vector3 axisOffset = rotation * Vector3.forward * halfLineLength;

        pointA = center + axisOffset;
        pointB = center - axisOffset;
    }

    /// <summary>
    /// 검사 대상이 없거나 잠수함 자신 또는 자식 오브젝트의 Collider인지 확인
    /// </summary>
    private bool IsOwnCollider(Collider candidate)
    {
        // 후보 충돌체가 잠수함 루트 자식인지 확인
        return candidate == null
            || candidate.attachedRigidbody == body
            || candidate.transform.IsChildOf(transform);
    }

    /// <summary>
    /// 충돌 속도 피해 한도 Collider별 재사용 대기 시간 기반 잠수함 피해 적용
    /// </summary>
    private void ApplyCollisionDamage(
        Collider sourceCollider,
        Vector3 hitPoint,
        Vector3 hitNormal,
        float normalSpeed,
        bool hasImpactPoint)
    {
        // 체력과 충돌 속도와 Collider별 재사용 대기 조건 검증
        if (health == null || health.IsDead || sourceCollider == null || normalSpeed <= minimumDamageSpeed)
            return;

        int colliderId = sourceCollider.GetInstanceID();
        float now = IsNetworkActive ? (float)Runner.SimulationTime : Time.time;
        if (lastDamageTimeByCollider.TryGetValue(colliderId, out float lastDamageTime)
            && now - lastDamageTime < collisionDamageCooldown)
        {
            return;
        }

        float damage = Mathf.Min(
            maximumCollisionDamage,
            (normalSpeed - minimumDamageSpeed) * damagePerExcessSpeed);
        if (damage <= 0f)
            return;

        lastDamageTimeByCollider[colliderId] = now;
        DamageInfo damageInfo = hasImpactPoint
            ? new DamageInfo(
                damage,
                sourceCollider.gameObject,
                hitPoint,
                hitNormal,
                DamageType.Collision,
                false)
            : DamageInfo.WithoutImpact(
                damage,
                sourceCollider.gameObject,
                DamageType.Collision,
                false);
        health.ApplyDamage(damageInfo);
        PlayCollisionAudio(colliderId);
    }

    private void PlayCollisionAudio(int seed)
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library == null)
            return;

        if (IsNetworkActive)
        {
            RPC_PlayCollisionAudio(seed);
            return;
        }

        VarcoAudio.PlayOneShotAt(
            transform,
            library.GetSubmarineImpact(seed),
            0.82f,
            3f,
            55f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayCollisionAudio(int seed)
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library != null)
            VarcoAudio.PlayOneShotAt(
                transform,
                library.GetSubmarineImpact(seed),
                0.82f,
                3f,
                55f);
    }

    // 현재 속도를 목표 속도 쪽으로 일정한 비율로 이동
    // 입력 여부에 따라 가속도 또는 자연 감속도 선택
    private static float MoveVelocity(
        float current,
        float target,
        float acceleration,
        float coastDeceleration,
        bool hasInput,
        float deltaTime)
    {
        // 입력 유무에 따라 가속률을 선택해 목표 속도로 보간
        float rate = hasInput ? acceleration : coastDeceleration;
        return Mathf.MoveTowards(current, target, rate * deltaTime);
    }

    // 잠수함이 비활성화될 때 충돌 피해 재사용 대기 기록 제거
    private void OnDisable()
    {
        // 로컬 테스트 이동 상태와 충돌 피해 기록 초기화
        lastDamageTimeByCollider.Clear();
        if (!IsNetworkActive || Object.HasStateAuthority)
            externalWorldVelocity = Vector3.zero;
    }

    // 에디터에서 잠수함 충돌 캡슐 표시
    private void OnDrawGizmosSelected()
    {
        // 현재 CapsuleCollider를 월드 좌표로 변환해 와이어 구체와 선 그리기
        Vector3 position = Application.isPlaying && body != null ? body.position : transform.position;
        Quaternion rotation = Application.isPlaying && body != null ? body.rotation : transform.rotation;
        GetWorldCapsule(position, rotation, out Vector3 pointA, out Vector3 pointB, out float radius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pointA, radius);
        Gizmos.DrawWireSphere(pointB, radius);
        Gizmos.DrawLine(pointA + transform.up * radius, pointB + transform.up * radius);
        Gizmos.DrawLine(pointA - transform.up * radius, pointB - transform.up * radius);
        Gizmos.DrawLine(pointA + transform.right * radius, pointB + transform.right * radius);
        Gizmos.DrawLine(pointA - transform.right * radius, pointB - transform.right * radius);
    }
}
