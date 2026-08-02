using UnityEngine;

// 박스(또는 캡슐) 프로토타입에 Rigidbody와 함께 붙이는 플레이어 이동 스크립트.
// 카메라의 "방향"만 참조하고, 카메라를 어떻게 붙이는지는 신경쓰지 않는다.
// -> 나중에 1인칭/3인칭을 바꿔도 이 스크립트는 그대로 재사용 가능.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("이동 감각")]
    [Tooltip("최대 일반 이동 속도")]
    public float moveSpeed = 4f;

    [Tooltip("쉬프트 키 대시 이동 속도")]
    public float dashSpeed = 8f;

    [Tooltip("목표 속도까지 도달하는 가속도. 클수록 반응이 즉각적")]
    public float acceleration = 8f;

    [Tooltip("입력이 없을 때 감속되는 정도 (물의 저항감)")]
    public float drag = 3f;

    public bool IsDashing { get; private set; }

    [Header("회전 감각")]
    [Tooltip("이동 방향으로 몸통이 얼마나 빨리 따라 도는지")]
    public float rotationSpeed = 8f;
    [Tooltip("카메라를 연결하면 View Mode를 자동으로 읽어와서 1인칭/3인칭 전환 시 이 값을 직접 안 건드려도 됨")]
    public PlayerCameraRig cameraRig;
    [Tooltip("카메라 회전(마우스 이동)에 따라 플레이어 몸통도 회전할지 여부")]
    public bool snapToLookDirection = true;

    [Header("참조")]
    [Tooltip("이동 기준이 되는 카메라(또는 카메라 리그) Transform")]
    public Transform lookReference;
    [Tooltip("플레이어 애니메이터 (미설정 시 자동 감지)")]
    public Animator animator;
    [Tooltip("플레이어 핫바 (무기 소지 상태 업데이트용, 미설정 시 자동 감지)")]
    public PlayerHotbar hotbar;
    [Tooltip("OtterVisual 트랜스폼 (미지정 시 'OtterVisual' 또는 첫 번째 자식 자동 감지)")]
    public Transform visualTransform;

    [Header("Q버튼 회전 효과")]
    public KeyCode quickRotateKey = KeyCode.Q;
    [Tooltip("OtterVisual 360도 스핀 애니메이션 지속 시간(초)")]
    public float spinDuration = 0.4f;

    private Rigidbody rb;
    private Vector3 inputDirection;
    private Coroutine spinCoroutine;
    [SerializeField] private bool isSwimMode = true;

    // 잠수함 내부 등 "걸어야 하는 공간"에 들어오면 false로, 나가면 true로 호출됨 (PlayerWalkZone에서 호출)
    public void SetSwimMode(bool swimming)
    {
        isSwimMode = swimming;
        rb.useGravity = !swimming;
    }

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");
    private static readonly int HasWeaponHash = Animator.StringToHash("HasWeapon");
    private static readonly int NoWeaponStateHash = Animator.StringToHash("NoWeapon");
    private static readonly int PushPullStateHash = Animator.StringToHash("PushPull");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int IsPushPullHash = Animator.StringToHash("IsPushPull");
    private static readonly int GetHash = Animator.StringToHash("Get");
    private static readonly int GettingStateHash = Animator.StringToHash("Getting");
    private static readonly int DefaultStateHash = Animator.StringToHash("Default");
    private static readonly int Swim1StateHash = Animator.StringToHash("Swim1");
    private static readonly int Swim2StateHash = Animator.StringToHash("Swim2");

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;      // 부력/중력은 별도 시스템에서 다룰 예정
        rb.linearDamping = 0f;      // 감속은 이 스크립트가 직접 처리
        rb.constraints = RigidbodyConstraints.FreezeRotation; // 회전은 물리 대신 스크립트가 담당
        rb.interpolation = RigidbodyInterpolation.Interpolate; // 움직임을 부드럽게
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 동굴 벽 얇은 지오메트리 통과 방지

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }

        if (hotbar == null)
        {
            hotbar = GetComponent<PlayerHotbar>();
            if (hotbar == null)
            {
                hotbar = GetComponentInChildren<PlayerHotbar>();
            }
        }

        if (visualTransform == null)
        {
            visualTransform = transform.Find("OtterVisual");
            if (visualTransform == null && transform.childCount > 0)
            {
                visualTransform = transform.GetChild(0);
            }
        }
    }

    void Update()
    {
        ReadInput();
        ApplyRotation();
        UpdateAnimator();
        HandleQuickRotate();
        HandleClickMotions();
    }

    void LateUpdate()
    {
        if (visualTransform == null) return;

        // 어떠한 애니메이션/동작 상태라도 OtterVisual의 좌표 및 로테이션은 0으로 고정
        visualTransform.localPosition = Vector3.zero;
        if (spinCoroutine == null)
        {
            visualTransform.localRotation = Quaternion.identity;
        }
    }

    private void HandleClickMotions()
    {
        if (animator == null) return;

        // E키: Getting 모션 실행
        if (Input.GetKeyDown(KeyCode.E))
        {
            animator.SetTrigger(GetHash);
            if (animator.HasState(0, GettingStateHash))
            {
                animator.Play(GettingStateHash, 0, 0f);
            }
        }

        // 맨손 상태 판별 (핫바가 없거나 슬롯 1인 경우)
        bool bareHanded = hotbar == null || hotbar.ActiveSlot == 1;
        if (!bareHanded) return;

        // 좌클릭: PushPull 모션 실행
        if (Input.GetMouseButtonDown(0))
        {
            animator.SetBool(IsPushPullHash, true);
            if (animator.HasState(0, PushPullStateHash))
            {
                animator.Play(PushPullStateHash, 0, 0f);
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            animator.SetBool(IsPushPullHash, false);
        }

        // 우클릭: NoWeapon 모션 실행
        if (Input.GetMouseButtonDown(1))
        {
            animator.SetTrigger(AttackHash);
            if (animator.HasState(0, NoWeaponStateHash))
            {
                animator.Play(NoWeaponStateHash, 0, 0f);
            }
        }
    }

    private void HandleQuickRotate()
    {
        if (Input.GetKeyDown(quickRotateKey))
        {
            if (visualTransform != null)
            {
                // Default 또는 Swim(Swim1, Swim2) 모션 외에 다른 모션(Getting, PushPull, NoWeapon 등)이 실행 중이면 Q키 제한
                if (animator != null)
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                    bool isDefaultOrSwim = stateInfo.shortNameHash == DefaultStateHash ||
                                           stateInfo.shortNameHash == Swim1StateHash ||
                                           stateInfo.shortNameHash == Swim2StateHash;
                    if (!isDefaultOrSwim)
                    {
                        return;
                    }
                }

                float currentSpeed = rb != null ? rb.linearVelocity.magnitude : inputDirection.magnitude * moveSpeed;
                bool isSwimming = inputDirection.sqrMagnitude > 0.01f || currentSpeed > 0.1f;

                // 수영 중이면 Z축 360도 회전(배럴 롤), 멈춰있으면(Default) Y축 360도 회전
                Vector3 spinAxis = isSwimming ? new Vector3(0f, 0f, 360f) : new Vector3(0f, 360f, 0f);

                if (spinCoroutine != null)
                {
                    StopCoroutine(spinCoroutine);
                }
                spinCoroutine = StartCoroutine(AnimateVisualSpin(spinAxis));
            }
        }
    }

    private System.Collections.IEnumerator AnimateVisualSpin(Vector3 spinAxis)
    {
        float elapsed = 0f;
        while (elapsed < spinDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / spinDuration);
            visualTransform.localPosition = Vector3.zero;
            visualTransform.localRotation = Quaternion.Euler(spinAxis * progress);
            yield return null;
        }
        visualTransform.localPosition = Vector3.zero;
        visualTransform.localRotation = Quaternion.identity;
        spinCoroutine = null;
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        float activeTargetSpeed = IsDashing ? dashSpeed : moveSpeed;
        float currentSpeed = rb != null ? rb.linearVelocity.magnitude : inputDirection.magnitude * activeTargetSpeed;
        bool isMoving = inputDirection.sqrMagnitude > 0.01f || currentSpeed > 0.1f;

        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(SpeedHash, currentSpeed);
        animator.SetBool(IsSwimmingHash, true);

        if (hotbar != null)
        {
            animator.SetBool(HasWeaponHash, hotbar.GetActiveItem() != null);
        }
    }

    void FixedUpdate()
    {
        ApplyMovement();
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S

        Vector3 forward = lookReference != null ? lookReference.forward : transform.forward;
        Vector3 right = lookReference != null ? lookReference.right : transform.right;

        float up = 0f;
        if (isSwimMode)
        {
            if (Input.GetKey(KeyCode.Space)) up += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) up -= 1f;
        }
        else
        {
            // 걷기 모드: 위아래를 봐도 바닥/천장으로 파고들지 않게 좌우 회전(요)만 사용
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        inputDirection = forward * v + right * h + Vector3.up * up;

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        IsDashing = shiftHeld && inputDirection.sqrMagnitude > 0.01f;
    }

    private void ApplyMovement()
    {
        float activeSpeed = IsDashing ? dashSpeed : moveSpeed;
        Vector3 targetVelocity = inputDirection * activeSpeed;

        if (isSwimMode)
        {
            Vector3 velocityError = targetVelocity - rb.linearVelocity;
            rb.AddForce(velocityError * acceleration, ForceMode.Acceleration);

            // 입력이 거의 없을 때만 추가 감속 (물의 저항감을 살리는 부분)
            if (inputDirection.sqrMagnitude < 0.01f)
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, drag * Time.fixedDeltaTime);
            }
        }
        else
        {
            // 걷기 모드: 수평(X/Z)만 제어하고 수직 속도는 중력에 맡김 (나중에 점프 추가하기 쉬움)
            Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            Vector3 horizontalError = targetVelocity - horizontalVelocity;
            rb.AddForce(new Vector3(horizontalError.x, 0f, horizontalError.z) * acceleration, ForceMode.Acceleration);

            if (inputDirection.sqrMagnitude < 0.01f)
            {
                Vector3 damped = Vector3.Lerp(horizontalVelocity, Vector3.zero, drag * Time.fixedDeltaTime);
                rb.linearVelocity = new Vector3(damped.x, rb.linearVelocity.y, damped.z);
            }
        }
    }

    private void ApplyRotation()
    {
        // snapToLookDirection이 true이거나 1인칭 또는 조준 중일 때 몸을 카메라 방향으로 정렬
        bool shouldFaceCamera = snapToLookDirection;
        if (cameraRig != null)
        {
            shouldFaceCamera = shouldFaceCamera || cameraRig.viewMode == PlayerCameraRig.ViewMode.FirstPerson || cameraRig.IsAiming;
        }

        if (shouldFaceCamera && lookReference != null)
        {
            // 마우스 이동으로 카메라가 보는 방향으로 플레이어 몸통도 정렬 (Z축 회전은 0으로 고정)
            Vector3 euler = lookReference.rotation.eulerAngles;
            float pitch = isSwimMode ? euler.x : 0f; // 걷기 모드에선 몸이 위아래로 안 기울어짐
            transform.rotation = Quaternion.Euler(pitch, euler.y, 0f);
            return;
        }

        // snapToLookDirection이 false인 경우: 실제로 이동하는 방향으로 서서히 회전 (Z축 회전 0으로 고정)
        if (inputDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(inputDirection, Vector3.up);
            Vector3 euler = targetRotation.eulerAngles;
            Quaternion fixedTarget = Quaternion.Euler(euler.x, euler.y, 0f);
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.fixedDeltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, fixedTarget, rotationSpeed * dt);
        }
    }
}