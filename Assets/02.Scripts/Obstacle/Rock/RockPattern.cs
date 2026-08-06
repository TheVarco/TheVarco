using System.Collections.Generic;
using UnityEngine;

// 지원 낙석 패턴 종류
public enum RockPatternType
{
    Alone, // 한 그룹 동시 반복
    Cross // 두 그룹 교차 반복
}

[DisallowMultipleComponent]
// 낙석 생성 그룹과 공용 시간표 설정을 연결하는 패턴 컴포넌트
public sealed class RockPattern : ObstaclePatternBase
{
    [Header("Pattern")]
    [SerializeField] private RockPatternType patternType = RockPatternType.Alone; // 실행할 낙석 패턴 종류
    [Tooltip("Alone의 동시 낙하 그룹이자 Cross의 첫 번째 그룹입니다.")]
    [SerializeField] private List<RockSpawner> groupA = new List<RockSpawner>(); // Alone 전체 또는 Cross 첫 그룹
    [Tooltip("Cross에서만 사용하는 두 번째 그룹입니다.")]
    [SerializeField] private List<RockSpawner> crossGroupB = new List<RockSpawner>(); // Cross 두 번째 그룹

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay; // 패턴 최초 시작 전 대기시간
    [SerializeField, Min(0f)] private float initialWarningDuration = 1f; // 첫 낙하 전 경고등 예고시간
    [SerializeField, Min(0.01f)] private float dropInterval = 2.5f; // 현재 그룹 낙하 후 다음 전환까지의 시간
    [SerializeField, Min(0f)] private float crossWarningLeadTime = 0.75f; // 현재 그룹 차례 종료 전 다음 그룹 예고시간
    [SerializeField, Min(0f)] private float aloneRecoveryDuration = 1.5f; // Alone 다음 반복 전 추가 휴식시간

    public RockPatternType PatternType => patternType; // 현재 선택한 낙석 패턴
    public IReadOnlyList<RockSpawner> GroupA => groupA; // 첫 그룹 읽기 전용 목록
    public IReadOnlyList<RockSpawner> CrossGroupB => crossGroupB; // Cross 두 번째 그룹 읽기 전용 목록

    protected override bool UsesCrossPattern => patternType == RockPatternType.Cross; // 공용 시간표에 Cross 선택 여부 전달
    protected override IReadOnlyList<IPatternTarget> ConfiguredGroupA => groupA; // 낙석 첫 그룹을 공용 대상 목록으로 전달
    protected override IReadOnlyList<IPatternTarget> ConfiguredCrossGroupB => crossGroupB; // 낙석 두 번째 그룹을 공용 대상 목록으로 전달
    protected override float PatternStartDelay => startDelay; // 공용 시간표에 최초 지연시간 전달
    protected override float InitialWarningDuration => initialWarningDuration; // 공용 시간표에 최초 예고시간 전달
    protected override float ActiveDuration => dropInterval; // 공용 시간표에 그룹 전환 간격 전달
    protected override float CrossWarningLeadTime => crossWarningLeadTime; // 공용 시간표에 Cross 사전 예고시간 전달
    protected override float AloneRecoveryDuration => aloneRecoveryDuration; // 공용 시간표에 Alone 추가 휴식시간 전달
    protected override string PatternDisplayName => nameof(RockPattern); // 경고 출력용 패턴 이름
    protected override string TargetDisplayName => nameof(RockSpawner); // 경고 출력용 대상 이름

    // Inspector 입력값을 실행 가능한 범위로 제한
    private void OnValidate()
    {
        startDelay = Mathf.Max(0f, startDelay);
        initialWarningDuration = Mathf.Max(0f, initialWarningDuration);
        dropInterval = Mathf.Max(0.01f, dropInterval);
        // 다음 그룹 예고가 낙하 간격보다 길어져 순서가 뒤집히지 않도록 상한 적용
        crossWarningLeadTime = Mathf.Clamp(crossWarningLeadTime, 0f, dropInterval);
        aloneRecoveryDuration = Mathf.Max(0f, aloneRecoveryDuration);
    }
}
