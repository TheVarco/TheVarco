using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Health))]
public class RepairableStructure : MonoBehaviour
{
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

    private const float DamageEpsilon = 0.001f;

    private Health health;
    private int activeRepairSlot = -1; // TODO : 멀티할 때 슬롯별 수리 상태로 변경하기

    public int SlotCount => damageSlots?.Length ?? 0;
    public float RepairCycleDuration => repairCycleDuration;

    private void Awake()
    {
        health = GetComponent<Health>();

        if (damageSlots == null)
            return;

        foreach (DamageDecalSlot slot in damageSlots)
        {
            if (slot == null)
                continue;

            // TODO : 세이브포인트 넣으면 별도 저장 필요
            slot.accumulatedDamage = 0f;
            slot.repairProgressSeconds = 0f;
            UpdateSlotDamageVisual(slot);
        }
    }

    private void OnEnable()
    {
        if (health == null)
            health = GetComponent<Health>();

        health.OnDamageApplied += HandleDamageApplied;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamageApplied -= HandleDamageApplied;

        activeRepairSlot = -1;
    }

    private void Update()
    {
        if (damageSlots == null)
            return;

        if (health == null || health.IsDead)
            activeRepairSlot = -1;

        float decay = repairDecaySecondsPerSecond * Time.deltaTime;
        if (decay <= 0f)
            return;

        for (int i = 0; i < damageSlots.Length; i++)
        {
            DamageDecalSlot slot = damageSlots[i];
            if (slot == null || i == activeRepairSlot || slot.repairProgressSeconds <= 0f)
                continue;

            slot.repairProgressSeconds = Mathf.Max(0f, slot.repairProgressSeconds - decay);
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
        // requireDamage를 false 하는 이유 > Raycast가 맞은 위치에서 가장 가까운 슬롯을 선택하고
        // 이후 CanRepairSlot()으로 해당 슬롯이 손상됐는지 확인하고 있는데
        // 이걸 true로 하면 근처의 다른 슬롯이 수정되는 현상 발생함
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
        UpdateSlotDamageVisual(slot);

        // 유리랑 선체가 사용하는 Renderer가 달라서 나눠서 확인
        string visualMaterialName = slot.glassOverlay != null
            ? slot.glassOverlay.CurrentMaterialName
            : slot.projector != null && slot.projector.material != null
                ? slot.projector.material.name
                : "없음";
        bool isVisualEnabled = slot.glassOverlay != null
            ? slot.glassOverlay.IsVisible
            : slot.projector != null && slot.projector.enabled;

        // TODO : 디버그 확인용으로 주석처리할것
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

            // requireDamage가 있다면 실제 손상이 있는 슬롯만 검사
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
        return IsValidSlotIndex(slotIndex)
            && health != null
            && !health.IsDead
            && damageSlots[slotIndex].accumulatedDamage > DamageEpsilon;
    }

    // HammerItem이 매 프레임 호출
    // 진행 시간이 한 주기를 채운 프레임에만 실제 HP 회복과 해당 슬롯의 누적 손상을 함께 감소
    public float AdvanceRepair(
        int slotIndex,
        float deltaTime,
        float repairAmount,
        out bool completedCycle)
    {
        completedCycle = false; // 수리 완료 상태 초기화

        // 수리 가능 여부 검사
        if (!CanRepairSlot(slotIndex) || deltaTime <= 0f)
        {
            StopRepair(slotIndex);
            return 0f;
        }

        // 이 슬롯은 Update()의 진행도 감소 대상에서 제외
        activeRepairSlot = slotIndex;

        // 수리 시간 누적
        DamageDecalSlot slot = damageSlots[slotIndex];
        slot.repairProgressSeconds += deltaTime;

        // 수리 완료 확인
        if (slot.repairProgressSeconds + DamageEpsilon < repairCycleDuration)
            return 0f;

        // 진행도 초기화
        slot.repairProgressSeconds = 0f;
        completedCycle = true;

        // 슬롯에 남은 손상보다 많이 회복하지 않고
        // Health가 실제로 회복한 양만 슬롯 손상에서도 차감해 HP와 데칼 상태를 항상 일치시킴
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

        // 완전 수리 처리
        if (!CanRepairSlot(slotIndex))
            activeRepairSlot = -1;

        return repairedAmount;
    }

    // 슬롯의 수리 활성 상태 해제
    public void StopRepair(int slotIndex)
    {
        if (activeRepairSlot == slotIndex)
            activeRepairSlot = -1;
    }

    // 수리 UI가 필요로 하는 위치, 표면 방향, 진행률을 한 번에 전달
    // Anchor가 없으면 UI를 올바른 위치에 표시할 수 없으므로 실패로 처리
    public bool TryGetRepairUIData(
        int slotIndex,
        out Vector3 worldPosition,
        out Vector3 worldNormal,
        out float progress01)
    {
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
        return damageSlots != null
            && slotIndex >= 0
            && slotIndex < damageSlots.Length
            && damageSlots[slotIndex] != null;
    }

    // 손상 수치를 0~최대 데칼 단계로 변환
    public static int CalculateDamageStage(float accumulatedDamage, float damagePerStage, int stageCount)
    {
        // 데칼이 없는 조건
        if (accumulatedDamage <= DamageEpsilon || damagePerStage <= 0f || stageCount <= 0)
            return 0;

        int stage = Mathf.CeilToInt((accumulatedDamage - DamageEpsilon) / damagePerStage); // 손상 단계
        return Mathf.Clamp(stage, 1, stageCount);
    }

    // 유리는 이미지 개수, 선체는 머티리얼 개수로 단계 계산
    private int GetStageCount(DamageDecalSlot slot)
    {
        if (slot?.glassOverlay != null)
            return glassDamageStageAlbedos?.Length ?? 0;

        return damageStageMaterials?.Length ?? 0;
    }

    // 누적 손상으로 단계를 계산하고 Projector 또는 유리 메시 오버레이를 갱신
    private void UpdateSlotDamageVisual(DamageDecalSlot slot)
    {
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
