using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상어의 상태 전환, 공격 판정, 애니메이션 관리.
/// 공통 EnemyTargeting 및 EnemyNavigator 기준.
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(EnemyTargeting))]
[RequireComponent(typeof(EnemyNavigator))]
public class SharkController : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData; // 상어 공통 설정값.

    [Header("Attack")]
    [SerializeField] private EnemyAttackHitbox attackHitbox; // 물기 공격 판정 영역.

    [Header("Animation")]
    [SerializeField] private Animator animator; // 상어 Animator.
    private static readonly int AttackHash = Animator.StringToHash("Attack"); // 공격 파라미터.
    private static readonly int IdleStateHash = Animator.StringToHash("Idle"); // 대기 상태.
    private static readonly int DieHash = Animator.StringToHash("Die"); // 사망 파라미터.

    private Health health;            // 상어 체력.
    private Vector3 spawnPosition;    // 최초 생성 위치.

    private Dictionary<SharkStateType, ISharkState> states; // 상태별 실행 객체.
    private ISharkState currentState;                        // 현재 실행 상태.
    private SharkStateType currentStateType;                 // 현재 상태 종류.
    private Coroutine delayedDestroyRoutine;

    public EnemyData Data => enemyData;
    public EnemyAttackHitbox AttackHitbox => attackHitbox;
    public EnemyTargeting Targeting { get; private set; }
    public EnemyNavigator Navigator { get; private set; }
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
        Targeting = GetComponent<EnemyTargeting>();
        Navigator = GetComponent<EnemyNavigator>();

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
        Navigator?.StopMovement();
    }

    private void Start()
    {
        ChangeState(SharkStateType.Idle);
    }

    private void FixedUpdate()
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

    // 사망 연출 이후 제거 예약
    // 체크포인트 복원을 위해 취소 가능한 코루틴 사용
    public void ScheduleDestroyAfterDeath(float delay)
    {
        CancelScheduledDestroy();
        delayedDestroyRoutine = StartCoroutine(DestroyAfterDelay(delay));
    }

    // 저장하지 않는 타깃과 공격 상태 초기화
    // 생존 상태면 Idle 상태에서 AI 재시작
    public void RestoreCheckpointAI()
    {
        CancelScheduledDestroy();
        Targeting?.ClearTarget();
        EndAttackHitbox();
        if (!IsDead)
            ChangeState(SharkStateType.Idle);
    }

    /// <summary>
    /// 생존 체크포인트 복원을 위한 지연 제거 취소
    /// </summary>
    public void CancelScheduledDestroyForCheckpoint()
    {
        CancelScheduledDestroy();
    }

    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        delayedDestroyRoutine = null;
        Destroy(gameObject);
    }

    private void CancelScheduledDestroy()
    {
        if (delayedDestroyRoutine == null)
            return;

        StopCoroutine(delayedDestroyRoutine);
        delayedDestroyRoutine = null;
    }

    /// <summary>
    /// 현재 상태 종료 및 새 상태 전환.
    /// </summary>
    public void ChangeState(SharkStateType newStateType)
    {
        if (currentState != null && currentStateType == newStateType)
            return;

        // 상태 전환 시 잔여 속도 제거.
        Navigator.StopMovement();
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

    /// <summary>
    /// 물기 공격 판정 시작.
    /// </summary>
    public void BeginAttackHitbox()
    {
        if (IsDead || currentStateType != SharkStateType.Attack)
            return;

        if (attackHitbox == null)
            return;

        attackHitbox.BeginBite(AttackDamage, gameObject);
    }

    /// <summary>
    /// 물기 공격 판정 종료.
    /// </summary>
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
    /// 순찰 목적지 및 장애물 탐색 반경 표시.
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

        // 순찰 경로 표시.
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, point);

        // 순찰 목적지 표시.
        Gizmos.DrawWireSphere(point, 0.3f);

        // 장애물 탐색 반경 표시.
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, probeRadius);
        Gizmos.DrawWireSphere(point, probeRadius);
    }
}
