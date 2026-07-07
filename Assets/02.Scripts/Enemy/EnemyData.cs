using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    // 기본 속도
    public float moveSpeed;
    public float rotateSpeed;

    // 시야각
    public float viewRadius;
    public float viewAngle;
    
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
