using UnityEngine;
using GogoGaga.OptimizedRopesAndCables;

// 좌클릭하면 밧줄(RopeProjectile)을 던지는 아이템.
// 이미 누군가와 연결되어 있는 상태면, 좌클릭이 "던지기"가 아니라 "연결 해제"로 동작함 (토글 방식).
public class RopeItem : CarryableItem
{
    [Header("발사 설정 (밸런싱용)")]
    public GameObject ropeProjectilePrefab;
    [Tooltip("입체적인 로프 비주얼을 그려주는 프리팹 (GogoGaga Rope 컴포넌트가 붙은 것)")]
    public GameObject ropeVisualPrefab;
    [Tooltip("빗나갔을 때 로프가 완전히 정리될 시간을 감안해서, Rope Projectile의 Life Time보다 여유 있게 설정 권장")]
    public float throwCooldown = 3f;
    [Tooltip("총구가 실제로 태어나는 위치를 지정하고 싶으면 이 아이템의 자식으로 만들어 연결. 비워두면 조준 기준점 위치에서 던져짐")]
    public Transform muzzlePoint;
    public float muzzleForwardOffset = 1f;

    private float cooldownTimer = 0f;
    private PlayerRopeTarget currentTarget; // 지금 이 밧줄로 연결하고 있는 대상 (없으면 null)

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        // 이미 누군가와 연결되어 있으면, 이번 클릭은 "연결 해제"
        // (DetachRope 안에서 OnDetached 이벤트가 터져서 currentTarget도 알아서 정리됨)
        if (currentTarget != null)
        {
            currentTarget.DetachRope();
            return false;
        }

        if (cooldownTimer > 0f) return false;

        Throw(user, aimReference);
        cooldownTimer = throwCooldown;
        return false;
    }

    private void Throw(GameObject user, Transform aimReference)
    {
        if (ropeProjectilePrefab == null || ropeVisualPrefab == null || aimReference == null)
        {
            Debug.LogWarning($"{itemName}: 필요한 프리팹/참조 중 비어있는 게 있음");
            return;
        }

        Vector3 spawnPosition = muzzlePoint != null
            ? muzzlePoint.position
            : aimReference.position + aimReference.forward * muzzleForwardOffset;

        GameObject projectileObj = Instantiate(
            ropeProjectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(aimReference.forward)
        );

        // 비활성 상태로 생성해서 Awake가 돌기 전에 시작점/끝점을 먼저 채워넣음
        GameObject visualObj = Instantiate(ropeVisualPrefab);
        visualObj.SetActive(false);

        Rope visualRope = visualObj.GetComponent<Rope>();
        if (visualRope != null)
        {
            visualRope.SetStartPoint(user.transform, false);
            visualRope.SetEndPoint(projectileObj.transform, false);
        }

        visualObj.SetActive(true);

        RopeProjectile projectile = projectileObj.GetComponent<RopeProjectile>();
        if (projectile != null)
        {
            projectile.owner = user;
            projectile.visualRope = visualRope;
            projectile.sourceItem = this;
        }
    }

    // RopeProjectile이 누군가를 맞혔을 때 호출됨
    public void SetCurrentTarget(PlayerRopeTarget target)
    {
        currentTarget = target;
        currentTarget.OnDetached += HandleTargetDetached; // 거리 초과 등으로 저쪽에서 스스로 끊어져도 알림받기
    }

    private void HandleTargetDetached()
    {
        if (currentTarget != null)
            currentTarget.OnDetached -= HandleTargetDetached;

        currentTarget = null;
    }
}