using System.Collections.Generic;
using Fusion;
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
// 운전자의 입력을 받아 잠수함의 전진/후진, 상승/하강, 좌우 회전 처리
// 속도를 즉시 바꾸지 않고 누적·감속하여 무거운 관성 적용
public class SubmarineController : NetworkBehaviour, IExternalMotionReceiver
{
    [Header("Forward / Reverse")]
    // 선체의 로컬 forward 방향으로 움직이는 속도와 가속/자연 감속 설정
    [SerializeField] private float maxForwardSpeed = 8f;
    [SerializeField] private float maxReverseSpeed = 4f;
    [SerializeField] private float forwardAcceleration = 2.5f;
    [SerializeField] private float forwardCoastDeceleration = 0.75f;

    [Header("Vertical")]
    // 월드 Y축 기준 상승/하강 속도와 가속/자연 감속 설정
    [SerializeField] private float maxVerticalSpeed = 3f;
    [SerializeField] private float verticalAcceleration = 2f;
    [SerializeField] private float verticalCoastDeceleration = 0.65f;

    [Header("Yaw")]
    // 월드 Y축 기준 좌우 회전 속도와 가속/자연 감속 설정
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
    public Vector3 DrivenWorldVelocity => transform.forward * forwardVelocity + Vector3.up * verticalVelocity; // 조종 입력으로 만든 속도
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

    private readonly RaycastHit[] castHits = new RaycastHit[32];
    private readonly Collider[] overlapHits = new Collider[32];
    private readonly Dictionary<int, float> lastDamageTimeByCollider = new Dictionary<int, float>();
    
    // 매 FixedUpdate마다 가속/감속되는 실제 내부 속도값들
    [Networked] private float NetworkedForwardVelocity { get; set; }
    [Networked] private float NetworkedVerticalVelocity { get; set; }
    [Networked] private float NetworkedYawVelocity { get; set; }
    [Networked] private Vector3 NetworkedExternalWorldVelocity { get; set; }

    // 네트워크 스폰 전 로컬 테스트 씬에서 사용하는 동일 상태 저장소
    private float localForwardVelocity;
    private float localVerticalVelocity;
    private float localYawVelocity;
    private Vector3 localExternalWorldVelocity;

    public bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

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

    // Rigidbody와 체력과 좌석 관리자와 충돌체 준비
    private void Awake()
    {
        // 필수 컴포넌트를 수집하고 네트워크와 로컬 이동 상태 초기화
        body = GetComponent<Rigidbody>();
        health = GetComponent<Health>();
        if (seatManager == null)
            seatManager = GetComponent<SubmarineSeatManager>();

        if (seatManager == null)
            Debug.LogError("SubmarineController: SubmarineSeatManager를 찾지 못했습니다.", this);

        body.useGravity = false;
        body.isKinematic = true; // Rigidbody 키네마틱 사용
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // 네트워크가 없는 씬의 Unity 물리 틱 이동
    private void FixedUpdate()
    {
        // Runner가 없을 때만 고정 시간으로 잠수함 이동 계산
        // 로컬 테스트 물리 실행
        if (!IsNetworkActive)
            SimulateMovement(Time.fixedDeltaTime);
    }

    // 호스트 권위 Fusion 틱 이동
    public override void FixedUpdateNetwork()
    {
        // StateAuthority만 합성 입력과 충돌과 이동 상태 변경
        if (!Object.HasStateAuthority)
            return;

        SimulateMovement(Runner.DeltaTime);
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
            out bool hitWall,
            out RaycastHit wallHit);

        if (hitWall)
        {
            // 벽 법선 방향 속도를 제거해 벽면을 따라 미끄러지게 처리
            Vector3 drivenSlidingVelocity = Vector3.ProjectOnPlane(drivenVelocityBeforeCollision, wallHit.normal);
            externalWorldVelocity = Vector3.ProjectOnPlane(externalVelocityBeforeCollision, wallHit.normal);
            forwardVelocity = Vector3.Dot(drivenSlidingVelocity, transform.forward);
            verticalVelocity = Vector3.Dot(drivenSlidingVelocity, Vector3.up);

            // 외부 속도 충돌 피해 감쇠
            float drivenNormalSpeed = Mathf.Abs(Vector3.Dot(drivenVelocityBeforeCollision, wallHit.normal)); // 벽을 향한 조종 속도 크기
            float externalNormalSpeed = Mathf.Abs(Vector3.Dot(externalVelocityBeforeCollision, wallHit.normal)); // 벽을 향한 외부 속도 크기
            float normalSpeed = drivenNormalSpeed + externalNormalSpeed * externalCollisionDamageMultiplier; // 감쇠한 외부 속도를 합친 피해 기준 속도
            ApplyCollisionDamage(
                wallHit.collider,
                wallHit.point,
                wallHit.normal,
                normalSpeed);
        }

        Vector3 nextPosition = body.position + resolvedDisplacement;
        Quaternion nextRotation = yawDelta * body.rotation;

        if (WouldIntroduceRotationOverlap(nextPosition, body.rotation, nextRotation, out Collider rotationBlocker))
        {
            ApplyRotationCollisionDamage(rotationBlocker, nextPosition);
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
        out bool hitWall,
        out RaycastHit firstHit)
    {
        // 이동 캡슐 캐스트 결과로 충돌 없는 변위와 충돌 정보 계산
        hitWall = false;
        firstHit = default;

        Vector3 resolved = Vector3.zero;
        Vector3 remaining = displacement;
        Vector3 castPosition = startPosition;

        // 첫 충돌에서 벽 법선 성분을 제거하고 모서리에서 한 번 더 검사
        for (int iteration = 0; iteration < 2; iteration++)
        {
            float distance = remaining.magnitude;
            if (distance <= 0.0001f)
                break;

            Vector3 direction = remaining / distance;
            if (!TryCapsuleCast(castPosition, rotation, direction, distance + collisionSkinWidth, out RaycastHit hit))
            {
                resolved += remaining;
                break;
            }

            if (!hitWall)
            {
                hitWall = true;
                firstHit = hit;
            }

            float travelDistance = Mathf.Max(0f, hit.distance - collisionSkinWidth);
            Vector3 travel = direction * Mathf.Min(travelDistance, distance);
            resolved += travel;
            castPosition += travel;

            Vector3 unconsumed = remaining - travel;
            remaining = Vector3.ProjectOnPlane(unconsumed, hit.normal);
        }

        return resolved;
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

            closestDistance = candidate.distance;
            closestHit = candidate;
        }

        return closestHit.collider != null;
    }

    /// <summary>
    /// 제안된 회전이 현재는 없던 외부 Collider 겹침을 새로 만드는지 확인
    /// </summary>
    private bool WouldIntroduceRotationOverlap(
        Vector3 position,
        Quaternion currentRotation,
        Quaternion proposedRotation,
        out Collider blocker)
    {
        // 다음 회전 자세에서 새 외부 겹침이 생기는지 검사
        blocker = null;

        if (Quaternion.Angle(currentRotation, proposedRotation) <= 0.001f)
            return false;

        // 이미 겹친 상태에서 회전을 모두 잠그지 않도록 새 회전에서 처음 생긴 겹침만 막는다.
        bool currentlyOverlapping = TryFindExternalOverlap(position, currentRotation, out _);
        bool proposedOverlapping = TryFindExternalOverlap(position, proposedRotation, out blocker);
        return !currentlyOverlapping && proposedOverlapping;
    }

    /// <summary>
    /// 지정한 위치와 회전에서 잠수함 캡슐과 겹치는 외부 Collider를 찾는다.
    /// </summary>
    private bool TryFindExternalOverlap(Vector3 position, Quaternion rotation, out Collider blocker)
    {
        // 지정 자세의 캡슐 겹침 중 잠수함 자체가 아닌 충돌체 검색
        GetWorldCapsule(position, rotation, out Vector3 pointA, out Vector3 pointB, out float radius);

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            radius,
            overlapHits,
            collisionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++)
        {
            Collider candidate = overlapHits[i];
            if (IsOwnCollider(candidate))
                continue;

            blocker = candidate;
            return true;
        }

        blocker = null;
        return false;
    }

    /// <summary>
    /// 잠수함의 위치, 회전, 스케일을 반영한 월드 공간 캡슐의 양 끝점과 반지름 계산
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
    /// 회전을 막은 Collider와 접촉 지점의 회전 속도를 계산해 충돌 피해 처리
    /// </summary>
    private void ApplyRotationCollisionDamage(Collider blocker, Vector3 nextPosition)
    {
        // 회전이 막힌 지점과 속도로 충돌 피해 계산 경로 호출
        if (blocker == null)
            return;

        Vector3 hitPoint = blocker.ClosestPoint(nextPosition);
        Vector3 normal = nextPosition - hitPoint;
        if (normal.sqrMagnitude <= 0.0001f)
            normal = nextPosition - blocker.bounds.center;

        Vector3 angularVelocity = Vector3.up * (yawVelocity * Mathf.Deg2Rad);
        Vector3 pointVelocity = Vector3.Cross(angularVelocity, hitPoint - body.worldCenterOfMass);
        float normalSpeed = Mathf.Abs(Vector3.Dot(pointVelocity, normal.normalized));

        ApplyCollisionDamage(blocker, hitPoint, normal, normalSpeed);
    }

    /// <summary>
    /// 충돌 속도, 피해 한도, Collider별 재사용 대기시간을 적용해 잠수함에 충돌 피해를 준다.
    /// </summary>
    private void ApplyCollisionDamage(
        Collider sourceCollider,
        Vector3 hitPoint,
        Vector3 hitNormal,
        float normalSpeed)
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
        health.ApplyDamage(new DamageInfo(
            damage,
            sourceCollider.gameObject,
            hitPoint,
            hitNormal,
            DamageType.Collision,
            false));
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
