using System.Collections.Generic;
using UnityEngine;

// TODO : 적 많아지면 Controller 책임 좀 분리해주기
[RequireComponent(typeof(Health))]
public class SharkController : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private EnemyData enemyData;

    [Header("Detection")]
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Attack")]
    [SerializeField] private EnemyAttackHitbox attackHitbox;
    
    [Header("Animation")]
    [SerializeField] private Animator animator;
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int DieHash = Animator.StringToHash("Die");
    
    private Transform target;

    // 체력 관리(저장/계산/사망 판정)는 공용 Health 컴포넌트로 관리
    private Health health;

    private Vector3 spawnPosition;
    
    private Dictionary<SharkStateType, ISharkState> states;
    private ISharkState currentState;
    private SharkStateType currentStateType;

    public EnemyData Data => enemyData;
    public Transform Target => target;
    public EnemyAttackHitbox AttackHitbox => attackHitbox;

    public Vector3 SpawnPosition => spawnPosition;

    #region SO 데이터 갖고오기
    public float MoveSpeed => enemyData.moveSpeed;
    public float RotateSpeed => enemyData.rotateSpeed;
    public float ProximityDetectRadius => enemyData.ProximityDetectRadius;
    public float ForwardDetectRadius => enemyData.ForwardDetectRadius;
    public float LoseTargetRadius => enemyData.loseTargetRadius;
    public float ViewAngle => enemyData.viewAngle;
    public float AttackRange => enemyData.attackRange;
    public float AttackCooldown => enemyData.attackCooldown;
    public int AttackDamage => enemyData.attackDamage;

    public float PatrolRadius => enemyData.patrolRadius;
    public float PatrolArriveDistance => enemyData.patrolArriveDistance;
    public float IdleWaitMin => enemyData.idleWaitMin;
    public float IdleWaitMax => enemyData.idleWaitMax;
    #endregion
    
    public bool IsDead => health != null && health.IsDead;

    void Awake()
    {
        health = GetComponent<Health>();

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

    void OnEnable()
    {
        // Health 이벤트 받아오기
        health.OnHealthChanged.AddListener(HandleHealthChanged);
        health.OnDeath.AddListener(HandleDeath);
    }

    void OnDisable()
    {
        health.OnHealthChanged.RemoveListener(HandleHealthChanged);
        health.OnDeath.RemoveListener(HandleDeath);
    }

    void Start()
    {
        ChangeState(SharkStateType.Idle);
    }

    void Update()
    {
        currentState?.Update();
    }

    // 플레이어에게 공격 받았을 때(사망이 아닌 경우) 피격 상태로 전환
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        if (health.IsDead)
            return;

        ChangeState(SharkStateType.Hit);
    }

    // 체력이 0이 되었을 때 사망 상태로 전환
    private void HandleDeath()
    {
        ChangeState(SharkStateType.Dead);
    }

    // 상태 전환용
    public void ChangeState(SharkStateType newStateType)
    {
        if (currentState != null && currentStateType == newStateType)
            return;
        
        currentState?.Exit();
        
        currentStateType = newStateType;
        currentState = states[newStateType];
        
        currentState.Enter();
    }

    // TODO : 장애물 피하는 기능 구현
    // 이동 속도는 상태마다 다를 수 있어(순찰/추격) 호출하는 쪽에서 지정해줌
    public void MoveToDirection(Vector3 direction, float speed)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        transform.position += direction.normalized * speed * Time.deltaTime;
    }
    
    public void RotateToDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            RotateSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// 플레이어 탐지 메서드
    /// 상어의 시야각 안에 있고 장애물에 가려지지 않은 가장 가까운 플레이어 찾기
    /// </summary>
    public bool TryFindTarget()
    {
        // 이미 발견한 타깃은 시야각을 벗어나도 계속 추적
        // 추격 해제 거리 밖으로 나가거나 장애물에 가려졌을 때만 놓친다.
        if (target != null)
        {
            if (CanContinueTracking(target))
                return true;

            target = null;
        }

        float searchRadius = Mathf.Max(ForwardDetectRadius, ProximityDetectRadius);
        Collider[] targetsInRadius = Physics.OverlapSphere(transform.position, searchRadius, targetLayer);

        Transform nearestTarget = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider targetCollider in targetsInRadius)
        {
            Transform candidate = targetCollider.transform;
            
            Vector3 offsetToTarget = candidate.position - transform.position;
            float distanceToTarget = offsetToTarget.magnitude;

            if (distanceToTarget <= Mathf.Epsilon)
                continue;

            Vector3 directionToTarget = offsetToTarget / distanceToTarget;
            bool isWithinProximity = distanceToTarget <= ProximityDetectRadius;
            bool isWithinForward =
                distanceToTarget <= ForwardDetectRadius &&
                Vector3.Angle(transform.forward, directionToTarget) <= ViewAngle * 0.5f;

            // 가까우면 360도로 감지하고
            // 멀리 있으면 전방 시야각 안에서만 감지
            if (!isWithinProximity && !isWithinForward)
                continue;

            if (IsTargetBlocked(directionToTarget, distanceToTarget))
                continue;

            if (distanceToTarget < nearestDistance)
            {
                nearestDistance = distanceToTarget;
                nearestTarget = candidate;
            }
        }
        
        target = nearestTarget;

        return target != null;
    }

    private bool CanContinueTracking(Transform trackedTarget)
    {
        Vector3 offsetToTarget = trackedTarget.position - transform.position;
        float distanceToTarget = offsetToTarget.magnitude;

        if (distanceToTarget > LoseTargetRadius)
            return false;

        if (distanceToTarget <= Mathf.Epsilon)
            return true;

        Vector3 directionToTarget = offsetToTarget / distanceToTarget;
        return !IsTargetBlocked(directionToTarget, distanceToTarget);
    }

    private bool IsTargetBlocked(Vector3 directionToTarget, float distanceToTarget)
    {
        return Physics.Raycast(
            transform.position,
            directionToTarget,
            distanceToTarget,
            obstacleLayer
        );
    }
    
    // 애니메이션
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
    
    // 히트박스 제어
    public void BeginAttackHitbox()
    {
        // 공격 상태가 끝난 뒤 늦게 도착한 이벤트 방지
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

    // 상어 시야 범위/시야각을 시각화
    private void OnDrawGizmosSelected()
    {
        if (enemyData == null)
            return;

        float radius = ForwardDetectRadius;
        float halfAngle = enemyData.viewAngle * 0.5f; // TryFindTarget과 동일하게 좌우 절반씩

        // 시야 반경 (탐지 거리)
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, radius);

        // 방향과 관계없이 감지하는 근거리 범위
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, ProximityDetectRadius);

        // 이미 발견한 타깃을 놓치는 거리
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, LoseTargetRadius);

        // 시야각 경계선: 정면 기준 좌우로 halfAngle만큼 회전한 두 방향
        Vector3 leftBoundary = Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0f, halfAngle, 0f) * transform.forward;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + leftBoundary * radius);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary * radius);

        // 두 경계선 사이를 호(arc)로 이어 부채꼴 형태로 표시
        const int segments = 20;
        Vector3 previousPoint = transform.position + leftBoundary * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -halfAngle + (enemyData.viewAngle * i / segments);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Vector3 currentPoint = transform.position + direction * radius;

            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }
}
