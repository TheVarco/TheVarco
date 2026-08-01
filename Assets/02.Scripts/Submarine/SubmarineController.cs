using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
// 운전자의 입력을 받아 잠수함의 전진/후진, 상승/하강, 좌우 회전 처리
// 속도를 즉시 바꾸지 않고 누적·감속하여 무거운 관성 적용
public class SubmarineController : MonoBehaviour
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

    [Header("Seat Manager")]
    [SerializeField] private SubmarineSeatManager seatManager;

    // 현재 조종 상태와 이동 상태를 읽을 때 사용하는 값들
    public bool HasDriver => seatManager != null && seatManager.HasDriver;
    public SubmarinePlayMode CurrentPlayMode => seatManager != null
        ? seatManager.CurrentPlayMode
        : SubmarinePlayMode.Solo;
    public float CurrentSpeed => new Vector2(forwardVelocity, verticalVelocity).magnitude;
    public float CurrentYawSpeed => Mathf.Abs(yawVelocity);
    
    // 하차하는 플레이어에게 전달할 수 있도록 현재 이동 속도를 월드 벡터로 변환
    public Vector3 CurrentWorldVelocity => transform.forward * forwardVelocity + Vector3.up * verticalVelocity;

    // 조이스틱 스크립트에서 읽을 수 있는 현재 입력값
    public float ThrottleInput => seatManager != null ? seatManager.ThrottleInput : 0f;
    public float SteeringInput => seatManager != null ? seatManager.SteeringInput : 0f;
    public float VerticalInput => seatManager != null ? seatManager.VerticalInput : 0f;

    private Rigidbody body;
    private Health health;

    private readonly RaycastHit[] castHits = new RaycastHit[32];
    private readonly Collider[] overlapHits = new Collider[32];
    private readonly Dictionary<int, float> lastDamageTimeByCollider = new Dictionary<int, float>();
    
    // 매 FixedUpdate마다 가속/감속되는 실제 내부 속도값들
    private float forwardVelocity;
    private float verticalVelocity;
    private float yawVelocity;

    private void Awake()
    {
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

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

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
        Vector3 worldVelocityBeforeCollision = CurrentWorldVelocity;
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
            Vector3 slidingVelocity = Vector3.ProjectOnPlane(worldVelocityBeforeCollision, wallHit.normal);
            forwardVelocity = Vector3.Dot(slidingVelocity, transform.forward);
            verticalVelocity = Vector3.Dot(slidingVelocity, Vector3.up);

            float normalSpeed = Mathf.Abs(Vector3.Dot(worldVelocityBeforeCollision, wallHit.normal));
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
        return candidate == null
            || candidate.attachedRigidbody == body
            || candidate.transform.IsChildOf(transform);
    }

    /// <summary>
    /// 회전을 막은 Collider와 접촉 지점의 회전 속도를 계산해 충돌 피해 처리
    /// </summary>
    private void ApplyRotationCollisionDamage(Collider blocker, Vector3 nextPosition)
    {
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
        if (health == null || health.IsDead || sourceCollider == null || normalSpeed <= minimumDamageSpeed)
            return;

        int colliderId = sourceCollider.GetInstanceID();
        if (lastDamageTimeByCollider.TryGetValue(colliderId, out float lastDamageTime)
            && Time.time - lastDamageTime < collisionDamageCooldown)
        {
            return;
        }

        float damage = Mathf.Min(
            maximumCollisionDamage,
            (normalSpeed - minimumDamageSpeed) * damagePerExcessSpeed);
        if (damage <= 0f)
            return;

        lastDamageTimeByCollider[colliderId] = Time.time;
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
        float rate = hasInput ? acceleration : coastDeceleration;
        return Mathf.MoveTowards(current, target, rate * deltaTime);
    }

    // 잠수함이 비활성화될 때 충돌 피해 재사용 대기 기록 제거
    private void OnDisable()
    {
        lastDamageTimeByCollider.Clear();
    }

    private void OnDrawGizmosSelected()
    {
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
