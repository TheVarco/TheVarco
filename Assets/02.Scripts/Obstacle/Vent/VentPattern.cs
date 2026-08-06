using System.Collections;
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
// 분출구 그룹 타이밍 제어
public sealed class VentPattern : MonoBehaviour
{
    [Header("Pattern")]
    [SerializeField] private VentPatternType patternType = VentPatternType.Alone; // 실행할 패턴 종류
    [Tooltip("Alone의 동시 분출 그룹이자 Cross의 첫 번째 그룹입니다.")]
    [SerializeField] private List<VentController> groupA = new List<VentController>(); // Alone 전체 또는 Cross 첫 그룹
    [Tooltip("Cross에서만 사용하는 두 번째 그룹입니다.")]
    [FormerlySerializedAs("groupB")]
    [SerializeField] private List<VentController> crossGroupB = new List<VentController>(); // Cross 두 번째 그룹

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay; // 패턴 최초 시작 전 대기 시간
    [SerializeField, Min(0f)] private float initialWarningDuration = 1f; // 첫 분출 전 예고 시간
    [SerializeField, Min(0.01f)] private float activeDuration = 2.5f; // 각 그룹의 분출 유지 시간
    [FormerlySerializedAs("nextWarningLeadTime")]
    [SerializeField, Min(0f)] private float crossWarningLeadTime = 0.75f; // 현재 그룹 종료 전 다음 그룹 예고 시간
    [FormerlySerializedAs("singleGroupRecoveryDuration")]
    [SerializeField, Min(0f)] private float aloneRecoveryDuration = 1.5f; // Alone 다음 반복 전 휴식 시간

    private readonly HashSet<VentController> uniqueVents = new HashSet<VentController>(); // 중복 분출구 처리 방지
    private readonly List<VentController> runtimeGroupA = new List<VentController>(); // 실제 확보한 첫 그룹
    private readonly List<VentController> runtimeCrossGroupB = new List<VentController>(); // 실제 확보한 Cross 두 번째 그룹
    private Coroutine patternRoutine; // 현재 실행 중인 중앙 패턴 코루틴

    public bool IsRunning { get; private set; } // 패턴 실행 여부
    public int CurrentGroupIndex { get; private set; } = -1; // 현재 분출 중인 그룹 번호
    public VentPatternType PatternType => patternType; // 현재 선택한 패턴
    public IReadOnlyList<VentController> GroupA => groupA; // 첫 그룹 읽기 전용 제공
    public IReadOnlyList<VentController> CrossGroupB => crossGroupB; // Cross 두 번째 그룹 읽기 전용 제공

    // 오브젝트 활성화와 동시에 패턴 시작
    private void OnEnable()
    {
        StartPattern();
    }

    // 비활성화 시 패턴 정리와 분출구 제어권 반환
    private void OnDisable()
    {
        StopAndReset();
        ReleasePatternControl();
    }

    // 등록 그룹의 제어권 확보 후 선택 패턴 시작
    public bool StartPattern()
    {
        if (IsRunning || !isActiveAndEnabled)
            return false;

        ClaimPatternControl(); // 다른 패턴이 사용하지 않는 분출구만 확보

        // 첫 그룹이 비어 있으면 실행 취소
        if (!HasAnyVent(runtimeGroupA))
        {
            Debug.LogWarning($"{nameof(VentPattern)} on {name} has no vent in Group A.", this);
            ResetAllVents();
            ReleasePatternControl();
            return false;
        }

        // Cross 두 번째 그룹이 비어 있으면 실행 취소
        if (patternType == VentPatternType.Cross && !HasAnyVent(runtimeCrossGroupB))
        {
            Debug.LogWarning($"{nameof(VentPattern)} on {name} uses Cross but has no available vent in Cross Group B.", this);
            ResetAllVents();
            ReleasePatternControl();
            return false;
        }

        ResetAllVents(); // 시작 시 모든 그룹 상태 통일
        IsRunning = true;
        // 선택한 패턴 하나만 중앙 코루틴으로 실행
        patternRoutine = StartCoroutine(patternType == VentPatternType.Alone
            ? RunAlonePattern()
            : RunCrossPattern());
        return true;
    }

    // 중앙 코루틴 중지와 확보 그룹 초기화
    public void StopAndReset()
    {
        IsRunning = false;
        CurrentGroupIndex = -1;

        if (patternRoutine != null)
        {
            StopCoroutine(patternRoutine);
            patternRoutine = null;
        }

        ResetAllVents();
    }

    // 시작 지연 후 첫 그룹의 예고 분출 휴식 반복
    private IEnumerator RunAlonePattern()
    {
        CurrentGroupIndex = 0;
        yield return WaitForDuration(startDelay); // 다른 Alone 패턴과 시작 시점 분리
        if (!IsRunning) yield break;

        while (IsRunning)
        {
            SetGroupState(runtimeGroupA, VentState.Warning);
            yield return WaitForDuration(initialWarningDuration);
            if (!IsRunning) yield break;

            SetGroupState(runtimeGroupA, VentState.Active);
            yield return WaitForDuration(activeDuration);
            if (!IsRunning) yield break;

            SetGroupState(runtimeGroupA, VentState.Inactive);
            yield return WaitForDuration(aloneRecoveryDuration);
        }
    }

    // 시작 지연 후 두 그룹을 끊김 없이 교차 실행
    private IEnumerator RunCrossPattern()
    {
        CurrentGroupIndex = 0;
        yield return WaitForDuration(startDelay);
        if (!IsRunning) yield break;

        SetGroupState(runtimeGroupA, VentState.Warning); // 첫 그룹 최초 예고
        yield return WaitForDuration(initialWarningDuration);
        if (!IsRunning) yield break;

        SetGroupState(runtimeGroupA, VentState.Active); // 첫 그룹 최초 분출

        while (IsRunning)
        {
            // 예고 시간이 분출 시간보다 길어지지 않도록 제한
            float warningLead = Mathf.Min(crossWarningLeadTime, activeDuration); // 실제 적용할 다음 그룹 예고 시간
            // 현재 그룹만 분출하는 시간을 전체 분출 시간에서 예고 시간만큼 차감
            float activeOnlyDuration = Mathf.Max(0f, activeDuration - warningLead); // 현재 그룹만 활성인 시간
            yield return WaitForDuration(activeOnlyDuration);
            if (!IsRunning) yield break;

            int nextGroupIndex = CurrentGroupIndex == 0 ? 1 : 0; // 첫 그룹과 두 번째 그룹 번호 교대
            IReadOnlyList<VentController> currentGroup = GetGroup(CurrentGroupIndex); // 현재 분출 그룹
            IReadOnlyList<VentController> nextGroup = GetGroup(nextGroupIndex); // 다음 분출 그룹
            WarnNextCrossGroup(currentGroup, nextGroup);
            yield return WaitForDuration(warningLead);
            if (!IsRunning) yield break;

            TransitionCrossGroups(currentGroup, nextGroup); // 예고 종료와 동시에 그룹 교체
            CurrentGroupIndex = nextGroupIndex;
        }
    }

    // 양수 시간만 대기해 불필요한 한 프레임 지연 방지
    private static IEnumerator WaitForDuration(float duration)
    {
        if (duration > 0f)
            yield return new WaitForSeconds(duration);
    }

    // 현재 그룹과 겹치지 않는 다음 그룹 분출구만 예고
    private static void WarnNextCrossGroup(
        IReadOnlyList<VentController> currentGroup,
        IReadOnlyList<VentController> nextGroup)
    {
        HashSet<VentController> currentVents = BuildGroupSet(currentGroup); // 현재 그룹 빠른 포함 검사
        foreach (VentController vent in nextGroup)
        {
            if (vent != null && !currentVents.Contains(vent))
                vent.SetState(VentState.Warning);
        }
    }

    // 현재 그룹 정지 후 다음 그룹 분출 시작
    private static void TransitionCrossGroups(
        IReadOnlyList<VentController> currentGroup,
        IReadOnlyList<VentController> nextGroup)
    {
        HashSet<VentController> nextVents = BuildGroupSet(nextGroup); // 다음 그룹과 공유된 분출구 보호

        // 다음 그룹에도 포함된 분출구는 활성 상태 유지
        foreach (VentController vent in currentGroup)
        {
            if (vent != null && !nextVents.Contains(vent))
                vent.SetState(VentState.Inactive);
        }

        // 다음 그룹 전체에 새로운 분출 시작 명령
        foreach (VentController vent in nextGroup)
        {
            if (vent != null)
                vent.SetState(VentState.Active);
        }
    }

    // 그룹 전체에 같은 상태 명령 전달
    private void SetGroupState(IReadOnlyList<VentController> group, VentState state)
    {
        if (group == null)
            return;

        foreach (VentController vent in group)
        {
            if (vent != null)
                vent.SetState(state);
        }
    }

    // 그룹 목록을 중복 없는 빠른 검색 집합으로 변환
    private static HashSet<VentController> BuildGroupSet(IReadOnlyList<VentController> group)
    {
        HashSet<VentController> result = new HashSet<VentController>(); // 중복 제거된 그룹 집합
        if (group == null)
            return result;

        foreach (VentController vent in group)
        {
            if (vent != null)
                result.Add(vent);
        }

        return result;
    }

    // 그룹 번호에 맞는 실제 확보 목록 반환
    private IReadOnlyList<VentController> GetGroup(int groupIndex)
    {
        return groupIndex == 0 ? runtimeGroupA : runtimeCrossGroupB;
    }

    // 그룹 안에 유효한 분출구가 하나라도 있는지 확인
    private static bool HasAnyVent(IReadOnlyList<VentController> group)
    {
        if (group == null)
            return false;

        foreach (VentController vent in group)
        {
            if (vent != null)
                return true;
        }

        return false;
    }

    // 실제 확보한 모든 분출구 중복 없이 초기화
    private void ResetAllVents()
    {
        uniqueVents.Clear();
        ResetGroup(runtimeGroupA);
        ResetGroup(runtimeCrossGroupB);
    }

    // 이전 확보 목록 반환 후 현재 설정에 맞춰 다시 확보
    private void ClaimPatternControl()
    {
        ReleasePatternControl();
        ClaimGroup(groupA, runtimeGroupA); // Alone과 Cross 공통 첫 그룹 확보

        if (patternType == VentPatternType.Cross)
            ClaimGroup(crossGroupB, runtimeCrossGroupB);
    }

    // 설정 그룹에서 확보 가능한 분출구만 실제 목록에 추가
    private void ClaimGroup(
        IReadOnlyList<VentController> configuredGroup,
        List<VentController> runtimeGroup)
    {
        if (configuredGroup == null)
            return;

        foreach (VentController vent in configuredGroup)
        {
            if (vent != null && !runtimeGroup.Contains(vent) && vent.ClaimPatternControl(this))
                runtimeGroup.Add(vent);
        }
    }

    // 확보한 모든 분출구를 자동 Alone 상태로 반환
    private void ReleasePatternControl()
    {
        uniqueVents.Clear();
        ReleaseGroup(runtimeGroupA);
        ReleaseGroup(runtimeCrossGroupB);
        runtimeGroupA.Clear();
        runtimeCrossGroupB.Clear();
    }

    // 그룹 제어권을 중복 없이 반환
    private void ReleaseGroup(IReadOnlyList<VentController> group)
    {
        foreach (VentController vent in group)
        {
            if (vent != null && uniqueVents.Add(vent))
                vent.ReleasePatternControl(this);
        }
    }

    // 그룹 상태를 중복 없이 초기화
    private void ResetGroup(IReadOnlyList<VentController> group)
    {
        if (group == null)
            return;

        foreach (VentController vent in group)
        {
            if (vent != null && uniqueVents.Add(vent))
                vent.ResetVent();
        }
    }

    // Inspector 입력값 유효 범위 제한
    private void OnValidate()
    {
        startDelay = Mathf.Max(0f, startDelay);
        initialWarningDuration = Mathf.Max(0f, initialWarningDuration);
        activeDuration = Mathf.Max(0.01f, activeDuration);
        crossWarningLeadTime = Mathf.Clamp(crossWarningLeadTime, 0f, activeDuration);
        aloneRecoveryDuration = Mathf.Max(0f, aloneRecoveryDuration);
    }
}
