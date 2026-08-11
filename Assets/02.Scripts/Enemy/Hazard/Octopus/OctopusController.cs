using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 문어의 상태 전환, 부착, 피격 반응 관리
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

    [SerializeField] private Animator animator;                         // 문어/오징어 Animator
    private static readonly int IsAttachedHash = Animator.StringToHash("IsAttached");
    private static readonly int StickHash = Animator.StringToHash("Stick");
    private static readonly int StickStateHash = Animator.StringToHash("stick");
    private static readonly int Take001StateHash = Animator.StringToHash("Take 001");

    private Health health;                                      // 문어 체력
    private HarvestableCreature harvestable;                    // 공통 부착 생물 상태
    private Dictionary<OctopusStateType, IOctopusState> states; // 상태별 실행 객체
    private IOctopusState currentState;                          // 현재 실행 상태
    private OctopusStateType currentStateType;                   // 현재 상태 종류

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
        Navigator?.StopMovement();
    }

    private void Start()
    {
        bool isAttached = harvestable != null && harvestable.Phase == HarvestableCreature.CreaturePhase.Attached;
        UpdateAnimation(isAttached);

        ChangeState(
            harvestable != null && harvestable.Phase == HarvestableCreature.CreaturePhase.Hazard
                ? OctopusStateType.Idle
                : OctopusStateType.Dead);
    }

    private void FixedUpdate()
    {
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
        if (health.IsDead || harvestable.Phase != HarvestableCreature.CreaturePhase.Hazard)
            return;

        if (Targeting.TrySetDamageTarget(source))
            ChangeState(OctopusStateType.Chase);
    }

    private void HandleDeath()
    {
        Targeting.ClearTarget();
        ChangeState(OctopusStateType.Dead);
    }

    private void HandleAttached(AttachmentSlot slot)
    {
        UpdateAnimation(true);
    }

    private void HandleDetached(AttachmentSlot previousSlot)
    {
        UpdateAnimation(false);
        Targeting.ClearTarget();
        ChangeState(OctopusStateType.Dead);
    }

    // 체크포인트 복원 후 타깃 제거
    // 생존 위험 단계면 Idle 상태에서 재시작
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
}
