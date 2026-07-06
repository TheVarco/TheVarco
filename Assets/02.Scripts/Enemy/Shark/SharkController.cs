using System.Collections.Generic;
using UnityEngine;

public class SharkController : MonoBehaviour
{
    [SerializeField] private EnemyData enemyData;
    
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private LayerMask obstacleLayer;

    private Transform target;

    private int currentHp;
    
    private Dictionary<SharkStateType, ISharkState> states;
    private ISharkState currentState;
    private SharkStateType currentStateType;

    public EnemyData Data => enemyData;
    public Transform Target => target;
    
    public float ViewRadius => enemyData.viewRadius;
    public float ViewAngle => enemyData.viewAngle;
    public float AttackRange => enemyData.attackRange;
    public float RotateSpeed => enemyData.rotateSpeed;

    public bool IsDead => currentHp <= 0;

    void Awake()
    {
        currentHp = enemyData.maxHp;
        
        states = new Dictionary<SharkStateType, ISharkState>
        {
            { SharkStateType.Idle, new SharkIdleState(this) },
            { SharkStateType.Chase, new SharkChaseState(this) },
            { SharkStateType.Attack, new SharkAttackState(this) },
            { SharkStateType.Hit, new SharkHitState(this) },
            { SharkStateType.Dead, new SharkDeadState(this) }
        };
    }

    void Start()
    {
        ChangeState(SharkStateType.Idle);
    }

    void Update()
    {
        currentState?.Update();
    }

    public void ChangeState(SharkStateType newStateType)
    {
        if (currentStateType == newStateType)
            return;
        
        currentState?.Exit();
        
        currentStateType = newStateType;
        currentState = states[newStateType];
        
        currentState.Enter();
    }

    // TODO : 시야에서 사라지면 바로 추적 멈추니까 보완하기
    /// <summary>
    /// 플레이어 탐지 메서드
    /// 상어의 시야각 안에 있고 장애물에 가려지지 않은 가장 가까운 플레이어 찾기
    /// </summary>
    public bool TryFindTarget()
    {
        Collider[] targetsInRadius = Physics.OverlapSphere(
            transform.position,
            enemyData.viewRadius,
            targetLayer
            );

        Transform nearestTarget = null;
        float nearestDistance = float.MaxValue;

        foreach (Collider targetCollider in targetsInRadius)
        {
            Transform candidate = targetCollider.transform;
            
            Vector3 directionToTarget = (candidate.position - transform.position).normalized;
            
            float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);
            
            if (angleToTarget > enemyData.viewAngle * 0.5f)
                continue;

            float distanceToTarget = Vector3.Distance(transform.position, candidate.position);

            bool isBlocked = Physics.Raycast(
                transform.position,
                directionToTarget,
                distanceToTarget,
                obstacleLayer
            );
            
            if (isBlocked)
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
}
