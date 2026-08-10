using System.Collections.Generic;
using UnityEngine;
using Fusion;

// 박스(또는 캡슐) 프로토타입에 Rigidbody와 함께 붙이는 플레이어 이동 스크립트.
// 카메라의 "방향"만 참조하고, 카메라를 어떻게 붙이는지는 신경쓰지 않는다.
// -> 나중에 1인칭/3인칭을 바꿔도 이 스크립트는 그대로 재사용 가능.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : NetworkBehaviour
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

    [Header("Q버튼 동작 설정")]
    [Tooltip("Q키를 눌렀을 때 실행할 단축키")]
    public KeyCode quickRotateKey = KeyCode.Q;

    [Header("걷기 모드 - 점프")]
    [Tooltip("점프 시 순간적으로 부여되는 위쪽 속도. 문턱 같은 낮은 턱을 넘는 용도라 약하게 잡음")]
    public float jumpForce = 3f;
    [Tooltip("발밑으로 이 거리 안에 바닥이 있어야 점프 가능")]
    public float groundCheckDistance = 0.2f;
    public LayerMask groundLayer = ~0;

    private Rigidbody rb;
    private Vector3 inputDirection;
    private bool jumpRequested;
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
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int ThrowStateHash = Animator.StringToHash("Throw");
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

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Animator[] anims = GetComponentsInChildren<Animator>(true);
            foreach (var a in anims)
            {
                if (a.runtimeAnimatorController != null)
                {
                    animator = a;
                    break;
                }
            }
            if (animator == null) animator = GetComponent<Animator>();
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.SetFloat("HP", 100f); // 첫 프레임 HP=0 평가로 인한 Dead 모션 자동 발동 방지
        }

        CacheAnimatorParameters();

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

    private HashSet<int> animatorParameterHashes;

    // 애니메이터 컨트롤러에 실제로 존재하는 파라미터만 기억해둔다.
    // (컨트롤러에서 파라미터가 빠져도 매 프레임 "Parameter does not exist" 경고가 쏟아지지 않게)
    private void CacheAnimatorParameters()
    {
        animatorParameterHashes = new HashSet<int>();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in animator.parameters)
            animatorParameterHashes.Add(p.nameHash);
    }

    private bool HasAnimatorParameter(int hash)
        => animatorParameterHashes != null && animatorParameterHashes.Contains(hash);

    // 이 오브젝트가 네트워크에 스폰된 직후 한 번 호출됨 (Fusion 콜백)
    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            if (cameraRig == null)
                cameraRig = FindFirstObjectByType<PlayerCameraRig>();

            if (cameraRig != null)
                cameraRig.SetTarget(transform);
        }
    }

    private PlayerInteractor interactor;

    void OnEnable()
    {
        if (interactor == null)
            interactor = GetComponent<PlayerInteractor>();
        if (interactor == null)
            interactor = GetComponentInChildren<PlayerInteractor>();

        if (interactor != null)
        {
            interactor.OnInteracted -= TriggerGetAnimation;
            interactor.OnInteracted += TriggerGetAnimation;
        }
    }

    void OnDisable()
    {
        if (interactor != null)
        {
            interactor.OnInteracted -= TriggerGetAnimation;
        }
    }

    public void TriggerGetAnimation()
    {
        if (animator == null) return;
        animator.SetTrigger(GetHash);
        if (animator.HasState(0, GettingStateHash))
        {
            animator.Play(GettingStateHash, 0, 0f);
        }
    }

    void Update()
    {
        UpdateAnimator(); // 순수 시각 동기화라 모든 인스턴스에서 실행 (원격 캐릭터도 애니메이션 정상 표시되게)

        if (Object != null && !Object.HasInputAuthority) return;

        HandleQuickThrow();
        HandleClickMotions();
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
    }

    void LateUpdate()
    {
        if (visualTransform == null) return;

        // 어떠한 애니메이션/동작 상태라도 OtterVisual의 좌표 및 로테이션은 0으로 고정
        visualTransform.localPosition = Vector3.zero;
        visualTransform.localRotation = Quaternion.identity;
    }

    public void TriggerThrowAnimation()
    {
        if (animator == null) return;
        if (HasAnimatorParameter(ThrowHash))
            animator.SetTrigger(ThrowHash);

        PlayMotionState(ThrowStateHash);
    }

    private void HandleQuickThrow()
    {
        if (Input.GetKeyDown(quickRotateKey))
        {
            TriggerThrowAnimation();
        }
    }

    private void HandleClickMotions()
    {
        if (animator == null) return;

        // 맨손 상태 판별 (핫바가 없거나 슬롯 1인 경우)
        bool bareHanded = hotbar == null || hotbar.ActiveSlot == 1;
        if (!bareHanded) return;

        // 좌클릭: PushPull 모션 실행
        if (Input.GetMouseButtonDown(0))
        {
            SetPushPull(true);
            PlayMotionState(PushPullStateHash);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            SetPushPull(false);
        }

        // 우클릭: NoWeapon 모션 실행
        if (Input.GetMouseButtonDown(1))
        {
            PlayMotionState(NoWeaponStateHash);
        }
    }

    // 네트워크 세션이면 모두에게 전파하고, 러너가 없는 씬(팀원 테스트 씬 등)이면 로컬에서만 재생한다.
    // RPC는 스폰된 NetworkObject가 있어야 하므로 Object가 null이면 호출하면 안 된다
    public void PlayMotionState(int stateHash)
    {
        if (Object != null)
        {
            RPC_PlayMotionState(stateHash);
            return;
        }

        if (animator != null && animator.HasState(0, stateHash))
            animator.Play(stateHash, 0, 0f);
    }

    // 클릭 모션처럼 순간적으로 한 번 재생되는 동작을 모든 클라이언트에 전파한다.
    // (이동/수영 애니메이션은 복제된 속도로 각자 계산되므로 여기서 다루지 않음)
    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_PlayMotionState(int stateHash)
    {
        if (animator != null && animator.HasState(0, stateHash))
            animator.Play(stateHash, 0, 0f);
    }

    private float GetAnimationSpeed()
    {
        // 내가 시뮬레이션하는 캐릭터는 로컬 값이 가장 즉각적이라 그대로 쓴다
        bool isLocallySimulated = Object == null || Object.HasInputAuthority || Object.HasStateAuthority;
        if (isLocallySimulated)
        {
            float activeTargetSpeed = IsDashing ? dashSpeed : moveSpeed;
            return rb != null ? rb.linearVelocity.magnitude : inputDirection.magnitude * activeTargetSpeed;
        }

        // 남의 캐릭터(프록시)는 kinematic이라 로컬 속도가 0이므로, 권한자가 보내준 값을 쓴다
        return NetworkedSpeed;
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        float currentSpeed = GetAnimationSpeed();
        bool isMoving = inputDirection.sqrMagnitude > 0.01f || currentSpeed > 0.1f;

        animator.SetBool(IsMovingHash, isMoving);
        animator.SetFloat(SpeedHash, currentSpeed);
        animator.SetBool(IsSwimmingHash, true);

        // 본인은 로컬 값이라 즉각 반응하고, 남의 캐릭터는 복제된 값을 쓴다
        bool pushPull = (Object == null || Object.HasInputAuthority) ? localPushPull : NetworkedPushPull;
        if (HasAnimatorParameter(IsPushPullHash)) animator.SetBool(IsPushPullHash, pushPull);

        if (hotbar != null && HasAnimatorParameter(HasWeaponHash))
        {
            animator.SetBool(HasWeaponHash, hotbar.GetActiveItem() != null);
        }
    }

    // 애니메이션용 속도. 프록시는 Physics Addon이 Rigidbody를 강제로 kinematic으로 만들고
    // 속도 복제를 건너뛰기 때문에 rb.linearVelocity가 항상 0이라, 권한자가 이 값을 실어 보낸다.
    [Networked] private float NetworkedSpeed { get; set; }

    // 물건을 미는 자세. 누르고 있는 동안 유지되는 상태라 한 번 재생하는 모션(RPC)과 달리 값으로 들고 있어야 한다
    private bool localPushPull;
    [Networked] private NetworkBool NetworkedPushPull { get; set; }

    public void SetPushPull(bool value)
    {
        if (localPushPull == value) return;
        localPushPull = value;

        if (Object == null) return; // 러너 없는 씬은 로컬 값만으로 충분
        RPC_SetPushPull(value);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetPushPull(NetworkBool value) => NetworkedPushPull = value;

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority && rb != null)
            NetworkedSpeed = rb.linearVelocity.magnitude;

        // 프록시(내 화면 속 남의 캐릭터)에서는 GetInput이 false라 입력이 없다.
        // 그 상태로 이동/회전을 계산하면 기본값(Yaw=0, 정지)으로 몸을 꺾고 제동을 걸어서
        // NetworkRigidbody3D가 복제하는 진짜 값과 싸우게 되므로, 입력이 있을 때만 계산한다.
        if (!GetInput(out NetworkInputData input)) return;

        ReadInput(input);
        ApplyRotation();
        ApplyMovement();

        if (jumpRequested)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
            jumpRequested = false;
        }
    }

    private NetworkInputData lastInput;

    private void ReadInput(NetworkInputData input)
    {
        lastInput = input;

        Quaternion lookRotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
        Vector3 forward = lookRotation * Vector3.forward;
        Vector3 right = lookRotation * Vector3.right;

        float up = 0f;
        if (isSwimMode)
        {
            if (input.Up) up += 1f;
            if (input.Down) up -= 1f;
        }
        else
        {
            // 걷기 모드: 위아래를 봐도 바닥/천장으로 파고들지 않게 좌우 회전(요)만 사용
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            // 걷기 모드에서 Space로 점프. 수영 모드에선 Space가 상하 이동으로 이미 쓰이고 있어서 여기서 관여 안 함
            if (input.Up && IsGrounded())
                jumpRequested = true;
        }

        inputDirection = forward * input.Vertical + right * input.Horizontal + Vector3.up * up;

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        IsDashing = input.Dash && inputDirection.sqrMagnitude > 0.01f;
    }

    private void ApplyMovement()
    {
        // 2개의 수영(Swim1, Swim2) 모션 재생 여부 판별
        bool isSwimMotion = false;
        if (animator != null)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            isSwimMotion = stateInfo.shortNameHash == Swim1StateHash || stateInfo.shortNameHash == Swim2StateHash;
        }

        // 수영 모션이 아닌 다른 동작 모션(공격, 수리, 아이템 주움 등) 실행 중일 때는 속도 증가 및 가속도 제한
        float activeSpeed = (IsDashing && isSwimMotion) ? dashSpeed : moveSpeed;
        if (!isSwimMotion)
        {
            activeSpeed = Mathf.Min(activeSpeed, moveSpeed * 0.4f); // 수영 모션이 아닐 때 이동 속도 40%로 상한 제한
        }

        float activeAcceleration = isSwimMotion ? acceleration : acceleration * 0.3f; // 수영 외 모션 시 가속도 제한

        Vector3 targetVelocity = inputDirection * activeSpeed;

        if (isSwimMode)
        {
            Vector3 velocityError = targetVelocity - rb.linearVelocity;
            rb.AddForce(velocityError * activeAcceleration, ForceMode.Acceleration);

            // 입력이 없거나 수영 외 다른 모션 실행 중일 경우 자연스러운 감속 처리
            if (inputDirection.sqrMagnitude < 0.01f || !isSwimMotion)
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, drag * Runner.DeltaTime);
            }
        }
        else
        {
            // 걷기 모드: 수평(X/Z)만 제어하고 수직 속도는 중력에 맡김
            Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            Vector3 horizontalError = targetVelocity - horizontalVelocity;
            rb.AddForce(new Vector3(horizontalError.x, 0f, horizontalError.z) * activeAcceleration, ForceMode.Acceleration);

            if (inputDirection.sqrMagnitude < 0.01f || !isSwimMotion)
            {
                Vector3 damped = Vector3.Lerp(horizontalVelocity, Vector3.zero, drag * Runner.DeltaTime);
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

        if (shouldFaceCamera)
        {
            // 마우스 이동으로 카메라가 보는 방향으로 플레이어 몸통도 정렬 (Z축 회전은 0으로 고정)
            float pitch = isSwimMode ? lastInput.Pitch : 0f; // 걷기 모드에선 몸이 위아래로 안 기울어짐
            transform.rotation = Quaternion.Euler(pitch, lastInput.Yaw, 0f);
            return;
        }

        // snapToLookDirection이 false인 경우: 실제로 이동하는 방향으로 서서히 회전 (Z축 회전 0으로 고정)
        if (inputDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(inputDirection, Vector3.up);
            Vector3 euler = targetRotation.eulerAngles;
            Quaternion fixedTarget = Quaternion.Euler(euler.x, euler.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, fixedTarget, rotationSpeed * Runner.DeltaTime);
        }
    }
}