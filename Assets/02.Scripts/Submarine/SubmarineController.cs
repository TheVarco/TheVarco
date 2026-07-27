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

    // 외부 스크립트가 현재 조종 상태와 이동 상태를 읽을 때 사용하는 값들
    public bool HasDriver => currentDriver != null;
    public float CurrentSpeed => new Vector2(forwardVelocity, verticalVelocity).magnitude;
    public float CurrentYawSpeed => Mathf.Abs(yawVelocity);
    
    // 하차하는 플레이어에게 전달할 수 있도록 현재 이동 속도를 월드 벡터로 변환
    public Vector3 CurrentWorldVelocity => transform.forward * forwardVelocity + Vector3.up * verticalVelocity;

    // 조이스틱 시각화 스크립트에서 읽을 수 있는 현재 입력값
    public float ThrottleInput { get; private set; }
    public float SteeringInput { get; private set; }
    public float VerticalInput { get; private set; }

    private Rigidbody body;
    private PlayerSeatController currentDriver;
    
    // 매 FixedUpdate마다 가속/감속되는 실제 내부 속도값들
    private float forwardVelocity;
    private float verticalVelocity;
    private float yawVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true; // Rigidbody 키네마틱 사용
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Update()
    {
        // 운전자가 없으면 입력만 즉시 0으로
        // 이미 쌓인 속도는 FixedUpdate에서 자연 감속하므로 바로 멈추지 않음
        if (!HasDriver)
        {
            ClearInput();
            return;
        }

        // W/S는 전후진
        // A/D는 좌우 회전을 담당
        ThrottleInput = Input.GetAxisRaw("Vertical");
        SteeringInput = Input.GetAxisRaw("Horizontal");

        // Space는 상승
        // Ctrl은 하강을 담당
        float vertical = 0f;
        if (Input.GetKey(KeyCode.Space)) vertical += 1f;
        if (Input.GetKey(KeyCode.LeftControl)) vertical -= 1f;
        VerticalInput = vertical;
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
        Vector3 displacement = CurrentWorldVelocity * dt;
        Quaternion yawDelta = Quaternion.AngleAxis(yawVelocity * dt, Vector3.up);

        // 키네마틱 Rigidbody는 Transform을 직접 변경하지 않고 Move 계열 API로 이동
        body.MovePosition(body.position + displacement);
        body.MoveRotation(yawDelta * body.rotation);
    }

    // 빈 잠수함에 플레이어를 운전자로 등록한다.
    // 같은 플레이어의 중복 요청은 허용하지만 다른 운전자가 있으면 거부
    public bool TryAssignDriver(PlayerSeatController driver)
    {
        if (driver == null || (currentDriver != null && currentDriver != driver))
            return false;

        currentDriver = driver;
        return true;
    }

    // 요청한 플레이어가 현재 운전자일 때만 조종권을 해제
    public void ReleaseDriver(PlayerSeatController driver)
    {
        if (currentDriver != driver)
            return;

        currentDriver = null;
        ClearInput();
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

    // 운전자 입력을 모두 중립 상태로 초기화
    private void ClearInput()
    {
        ThrottleInput = 0f;
        SteeringInput = 0f;
        VerticalInput = 0f;
    }

    // 잠수함이 비활성화될 때 운전자 참조와 잔여 입력을 제거
    private void OnDisable()
    {
        currentDriver = null;
        ClearInput();
    }
}
