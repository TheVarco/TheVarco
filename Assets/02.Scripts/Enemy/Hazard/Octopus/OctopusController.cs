using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 문어의 상태 전환 부착 피격 반응 관리
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(HarvestableCreature))]
[RequireComponent(typeof(EnemyTargeting))]
[RequireComponent(typeof(EnemyNavigator))]
public class OctopusController : MonoBehaviour, IEnemyTargetFilter
{
    [SerializeField] private EnemyData enemyData;                       // 공통 적 설정값
    [Min(0f)] [SerializeField] private float attachDistance = 0.75f;   // 얼굴 부착 거리
    [Min(0f)] [SerializeField] private float chaseSpeedBonus = 3f;     // 추격 추가 속도

    [SerializeField] private Animator animator;                         // 문어와 오징어 Animator

    [Header("Local Vision Blocker")]
    [SerializeField] private Sprite visionBlockerSprite;
    [SerializeField] private Vector2 visionBlockerSize = new Vector2(1100f, 1100f);
    [Range(0f, 1f)] [SerializeField] private float visionDarkness = 0.45f;

    private static readonly int IsAttachedHash = Animator.StringToHash("IsAttached");
    private static readonly int StickHash = Animator.StringToHash("Stick");
    private static readonly int StickStateHash = Animator.StringToHash("stick");
    private static readonly int Take001StateHash = Animator.StringToHash("Take 001");

    private Health health;                                      // 문어 체력
    private HarvestableCreature harvestable;                    // 공통 부착 생물 상태
    private Dictionary<OctopusStateType, IOctopusState> states; // 상태별 실행 객체
    private IOctopusState currentState;                          // 현재 실행 상태
    private OctopusStateType currentStateType;                   // 현재 상태 종류
    private NetworkObject networkObject; // 권위 확인 대상
    private EnemyHealthNetworkSync networkSync; // AI 상태 게시 대상
    private OctopusVisionBlocker localVisionBlocker;

    public EnemyTargeting Targeting { get; private set; }
    public EnemyNavigator Navigator { get; private set; }
    public OctopusStateType CurrentState => currentStateType;

    public float MoveSpeed => enemyData != null ? enemyData.moveSpeed : 0f;
    public float AttachDistance => attachDistance;
    public float ChaseSpeedBonus => chaseSpeedBonus;
    public float PatrolRadius => enemyData != null ? enemyData.patrolRadius : 0f;
    public float PatrolArriveDistance => enemyData != null ? enemyData.patrolArriveDistance : 0.5f;
    public float PatrolStuckTime => enemyData != null ? enemyData.patrolStuckTime : 2f;
    public float IdleWaitMin => enemyData != null ? enemyData.idleWaitMin : 1f;
    public float IdleWaitMax => enemyData != null ? enemyData.idleWaitMax : 3f;

    private void Awake()
    {
        health = GetComponent<Health>();
        harvestable = GetComponent<HarvestableCreature>();
        Targeting = GetComponent<EnemyTargeting>();
        Navigator = GetComponent<EnemyNavigator>();
        networkObject = GetComponent<NetworkObject>(); // 같은 문어의 네트워크 오브젝트
        networkSync = GetComponent<EnemyHealthNetworkSync>(); // 같은 문어의 동기화 컴포넌트

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        states = new Dictionary<OctopusStateType, IOctopusState>
        {
            { OctopusStateType.Idle, new OctopusIdleState(this) },
            { OctopusStateType.Patrol, new OctopusPatrolState(this) },
            { OctopusStateType.Chase, new OctopusChaseState(this) },
            { OctopusStateType.Attached, new PassiveState() },
            { OctopusStateType.Dead, new PassiveState() }
        };
    }

    private void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
        health.OnDeath.AddListener(HandleDeath);
        if (harvestable != null)
        {
            harvestable.OnAttached += HandleAttached;
            harvestable.OnDetached += HandleDetached;
        }
    }

    private void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
        health.OnDeath.RemoveListener(HandleDeath);
        if (harvestable != null)
        {
            harvestable.OnAttached -= HandleAttached;
            harvestable.OnDetached -= HandleDetached;
        }
        HideLocalVisionBlocker();
        Navigator?.StopMovement();
    }

    private void Start()
    {
        TryStartSimulation();

        // Start 이전에 이미 부착 상태가 복제/복원된 경우도 처리한다.
        if (harvestable != null && harvestable.IsAttached)
            ShowLocalVisionBlocker(harvestable.AttachedSlot);
    }

    // Fusion이 State Authority를 부여한 뒤에만 AI를 시작한다.
    // NetworkObject가 없는 로컬 전용 문어는 기존처럼 즉시 시작한다.
    internal void TryStartSimulation()
    {
        if (!HasSimulationAuthority || currentState != null)
            return;

        bool isAttached = harvestable != null && harvestable.Phase == HarvestableCreature.CreaturePhase.Attached;
        UpdateAnimation(isAttached);

        ChangeState(
            harvestable != null && harvestable.Phase == HarvestableCreature.CreaturePhase.Hazard
                ? OctopusStateType.Idle
                : OctopusStateType.Dead);
    }

    private void FixedUpdate()
    {
        // AI 갱신은 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        currentState?.Update();
    }

    /// <summary>
    /// 현재 상태 종료 및 새 상태 전환
    /// </summary>
    public void ChangeState(OctopusStateType newStateType)
    {
        if (currentState != null && currentStateType == newStateType)
            return;

        Navigator.StopMovement();
        currentState?.Exit();
        currentStateType = newStateType;
        currentState = states[newStateType];
        currentState.Enter();

        UpdateAnimation(newStateType == OctopusStateType.Attached);
        // 확정된 AI 상태 게시
        networkSync?.PublishAiState((int)newStateType);
    }

    // 프록시 상태와 연출 반영
    public void ApplyReplicatedState(OctopusStateType replicatedState)
    {
        // 권위자와 잘못된 상태 제외
        if (HasSimulationAuthority || !states.TryGetValue(replicatedState, out IOctopusState state))
            return;

        // 프록시 이동 정지와 상태 교체
        Navigator?.StopMovement();
        currentStateType = replicatedState;
        currentState = state;
        UpdateAnimation(replicatedState == OctopusStateType.Attached);
    }

    private void UpdateAnimation(bool isAttached)
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null) return;

        animator.SetBool(IsAttachedHash, isAttached);
        animator.SetBool(StickHash, isAttached);

        int targetStateHash = isAttached ? StickStateHash : Take001StateHash;
        if (animator.HasState(0, targetStateHash))
        {
            animator.CrossFade(targetStateHash, 0.1f);
        }
    }

    /// <summary>
    /// 현재 추적 대상의 얼굴 슬롯에 문어 부착
    /// </summary>
    public bool TryAttachToCurrentTarget()
    {
        // 부착 판정은 권위자만 실행
        if (!HasSimulationAuthority)
            return false;

        Transform target = Targeting.Target;
        if (target == null)
            return false;

        AttachmentSlot slot = target.GetComponentInParent<AttachmentSlot>();
        if (slot == null || !harvestable.TryAttach(slot))
            return false;

        Targeting.ClearTarget();
        ChangeState(OctopusStateType.Attached);
        return true;
    }

    private void HandleDamaged(float amount, GameObject source)
    {
        // 피격 AI 전환은 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        if (health.IsDead || harvestable.Phase != HarvestableCreature.CreaturePhase.Hazard)
            return;

        if (Targeting.TrySetDamageTarget(source))
            ChangeState(OctopusStateType.Chase);
    }

    private void HandleDeath()
    {
        HideLocalVisionBlocker();
        Targeting.ClearTarget();
        ChangeState(OctopusStateType.Dead);
    }

    private void HandleAttached(AttachmentSlot slot)
    {
        UpdateAnimation(true);
        ShowLocalVisionBlocker(slot);
    }

    private void HandleDetached(AttachmentSlot previousSlot)
    {
        HideLocalVisionBlocker();
        UpdateAnimation(false);
        Targeting.ClearTarget();
        ChangeState(OctopusStateType.Dead);
    }

    private void ShowLocalVisionBlocker(AttachmentSlot slot)
    {
        if (localVisionBlocker != null || slot == null || !IsLocalPlayerSlot(slot))
            return;

        localVisionBlocker = OctopusVisionBlocker.Create(
            visionBlockerSprite,
            visionBlockerSize,
            visionDarkness);
    }

    private static bool IsLocalPlayerSlot(AttachmentSlot slot)
    {
        NetworkObject playerObject = slot.GetComponentInParent<NetworkObject>();
        if (playerObject != null && playerObject.IsValid)
            return playerObject.HasInputAuthority;

        // Fusion이 없는 오프라인 Play Mode용 폴백.
        PlayerCameraRig cameraRig = FindFirstObjectByType<PlayerCameraRig>();
        return cameraRig != null
            && cameraRig.target != null
            && cameraRig.target.root == slot.transform.root;
    }

    private void HideLocalVisionBlocker()
    {
        if (localVisionBlocker == null)
            return;

        Destroy(localVisionBlocker.gameObject);
        localVisionBlocker = null;
    }

    // 체크포인트 복원 후 타깃 제거
    // 생존 위험 단계면 대기 상태에서 재시작
    public void RestoreCheckpointAI()
    {
        UpdateAnimation(false);
        Targeting?.ClearTarget();
        ChangeState(
            !health.IsDead && harvestable.Phase == HarvestableCreature.CreaturePhase.Hazard
                ? OctopusStateType.Idle
                : OctopusStateType.Dead);
    }

    /// <summary>
    /// 생존 및 빈 얼굴 슬롯 기준 타깃 허용
    /// </summary>
    public bool CanTarget(Transform candidate)
    {
        AttachmentSlot slot = candidate != null
            ? candidate.GetComponentInParent<AttachmentSlot>()
            : null;

        if (slot == null || !slot.IsAvailable(AttachmentSlotType.Face))
            return false;

        Health targetHealth = slot.GetComponent<Health>();
        return targetHealth == null || !targetHealth.IsDead;
    }

    private sealed class PassiveState : IOctopusState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    // 로컬 실행 또는 State Authority 여부
    private bool HasSimulationAuthority =>
        networkObject == null || (networkObject.IsValid && networkObject.HasStateAuthority);
}

/// <summary>
/// 로컬 플레이어에게만 문어 시야 방해 UI를 표시한다.
/// 실제 문어는 다른 플레이어에게 보이도록 얼굴 부착 상태를 그대로 유지한다.
/// </summary>
internal sealed class OctopusVisionBlocker : MonoBehaviour
{
    private RectTransform octopusGraphic;
    private Vector3 baseScale;

    internal static OctopusVisionBlocker Create(Sprite sprite, Vector2 graphicSize, float darkness)
    {
        if (sprite == null)
            return null;

        GameObject root = new GameObject(
            "Octopus Vision Blocker",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(OctopusVisionBlocker));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        CreateDarkness(root.transform, Mathf.Clamp01(darkness));

        GameObject graphicObject = new GameObject(
            "Octopus Graphic",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        graphicObject.transform.SetParent(root.transform, false);

        RectTransform graphic = graphicObject.GetComponent<RectTransform>();
        graphic.anchorMin = new Vector2(0.5f, 0.5f);
        graphic.anchorMax = new Vector2(0.5f, 0.5f);
        graphic.pivot = new Vector2(0.5f, 0.5f);
        graphic.sizeDelta = graphicSize;

        Image image = graphicObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        OctopusVisionBlocker blocker = root.GetComponent<OctopusVisionBlocker>();
        blocker.octopusGraphic = graphic;
        blocker.baseScale = graphic.localScale;
        return blocker;
    }

    private static void CreateDarkness(Transform parent, float darkness)
    {
        if (darkness <= 0f)
            return;

        GameObject darknessObject = new GameObject(
            "Vision Darkness",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        darknessObject.transform.SetParent(parent, false);

        RectTransform rect = darknessObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = darknessObject.GetComponent<Image>();
        image.color = new Color(0.01f, 0.025f, 0.03f, darkness);
        image.raycastTarget = false;
    }

    private void Update()
    {
        if (octopusGraphic == null)
            return;

        // 정적인 아이콘처럼 보이지 않도록 호흡과 흔들림을 약하게 더한다.
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.4f) * 0.035f;
        float sway = Mathf.Sin(Time.unscaledTime * 1.35f) * 2.5f;
        octopusGraphic.localScale = baseScale * pulse;
        octopusGraphic.localRotation = Quaternion.Euler(0f, 0f, sway);
    }
}
