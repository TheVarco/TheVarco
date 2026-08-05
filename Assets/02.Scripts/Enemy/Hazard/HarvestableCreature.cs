using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 위험 생물의 부착, 수집, 음식 전환 관리
/// </summary>
[RequireComponent(typeof(Health))]
public class HarvestableCreature : CarryableItem
{
    public enum CreaturePhase
    {
        Hazard,     // 위험요소로 실행
        Attached,   // 플레이어 몸에 부착
        Collectible // 일반 아이템으로 획득 가능
    }

    [Header("Creature Lifecycle")]
    [SerializeField] private CreaturePhase phase = CreaturePhase.Hazard; // 현재 생물 단계
    [SerializeField] private AttachmentSlotType attachmentSlot;          // 사용할 신체 슬롯

    [Header("Food Restore")]
    [Min(0f)] [SerializeField] private float healthRestoreAmount = 10f; // 섭취 시 체력 회복량
    [Min(0f)] [SerializeField] private float hungerRestoreAmount = 10f; // 섭취 시 배고픔 회복량

    private Health health;                                      // 생물 체력
    private AttachmentSlot _attachedSlot;                // 현재 부착된 플레이어 슬롯
    private RigidbodyInterpolation detachedInterpolation;       // 분리 상태 Rigidbody 보간값
    private readonly List<Collider> ignoredHostColliders = new List<Collider>(); // 충돌을 끈 숙주 Collider 목록

    public CreaturePhase Phase => phase;
    public AttachmentSlotType AttachmentSlot => attachmentSlot;
    public AttachmentSlot AttachedSlot => _attachedSlot;
    public GameObject AttachedPlayer => _attachedSlot != null ? _attachedSlot.gameObject : null;
    public bool IsAttached => phase == CreaturePhase.Attached && _attachedSlot != null;

    public event Action<AttachmentSlot> OnAttached;
    public event Action<AttachmentSlot> OnDetached;

    protected override void Awake()
    {
        base.Awake();
        health = GetComponent<Health>();

        if (rb != null)
            detachedInterpolation = rb.interpolation;
    }

    /// <summary>
    /// 애니메이션 본 기준 부착 상태 유지
    /// </summary>
    private void LateUpdate()
    {
        if (!IsAttached)
            return;

        Transform anchor = _attachedSlot.GetAnchor(attachmentSlot);
        if (anchor == null)
            return;

        // 애니메이션 본 위치 기준
        if (transform.parent != anchor)
            transform.SetParent(anchor, false);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath.AddListener(HandleDeath);

        if (IsAttached)
            IgnoreHostCollisions(_attachedSlot);
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath.RemoveListener(HandleDeath);

        RestoreHostCollisions();
    }

    /// <summary>
    /// 사용 가능한 슬롯에 생물 부착
    /// </summary>
    public bool TryAttach(AttachmentSlot slot)
    {
        if (phase != CreaturePhase.Hazard || slot == null || health == null || health.IsDead)
            return false;

        if (!slot.TryOccupy(attachmentSlot, this, out Transform anchor))
            return false;

        _attachedSlot = slot;
        phase = CreaturePhase.Attached;

        if (rb != null)
        {
            detachedInterpolation = rb.interpolation;

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        if (col != null)
            col.enabled = true;

        IgnoreHostCollisions(slot);

        transform.SetParent(anchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));

        OnAttached?.Invoke(slot);
        return true;
    }

    public override string GetInteractionPrompt()
    {
        if (phase == CreaturePhase.Attached)
            return $"E : {itemName} 떼어내기";

        return base.GetInteractionPrompt();
    }

    /// <summary>
    /// 상호작용 주체의 획득 및 제거 가능 여부 반환
    /// </summary>
    public override bool CanInteract(GameObject interactor)
    {
        if (interactor == null || phase == CreaturePhase.Hazard)
            return false;

        if (phase == CreaturePhase.Collectible)
            return base.CanInteract(interactor);

        if (_attachedSlot == null || interactor.transform.root == _attachedSlot.transform.root)
            return false;

        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        return hotbar != null && hotbar.HasFreeSlot();
    }

    /// <summary>
    /// 생물 수집 또는 동료 몸에서 제거
    /// </summary>
    public override void Interact(GameObject interactor)
    {
        if (phase == CreaturePhase.Collectible)
        {
            base.Interact(interactor);
            return;
        }

        if (phase != CreaturePhase.Attached || !CanInteract(interactor))
            return;

        PlayerHotbar hotbar = interactor.GetComponent<PlayerHotbar>();
        if (hotbar == null || !hotbar.HasFreeSlot())
            return;

        MakeCollectible();
        hotbar.TryAddItem(this);
    }

    /// <summary>
    /// 수집된 생물을 손 소켓에 부착
    /// </summary>
    public override void OnPickedUp(Transform handSocket)
    {
        if (phase == CreaturePhase.Collectible)
        {
            base.OnPickedUp(handSocket);

            if (rb != null)
                rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    /// <summary>
    /// 지정 위치에 생물 드롭
    /// </summary>
    public override void OnDropped(Vector3 dropPosition)
    {
        phase = CreaturePhase.Collectible;
        base.OnDropped(dropPosition);

        if (rb != null)
            rb.interpolation = detachedInterpolation;

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));
    }

    /// <summary>
    /// 대상 체력 및 배고픔 회복
    /// </summary>
    public override void OnUse(GameObject user, GameObject target)
    {
        if (target == null)
            return;

        Health targetHealth = target.GetComponentInChildren<Health>();
        HungerStat hunger = target.GetComponentInChildren<HungerStat>();

        if (targetHealth != null)
            targetHealth.Heal(healthRestoreAmount);

        if (hunger != null)
            hunger.Refill(hungerRestoreAmount);
    }

    /// <summary>
    /// 위험 기능 종료 및 수집 가능한 음식으로 전환
    /// </summary>
    public void MakeCollectible()
    {
        if (phase == CreaturePhase.Collectible)
            return;

        Vector3 worldPosition = transform.position;
        AttachmentSlot previousSlot = _attachedSlot;

        RestoreHostCollisions();

        if (previousSlot != null)
            previousSlot.Release(attachmentSlot, this);

        _attachedSlot = null;
        phase = CreaturePhase.Collectible;
        transform.SetParent(null, true);
        transform.position = worldPosition;

        if (col != null)
            col.enabled = true;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = detachedInterpolation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));

        if (previousSlot != null)
            OnDetached?.Invoke(previousSlot);
    }

    private void HandleDeath()
    {
        MakeCollectible();
    }

    /// <summary>
    /// 생물과 숙주 몸체 사이의 물리 충돌만 제외
    /// </summary>
    private void IgnoreHostCollisions(AttachmentSlot slot)
    {
        RestoreHostCollisions();

        if (col == null || slot == null)
            return;

        Rigidbody hostBody = slot.GetComponent<Rigidbody>();
        if (hostBody == null)
            return;

        Collider[] hostColliders = slot.GetComponentsInChildren<Collider>(true);
        foreach (Collider hostCollider in hostColliders)
        {
            if (hostCollider == null || hostCollider == col)
                continue;

            // 숙주 Rigidbody 소속 Collider 기준
            if (hostCollider.attachedRigidbody != hostBody)
                continue;

            Physics.IgnoreCollision(col, hostCollider, true);
            ignoredHostColliders.Add(hostCollider);
        }
    }

    /// <summary>
    /// 숙주와의 물리 충돌 복원
    /// </summary>
    private void RestoreHostCollisions()
    {
        if (col != null)
        {
            foreach (Collider hostCollider in ignoredHostColliders)
            {
                if (hostCollider != null)
                    Physics.IgnoreCollision(col, hostCollider, false);
            }
        }

        ignoredHostColliders.Clear();
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
            return;

        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
