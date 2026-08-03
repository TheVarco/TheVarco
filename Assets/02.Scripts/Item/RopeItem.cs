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
    [Tooltip("던지기 버튼 누른 후 로프 투사체가 실제로 발사되기까지의 대기 시간(초)")]
    public float throwDelay = 0.5f;
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
        if (currentTarget != null)
        {
            currentTarget.DetachRope();
            return false;
        }

        if (cooldownTimer > 0f) return false;

        // 1. 클릭 직후 조준 방향으로 플레이어 회전
        RotatePlayerToTarget(user, aimReference);

        // 2. Throw 애니메이션 트리거 실행
        if (user != null)
        {
            Animator anim = user.GetComponentInChildren<Animator>();
            if (anim == null) anim = user.GetComponentInParent<Animator>();
            if (anim == null) anim = user.GetComponent<Animator>();

            if (anim != null)
            {
                anim.SetTrigger("Throw");
                Debug.Log($"[RopeItem] SetTrigger('Throw') 실행됨! ({throwDelay}초 후 밧줄 던지기)");
            }
        }

        // 3. 지정된 throwDelay 초 후 밧줄 투사체 생성
        if (user != null && user.activeInHierarchy)
        {
            MonoBehaviour runner = user.GetComponent<MonoBehaviour>();
            if (runner != null)
            {
                runner.StartCoroutine(DelayedThrowRoutine(user, aimReference, throwDelay));
            }
            else
            {
                StartCoroutine(DelayedThrowRoutine(user, aimReference, throwDelay));
            }
        }

        cooldownTimer = throwCooldown + throwDelay;
        return false;
    }

    private System.Collections.IEnumerator DelayedThrowRoutine(GameObject user, Transform aimReference, float delay)
    {
        yield return new WaitForSeconds(delay);
        Throw(user, aimReference);
    }

    private void Throw(GameObject user, Transform aimReference)
    {
        if (ropeProjectilePrefab == null || ropeVisualPrefab == null || aimReference == null)
        {
            Debug.LogWarning($"{itemName}: 필요한 프리팹/참조 중 비어있는 게 있음");
            return;
        }

        // 1. 플레이어 본인 제외 레이캐스트 조준점 계산
        Vector3 targetPoint = GetTargetPointIgnoringUser(user, aimReference);

        // 2. 던지는 방향으로 플레이어 회전
        if (user != null)
        {
            Vector3 lookDir = targetPoint - user.transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Vector3 euler = Quaternion.LookRotation(lookDir).eulerAngles;
                user.transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
            }
        }

        // 3. 로프 투사체 및 비주얼 생성 시작 지점 = 플레이어 오른손(RightHand)
        Transform rightHandBone = GetRightHandBone(user);

        Vector3 spawnPosition;
        if (muzzlePoint != null)
        {
            spawnPosition = muzzlePoint.position;
        }
        else if (rightHandBone != null)
        {
            spawnPosition = rightHandBone.position;
        }
        else
        {
            spawnPosition = user != null
                ? user.transform.position + user.transform.forward * 0.5f + Vector3.up * 1.0f
                : aimReference.position;
        }

        Vector3 throwDirection = (targetPoint - spawnPosition).normalized;

        GameObject projectileObj = Instantiate(
            ropeProjectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(throwDirection)
        );

        // 비활성 상태로 생성해서 Awake가 돌기 전에 시작점/끝점을 먼저 채워넣음
        GameObject visualObj = Instantiate(ropeVisualPrefab);
        visualObj.SetActive(false);

        Rope visualRope = visualObj.GetComponent<Rope>();
        if (visualRope != null)
        {
            Transform startTransform = rightHandBone != null ? rightHandBone : user.transform;
            visualRope.SetStartPoint(startTransform, false);
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

    private Transform GetRightHandBone(GameObject user)
    {
        if (user == null) return null;

        Animator anim = user.GetComponentInChildren<Animator>();
        if (anim == null) anim = user.GetComponentInParent<Animator>();
        if (anim == null) anim = user.GetComponent<Animator>();

        if (anim != null && anim.isHuman)
        {
            Transform bone = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (bone != null) return bone;
        }

        Transform[] allTransforms = user.GetComponentsInChildren<Transform>();
        foreach (Transform t in allTransforms)
        {
            string lowerName = t.name.ToLower();
            if (lowerName.Contains("righthand") || lowerName.Contains("hand_r") || lowerName.Contains("hand.r"))
            {
                return t;
            }
        }
        return null;
    }

    private void RotatePlayerToTarget(GameObject user, Transform aimReference)
    {
        if (user == null || aimReference == null) return;

        Vector3 targetPoint = GetTargetPointIgnoringUser(user, aimReference);
        Vector3 lookDir = targetPoint - user.transform.position;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Vector3 euler = Quaternion.LookRotation(lookDir).eulerAngles;
            user.transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
        }
    }

    private Vector3 GetTargetPointIgnoringUser(GameObject user, Transform aimReference)
    {
        Ray ray = new Ray(aimReference.position, aimReference.forward);
        float maxDistance = 150f;
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            // 플레이어 본인 및 자식 콜라이더인 경우 레이캐스트 대상에서 무시
            if (user != null && hit.collider != null &&
                (hit.collider.gameObject == user || hit.collider.transform.IsChildOf(user.transform)))
            {
                continue;
            }
            return hit.point;
        }
        return ray.GetPoint(maxDistance);
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