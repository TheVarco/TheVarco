using Fusion;
using UnityEngine;

/// <summary>
/// 성게의 접촉 부착 및 주기 피해
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(HarvestableCreature))]
public class UrchinController : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float damageAmount = 2f;      // 1회 피해량
    [Min(0.01f)] [SerializeField] private float damageInterval = 5f; // 피해 간격

    private HarvestableCreature harvestable; // 공통 부착 생물 상태
    private Health attachedPlayerHealth;      // 부착된 플레이어 체력
    private float damageTimer;                // 다음 피해까지 경과 시간
    private NetworkObject networkObject; // 권위 확인 대상

    public float DamageAmount => damageAmount;
    public float DamageInterval => damageInterval;

    private void Awake()
    {
        harvestable = GetComponent<HarvestableCreature>();
        networkObject = GetComponent<NetworkObject>(); // 같은 성게의 네트워크 오브젝트
    }

    private void OnEnable()
    {
        harvestable.OnAttached += HandleAttached;
        harvestable.OnDetached += HandleDetached;

        // Late Join이나 체크포인트 복원 직후 이미 붙어 있는 상태일 수 있다.
        SynchronizeCheckpointLifecycle();
    }

    private void OnDisable()
    {
        harvestable.OnAttached -= HandleAttached;
        harvestable.OnDetached -= HandleDetached;
    }

    /// <summary>
    /// 설정 간격 기준 부착 플레이어 피해
    /// </summary>
    private void Update()
    {
        // 부착 피해 타이머는 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        if (!harvestable.IsAttached)
        {
            attachedPlayerHealth = null;
            damageTimer = 0f;
            return;
        }

        if (attachedPlayerHealth == null)
            attachedPlayerHealth = harvestable.AttachedSlot != null
                ? harvestable.AttachedSlot.GetComponent<Health>()
                : null;

        if (attachedPlayerHealth == null)
            return;

        if (attachedPlayerHealth.IsDead)
        {
            damageTimer = 0f;
            return;
        }

        damageTimer += Time.deltaTime;
        if (damageTimer < damageInterval)
            return;

        damageTimer -= damageInterval;
        attachedPlayerHealth.TakeDamage(damageAmount, gameObject);
    }

    /// <summary>
    /// 접촉 플레이어의 다리 슬롯에 성게 부착
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // 최초 접촉 피해는 권위자만 실행
        if (!HasSimulationAuthority)
            return;

        if (harvestable.Phase != HarvestableCreature.CreaturePhase.Hazard)
            return;

        AttachmentSlot slot = other.GetComponentInParent<AttachmentSlot>();
        if (slot == null)
            return;

        Health playerHealth = slot.GetComponent<Health>();
        if (playerHealth != null && playerHealth.IsDead)
            return;

        harvestable.TryAttach(slot);
    }

    /// <summary>
    /// 부착 대상 및 피해 타이머 설정
    /// </summary>
    private void HandleAttached(AttachmentSlot slot)
    {
        attachedPlayerHealth = slot != null ? slot.GetComponent<Health>() : null;
        damageTimer = 0f;
    }

    /// <summary>
    /// 부착 대상 및 피해 타이머 해제
    /// </summary>
    private void HandleDetached(AttachmentSlot slot)
    {
        attachedPlayerHealth = null;
        damageTimer = 0f;
    }

    /// <summary>
    /// 체크포인트/복제 적용 뒤 피해 대상과 타이머를 현재 CreaturePhase에 맞춘다.
    /// Collectible 및 Hazard 상태에서는 이전 숙주 참조가 남지 않는다.
    /// </summary>
    public void SynchronizeCheckpointLifecycle()
    {
        if (harvestable != null && harvestable.IsAttached)
        {
            attachedPlayerHealth = harvestable.AttachedSlot != null
                ? harvestable.AttachedSlot.GetComponent<Health>()
                : null;
        }
        else
        {
            attachedPlayerHealth = null;
        }

        damageTimer = 0f;
    }

    // 로컬 실행 또는 State Authority 여부
    private bool HasSimulationAuthority =>
        networkObject == null || !networkObject.IsValid || networkObject.HasStateAuthority;
}
