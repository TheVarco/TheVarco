using UnityEngine;

// 원거리 무기 아이템. CarryableItem의 좌/우클릭 기본 동작을 재정의해서,
// "이 무기가 어떻게 쏘는지"를 무기 자신이 직접 책임진다.
// 산탄총처럼 다르게 쏘는 무기를 만들고 싶으면, 이 클래스를 상속받아 Fire()만 재정의하면 됨.
public class RangedWeaponItem : CarryableItem
{
    [Header("발사 설정 (밸런싱용)")]
    public GameObject projectilePrefab;
    [Tooltip("다시 발사 가능해지기까지 걸리는 시간(초)")]
    public float fireCooldown = 0.3f;
    [Tooltip("총구가 실제로 태어나는 위치를 지정하고 싶으면 이 무기 모델의 자식으로 만들어 연결. 비워두면 조준 기준점(카메라) 위치에서 발사됨")]
    public Transform muzzlePoint;
    [Tooltip("muzzlePoint를 안 정했을 때, 조준 기준점에서 얼마나 앞에서 발사할지")]
    public float muzzleForwardOffset = 1f;

    private float cooldownTimer = 0f;

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    // 좌클릭 = 발사. CarryableItem의 기본 동작(OnUse 호출)을 완전히 대체함
    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        if (cooldownTimer > 0f) return false; // 쿨타임 중이면 아무 일도 안 함, 소모(제거)도 안 됨

        Fire(user, aimReference);
        cooldownTimer = fireCooldown;
        return false; // 총은 한 발 쐈다고 핫바에서 사라지지 않음 (소모품 아님)
    }

    // 우클릭 홀드 = 조준(줌 + 몸 정렬). CarryableItem의 기본(아무것도 안 함)을 대체함
    public override void OnSecondaryHeld(GameObject user, Transform aimReference, bool isHeld)
    {
        // aimReference는 보통 CameraRig 오브젝트 자체를 가리키므로, 거기서 바로 컴포넌트를 찾음
        PlayerCameraRig cameraRig = aimReference != null ? aimReference.GetComponent<PlayerCameraRig>() : null;
        if (cameraRig != null) cameraRig.SetAiming(isHeld);
    }

    // 실제로 투사체를 만들어내는 부분. 다른 발사 방식이 필요한 무기는 이 함수만 재정의하면 됨
    protected virtual void Fire(GameObject user, Transform aimReference)
    {
        if (projectilePrefab == null || aimReference == null)
        {
            Debug.LogWarning($"{itemName}: projectilePrefab 또는 aimReference가 비어있음");
            return;
        }

        Vector3 spawnPosition = muzzlePoint != null
            ? muzzlePoint.position
            : aimReference.position + aimReference.forward * muzzleForwardOffset;

        GameObject spawned = Instantiate(
            projectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(aimReference.forward)
        );

        Projectile projectile = spawned.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.owner = user; // 자기 자신에게 안 맞도록 발사자 등록
        }
    }
}