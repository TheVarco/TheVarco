using UnityEngine;

// 플레이어의 원거리 공격. 카메라가 보는 방향으로 투사체를 발사한다.
public class RangedAttack : MonoBehaviour
{
    [Header("발사 설정 (밸런싱용)")]
    public GameObject projectilePrefab;
    [Tooltip("다시 발사 가능해지기까지 걸리는 시간(초)")]
    public float fireCooldown = 0.3f;
    public KeyCode fireKey = KeyCode.Mouse0;
    [Tooltip("조준(줌+몸 정렬)만 담당하는 별도 키. 필요 없으면 기능을 안 쓰면 그만이라 남겨둠")]
    public KeyCode aimKey = KeyCode.Mouse1;

    [Header("참조")]
    [Tooltip("투사체가 실제로 태어나는 위치 (손/총구 등, Player 자식으로 만들어 연결 - 시각적으로 여기서 나가야 자연스러움)")]
    public Transform firePoint;
    [Tooltip("조준 기준이 되는 카메라(또는 카메라 리그) Transform - 날아가는 '방향'만 여기서 가져옴")]
    public Transform lookReference;
    [Tooltip("조준 중 줌/몸 회전 연동을 위해 카메라 리그 연결 (선택 사항)")]
    public PlayerCameraRig cameraRig;
    [Tooltip("무기 슬롯(2, 3)일 때만 발사 가능하게 하려면 연결. 안 하면 항상 발사 가능")]
    public PlayerHotbar hotbar;

    private float cooldownTimer = 0f;

    void Update()
    {
        // 슬롯 번호가 아니라, 지금 그 슬롯에 "진짜 무기(RangedWeaponItem)"가 있는지로 판단
        // -> 2/3번 슬롯이어도 산소통 같은 다른 아이템이면 발사 안 되게 함
        bool weaponEquipped = hotbar == null || hotbar.GetActiveItem() is RangedWeaponItem;
        if (!weaponEquipped)
        {
            if (cameraRig != null) cameraRig.SetAiming(false);
            return;
        }

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        // aimKey를 누르고 있는 동안만 "조준 중" 상태로 카메라에 알려줌 (줌 + 몸 회전용)
        // 발사(fireKey)와 완전히 분리되어 있어서, 조준 없이 그냥 쏘기만 할 수도 있음
        if (cameraRig != null)
            cameraRig.SetAiming(Input.GetKey(aimKey));

        if (Input.GetKeyDown(fireKey) && cooldownTimer <= 0f)
        {
            Fire();
            cooldownTimer = fireCooldown;
        }
    }

    private void Fire()
    {
        if (projectilePrefab == null || firePoint == null || lookReference == null)
        {
            Debug.LogWarning("RangedAttack: projectilePrefab, firePoint, lookReference 중 비어있는 게 있음");
            return;
        }

        // 위치는 총구(firePoint, 몸에 붙어 시각적으로 자연스러움)
        // 방향은 카메라(lookReference, 조준 정밀도) - 이 둘을 분리해서 각자의 장점만 취함
        GameObject spawned = Instantiate(
            projectilePrefab,
            firePoint.position,
            Quaternion.LookRotation(lookReference.forward)
        );

        Projectile projectile = spawned.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.owner = gameObject; // 자기 자신에게 안 맞도록 발사자 등록
        }
    }
}