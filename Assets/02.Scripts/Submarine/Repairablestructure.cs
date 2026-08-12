using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 잠수함 손상 수리 동기화
// 열 개 손상 슬롯의 피해와 수리 진행 복제
// 호스트가 수리자 장비와 거리와 시야 검증
// 같은 슬롯은 한 명만 수리하도록 점유자 관리
// 복제 상태를 데칼과 유리 오버레이와 체력에 반영

[RequireComponent(typeof(Health))]
public class RepairableStructure : NetworkBehaviour
{
    private static readonly int IsFixingHash = Animator.StringToHash("IsFixing");

    // 서영 추가
    // 잠수함의 손상 부위를 각각 관리하기 위해 넣음
    [Serializable]
    public sealed class DamageDecalSlot
    {
        [Tooltip("Inspector와 디버그 로그에서 구분할 슬롯 이름")]
        public string slotName;

        [Tooltip("피격점/수리점과 거리를 비교할 선체 표면 기준점. forward는 선체 바깥쪽을 향하게 배치")]
        public Transform anchor;

        [Tooltip("불투명 선체 슬롯의 손상 단계를 표시할 URP Decal Projector")]
        public DecalProjector projector;

        // 전/후면 유리는 데칼이 안 먹어서 따로 처리
        [Tooltip("투명 유리처럼 Decal Projector를 받을 수 없는 표면에 표시할 메시 오버레이")]
        public GlassDamageOverlay glassOverlay;

        [NonSerialized] public float accumulatedDamage;
        [NonSerialized] public float repairProgressSeconds;
    }

    [Header("손상 단계")]
    [SerializeField, Min(0.01f)] private float damagePerStage = 10f;

    [Tooltip("불투명 선체 Decal Projector에 단계별로 적용할 머티리얼")]
    [SerializeField] private Material[] damageStageMaterials = new Material[5];

    // 유리는 머티리얼 대신 이미지랑 노멀맵을 직접 넣어줌
    [Tooltip("투명 유리에 표시할 1~5단계 균열 알베도 텍스처")]
    [SerializeField] private Texture2D[] glassDamageStageAlbedos = new Texture2D[5];

    [Tooltip("투명 유리에 표시할 1~5단계 균열 노멀 텍스처")]
    [SerializeField] private Texture2D[] glassDamageStageNormals = new Texture2D[5];

    [Header("부위별 고정 손상 표시 슬롯 (전1/후1/좌2/우2/상2/하2)")]
    [SerializeField] private DamageDecalSlot[] damageSlots = new DamageDecalSlot[10];

    [Header("수리 진행")]
    [SerializeField, Min(0.01f)] private float repairCycleDuration = 5f;
    [SerializeField, Min(0f)] private float repairDecaySecondsPerSecond = 1f;
    [SerializeField, Min(0.01f)] private float networkRepairAmount = 10f;
    [SerializeField, Min(0.1f)] private float networkRepairRange = 2f;
    [SerializeField] private LayerMask networkRepairObstructionMask;

    private const float DamageEpsilon = 0.001f;

    private Health health;
    // 네트워크 수리 점유 관리
    private int activeRepairSlot = -1;
    // Player 코드 변경 없는 원격 수리 애니메이션 캐시
    private readonly Dictionary<PlayerRef, Animator> repairAnimatorByPlayer = new();

    [Networked, Capacity(10)] private NetworkArray<float> NetworkedDamage => default;
    [Networked, Capacity(10)] private NetworkArray<float> NetworkedRepairProgress => default;
    [Networked, Capacity(10)] private NetworkArray<PlayerRef> NetworkedRepairers => default;

    private bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;
    public bool UsesNetworkAuthority => IsNetworkActive;

    public int SlotCount => damageSlots?.Length ?? 0;
    public float RepairCycleDuration => repairCycleDuration;

    // 체크포인트용 부위별 누적 손상 복사
    public float[] CaptureCheckpointDamage()
    {
        // 슬롯 순서를 유지하며 누적 피해값을 새 배열로 복사
        float[] values = new float[SlotCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = damageSlots[i]?.accumulatedDamage ?? 0f;
        return values;
    }

    // 체크포인트용 부위별 수리 진행 시간 복사
    public float[] CaptureCheckpointRepairProgress()
    {
        // 슬롯별 수리 진행 시간을 체크포인트 배열로 복사
        float[] values = new float[SlotCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = damageSlots[i]?.repairProgressSeconds ?? 0f;
        return values;
    }

    // 체크포인트 데이터로 부위 손상과 수리 진행 상태 복원
    // 복원 이후 데칼 표시 즉시 갱신
    public void RestoreCheckpointDamage(float[] accumulatedDamage, float[] repairProgress)
    {
        // 호스트 권한을 확인한 뒤 배열 범위 안의 슬롯 상태 복원
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        activeRepairSlot = -1;
        if (damageSlots == null)
            return;

        for (int i = 0; i < damageSlots.Length; i++)
        {
            DamageDecalSlot slot = damageSlots[i];
            if (slot == null)
                continue;

            slot.accumulatedDamage = accumulatedDamage != null && i < accumulatedDamage.Length
                ? Mathf.Max(0f, accumulatedDamage[i])
                : 0f;
            slot.repairProgressSeconds = repairProgress != null && i < repairProgress.Length
                ? Mathf.Clamp(repairProgress[i], 0f, repairCycleDuration)
                : 0f;
            WriteNetworkSlot(i, slot);
            UpdateSlotDamageVisual(slot);
        }
    }

    // 체력 참조와 손상 슬롯 기본값 준비
    private void Awake()
    {
        // Health 탐색 및 슬롯별 누적 피해와 수리 진행 초기화
        health = GetComponent<Health>();

        if (damageSlots == null)
            return;

        foreach (DamageDecalSlot slot in damageSlots)
        {
            if (slot == null)
                continue;

            // TODO 세이브포인트 추가 시 별도 저장 필요
            slot.accumulatedDamage = 0f;
            slot.repairProgressSeconds = 0f;
            UpdateSlotDamageVisual(slot);
        }
    }

    // 체력 피해 이벤트 연결
    private void OnEnable()
    {
        // Health 참조 보정 및 피해 적용 Callback 등록
        if (health == null)
            health = GetComponent<Health>();

        health.OnDamageApplied += HandleDamageApplied;
    }

    // 손상 슬롯의 초기 권위 상태와 클라이언트 표시 준비
    public override void Spawned()
    {
        // 고정 크기 네트워크 배열보다 슬롯이 많으면 설정 오류 출력
        if (damageSlots != null && damageSlots.Length > NetworkedDamage.Length)
            Debug.LogError($"RepairableStructure: 네트워크 손상 슬롯 최대치({NetworkedDamage.Length})를 초과했습니다.", this);

        if (Object.HasStateAuthority)
        {
            // 호스트는 인스펙터의 현재 손상값을 네트워크 배열에 기록
            int count = Mathf.Min(SlotCount, NetworkedDamage.Length);
            for (int i = 0; i < count; i++)
            {
                WriteNetworkSlot(i, damageSlots[i]);
                NetworkedRepairers.Set(i, PlayerRef.None);
            }
        }
        else
        {
            // 클라이언트는 처음 받은 네트워크 값을 로컬 시각 상태에 적용
            ApplyNetworkSlotsToVisuals();
        }
    }

    // 호스트 틱에서 점유 중인 슬롯 수리 진행
    public override void FixedUpdateNetwork()
    {
        // State Authority 전용 네트워크 수리 진행값 변경
        if (!Object.HasStateAuthority)
            return;

        AdvanceNetworkRepairs();
        DecayRepairProgress(Runner.DeltaTime);
    }

    // 복제된 손상값을 클라이언트 시각 상태에 적용
    public override void Render()
    {
        // 네트워크 배열을 로컬 슬롯과 데칼 상태로 변환
        if (IsNetworkActive && !Object.HasStateAuthority)
            ApplyNetworkSlotsToVisuals();

        ApplyNetworkRepairAnimations();
    }

    // 비활성화 시 이벤트와 수리 점유 정리
    private void OnDisable()
    {
        // 피해 이벤트를 해제하고 로컬 수리 슬롯 초기화
        if (health != null)
            health.OnDamageApplied -= HandleDamageApplied;

        activeRepairSlot = -1;
        ClearNetworkRepairAnimations();
    }

    // 네트워크가 없는 씬의 수리 진행 감쇠 처리
    private void Update()
    {
        // Runner 부재 시 Frame 시간 기준 수리 진행 감소
        if (IsNetworkActive)
            return;

        DecayRepairProgress(Time.deltaTime);
    }

    // 수리 중이 아닌 슬롯의 남은 진행 시간 감소
    private void DecayRepairProgress(float deltaTime)
    {
        // 활성 수리 슬롯을 제외한 모든 슬롯을 순회하며 값 감소
        if (damageSlots == null)
            return;

        if (health == null || health.IsDead)
            activeRepairSlot = -1;

        float decay = repairDecaySecondsPerSecond * deltaTime;
        if (decay <= 0f)
            return;

        for (int i = 0; i < damageSlots.Length; i++)
        {
            DamageDecalSlot slot = damageSlots[i];
            bool networkRepairActive = IsNetworkActive
                && i < NetworkedRepairers.Length
                && !NetworkedRepairers.Get(i).IsNone;
            if (slot == null || networkRepairActive || (!IsNetworkActive && i == activeRepairSlot)
                || slot.repairProgressSeconds <= 0f)
                continue;

            slot.repairProgressSeconds = Mathf.Max(0f, slot.repairProgressSeconds - decay);
            WriteNetworkSlot(i, slot);
        }
    }

    /// <summary>
    /// - 피격 위치 결정
    /// - 가장 가까운 데칼 슬롯 선택
    /// - 실제 데미지 누적
    /// - 데칼 단계 갱신 작업
    /// </summary>
    private void HandleDamageApplied(DamageAppliedInfo appliedInfo)
    {
        // 피해 위치와 표면 방향을 기준으로 가장 가까운 손상 슬롯 선택
        if (IsNetworkActive && !Object.HasStateAuthority)
            return;

        // 유효 데미지 확인
        if (appliedInfo.AppliedAmount <= 0f || damageSlots == null || damageSlots.Length == 0)
            return;

        // 피격 위치 결정
        Vector3 impactPoint;
        if (appliedInfo.Damage.HasImpactPoint) // 정학환 피격 좌표가 있을 경우
        {
            impactPoint = appliedInfo.Damage.Point;
        }
        else if (appliedInfo.Damage.Source != null) // 좌표는 없지만 공격자가 있으면 공격자 위치
        {
            impactPoint = appliedInfo.Damage.Source.transform.position;
        }
        else // 둘 다 없으면 잠수함 중심으로 처리
        {
            impactPoint = transform.position;
        }

        // 가장 가까운 슬롯 선택
        // requireDamage 비활성으로 Raycast 최근접 슬롯 선택
        // CanRepairSlot 기준 선택 슬롯 손상 여부 확인
        // 활성화 시 인접 슬롯 오선택 가능
        if (!TryFindClosestSlot(impactPoint, false, out int slotIndex))
            return;

        // 부위별 손상 누적
        DamageDecalSlot slot = damageSlots[slotIndex];
        slot.accumulatedDamage += appliedInfo.AppliedAmount;

        // 데칼 단계 계산
        int damageStage = CalculateDamageStage(
            slot.accumulatedDamage,
            damagePerStage,
            GetStageCount(slot));

        // 부위 이름 결정
        string resolvedSlotName = string.IsNullOrWhiteSpace(slot.slotName)
            ? $"Slot {slotIndex + 1}"
            : slot.slotName;

        // 손상 표시 갱신
        WriteNetworkSlot(slotIndex, slot);
        UpdateSlotDamageVisual(slot);

        // 유리와 선체의 Renderer 분리 확인
        string visualMaterialName = slot.glassOverlay != null
            ? slot.glassOverlay.CurrentMaterialName
            : slot.projector != null && slot.projector.material != null
                ? slot.projector.material.name
                : "없음";
        bool isVisualEnabled = slot.glassOverlay != null
            ? slot.glassOverlay.IsVisible
            : slot.projector != null && slot.projector.enabled;

        // TODO Debug 확인 후 제거
        Debug.Log(
            $"[SubmarineDamage] 피격 부위={resolvedSlotName}, " +
            $"슬롯 인덱스={slotIndex}, 피격 위치={impactPoint:F2}, " +
            $"받은 데미지={appliedInfo.AppliedAmount:F1}, " +
            $"부위 누적 데미지={slot.accumulatedDamage:F1}, " +
            $"데칼 단계={damageStage}, " +
            $"손상 표시 활성={isVisualEnabled}, " +
            $"머티리얼={visualMaterialName}",
            this);
    }

    // 전달받은 월드 좌표에서 가장 가까운 데칼 슬롯 찾기
    public bool TryFindClosestSlot(Vector3 worldPoint, bool requireDamage, out int slotIndex)
    {
        // 월드 지점과 슬롯 앵커 거리 비교로 가장 가까운 유효 슬롯 검색
        slotIndex = -1;
        float closestSqrDistance = float.PositiveInfinity;

        if (damageSlots == null)
            return false;

        // 슬롯 검사
        for (int i = 0; i < damageSlots.Length; i++)
        {
            DamageDecalSlot slot = damageSlots[i];
            if (slot == null || slot.anchor == null)
                continue;

            // requireDamage 활성 시 실제 손상 슬롯만 검사
            if (requireDamage && slot.accumulatedDamage <= DamageEpsilon)
                continue;

            // 거리 계산
            float sqrDistance = (slot.anchor.position - worldPoint).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance)
                continue;

            // 현재까지 가장 가까운 슬롯보다 거리가 작으면 결과 교체
            closestSqrDistance = sqrDistance;
            slotIndex = i;
        }

        return slotIndex >= 0;
    }

    // 슬롯이 수리 가능한지 검사
    public bool CanRepairSlot(int slotIndex)
    {
        // 슬롯 번호와 누적 피해와 현재 점유 상태를 함께 확인
        return IsValidSlotIndex(slotIndex)
            && health != null
            && !health.IsDead
            && damageSlots[slotIndex].accumulatedDamage > DamageEpsilon;
    }

    // HammerItem의 매 Frame 호출
    // 진행 시간이 한 주기를 채운 프레임에만 실제 HP 회복과 해당 슬롯의 누적 손상을 함께 감소
    public float AdvanceRepair(
        int slotIndex,
        float deltaTime,
        float repairAmount,
        out bool completedCycle)
    {
        // 수리 가능 조건을 확인하고 진행 시간을 한 주기씩 누적
        completedCycle = false; // 수리 완료 상태 초기화

        if (IsNetworkActive && !Object.HasStateAuthority)
            return 0f;

        // 수리 가능 여부 검사
        if (!CanRepairSlot(slotIndex) || deltaTime <= 0f)
        {
            StopRepair(slotIndex);
            return 0f;
        }

        // 현재 슬롯의 Update 진행도 감소 제외
        activeRepairSlot = slotIndex;

        // 수리 시간 누적
        DamageDecalSlot slot = damageSlots[slotIndex];
        slot.repairProgressSeconds += deltaTime;
        WriteNetworkSlot(slotIndex, slot);

        // 수리 완료 확인
        if (slot.repairProgressSeconds + DamageEpsilon < repairCycleDuration)
            return 0f;

        // 진행도 초기화
        slot.repairProgressSeconds = 0f;
        completedCycle = true;

        // 슬롯에 남은 손상보다 많이 회복하지 않고
        // Health 실제 회복량 기준 슬롯 손상 차감
        float allowedAmount = Mathf.Min(repairAmount, slot.accumulatedDamage);
        float repairedAmount = health.Heal(allowedAmount);

        if (repairedAmount > 0f)
        {
            slot.accumulatedDamage = Mathf.Max(0f, slot.accumulatedDamage - repairedAmount);

            if (slot.accumulatedDamage <= DamageEpsilon)
            {
                slot.accumulatedDamage = 0f;
                slot.repairProgressSeconds = 0f;
            }

            UpdateSlotDamageVisual(slot);
        }

        WriteNetworkSlot(slotIndex, slot);

        // 완전 수리 처리
        if (!CanRepairSlot(slotIndex))
            activeRepairSlot = -1;

        return repairedAmount;
    }

    // 슬롯의 수리 활성 상태 해제
    public void StopRepair(int slotIndex)
    {
        // 요청한 슬롯이 현재 로컬 수리 슬롯일 때 활성 상태 해제
        if (activeRepairSlot == slotIndex)
            activeRepairSlot = -1;
    }

    // 수리 시작과 종료 요청을 호스트 RPC로 전달
    public bool RequestNetworkRepair(GameObject user, int slotIndex, bool held)
    {
        // 네트워크 상태와 사용자 소유권과 슬롯 번호를 먼저 확인
        // 수리 진행 시간을 누적하고 한 주기가 끝나면 체력과 피해 감소
        if (!IsNetworkActive)
            return false;

        NetworkObject playerObject = user != null ? user.GetComponentInParent<NetworkObject>() : null;
        if (playerObject == null || !playerObject.IsValid || !playerObject.HasInputAuthority)
            return false;

        RPC_SetRepairing(playerObject.Id, slotIndex, held);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    // 호스트가 요청자 신원과 수리 조건을 검증해 슬롯 점유 변경
    private void RPC_SetRepairing(
        NetworkId playerId,
        int slotIndex,
        NetworkBool held,
        RpcInfo info = default)
    {
        // PlayerRef 출처와 NetworkId가 같은 플레이어를 가리키는지 확인
        if (!Runner.TryFindObject(playerId, out NetworkObject playerObject)
            || playerObject.InputAuthority != info.Source
            || !IsValidSlotIndex(slotIndex)
            || slotIndex >= NetworkedRepairers.Length)
        {
            return;
        }

        PlayerRef current = NetworkedRepairers.Get(slotIndex);
        if (!held)
        {
            if (current == playerObject.InputAuthority)
                NetworkedRepairers.Set(slotIndex, PlayerRef.None);
            return;
        }

        if (!current.IsNone && current != playerObject.InputAuthority)
            return;

        if (!CanPlayerRepair(playerObject, slotIndex))
            return;

        NetworkedRepairers.Set(slotIndex, playerObject.InputAuthority);
    }

    // 점유자별 네트워크 수리를 한 틱씩 진행
    private void AdvanceNetworkRepairs()
    {
        // 슬롯 점유자 연결과 수리 가능 상태를 확인한 뒤 진행값 기록
        int count = Mathf.Min(SlotCount, NetworkedRepairers.Length);
        for (int i = 0; i < count; i++)
        {
            PlayerRef repairer = NetworkedRepairers.Get(i);
            if (repairer.IsNone)
                continue;

            if (!Runner.TryGetPlayerObject(repairer, out NetworkObject playerObject)
                || !CanPlayerRepair(playerObject, i)
                || !CanRepairSlot(i))
            {
                NetworkedRepairers.Set(i, PlayerRef.None);
                continue;
            }

            AdvanceRepair(i, Runner.DeltaTime, networkRepairAmount, out _);
            if (!CanRepairSlot(i))
                NetworkedRepairers.Set(i, PlayerRef.None);
        }
    }

    // 호스트가 장비와 거리와 시야 조건 검증
    private bool CanPlayerRepair(NetworkObject playerObject, int slotIndex)
    {
        // 플레이어 핫바와 활성 해머와 슬롯 앵커를 순서대로 확인
        if (playerObject == null || !IsValidSlotIndex(slotIndex))
            return false;

        // PlayerHotbar 슬롯은 입력 권한을 가진 로컬 머신에서만 관리된다.
        // 호스트는 원격 플레이어 손에 붙은 해머의 복제 상태로 장착 여부를 검증한다.
        HammerItem[] hammers = playerObject.GetComponentsInChildren<HammerItem>(true);
        bool hasEquippedHammer = false;
        foreach (HammerItem hammer in hammers)
        {
            if (hammer != null && hammer.IsEquippedBy(playerObject))
            {
                hasEquippedHammer = true;
                break;
            }
        }

        if (!hasEquippedHammer)
            return false;

        Transform anchor = damageSlots[slotIndex].anchor;
        if (anchor == null || Vector3.Distance(playerObject.transform.position, anchor.position) > networkRepairRange)
            return false;

        if (networkRepairObstructionMask.value == 0)
            return true;

        Vector3 origin = playerObject.transform.position;
        Vector3 offset = anchor.position - origin;
        return !Physics.Raycast(
            origin,
            offset.normalized,
            offset.magnitude,
            networkRepairObstructionMask,
            QueryTriggerInteraction.Ignore);
    }

    // 로컬 슬롯 값을 고정 크기 네트워크 배열에 기록
    private void WriteNetworkSlot(int slotIndex, DamageDecalSlot slot)
    {
        // 슬롯 범위를 제한하고 피해와 수리 진행값 저장
        if (!IsNetworkActive || !Object.HasStateAuthority || slot == null
            || slotIndex < 0 || slotIndex >= NetworkedDamage.Length)
        {
            return;
        }

        NetworkedDamage.Set(slotIndex, Mathf.Max(0f, slot.accumulatedDamage));
        NetworkedRepairProgress.Set(
            slotIndex,
            Mathf.Clamp(slot.repairProgressSeconds, 0f, repairCycleDuration));
    }

    // 네트워크 배열을 로컬 슬롯과 데칼에 적용
    private void ApplyNetworkSlotsToVisuals()
    {
        // 슬롯별 값이 달라진 경우에만 로컬 상태와 시각 단계 갱신
        if (damageSlots == null)
            return;

        int count = Mathf.Min(damageSlots.Length, NetworkedDamage.Length);
        for (int i = 0; i < count; i++)
        {
            DamageDecalSlot slot = damageSlots[i];
            if (slot == null)
                continue;

            float damage = NetworkedDamage.Get(i);
            float progress = NetworkedRepairProgress.Get(i);
            bool visualChanged = !Mathf.Approximately(slot.accumulatedDamage, damage);
            slot.accumulatedDamage = damage;
            slot.repairProgressSeconds = progress;
            if (visualChanged)
                UpdateSlotDamageVisual(slot);
        }
    }

    // 권위 승인 수리 점유 상태 기반 원격 Player 애니메이션 구동
    // 로컬 Input Authority Player는 HammerItem 즉시 반응 유지
    private void ApplyNetworkRepairAnimations()
    {
        if (!IsNetworkActive)
            return;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject)
                || playerObject == null
                || playerObject.HasInputAuthority)
            {
                continue;
            }

            Animator animator = GetRepairAnimator(player, playerObject);
            if (animator != null)
                animator.SetBool(IsFixingHash, IsNetworkRepairer(player));
        }
    }

    private Animator GetRepairAnimator(PlayerRef player, NetworkObject playerObject)
    {
        if (repairAnimatorByPlayer.TryGetValue(player, out Animator cachedAnimator)
            && cachedAnimator != null)
        {
            return cachedAnimator;
        }

        Animator animator = playerObject.GetComponent<Animator>();
        if (animator == null)
            animator = playerObject.GetComponentInChildren<Animator>(true);

        if (animator != null)
            repairAnimatorByPlayer[player] = animator;

        return animator;
    }

    private bool IsNetworkRepairer(PlayerRef player)
    {
        int count = Mathf.Min(SlotCount, NetworkedRepairers.Length);
        for (int i = 0; i < count; i++)
        {
            if (NetworkedRepairers.Get(i) == player)
                return true;
        }

        return false;
    }

    private void ClearNetworkRepairAnimations()
    {
        foreach (Animator animator in repairAnimatorByPlayer.Values)
        {
            if (animator != null)
                animator.SetBool(IsFixingHash, false);
        }

        repairAnimatorByPlayer.Clear();
    }

    // 수리 UI용 위치 표면 방향 진행률 전달
    // Anchor가 없으면 UI를 올바른 위치에 표시할 수 없으므로 실패로 처리
    public bool TryGetRepairUIData(
        int slotIndex,
        out Vector3 worldPosition,
        out Vector3 worldNormal,
        out float progress01)
    {
        // 슬롯 앵커 위치와 표면 방향과 진행률을 UI 데이터로 반환
        worldPosition = transform.position;
        worldNormal = transform.up;
        progress01 = 0f;

        if (!IsValidSlotIndex(slotIndex)
            || damageSlots[slotIndex].anchor == null
            || repairCycleDuration <= 0f)
        {
            return false;
        }

        DamageDecalSlot slot = damageSlots[slotIndex];
        worldPosition = slot.anchor.position;
        worldNormal = slot.anchor.forward;
        progress01 = Mathf.Clamp01(slot.repairProgressSeconds / repairCycleDuration);
        return true;
    }

    // 안정성 검사용
    private bool IsValidSlotIndex(int slotIndex)
    {
        // 배열 존재 여부와 슬롯 번호 범위와 슬롯 참조 확인
        return damageSlots != null
            && slotIndex >= 0
            && slotIndex < damageSlots.Length
            && damageSlots[slotIndex] != null;
    }

    // 손상 수치를 0~최대 데칼 단계로 변환
    public static int CalculateDamageStage(float accumulatedDamage, float damagePerStage, int stageCount)
    {
        // 누적 피해를 단계 크기로 나눈 뒤 유효 단계 범위로 제한
        // 데칼이 없는 조건
        if (accumulatedDamage <= DamageEpsilon || damagePerStage <= 0f || stageCount <= 0)
            return 0;

        int stage = Mathf.CeilToInt((accumulatedDamage - DamageEpsilon) / damagePerStage); // 손상 단계
        return Mathf.Clamp(stage, 1, stageCount);
    }

    // 유리는 이미지 개수 기준 선체는 Material 개수 기준 단계 계산
    private int GetStageCount(DamageDecalSlot slot)
    {
        // 데칼과 유리 오버레이 중 더 많은 단계 수를 사용
        if (slot?.glassOverlay != null)
            return glassDamageStageAlbedos?.Length ?? 0;

        return damageStageMaterials?.Length ?? 0;
    }

    // 누적 손상으로 단계를 계산하고 Projector 또는 유리 메시 오버레이를 갱신
    private void UpdateSlotDamageVisual(DamageDecalSlot slot)
    {
        // 계산된 손상 단계에 맞춰 데칼과 유리 오버레이 활성 상태 갱신
        if (slot == null)
            return;

        int stage = CalculateDamageStage(
            slot.accumulatedDamage,
            damagePerStage,
            GetStageCount(slot));

        // 0단계면 이전 이미지가 남지 않게 둘 다 끄기
        if (stage <= 0)
        {
            if (slot.projector != null)
                slot.projector.enabled = false;
            slot.glassOverlay?.Hide();
            return;
        }

        // 유리면 현재 단계 이미지랑 노멀맵 적용
        if (slot.glassOverlay != null)
        {
            Texture2D albedo = glassDamageStageAlbedos != null
                && stage <= glassDamageStageAlbedos.Length
                ? glassDamageStageAlbedos[stage - 1]
                : null;
            Texture2D normal = glassDamageStageNormals != null
                && stage <= glassDamageStageNormals.Length
                ? glassDamageStageNormals[stage - 1]
                : null;

            // 유리 슬롯에 기존 Projector가 남아있어도 사용 안 함
            if (slot.projector != null)
                slot.projector.enabled = false;
            slot.glassOverlay.Show(albedo, normal);
            return;
        }

        // 나머지 선체는 기존 Decal Projector 사용
        if (slot.projector == null
            || damageStageMaterials == null
            || stage > damageStageMaterials.Length)
        {
            return;
        }

        // 머티리얼 비어있으면 데칼 끄기
        Material stageMaterial = damageStageMaterials[stage - 1];
        slot.projector.material = stageMaterial;
        slot.projector.enabled = stageMaterial != null;
    }
}
