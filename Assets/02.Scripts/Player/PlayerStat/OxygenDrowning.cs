using UnityEngine;

// 산소(DepletingStat)가 0이 된 동안, 체력을 지속적으로 깎는 역할만 하는 작은 연결 스크립트.
// 산소 자체의 증감 로직은 DepletingStat이 담당하고, 여긴 "0일 때 뭘 할지"만 책임진다.
[RequireComponent(typeof(OxygenStat))]
public class OxygenDrowning : MonoBehaviour
{
    [Tooltip("데미지를 받을 대상 (보통 같은 오브젝트의 Health)")]
    public Health health;
    [Tooltip("산소가 0인 동안 초당 깎이는 체력")]
    public float damagePerSecond = 5f;

    private OxygenStat oxygen;

    void Awake()
    {
        oxygen = GetComponent<OxygenStat>();
    }

    void Update()
    {
        if (oxygen.IsDepleted && health != null && !health.IsDead)
        {
            health.TakeDamage(damagePerSecond * Time.deltaTime, gameObject, false);
        }
    }
}