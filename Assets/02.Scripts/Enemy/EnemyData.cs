using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    // 기본 속도
    [Header("Move")]
    public float moveSpeed;
    public float rotateSpeed;

    /// <summary>
    /// 시야각
    /// </summary>
    [Header("Detection")]
    [Min(0f)] public float ProximityDetectRadius = 15f; // 해당 거리 내에 접근하면 시야 관계없이 플레이어 감지
    [Min(0f)] public float ForwardDetectRadius = 20f; // 플레이어 탐지 최대 거리 (전방 기준)
    [Min(0f)] public float loseTargetRadius = 20f; // 이미 발견한 플레이어가 해당 거리보다 멀어지면 추격 중단

    /// <summary>
    /// 공격받았을 때의 탐지
    /// </summary>
    [Header("Detection_Hit")]
    [Min(0f)] public float damageTargetLockDuration = 1.5f; // 공격한 플레이어 우선 타겟 유효 시간
    [Min(0f)] public float retargetInterval = 0.25f; // 추격 중 가장 가까운 플레이어를 다시 탐색하는 주기
    [Min(0f)] public float targetSwitchDistanceMargin = 1f; // 해당 거리 내에 다른 타겟 탐색되면 타겟 변경하는 반경

    [Range(0f, 360f)] public float viewAngle;
    
    /// <summary>
    /// 정찰 범위 및 정찰 대기시간
    /// </summary>
    [Header("Patrol")]
    public float patrolRadius;
    public float patrolArriveDistance;
    public float idleWaitMin;
    public float idleWaitMax;

    /// <summary>
    /// 정찰 목적지 배치(벽 앞에서 멈추기) 및 정체 감지
    /// </summary>
    [Header("Patrol_Avoidance")]
    [Min(0f)] public float patrolProbeRadius = 1f;   // 목적지 배치 시 벽/좁은 통로 판정에 쓰는 상어 몸통 반경
    [Min(0f)] public float patrolWallBuffer = 1.5f;  // 벽에 닿기 전 이만큼 앞에 목적지를 놓아 여유 확보
    [Min(0f)] public float patrolStuckTime = 2f;     // 이 시간 동안 목적지에 못 가까워지면 정체로 판단하고 Idle 복귀
    
    /// <summary>
    /// 공격
    /// </summary>
    [Header("Attack")]
    public float attackRange;
    public float attackCooldown;
    public int attackDamage;
}
