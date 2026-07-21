using UnityEngine;

// 발사된 후 앞으로 날아가다가, 뭔가에 부딪히면 데미지를 주고 사라지는 투사체.
// 프리팹의 Collider는 반드시 Is Trigger 체크 필요.
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("투사체 수치 (밸런싱용)")]
    public float speed = 20f;
    public float damage = 15f;
    [Tooltip("아무것도 안 맞고 이 시간(초)이 지나면 자동으로 사라짐 (허공으로 날아가다 안 없어지는 것 방지)")]
    public float lifeTime = 5f;

    // 쏜 사람 (자기 자신에게 맞지 않게 구분하고, TakeDamage에 누가 쐈는지 전달하는 용도)
    [HideInInspector] public GameObject owner;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // 빠른 속도로 얇은 대상을 그냥 통과해버리는 터널링 현상 방지
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void Start()
    {
        rb.linearVelocity = transform.forward * speed;
        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[Projectile] {other.gameObject.name}에 부딪힘");

        // 자기를 쏜 사람은 무시. Collider가 owner의 자식 오브젝트에 있어도 걸러지도록
        // 정확히 같은 오브젝트가 아니라 "최상위 루트가 같은지"로 비교함
        if (owner != null && other.transform.root == owner.transform.root) return;

        Damageable target = other.GetComponentInParent<Damageable>();
        if (target != null && !target.IsDead)
        {
            target.TakeDamage(damage, owner);
        }

        // 데미지를 줬든 안 줬든(벽에 맞았든) 부딪히면 사라짐
        Destroy(gameObject);
    }
}