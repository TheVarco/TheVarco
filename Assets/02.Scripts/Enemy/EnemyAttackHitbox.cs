using UnityEngine;

// 몬스터의 공격 판정용 히트박스
// 평소엔 콜라이더가 꺼져 있고 공격 시작될 때만 켜짐
// 켜져 있는 동안 플레이어에 실제로 닿으면 한 번만 데미지를 줌
[RequireComponent(typeof(Collider))]
public class EnemyAttackHitbox : MonoBehaviour
{
    [SerializeField] private LayerMask targetLayer;

    private int damage;
    private GameObject owner; // 데미지 출처 (상어 본체)
    private bool hasHit; // 1회 데미지 (중복 데미지 방지용)

    private Collider hitboxCollider;

    void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        hitboxCollider.isTrigger = true;
        hitboxCollider.enabled = false; // 평소엔 판정 꺼둠
    }

    // 공격 시작
    public void BeginBite(int damage, GameObject owner)
    {
        // Debug.Log($"BeginBite at {Time.time}");
            
        this.damage = damage;
        this.owner = owner;
        hasHit = false;
        hitboxCollider.enabled = true;
    }

    // 공격 종료
    public void EndBite()
    {
        hitboxCollider.enabled = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasHit)
            return;

        // 대상 레이어(플레이어)가 아니면 무시
        if ((targetLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        Damageable target = other.GetComponentInParent<Damageable>();
        if (target == null || target.IsDead)
            return;

        target.TakeDamage(damage, owner);
        hasHit = true;
    }
}
