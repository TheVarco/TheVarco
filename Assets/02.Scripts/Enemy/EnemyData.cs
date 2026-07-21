using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    // 기본 속도
    public float moveSpeed;
    public float rotateSpeed;

    // 시야각
    [Header("Detection")]
    // 해당 거리 내에 접근하면 시야 관계없이 플레이어 감지
    [Min(0f)] public float ProximityDetectRadius = 15f;

    // 플레이어 탐지 최대 거리 (전방 기준)
    [Min(0f)] public float ForwardDetectRadius = 20f;

    // 이미 발견한 플레이어가 해당 거리보다 멀어지면 추격 중단
    [Min(0f)] public float loseTargetRadius = 20f;

    [Range(0f, 360f)] public float viewAngle;
    
    // 정찰 범위 및 정찰 대기시간
    public float patrolRadius;
    public float patrolArriveDistance;
    public float idleWaitMin;
    public float idleWaitMax;
    
    // 공격
    public float attackRange;
    public float attackCooldown;
    public int attackDamage;
}
