using Fusion;
using UnityEngine;

// 산소(DepletingStat)가 0이 된 동안, 체력을 지속적으로 깎는 역할만 하는 작은 연결 스크립트.
// 산소 자체의 증감 로직은 DepletingStat이 담당하고, 여긴 "0일 때 뭘 할지"만 책임진다.
[RequireComponent(typeof(OxygenStat))]
public class OxygenDrowning : NetworkBehaviour
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
        // 익사 데미지는 권한자(호스트)만 적용한다. 모든 피어가 각자 적용하면
        // Health의 라우터를 통해 호스트에 중복 전달돼서 접속자 수만큼 데미지가 배로 들어간다
        if (Object != null && !Object.HasStateAuthority) return;

        if (oxygen.IsDepleted && health != null && !health.IsDead)
        {
            health.TakeDamage(damagePerSecond * Time.deltaTime, gameObject, false);
        }
    }
}