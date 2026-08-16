using Fusion;
using UnityEngine;

// 원거리 무기 아이템. CarryableItem의 좌/우클릭 기본 동작을 재정의해서,
// "이 무기가 어떻게 쏘는지"를 무기 자신이 직접 책임진다.
// 산탄총처럼 다르게 쏘는 무기를 만들고 싶으면, 이 클래스를 상속받아 Fire()만 재정의하면 됨.
public class RangedWeaponItem : CarryableItem
{
    [Header("발사 설정 (밸런싱용)")]
    public GameObject projectilePrefab;
    [Tooltip("네트워크 스폰용 총알 프리팹. 비어있으면 위의 projectilePrefab으로 로컬 생성 (러너 없는 씬용)")]
    public NetworkPrefabRef projectilePrefabRef;
    [Tooltip("다시 발사 가능해지기까지 걸리는 시간(초)")]
    public float fireCooldown = 0.5f;
    [Tooltip("총구가 실제로 태어나는 위치를 지정하고 싶으면 이 무기 모델의 자식으로 만들어 연결. 비워두면 조준 기준점(카메라) 위치에서 발사됨")]
    public Transform muzzlePoint;
    [Tooltip("muzzlePoint를 안 정했을 때, 조준 기준점에서 얼마나 앞에서 발사할지")]
    public float muzzleForwardOffset = 1f;

    private float cooldownTimer = 0f;
    private Animator pendingFireAnimator;

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    // 좌클릭 = 1초 딜레이 후 발사 및 원거리 공격 모션 실행
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        if (cooldownTimer > 0f) return false; // 쿨타임 중이면 아무 일도 안 함

        // 클릭 직후 회전 및 공격 모션(Ranged) 즉시 시작
        RotatePlayerToTarget(user, aimReference);

        if (user != null)
        {
            Animator anim = user.GetComponentInChildren<Animator>();
            if (anim == null) anim = user.GetComponentInParent<Animator>();
            if (anim == null) anim = user.GetComponent<Animator>();

            if (anim != null)
            {
                anim.SetTrigger("Ranged");
                pendingFireAnimator = anim;
                Debug.Log($"[RangedWeaponItem] SetTrigger('Ranged') 실행됨! (1초 후 발사 예정)");
            }
        }

        // 1초 딜레이 후 실제 총알 발사 실행
        if (user != null && user.activeInHierarchy)
            StartCoroutine(DelayedFireRoutine(user, aimReference, 1.0f));

        cooldownTimer = fireCooldown; // 딜레이 시간(1초) 포함 쿨타임 처리
        return false;
    }

    private System.Collections.IEnumerator DelayedFireRoutine(GameObject user, Transform aimReference, float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingFireAnimator = null;
        Fire(user, aimReference);
    }

    public override void PrepareForCheckpointRestore()
    {
        base.PrepareForCheckpointRestore();

        // 코루틴을 플레이어가 아닌 아이템이 소유하므로 체크포인트 복원 때
        // 예약된 총알이 뒤늦게 생성되는 일을 확실히 막을 수 있다.
        // fireCooldown이 delay보다 짧게 설정된 경우 여러 예약이 동시에 존재할 수 있다.
        StopAllCoroutines();

        if (pendingFireAnimator != null)
            pendingFireAnimator.ResetTrigger("Ranged");
        pendingFireAnimator = null;
        cooldownTimer = 0f;
    }

    // 우클릭 홀드 = 조준(줌 + 몸 정렬). CarryableItem의 기본(아무것도 안 함)을 대체함
    public override void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        PlayerCameraRig cameraRig = aimReference != null ? aimReference.GetComponent<PlayerCameraRig>() : null;
        if (cameraRig != null) cameraRig.SetAiming(isHeld);
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

    // 실제로 투사체를 만들어내는 부분. 다른 발사 방식이 필요한 무기는 이 함수만 재정의하면 됨
    protected virtual void Fire(GameObject user, Transform aimReference)
    {
        if (projectilePrefab == null || aimReference == null)
        {
            Debug.LogWarning($"{itemName}: projectilePrefab 또는 aimReference가 비어있음");
            return;
        }

        // 1. 플레이어 본인 제외 레이캐스트 조준점 계산
        Vector3 targetPoint = GetTargetPointIgnoringUser(user, aimReference);

        // 2. 발사 순간 플레이어 로테이션 재조정
        if (user != null)
        {
            Vector3 lookDir = targetPoint - user.transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Vector3 euler = Quaternion.LookRotation(lookDir).eulerAngles;
                user.transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
            }
        }

        // 3. 총알 발사 시작 지점 = 플레이어 위치
        Vector3 spawnPosition = user != null
            ? user.transform.position + user.transform.forward * 0.5f + Vector3.up * 1.0f
            : aimReference.position;

        if (muzzlePoint != null)
        {
            spawnPosition = muzzlePoint.position;
        }

        Vector3 fireDirection = (targetPoint - spawnPosition).normalized;

        PlayerController player = user != null ? user.GetComponentInParent<PlayerController>() : null;
        player?.RequestPlayerAudio(PlayerAudioCue.BubbleGunShot);

        // 네트워크 세션이면 호스트가 총알을 스폰하고 판정까지 담당한다
        PlayerHotbar userHotbar = user != null ? user.GetComponent<PlayerHotbar>() : null;
        if (userHotbar != null && userHotbar.Object != null && projectilePrefabRef.IsValid)
        {
            userHotbar.SpawnProjectile(projectilePrefabRef, spawnPosition, fireDirection);
            return;
        }

        GameObject spawned = Instantiate(
            projectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(fireDirection)
        );

        Projectile projectile = spawned.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.owner = user; // 자기 자신에게 안 맞도록 발사자 등록
        }
    }
}
