using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    public int maxHp;
    
    public float moveSpeed;
    public float rotateSpeed;

    public float viewRadius;
    public float viewAngle;
    
    public float patrolRadius;
    public float patrolArriveDistance;
    
    public float attackRange;
    
    public int bodyDamage;
    public int biteDamage;
}
