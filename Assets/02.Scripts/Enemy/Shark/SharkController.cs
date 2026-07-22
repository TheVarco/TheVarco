using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Health))]
[RequireComponent(typeof(SharkTargeting))]
[RequireComponent(typeof(SharkNavigator))]
public class SharkController : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData;

    [Header("Attack")]
    [SerializeField] private EnemyAttackHitbox attackHitbox;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private Health health;
    private Vector3 spawnPosition;

    private Dictionary<SharkStateType, ISharkState> states;
    private ISharkState currentState;
    private SharkStateType currentStateType;

    public EnemyData Data => enemyData;
    public EnemyAttackHitbox AttackHitbox => attackHitbox;
    public SharkTargeting Targeting { get; private set; }
    public SharkNavigator Navigator { get; private set; }
    public Vector3 SpawnPosition => spawnPosition;

    #region SO 데이터 가져오기
    public float MoveSpeed => enemyData.moveSpeed;
    public float AttackRange => enemyData.attackRange;
    public float AttackCooldown => enemyData.attackCooldown;
    public int AttackDamage => enemyData.attackDamage;

    public float PatrolRadius => enemyData.patrolRadius;
    public float PatrolArriveDistance => enemyData.patrolArriveDistance;
    public float PatrolStuckTime => enemyData.patrolStuckTime;
    public float IdleWaitMin => enemyData.idleWaitMin;
    public float IdleWaitMax => enemyData.idleWaitMax;
    #endregion

    public bool IsDead => health != null && health.IsDead;

    private void Awake()
    {
        health = GetComponent<Health>();
        Targeting = GetComponent<SharkTargeting>();
        Navigator = GetComponent<SharkNavigator>();

        spawnPosition = transform.position;

        states = new Dictionary<SharkStateType, ISharkState>
        {
            { SharkStateType.Idle, new SharkIdleState(this) },
            { SharkStateType.Patrol, new SharkPatrolState(this) },
            { SharkStateType.Chase, new SharkChaseState(this) },
            { SharkStateType.Attack, new SharkAttackState(this) },
            { SharkStateType.Hit, new SharkHitState(this) },
            { SharkStateType.Dead, new SharkDeadState(this) }
        };
    }

    private void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
        health.OnDeath.AddListener(HandleDeath);
    }

    private void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
        health.OnDeath.RemoveListener(HandleDeath);
    }

    private void Start()
    {
        ChangeState(SharkStateType.Idle);
    }

    private void Update()
    {
        currentState?.Update();
    }

    private void HandleDamaged(float amount, GameObject source)
    {
        if (IsDead || !Targeting.TrySetDamageTarget(source))
            return;

        ChangeState(SharkStateType.Hit);
    }

    private void HandleDeath()
    {
        ChangeState(SharkStateType.Dead);
    }

    public void ChangeState(SharkStateType newStateType)
    {
        if (currentState != null && currentStateType == newStateType)
            return;

        currentState?.Exit();

        currentStateType = newStateType;
        currentState = states[newStateType];

        currentState.Enter();
    }

    public void PlayAttackAnimation()
    {
        animator.SetTrigger(AttackHash);
    }

    public void PlayIdleAnimation()
    {
        animator.CrossFade(IdleStateHash, 0.1f);
    }

    public void PlayDieAnimation()
    {
        animator.SetTrigger(DieHash);
    }

    public void BeginAttackHitbox()
    {
        if (IsDead || currentStateType != SharkStateType.Attack)
            return;

        if (attackHitbox == null)
            return;

        attackHitbox.BeginBite(AttackDamage, gameObject);
    }

    public void EndAttackHitbox()
    {
        if (attackHitbox == null)
            return;

        attackHitbox.EndBite();
    }

    private void OnDrawGizmosSelected()
    {
        if (enemyData == null)
            return;

        if (attackHitbox != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireSphere(attackHitbox.transform.position, AttackRange);
        }

        DrawPatrolGizmos();
    }

    /// <summary>
    /// 정찰 중일 때 현재 목적지와 SphereCast 프로브 반경(상어 몸통 굵기)을 그린다.
    /// 목적지는 런타임에만 정해지므로 플레이 모드에서만 표시된다.
    /// </summary>
    private void DrawPatrolGizmos()
    {
        if (!Application.isPlaying || states == null)
            return;

        if (currentStateType != SharkStateType.Patrol)
            return;

        if (!states.TryGetValue(SharkStateType.Patrol, out ISharkState patrolState)
            || patrolState is not SharkPatrolState patrol)
            return;

        Vector3 point = patrol.PatrolPoint;
        float probeRadius = enemyData.patrolProbeRadius;

        // 상어 → 목적지 경로
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, point);

        // 목적지 지점 표시
        Gizmos.DrawWireSphere(point, 0.3f);

        // SphereCast에 쓰인 프로브 반경(상어 몸통 굵기)을 출발/도착 양 끝에 표시
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, probeRadius);
        Gizmos.DrawWireSphere(point, probeRadius);
    }
}
