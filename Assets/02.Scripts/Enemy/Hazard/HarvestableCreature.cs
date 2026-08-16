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
    private bool hazardIsKinematic;                              // 프리팹 위험 상태 물리값
    private RigidbodyConstraints hazardConstraints;
    private CollisionDetectionMode hazardCollisionDetection;
    private bool hazardColliderIsTrigger;
    private int hazardLayer;
    private readonly List<Collider> ignoredHostColliders = new List<Collider>(); // 충돌 비활성 숙주 Collider 목록

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

        if (rb != null)
        {
            detachedInterpolation = rb.interpolation;
            hazardIsKinematic = rb.isKinematic;
            hazardConstraints = rb.constraints;
            hazardCollisionDetection = rb.collisionDetectionMode;
        }

        if (col != null)
            hazardColliderIsTrigger = col.isTrigger;
        hazardLayer = gameObject.layer;
    }

    // 권위 상태 게시
    // 프록시 상태 초기화
    public override void Spawned()
    {
        base.Spawned();
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
        // Holder/Item Zone이 같은 프레임에 아직 생성되지 않은 경우 Carryable의
        // pending 배치를 계속 해석한다.
        base.Render();

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
        {
            transform.SetParent(anchor, false);
            RestoreDefaultWorldScale();
        }

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
        return TryAttachInternal(slot, requireLivingCreature: true);
    }

    private bool TryAttachInternal(AttachmentSlot slot, bool requireLivingCreature)
    {
        // 프록시의 독립 부착 판정 차단
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            return false;

        if (phase != CreaturePhase.Hazard
            || slot == null
            || health == null
            || requireLivingCreature && health.IsDead)
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

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
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
        RestoreDefaultWorldScale();
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));

        OnAttached?.Invoke(slot);
        // Carryable 배치 상태도 CreatureAttached로 맞춰 Late Join과 체크포인트가
        // 단순 월드 아이템으로 오인하지 않게 한다.
        CommitCreatureAttachedPlacementFromAuthority();
        // 확정된 슬롯과 단계 게시
        PublishCreatureState();
        return true;
    }

    protected override void OnAuthorityPickupConfirmed()
    {
        // Holder 커밋 전에 부착 관계와 위험 기능을 먼저 해제한다.
        MakeCollectibleLocal(true);
        base.OnAuthorityPickupConfirmed();
    }

    public override void PrepareForCheckpointRestore()
    {
        AttachmentSlot previousSlot = _attachedSlot;
        if (previousSlot != null)
        {
            previousSlot.Release(attachmentSlot, this);
            RestoreHostCollisions();
            _attachedSlot = null;
            phase = CreaturePhase.Collectible;
            OnDetached?.Invoke(previousSlot);
        }

        base.PrepareForCheckpointRestore();
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
        {
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
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
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = GetDetachedInterpolation();
        }

        if (col != null)
            col.isTrigger = false;

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));
        // 드롭 이후 수집 단계 게시
        PublishCreatureState();
    }

    public override void OnStored(SubmarineItemZone zone, int slotIndex)
    {
        phase = CreaturePhase.Collectible;
        SetTransformReplicationEnabled(false);
        base.OnStored(zone, slotIndex);

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.interpolation = RigidbodyInterpolation.None;
        }
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Interaction"));
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
        CommitWorldDroppedPlacementFromAuthority(transform.position, transform.rotation);
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

        // Held/Stored/Consumed의 부모, Collider, 가시성은 Carryable 배치 상태가
        // 단일 소유한다. 생명주기 복제 순서가 뒤늦게 도착해도 이를 월드 상태로
        // 되돌리지 않는다.
        bool preserveCarryablePresentation = PlacementMode is
            CarryablePlacementMode.Held
            or CarryablePlacementMode.Stored
            or CarryablePlacementMode.Consumed;
        if (preserveCarryablePresentation)
        {
            if (previousSlot != null)
                OnDetached?.Invoke(previousSlot);
            if (publish)
                PublishCreatureState();
            return;
        }

        transform.SetParent(null, true);
        transform.position = worldPosition;
        SetTransformReplicationEnabled(true);

        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = false;
        }

        if (rb != null)
        {
            // 프록시는 복제 위치만 사용
            bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
            rb.isKinematic = isNetworkProxy;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
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
            MakeCollectibleLocal(true);

        if (targetPhase == CreaturePhase.Attached && targetSlot != null)
        {
            phase = CreaturePhase.Hazard;
            if (RestoreCreatureAttachedFromCheckpoint(targetSlot))
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
            ApplyHazardPhysicsLocal();
            PublishCreatureState();
            return;
        }

        MakeCollectibleLocal(true);
    }

    /// <summary>
    /// Carryable 체크포인트 참가자가 CreatureAttached 배치를 복원할 때 사용하는
    /// 단일 진입점. 슬롯 점유 실패 시 false를 반환해 전체 복원을 중단할 수 있다.
    /// </summary>
    public override bool RestoreCreatureAttachedFromCheckpoint(AttachmentSlot targetSlot)
    {
        if (targetSlot == null
            || (Object != null && Object.IsValid && !Object.HasStateAuthority))
        {
            return false;
        }

        if (IsAttached && _attachedSlot == targetSlot)
            return true;

        if (_attachedSlot != null)
            _attachedSlot.Release(attachmentSlot, this);

        RestoreHostCollisions();
        _attachedSlot = null;
        phase = CreaturePhase.Hazard;
        transform.SetParent(null, true);
        ApplyHazardPhysicsLocal();
        // 적 Health는 RestoreOrder 50에서 이 참가자(40)보다 나중에 복원된다.
        // 체크포인트 당시 Attached였다는 검증을 통과했으므로 현재의 사망 플래그만으로
        // 부착을 거절하지 않고 관계를 먼저 복원한다.
        return TryAttachInternal(targetSlot, requireLivingCreature: false);
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

        if (!CanInteract(requester.gameObject)
            || !IsWithinAuthorityInteractionRange(requester))
        {
            return;
        }

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

        // Carryable 배치와 생물 단계는 같은 tick에 게시되지만 프록시의 Render 적용
        // 순서는 보장되지 않는다. Stored/Held/Consumed가 먼저 도착한 한 프레임에
        // 이전 Hazard/Attached 단계를 적용하면 ItemZone/손 부모를 풀어버리고,
        // 이후 PlacementRevision이 바뀌지 않아 잘못된 월드 위치에 남을 수 있다.
        // 이 조합은 전이 중 상태이므로 Collectible 단계가 도착할 때까지 기다린다.
        if ((PlacementMode is CarryablePlacementMode.Held
                or CarryablePlacementMode.Stored
                or CarryablePlacementMode.Consumed)
            && replicatedPhase != CreaturePhase.Collectible)
        {
            return;
        }

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
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
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
        RestoreDefaultWorldScale();
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
        ApplyHazardPhysicsLocal();

        if (previousSlot != null)
            OnDetached?.Invoke(previousSlot);
    }

    private RigidbodyInterpolation GetDetachedInterpolation()
    {
        bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
        return isNetworkProxy ? RigidbodyInterpolation.None : detachedInterpolation;
    }

    /// <summary>
    /// 문어와 성게가 프리팹에서 가진 서로 다른 위험 상태 물리를 정확히 복원한다.
    /// 성게는 Trigger + FreezeAll + Kinematic, 문어는 FreezeRotation + Dynamic이다.
    /// </summary>
    private void ApplyHazardPhysicsLocal()
    {
        SetLayerRecursively(gameObject, hazardLayer);
        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = hazardColliderIsTrigger;
        }
        RestoreInitialColliderCollisionMask();

        if (rb == null)
            return;

        bool isNetworkProxy = Object != null && Object.IsValid && !Object.HasStateAuthority;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        bool targetKinematic = isNetworkProxy || hazardIsKinematic;
        CollisionDetectionMode targetCollision = isNetworkProxy
            ? CollisionDetectionMode.Discrete
            : hazardCollisionDetection;
        if (targetKinematic)
            rb.collisionDetectionMode = targetCollision;
        rb.isKinematic = targetKinematic;
        rb.useGravity = false;
        rb.constraints = hazardConstraints;
        rb.collisionDetectionMode = targetCollision;
        rb.interpolation = isNetworkProxy ? RigidbodyInterpolation.None : detachedInterpolation;
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
