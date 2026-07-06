using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    public int maxHp;
    
    public float moveSpeed;
    public float rotateSpeed;
    
    public float viewRadius = 10f;
    public float viewAngle = 90f;
    
    public float attackRange = 2f;
    
    public int bodyDamage;
    public int biteDamage;
}
