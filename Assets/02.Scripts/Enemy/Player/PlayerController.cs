using UnityEngine;

// 박스(또는 캡슐) 프로토타입에 Rigidbody와 함께 붙이는 플레이어 이동 스크립트.
// 카메라의 "방향"만 참조하고, 카메라를 어떻게 붙이는지는 신경쓰지 않는다.
// -> 나중에 1인칭/3인칭을 바꿔도 이 스크립트는 그대로 재사용 가능.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("이동 감각")]
    [Tooltip("최대 이동 속도")]
    public float moveSpeed = 4f;

    [Tooltip("목표 속도까지 도달하는 가속도. 클수록 반응이 즉각적")]
    public float acceleration = 8f;

    [Tooltip("입력이 없을 때 감속되는 정도 (물의 저항감)")]
    public float drag = 3f;

    [Header("회전 감각")]
    [Tooltip("이동 방향으로 몸통이 얼마나 빨리 따라 도는지")]
    public float rotationSpeed = 8f;
    [Tooltip("카메라를 연결하면 View Mode를 자동으로 읽어와서 1인칭/3인칭 전환 시 이 값을 직접 안 건드려도 됨")]
    public PlayerCameraRig cameraRig;
    [Tooltip("cameraRig를 연결 안 했을 때만 사용되는 수동 설정값")]
    public bool snapToLookDirection = false;

    [Header("참조")]
    [Tooltip("이동 기준이 되는 카메라(또는 카메라 리그) Transform")]
    public Transform lookReference;

    private Rigidbody rb;
    private Vector3 inputDirection;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;      // 부력/중력은 별도 시스템에서 다룰 예정
        rb.linearDamping = 0f;      // 감속은 이 스크립트가 직접 처리
        rb.constraints = RigidbodyConstraints.FreezeRotation; // 회전은 물리 대신 스크립트가 담당
        rb.interpolation = RigidbodyInterpolation.Interpolate; // 움직임을 부드럽게
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 동굴 벽 얇은 지오메트리 통과 방지
    }

    void Update()
    {
        ReadInput();
    }

    void FixedUpdate()
    {
        ApplyMovement();
        ApplyRotation();
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        float up = 0f;
        if (Input.GetKey(KeyCode.Space)) up += 1f;
        if (Input.GetKey(KeyCode.LeftControl)) up -= 1f;

        if (lookReference == null)
        {
            inputDirection = new Vector3(h, up, v);
        }
        else
        {
            // 카메라가 보는 수평/수직 방향을 그대로 이동 방향으로 사용 (자유 유영)
            Vector3 forward = lookReference.forward;
            Vector3 right = lookReference.right;
            inputDirection = forward * v + right * h + Vector3.up * up;
        }

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();
    }

    private void ApplyMovement()
    {
        Vector3 targetVelocity = inputDirection * moveSpeed;
        Vector3 velocityError = targetVelocity - rb.linearVelocity;
        rb.AddForce(velocityError * acceleration, ForceMode.Acceleration);

        // 입력이 거의 없을 때만 추가 감속 (물의 저항감을 살리는 부분)
        if (inputDirection.sqrMagnitude < 0.01f)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, drag * Time.fixedDeltaTime);
        }
    }

    private void ApplyRotation()
    {
        // cameraRig가 연결되어 있으면 "1인칭이거나 조준 중"일 때 몸을 카메라 방향으로 정렬
        // 연결 안 했으면 기존처럼 수동 체크박스(snapToLookDirection)를 따름
        bool shouldFaceCamera = snapToLookDirection;
        if (cameraRig != null)
        {
            shouldFaceCamera = cameraRig.viewMode == PlayerCameraRig.ViewMode.FirstPerson || cameraRig.IsAiming;
        }

        if (shouldFaceCamera && lookReference != null)
        {
            // 카메라가 보는 방향으로 몸도 즉시 정렬
            transform.rotation = lookReference.rotation;
            return;
        }

        // 평소(비조준, 3인칭)에는 실제로 이동하는 방향으로 서서히 회전
        if (inputDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(inputDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }
}