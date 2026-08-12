using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 상어의 상태 전환 공격 판정 애니메이션 관리
/// 공통 EnemyTargeting 및 EnemyNavigator 기준
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(EnemyTargeting))]
[RequireComponent(typeof(EnemyNavigator))]
public class SharkController : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData; // 상어 공통 설정값

    [Header("Attack")]
    [SerializeField] private EnemyAttackHitbox attackHitbox; // 물기 공격 판정 영역

    [Header("Animation")]
    [SerializeField] private Animator animator; // 상어 Animator
    private static readonly int AttackHash = Animator.StringToHash("Attack"); // 공격 파라미터
    private static readonly int IdleStateHash = Animator.StringToHash("Idle"); // 대기 상태
    private static readonly int DieHash = Animator.StringToHash("Die"); // 사망 파라미터

    private Health health;            // 상어 체력
    private Vector3 spawnPosition;    // 최초 생성 위치
    private Quaternion spawnRotation; // 최초 생성 회전

    private Dictionary<SharkStateType, ISharkState> states; // 상태별 실행 객체
    private ISharkState currentState;                        // 현재 실행 상태
    private SharkStateType currentStateType;                 // 현재 상태 종류
    private Coroutine delayedDestroyRoutine;
    private SharkDetectionIndicator detectionIndicator;
    private bool isSuspicious;
    private NetworkObject networkObject; // 권위 확인 대상
    private NetworkTransform networkTransform; // 순간이동을 복제할 위치 동기화 대상
    private EnemyHealthNetworkSync networkSync; // 상태와 공격 게시 대상

    public EnemyData Data => enemyData;
    public EnemyAttackHitbox AttackHitbox => attackHitbox;
    public EnemyTargeting Targeting { get; private set; }
    public EnemyNavigator Navigator { get; private set; }
    public Vector3 SpawnPosition => spawnPosition;
    public SharkStateType CurrentState => currentStateType; // 권위 AI 상태 조회

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
    public bool IsSuspicious => isSuspicious;

    private void Awake()
    {
        health = GetComponent<Health>();
        Targeting = GetComponent<EnemyTargeting>();
        Navigator = GetComponent<EnemyNavigator>();
        networkObject = GetComponent<NetworkObject>(); // 같은 상어의 네트워크 오브젝트
        networkTransform = GetComponent<NetworkTransform>(); // 같은 상어의 위치 동기화 컴포넌트
        networkSync = GetComponent<EnemyHealthNetworkSync>(); // 같은 상어의 동기화 컴포넌트

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        detectionIndicator = GetComponent<SharkDetectionIndicator>();

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
        TryStartSimulation();
    }

    // Fusion이 State Authority를 부여한 뒤에만 AI를 시작한다.
    // NetworkObject가 없는 로컬 전용 상어는 기존처럼 즉시 시작한다.
    internal void TryStartSimulation()
    {
        if (!HasSimulationAuthority || currentState != null || IsDead)
            return;

        ChangeState(SharkStateType.Idle);
    }

    private void FixedUpdate()
    {
        // AI 갱신은 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        currentState?.Update();
    }

    private void HandleDamaged(float amount, GameObject source)
    {
        // 피격 AI 전환은 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        if (IsDead || !Targeting.TrySetDamageTarget(source))
            return;

        ChangeState(SharkStateType.Hit);
    }

    private void HandleDeath()
    {
        if (!HasSimulationAuthority)
        {
            // 프록시는 사망 연출만 적용
            EndAttackHitbox();
            PlayDieAnimation();
            return;
        }

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
        Navigator?.StopMovement();
        EndAttackHitbox();
        if (!IsDead)
            ChangeState(SharkStateType.Idle);
    }

    // 최초 이동 전 상어 자세 복원
    // 네트워크 Proxy의 자세 쓰기 차단
    // State Authority의 Teleport로 원격 보간 방지
    public void RestoreInitialCheckpointPose()
    {
        if (networkObject != null && networkObject.IsValid)
        {
            if (!networkObject.HasStateAuthority)
                return;

            if (networkTransform != null
                && networkTransform.isActiveAndEnabled
                && networkObject.IsInSimulation)
            {
                networkTransform.Teleport(spawnPosition, spawnRotation);
                return;
            }
        }

        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
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

        if (networkObject != null && networkObject.IsValid)
        {
            // 네트워크 상어는 권위자가 제거
            if (networkObject.HasStateAuthority)
                networkObject.Runner.Despawn(networkObject);
            yield break;
        }

        // 로컬 상어는 기존 방식으로 제거
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
    /// 현재 상태 종료 및 새 상태 전환
    /// </summary>
    public void ChangeState(SharkStateType newStateType)
    {
        if (currentState != null && currentStateType == newStateType)
            return;

        // 상태 전환 시 잔여 속도 제거
        Navigator.StopMovement();
        currentState?.Exit();

        currentStateType = newStateType;
        currentState = states[newStateType];

        currentState.Enter();
        // 확정된 AI 상태 게시
        networkSync?.PublishAiState((int)newStateType);
    }

    // 프록시 상태와 연출 반영
    public void ApplyReplicatedState(SharkStateType replicatedState)
    {
        // 권위자와 잘못된 상태 제외
        if (HasSimulationAuthority || !states.TryGetValue(replicatedState, out ISharkState state))
            return;

        // 프록시 이동 정지와 상태 교체
        Navigator?.StopMovement();
        currentStateType = replicatedState;
        currentState = state;

        if (replicatedState == SharkStateType.Idle)
            PlayIdleAnimation();
        else if (replicatedState == SharkStateType.Dead)
        {
            EndAttackHitbox();
            PlayDieAnimation();
        }
    }

    public void PlayAttackAnimation()
    {
        animator.SetTrigger(AttackHash);
        // 공격 연출 번호 게시
        networkSync?.PublishSharkAttack();
    }

    // 프록시 공격 애니메이션 재생
    public void PlayReplicatedAttackAnimation()
    {
        if (animator != null)
            animator.SetTrigger(AttackHash);
    }

    // Applies the local question indicator and publishes it for proxies.
    public void SetSuspicious(bool suspicious)
    {
        if (isSuspicious == suspicious)
            return;

        isSuspicious = suspicious;
        detectionIndicator?.SetQuestionVisible(suspicious);
        networkSync?.PublishSharkSuspicion(suspicious);
    }

    public void ApplyReplicatedSuspicion(bool suspicious)
    {
        isSuspicious = suspicious;
        detectionIndicator?.SetQuestionVisible(suspicious);
    }

    public void PlayReplicatedDetectionIndicator()
    {
        detectionIndicator?.ShowReplicatedDetection();
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
    /// 물기 공격 판정 시작
    /// </summary>
    public void BeginAttackHitbox()
    {
        // 공격 판정은 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        if (IsDead || currentStateType != SharkStateType.Attack)
            return;

        if (attackHitbox == null)
            return;

        attackHitbox.BeginBite(AttackDamage, gameObject);
    }

    /// <summary>
    /// 물기 공격 판정 종료
    /// </summary>
    public void EndAttackHitbox()
    {
        if (attackHitbox == null)
            return;

        attackHitbox.EndBite();
    }

    // 로컬 실행 또는 State Authority 여부
    private bool HasSimulationAuthority =>
        networkObject == null || (networkObject.IsValid && networkObject.HasStateAuthority);

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
    /// 순찰 목적지 및 장애물 탐색 반경 표시
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

        // 순찰 경로 표시
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, point);

        // 순찰 목적지 표시
        Gizmos.DrawWireSphere(point, 0.3f);

        // 장애물 탐색 반경 표시
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, probeRadius);
        Gizmos.DrawWireSphere(point, probeRadius);
    }
}
