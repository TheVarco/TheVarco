using UnityEngine;
using static pxr.GfCamera;

// 플레이어의 원거리 공격. 카메라가 보는 방향으로 투사체를 발사한다.
public class RangedAttack : MonoBehaviour
{
    [Header("발사 설정 (밸런싱용)")]
    public GameObject projectilePrefab;
    [Tooltip("다시 발사 가능해지기까지 걸리는 시간(초)")]
    public float fireCooldown = 0.3f;
    public KeyCode fireKey = KeyCode.Mouse1;

    [Header("참조")]
    [Tooltip("투사체가 실제로 생성되는 위치 (손끝 등, Player 자식으로 만들어 연결)")]
    public Transform firePoint;
    [Tooltip("조준 기준이 되는 카메라(또는 카메라 리그) Transform")]
    public Transform lookReference;

    private float cooldownTimer = 0f;

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

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

        GameObject spawned = Instantiate(
            projectilePrefab,
            firePoint.position,
            Quaternion.LookRotation(lookReference.forward) // 카메라가 보는 방향으로 투사체 회전
        );

        Projectile projectile = spawned.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.owner = gameObject; // 자기 자신에게 안 맞도록 발사자 등록
        }
    }
}