using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// 지원 패턴 종류
public enum VentPatternType
{
    Alone, // 한 그룹 동시 반복
    Cross // 두 그룹 교차 반복
}

[DisallowMultipleComponent]
// 분출구 그룹과 공용 시간표 설정을 연결하는 패턴 컴포넌트
public sealed class VentPattern : ObstaclePatternBase
{
    [Header("Pattern")]
    [SerializeField] private VentPatternType patternType = VentPatternType.Alone; // 실행할 분출 패턴 종류
    [Tooltip("Alone의 동시 분출 그룹이자 Cross의 첫 번째 그룹입니다.")]
    [SerializeField] private List<VentController> groupA = new List<VentController>(); // Alone 전체 또는 Cross 첫 그룹
    [Tooltip("Cross에서만 사용하는 두 번째 그룹입니다.")]
    [FormerlySerializedAs("groupB")]
    [SerializeField] private List<VentController> crossGroupB = new List<VentController>(); // Cross 두 번째 그룹

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay; // 패턴 최초 시작 전 대기시간
    [SerializeField, Min(0f)] private float initialWarningDuration = 1f; // 첫 분출 전 예고시간
    [SerializeField, Min(0.01f)] private float activeDuration = 2.5f; // 각 그룹의 분출 유지시간
    [FormerlySerializedAs("nextWarningLeadTime")]
    [SerializeField, Min(0f)] private float crossWarningLeadTime = 0.75f; // 현재 그룹 종료 전 다음 그룹 예고시간
    [FormerlySerializedAs("singleGroupRecoveryDuration")]
    [SerializeField, Min(0f)] private float aloneRecoveryDuration = 1.5f; // Alone 다음 반복 전 휴식시간

    public VentPatternType PatternType => patternType; // 현재 선택한 분출 패턴
    public IReadOnlyList<VentController> GroupA => groupA; // 첫 그룹 읽기 전용 목록
    public IReadOnlyList<VentController> CrossGroupB => crossGroupB; // Cross 두 번째 그룹 읽기 전용 목록

    protected override bool UsesCrossPattern => patternType == VentPatternType.Cross; // 공용 시간표에 Cross 선택 여부 전달
    protected override IReadOnlyList<IPatternTarget> ConfiguredGroupA => groupA; // 분출구 첫 그룹을 공용 대상 목록으로 전달
    protected override IReadOnlyList<IPatternTarget> ConfiguredCrossGroupB => crossGroupB; // 분출구 두 번째 그룹을 공용 대상 목록으로 전달
    protected override float PatternStartDelay => startDelay; // 공용 시간표에 최초 지연시간 전달
    protected override float InitialWarningDuration => initialWarningDuration; // 공용 시간표에 최초 예고시간 전달
    protected override float ActiveDuration => activeDuration; // 공용 시간표에 분출 유지시간 전달
    protected override float CrossWarningLeadTime => crossWarningLeadTime; // 공용 시간표에 Cross 사전 예고시간 전달
    protected override float AloneRecoveryDuration => aloneRecoveryDuration; // 공용 시간표에 Alone 휴식시간 전달
    protected override string PatternDisplayName => nameof(VentPattern); // 경고 출력용 패턴 이름
    protected override string TargetDisplayName => nameof(VentController); // 경고 출력용 대상 이름

    // Inspector 입력값을 실행 가능한 범위로 제한
    private void OnValidate()
    {
        startDelay = Mathf.Max(0f, startDelay);
        initialWarningDuration = Mathf.Max(0f, initialWarningDuration);
        activeDuration = Mathf.Max(0.01f, activeDuration);
        // Cross 예고가 현재 분출시간보다 길어져 순서가 뒤집히지 않도록 상한 적용
        crossWarningLeadTime = Mathf.Clamp(crossWarningLeadTime, 0f, activeDuration);
        aloneRecoveryDuration = Mathf.Max(0f, aloneRecoveryDuration);
    }
}
