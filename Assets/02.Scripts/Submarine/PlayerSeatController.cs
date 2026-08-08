using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
// 플레이어의 착석 상태를 관리
// 착석할 때는 플레이어 이동/행동과 충돌을 잠그고 조종석에 고정
// 하차할 때는 착석 전에 저장해 둔 상태를 다시 복원
public class PlayerSeatController : NetworkBehaviour
{
    private KeyCode exitKey = KeyCode.E; // 하차 상호작용 키
    private float statusMessageDuration = 1.5f; // 메시지 표시 시간

    [Header("References")]
    // 상호작용 UI, 착석 애니메이션, 공간 검사에 사용할 플레이어 컴포넌트
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private Animator animator;
    [SerializeField] private CapsuleCollider playerCollider;
    
    // // Inspector에서 추가로 지정한 컴포넌트도 착석 중 비활성화
    // [SerializeField] private MonoBehaviour[] disableWhileSeated;

    // currentSeat가 존재하면 플레이어가 착석 중인 것으로 판단
    public bool IsSeated => currentSeat != null;
    public CockpitSeat CurrentSeat => currentSeat;

    // Animator 파라미터 캐시
    private const string ExitPrompt = "E : 조종석에서 내리기";
    private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int IsPushPullHash = Animator.StringToHash("IsPushPull");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // 착석 전에 각 컴포넌트가 활성화되어 있었는지 저장
    // 하차할 때 무조건 켜는 대신 원래 상태 그대로 복원하기 위해 사용
    private readonly Dictionary<MonoBehaviour, bool> behaviourStates = new Dictionary<MonoBehaviour, bool>();
    private readonly Dictionary<Collider, bool> colliderStates = new Dictionary<Collider, bool>();

    private Rigidbody body;
    private Health health;
    private PlayerDownedState downedState;
    private CockpitSeat currentSeat;
    private Transform originalParent;
    private Vector3 originalLocalScale;
    private bool originalIsKinematic;
    private bool originalDetectCollisions;
    private bool originalUseGravity;
    private int enteredFrame = -1;
    private Coroutine promptRoutine;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = GetComponent<Health>();
        downedState = GetComponent<PlayerDownedState>();

        if (interactor == null)
            interactor = GetComponent<PlayerInteractor>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (playerCollider == null)
            playerCollider = GetComponent<CapsuleCollider>();

        if (health != null)
        {
            // 착석 중 사망하면 조종석에 남지 않도록 강제로 하차시킨다.
            health.OnDeath.AddListener(ForceExitFromSeat);
            health.OnRevived.AddListener(HandleRevived);
        }
    }

    private void Update()
    {
        // 내 캐릭터가 아니면(원격 플레이어) 로컬 입력을 읽지 않음. 비네트워크 씬에선 Object가 null이라 그대로 동작
        if (Object != null && !Object.HasInputAuthority) return;

        // 착석한 프레임에는 E 입력 무시 > 앉을 때 사용한 E가 곧바로 하차 입력으로 처리되는 거 방지
        if (!IsSeated || Time.frameCount <= enteredFrame)
            return;

        if (Input.GetKeyDown(exitKey) && currentSeat.TryExit(this))
            return;

        float throttle = 0f;
        float steering = 0f;
        bool ascend = false;
        bool descend = false;

        switch (currentSeat.SeatType)
        {
            case SubmarineSeatType.Solo:
                throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                steering = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                ascend = Input.GetKey(KeyCode.Space);
                descend = Input.GetKey(KeyCode.LeftControl);
                break;
            case SubmarineSeatType.WSS:
                throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                ascend = Input.GetKey(KeyCode.Space);
                break;
            case SubmarineSeatType.ADC:
                steering = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                descend = Input.GetKey(KeyCode.LeftControl);
                break;
        }

        currentSeat.SubmitInput(this, throttle, steering, ascend, descend);
    }

    // 지정된 조종석에 플레이어를 착석.
    public bool EnterSeat(CockpitSeat seat, Transform seatAnchor)
    {
        if (seat == null || seatAnchor == null || IsSeated || seat.Controller == null)
            return false;

        // 하차할 때 원래 계층과 크기로 돌아가기 위한 값을 저장
        currentSeat = seat;
        enteredFrame = Time.frameCount;
        originalParent = transform.parent;
        originalLocalScale = transform.localScale;

        // 플레이어가 별도로 움직이거나 충돌하지 않도록 행동과 Collider 잠금처리
        CacheAndDisableInputBehaviours();
        CacheAndDisableColliders();

        // Rigidbody도 원래 설정을 저장한 뒤 물리 영향을 받지 않게 변경
        originalIsKinematic = body.isKinematic;
        originalDetectCollisions = body.detectCollisions;
        originalUseGravity = body.useGravity;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.detectCollisions = false;
        body.useGravity = false;

        // 조종석의 자식으로 만들어 잠수함이 이동할 때 플레이어도 함께 이동되도록 처리
        transform.SetParent(seat.Controller.transform, true);
        transform.SetPositionAndRotation(seatAnchor.position, seatAnchor.rotation);

        // 앉기 애니메이션을 켜고 일반 하차 문구 고정 표시
        SetSittingAnimation(true);
        interactor?.SetInteractionLock(true, ExitPrompt);
        return true;
    }

    // 조종석에서 플레이어를 내리고 착석 전 상태를 복원
    public void ExitSeat(Transform exitAnchor, Vector3 inheritedVelocity)
    {
        if (!IsSeated)
            return;

        // 하차 실패 안내 메시지가 표시 중이었다면 먼저 종료
        if (promptRoutine != null)
        {
            StopCoroutine(promptRoutine);
            promptRoutine = null;
        }

        currentSeat = null;

        // 잠수함 계층에서 분리하고 착석 전 부모와 로컬 크기를 복원
        transform.SetParent(originalParent, true);
        transform.localScale = originalLocalScale;

        // 착석 위치와 하차 위치로 같은 Transform을 사용
        if (exitAnchor != null)
            transform.SetPositionAndRotation(exitAnchor.position, exitAnchor.rotation);

        // 원래 Rigidbody 설정 복원
        body.isKinematic = originalIsKinematic;
        body.detectCollisions = originalDetectCollisions;
        body.useGravity = originalUseGravity;
        body.angularVelocity = Vector3.zero;
        if (!body.isKinematic)
            body.linearVelocity = inheritedVelocity;

        RestoreColliders();
        SetSittingAnimation(false);
        RestoreInputBehaviours();
        interactor?.SetInteractionLock(false);
    }

    // 정상 하차 조건을 검사하지 않고 현재 조종석에 강제 하차를 요청한다.
    private void ForceExitFromSeat()
    {
        currentSeat?.ForceExit(this);
    }

    // 하차가 거부된 이유 등을 잠시 상호작용 UI에 표시
    public void ShowSeatMessage(string message)
    {
        if (!IsSeated || interactor == null)
            return;

        if (promptRoutine != null)
            StopCoroutine(promptRoutine);
        promptRoutine = StartCoroutine(RestoreExitPromptAfterDelay(message));
    }

    // 하차 공간 검사
    public bool TryGetCapsuleAt(
        Vector3 rootPosition,
        Quaternion rootRotation,
        out Vector3 pointA,
        out Vector3 pointB,
        out float radius)
    {
        if (playerCollider == null)
        {
            // CapsuleCollider 참조가 없을 때 사용할 안전한 기본 캡슐
            pointA = rootPosition + Vector3.up * 0.35f;
            pointB = rootPosition + Vector3.up * 0.85f;
            radius = 0.35f;
            return false;
        }

        // 부모의 크기까지 반영된 월드 스케일을 사용
        Vector3 scale = transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        Vector3 localAxis;
        float axisScale;
        float perpendicularScale;
        
        // CapsuleCollider가 X/Y/Z 중 어느 축을 높이 방향으로 사용하는지 처리
        switch (playerCollider.direction)
        {
            case 0:
                localAxis = Vector3.right;
                axisScale = scale.x;
                perpendicularScale = Mathf.Max(scale.y, scale.z);
                break;
            case 2:
                localAxis = Vector3.forward;
                axisScale = scale.z;
                perpendicularScale = Mathf.Max(scale.x, scale.y);
                break;
            default:
                localAxis = Vector3.up;
                axisScale = scale.y;
                perpendicularScale = Mathf.Max(scale.x, scale.z);
                break;
        }

        radius = playerCollider.radius * perpendicularScale;
        float height = Mathf.Max(playerCollider.height * axisScale, radius * 2f);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 center = rootPosition + rootRotation * Vector3.Scale(playerCollider.center, scale);
        Vector3 worldAxis = rootRotation * localAxis;
        pointA = center + worldAxis * halfSegment;
        pointB = center - worldAxis * halfSegment;
        return true;
    }

    // 착석 중 막아야 하는 행동 컴포넌트의 활성 상태를 저장하고 비활성화
    private void CacheAndDisableInputBehaviours()
    {
        behaviourStates.Clear();
        HashSet<MonoBehaviour> targets = new HashSet<MonoBehaviour>();

        // Inspector에서 직접 지정한 대상부터 수집한다.
        // if (disableWhileSeated != null)
        // {
        //     foreach (MonoBehaviour behaviour in disableWhileSeated)
        //         if (behaviour != null) targets.Add(behaviour);
        // }

        // 코드에서 알고 있는 기본 플레이어 행동 컴포넌트도 자동 수집
        foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
        {
            if (ShouldDisableWhileSeated(behaviour))
                targets.Add(behaviour);
        }

        foreach (MonoBehaviour behaviour in targets)
        {
            if (behaviour == null || behaviour == this || behaviour == interactor)
                continue;

            // 원래 비활성 상태였던 컴포넌트를 하차 시 잘못 켜지 않도록 상태 기록
            behaviourStates[behaviour] = behaviour.enabled;
            behaviour.enabled = false;
        }
    }

    // 착석 중 비활성화해야 하는 플레이어 행동인지 판별
    private static bool ShouldDisableWhileSeated(MonoBehaviour behaviour)
    {
        if (behaviour == null)
            return false;

        return behaviour is PlayerController
            || behaviour is PlayerGrabber
            || behaviour is PlayerHotbar
            || behaviour is MeleeAttack
            || behaviour is PlayerItemGiver
            || behaviour is PlayerReviver
            || behaviour.GetType().Name == "CaveSwimController";
    }

    private void RestoreInputBehaviours()
    {
        // 기절 또는 사망 상태라면 이동 컴포넌트를 다시 켜면 안됨
        bool isDowned = (downedState != null && downedState.IsDowned)
            || (health != null && health.IsDead);
        if (isDowned)
            return;

        foreach (KeyValuePair<MonoBehaviour, bool> state in behaviourStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }
        behaviourStates.Clear();
    }

    // 착석 중 플레이어가 잠수함 내부와 충돌하지 않도록 Collider 상태를 저장하고 끔
    private void CacheAndDisableColliders()
    {
        colliderStates.Clear();
        foreach (Collider col in GetComponents<Collider>())
        {
            colliderStates[col] = col.enabled;
            col.enabled = false;
        }
    }

    // 하차할 때 각 Collider를 착석 전 활성 상태로 되돌림
    private void RestoreColliders()
    {
        foreach (KeyValuePair<Collider, bool> state in colliderStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }
        colliderStates.Clear();
    }

    // 착석 여부를 Animator에 전달하고 이동/밀기 관련 파라미터를 초기화
    private void SetSittingAnimation(bool sitting)
    {
        if (animator == null)
            return;

        animator.SetBool(IsSittingHash, sitting);
        animator.SetBool(IsMovingHash, false);
        animator.SetBool(IsPushPullHash, false);
        animator.SetFloat(SpeedHash, 0f);
    }

    // 상태 메시지를 일정 시간 보여준 다음 기본 하차 문구로 되돌리기
    private IEnumerator RestoreExitPromptAfterDelay(string message)
    {
        interactor.SetInteractionLock(true, message);
        yield return new WaitForSeconds(statusMessageDuration);

        if (IsSeated)
            interactor.SetInteractionLock(true, ExitPrompt);
        promptRoutine = null;
    }

    // 부활했고 조종석에 앉아 있지 않다면 저장해 둔 행동 상태를 복원
    private void HandleRevived()
    {
        if (!IsSeated)
            RestoreInputBehaviours();
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            ForceExitFromSeat();
    }

    private void OnDestroy()
    {
        // 플레이어가 파괴될 때 Health 이벤트 구독 해제
        if (health != null)
        {
            health.OnDeath.RemoveListener(ForceExitFromSeat);
            health.OnRevived.RemoveListener(HandleRevived);
        }
    }
}
