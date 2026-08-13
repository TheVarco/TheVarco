using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 위험 생물의 부착 수집 음식 전환 관리
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
    private readonly List<Collider> ignoredHostColliders = new List<Collider>(); // 충돌 비활성 숙주 Collider 목록
    private NetworkTransform networkTransform;            // 분리 상태 월드 위치 복제

    // 호스트 기준 생물 단계
    [Networked] private int NetworkedPhase { get; set; }
    // 호스트 기준 부착 대상
    [Networked] private NetworkId NetworkedAttachedPlayer { get; set; }

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
        networkTransform = GetComponent<NetworkTransform>();

        if (rb != null)
            detachedInterpolation = rb.interpolation;
    }

    // 권위 상태 게시
    // 프록시 상태 초기화
    public override void Spawned()
    {
        // 권위자는 현재 로컬 단계 게시
        if (Object.HasStateAuthority)
            PublishCreatureState();
        // 프록시는 수신한 단계 즉시 적용
        else
            ApplyReplicatedCreatureState();
    }

    // 프록시 단계와 부착 갱신
    public override void Render()
    {
        // 프록시만 복제 상태를 화면에 반영
        if (!Object.HasStateAuthority)
            ApplyReplicatedCreatureState();
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
        // 프록시의 독립 부착 판정 차단
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return false;

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

        SetTransformReplicationEnabled(false);
        transform.SetParent(anchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));

        OnAttached?.Invoke(slot);
        // 확정된 슬롯과 단계 게시
        PublishCreatureState();
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

        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            // 상호작용 플레이어의 네트워크 식별자 확보
            NetworkObject interactorObject = interactor.GetComponentInParent<NetworkObject>();
            if (interactorObject != null && interactorObject.IsValid)
                // 권위자에게 떼어내기와 획득 요청
                RPC_RequestDetachAndPickup(interactorObject.Id);
            return;
        }

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
        if (phase != CreaturePhase.Collectible)
        {
            // 복제 순서 차이로 남은 부착 상태 보정
            if (Object == null || !Object.IsValid)
                return;
            MakeCollectibleLocal(false);
        }

        // 공용 아이템 손 부착 처리 실행
        SetTransformReplicationEnabled(false);
        base.OnPickedUp(handSocket);

        if (rb != null)
            rb.interpolation = RigidbodyInterpolation.None;
    }

    /// <summary>
    /// 지정 위치에 생물 드롭
    /// </summary>
    public override void OnDropped(Vector3 dropPosition)
    {
        phase = CreaturePhase.Collectible;
        base.OnDropped(dropPosition);
        SetTransformReplicationEnabled(true);

        if (rb != null)
        {
            bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
            if (isNetworkProxy && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.isKinematic = isNetworkProxy;
            rb.useGravity = !isNetworkProxy;
            rb.interpolation = GetDetachedInterpolation();
        }

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));
        // 드롭 이후 수집 단계 게시
        PublishCreatureState();
    }

    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        TriggerEatAnimation(user);
        OnUse(user, user);
        return isConsumable;
    }

    /// <summary>
    /// 대상 체력 및 배고픔 회복
    /// </summary>
    public override void OnUse(GameObject user, GameObject target)
    {
        TriggerEatAnimation(user);
        if (target != user) TriggerEatAnimation(target);

        if (target == null)
            return;

        Health targetHealth = target.GetComponentInChildren<Health>();
        HungerStat hunger = target.GetComponentInChildren<HungerStat>();

        if (targetHealth != null)
            targetHealth.Heal(healthRestoreAmount);

        if (hunger != null)
            hunger.Refill(hungerRestoreAmount);

        user?.GetComponentInParent<PlayerController>()
            ?.RequestPlayerAudio(PlayerAudioCue.Eat);
    }

    private void TriggerEatAnimation(GameObject character)
    {
        if (character == null) return;
        Animator anim = character.GetComponentInChildren<Animator>();
        if (anim == null) anim = character.GetComponentInParent<Animator>();
        if (anim == null) anim = character.GetComponent<Animator>();

        if (anim != null)
        {
            anim.SetTrigger("Eat");
        }
    }

    /// <summary>
    /// 위험 기능 종료 및 수집 가능한 음식으로 전환
    /// </summary>
    public void MakeCollectible()
    {
        // 프록시의 독립 단계 변경 차단
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        // 권위 변경과 게시를 함께 수행
        MakeCollectibleLocal(true);
    }

    // 로컬 수집 단계 전환
    // 게시 여부 선택 지원
    private void MakeCollectibleLocal(bool publish)
    {
        if (phase == CreaturePhase.Collectible)
        {
            if (publish)
                PublishCreatureState();
            return;
        }

        Vector3 worldPosition = transform.position;
        AttachmentSlot previousSlot = _attachedSlot;

        RestoreHostCollisions();

        if (previousSlot != null)
            previousSlot.Release(attachmentSlot, this);

        _attachedSlot = null;
        phase = CreaturePhase.Collectible;
        transform.SetParent(null, true);
        transform.position = worldPosition;
        SetTransformReplicationEnabled(true);

        if (col != null)
            col.enabled = true;

        if (rb != null)
        {
            // 프록시는 복제 위치만 사용
            bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
            rb.isKinematic = isNetworkProxy;
            rb.useGravity = !isNetworkProxy;
            rb.interpolation = GetDetachedInterpolation();
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));

        if (previousSlot != null)
            OnDetached?.Invoke(previousSlot);

        if (publish)
            // 권위 변경 결과 게시
            PublishCreatureState();
    }

    // 체크포인트 당시 채집 단계와 부착 슬롯 복원
    // 기존 부착 관계를 먼저 해제한 뒤 목표 단계 적용
    public void RestoreCheckpointPhase(CreaturePhase targetPhase, AttachmentSlot targetSlot)
    {
        // 체크포인트 복원은 권위자만 실행
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return;

        if (IsAttached)
            MakeCollectible();

        if (targetPhase == CreaturePhase.Attached && targetSlot != null)
        {
            phase = CreaturePhase.Hazard;
            if (TryAttach(targetSlot))
                return;
        }

        if (targetPhase == CreaturePhase.Hazard)
        {
            RestoreHostCollisions();
            _attachedSlot = null;
            phase = CreaturePhase.Hazard;
            transform.SetParent(null, true);
            SetTransformReplicationEnabled(true);
            if (col != null)
                col.enabled = true;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.interpolation = GetDetachedInterpolation();
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            PublishCreatureState();
            return;
        }

        MakeCollectible();
    }

    private void HandleDeath()
    {
        // 사망 단계 변경은 권위자만 실행
        if (Object == null || !Object.IsValid || Object.HasStateAuthority)
            MakeCollectible();
    }

    // 비호스트 떼어내기 요청
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDetachAndPickup(NetworkId requesterId)
    {
        // 부착 상태와 요청자 유효성 검증
        if (phase != CreaturePhase.Attached
            || !requesterId.IsValid
            || !Runner.TryFindObject(requesterId, out NetworkObject requester))
        {
            return;
        }

        if (_attachedSlot != null && requester.transform.root == _attachedSlot.transform.root)
            return;

        // 부착 해제와 소유자 확정을 같은 권위 처리로 실행
        MakeCollectibleLocal(true);
        TryAssignHolderFromStateAuthority(requesterId);
    }

    // 권위 생물 상태 게시
    private void PublishCreatureState()
    {
        // 유효한 권위 오브젝트만 게시 허용
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            return;

        // 현재 단계 저장
        NetworkedPhase = (int)phase;
        // 부착 슬롯의 플레이어 식별자 탐색
        NetworkObject attachedObject = _attachedSlot != null
            ? _attachedSlot.GetComponentInParent<NetworkObject>()
            : null;
        NetworkedAttachedPlayer = attachedObject != null && attachedObject.IsValid
            ? attachedObject.Id
            : default;
    }

    // 복제 단계 분기 적용
    private void ApplyReplicatedCreatureState()
    {
        CreaturePhase replicatedPhase = (CreaturePhase)NetworkedPhase;

        if (replicatedPhase == CreaturePhase.Attached)
        {
            // 부착 대상이 아직 없으면 다음 프레임 재시도
            if (!NetworkedAttachedPlayer.IsValid
                || !Runner.TryFindObject(NetworkedAttachedPlayer, out NetworkObject playerObject))
            {
                return;
            }

            AttachmentSlot targetSlot = playerObject.GetComponent<AttachmentSlot>();
            // 슬롯이나 단계가 다를 때만 재부착
            if (targetSlot != null && (_attachedSlot != targetSlot || phase != CreaturePhase.Attached))
                ApplyAttachedLocal(targetSlot);
            return;
        }

        if (replicatedPhase == CreaturePhase.Collectible)
        {
            // 게시 없이 로컬 수집 단계 적용
            MakeCollectibleLocal(false);
            return;
        }

        ApplyHazardLocal();
    }

    // 복제 부착 상태 적용
    private void ApplyAttachedLocal(AttachmentSlot slot)
    {
        // 기존의 다른 슬롯 점유 해제
        if (_attachedSlot != null && _attachedSlot != slot)
            _attachedSlot.Release(attachmentSlot, this);

        if (!slot.TryOccupy(attachmentSlot, this, out Transform anchor)
            && slot.GetOccupant(attachmentSlot) != this)
        {
            // 다른 생물이 점유한 슬롯 제외
            return;
        }

        // 슬롯 종류에 맞는 부착 기준점 확보
        anchor ??= slot.GetAnchor(attachmentSlot);
        if (anchor == null)
            return;

        _attachedSlot = slot;
        phase = CreaturePhase.Attached;
        if (rb != null)
        {
            // 부착 전에 남은 속도 제거
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
        // 숙주와 생물 사이 물리 충돌 제외
        IgnoreHostCollisions(slot);
        // 각 피어의 로컬 앵커에 부착
        SetTransformReplicationEnabled(false);
        transform.SetParent(anchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));
        OnAttached?.Invoke(slot);
    }

    // 복제 위험 상태 적용
    private void ApplyHazardLocal()
    {
        // 이전 슬롯 점유 해제 대상 저장
        AttachmentSlot previousSlot = _attachedSlot;
        if (previousSlot != null)
            previousSlot.Release(attachmentSlot, this);

        RestoreHostCollisions();
        // 부착 관계와 부모 제거
        _attachedSlot = null;
        phase = CreaturePhase.Hazard;
        transform.SetParent(null, true);
        SetTransformReplicationEnabled(true);
        if (col != null)
            col.enabled = true;
        if (rb != null)
        {
            // 프록시 물리 시뮬레이션 차단
            rb.isKinematic = Object != null && Object.IsValid && !Object.HasStateAuthority;
            rb.useGravity = false;
            rb.interpolation = GetDetachedInterpolation();
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (previousSlot != null)
            OnDetached?.Invoke(previousSlot);
    }

    // 각 피어의 로컬 플레이어 기준점에 부착 생물 배치
    // 부착 중 NetworkTransform Render 덮어쓰기 방지
    // 월드 공간 복귀 시 위치 동기화 재개
    private void SetTransformReplicationEnabled(bool enabled)
    {
        if (networkTransform != null)
            networkTransform.enabled = enabled;
    }

    private RigidbodyInterpolation GetDetachedInterpolation()
    {
        bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
        return isNetworkProxy ? RigidbodyInterpolation.None : detachedInterpolation;
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
