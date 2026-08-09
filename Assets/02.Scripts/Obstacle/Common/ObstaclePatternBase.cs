using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 장애물 종류와 관계없이 Alone과 Cross 시간표를 실행하는 공용 기반 클래스
public abstract class ObstaclePatternBase : MonoBehaviour
{
    private readonly HashSet<IPatternTarget> uniqueTargets = new HashSet<IPatternTarget>(); // 그룹 간 중복 처리를 막는 임시 집합
    private readonly List<IPatternTarget> runtimeGroupA = new List<IPatternTarget>(); // 실제 제어권을 확보한 첫 그룹
    private readonly List<IPatternTarget> runtimeCrossGroupB = new List<IPatternTarget>(); // 실제 제어권을 확보한 Cross 두 번째 그룹
    private Coroutine patternRoutine; // 현재 실행 중인 공용 시간표 코루틴

    public bool IsRunning { get; private set; } // 공용 시간표 실행 여부
    public int CurrentGroupIndex { get; private set; } = -1; // 현재 활성 명령을 받은 그룹 번호

    protected abstract bool UsesCrossPattern { get; } // Cross 시간표 사용 여부
    protected abstract IReadOnlyList<IPatternTarget> ConfiguredGroupA { get; } // Inspector에서 설정한 첫 그룹
    protected abstract IReadOnlyList<IPatternTarget> ConfiguredCrossGroupB { get; } // Inspector에서 설정한 Cross 두 번째 그룹
    protected abstract float PatternStartDelay { get; } // 최초 예고 전 시작 지연시간
    protected abstract float InitialWarningDuration { get; } // 첫 활성 명령 전 예고시간
    protected abstract float ActiveDuration { get; } // 현재 그룹 활성 후 다음 전환까지의 시간
    protected abstract float CrossWarningLeadTime { get; } // 현재 그룹 종료 전 다음 그룹 예고시간
    protected abstract float AloneRecoveryDuration { get; } // Alone 비활성 후 다음 예고 전 휴식시간
    protected abstract string PatternDisplayName { get; } // 유효성 경고에 표시할 패턴 이름
    protected abstract string TargetDisplayName { get; } // 유효성 경고에 표시할 대상 이름

    // 오브젝트 활성화와 동시에 등록된 패턴 시작
    protected virtual void OnEnable()
    {
        StartPattern();
    }

    // 비활성화 시 코루틴과 대상 상태 정리 후 제어권 반환
    protected virtual void OnDisable()
    {
        StopAndReset();
        ReleasePatternControl();
    }

    // 설정 그룹에서 제어 가능한 대상만 확보해 선택된 시간표 시작
    public bool StartPattern()
    {
        return StartPatternAtGroup(0);
    }

    // 저장된 그룹부터 새 타이머로 패턴 재시작
    public bool RestartPatternAtGroup(int groupIndex)
    {
        StopAndReset();
        return StartPatternAtGroup(groupIndex);
    }

    private bool StartPatternAtGroup(int groupIndex)
    {
        if (IsRunning || !isActiveAndEnabled)
            return false;

        ClaimPatternControl();

        // 첫 그룹 확보에 실패하면 빈 패턴 실행을 막고 자동 Alone 제어권 복구
        if (!HasAnyTarget(runtimeGroupA))
        {
            Debug.LogWarning($"{PatternDisplayName} on {name} has no available {TargetDisplayName} in Group A", this);
            ResetAllTargets();
            ReleasePatternControl();
            return false;
        }

        // Cross 두 번째 그룹 확보에 실패하면 한 그룹만 계속 활성화되는 잘못된 실행 차단
        if (UsesCrossPattern && !HasAnyTarget(runtimeCrossGroupB))
        {
            Debug.LogWarning($"{PatternDisplayName} on {name} uses Cross but has no available {TargetDisplayName} in Cross Group B", this);
            ResetAllTargets();
            ReleasePatternControl();
            return false;
        }

        ResetAllTargets();
        IsRunning = true;
        patternRoutine = StartCoroutine(UsesCrossPattern
            ? RunCrossPattern(Mathf.Clamp(groupIndex, 0, 1))
            : RunAlonePattern());
        return true;
    }

    // 공용 코루틴을 중지하고 확보한 모든 대상을 초기 상태로 복구
    public void StopAndReset()
    {
        IsRunning = false;
        CurrentGroupIndex = -1;

        if (patternRoutine != null)
        {
            StopCoroutine(patternRoutine);
            patternRoutine = null;
        }

        ResetAllTargets();
    }

    // 시작 지연 후 첫 그룹의 예고 활성 비활성 휴식을 반복
    private IEnumerator RunAlonePattern()
    {
        CurrentGroupIndex = 0;
        yield return WaitForDuration(GetSafeStartDelay());
        if (!IsRunning)
            yield break;

        while (IsRunning)
        {
            SetGroupWarning(runtimeGroupA);
            yield return WaitForDuration(GetSafeWarningDuration());
            if (!IsRunning)
                yield break;

            SetGroupActive(runtimeGroupA);
            yield return WaitForDuration(GetSafeActiveDuration());
            if (!IsRunning)
                yield break;

            SetGroupInactive(runtimeGroupA);
            yield return WaitForDuration(GetSafeRecoveryDuration());
        }
    }

    // 시작 지연 후 두 그룹을 사전 예고시간에 맞춰 교대로 활성화
    private IEnumerator RunCrossPattern(int initialGroupIndex)
    {
        CurrentGroupIndex = initialGroupIndex;
        yield return WaitForDuration(GetSafeStartDelay());
        if (!IsRunning)
            yield break;

        IReadOnlyList<IPatternTarget> initialGroup = GetRuntimeGroup(CurrentGroupIndex);
        SetGroupWarning(initialGroup);
        yield return WaitForDuration(GetSafeWarningDuration());
        if (!IsRunning)
            yield break;

        SetGroupActive(initialGroup);

        while (IsRunning)
        {
            float activeDuration = GetSafeActiveDuration(); // 현재 그룹의 전체 차례 시간
            float warningLead = Mathf.Clamp(CrossWarningLeadTime, 0f, activeDuration); // 전체 차례 안에 들어오도록 제한한 다음 그룹 예고시간
            float activeOnlyDuration = Mathf.Max(0f, activeDuration - warningLead); // 다음 예고가 시작되기 전 현재 그룹만 작동하는 시간

            yield return WaitForDuration(activeOnlyDuration);
            if (!IsRunning)
                yield break;

            int nextGroupIndex = CurrentGroupIndex == 0 ? 1 : 0; // 두 그룹 번호를 매 차례 반대로 선택
            IReadOnlyList<IPatternTarget> currentGroup = GetRuntimeGroup(CurrentGroupIndex); // 현재 활성 그룹 참조
            IReadOnlyList<IPatternTarget> nextGroup = GetRuntimeGroup(nextGroupIndex); // 다음 활성 그룹 참조

            WarnNextCrossGroup(currentGroup, nextGroup);
            yield return WaitForDuration(warningLead);
            if (!IsRunning)
                yield break;

            TransitionCrossGroups(currentGroup, nextGroup);
            CurrentGroupIndex = nextGroupIndex;
        }
    }

    // 양수 시간만 대기해 0초 설정에서 불필요한 추가 프레임 지연 방지
    private static IEnumerator WaitForDuration(float duration)
    {
        if (duration > 0f)
            yield return new WaitForSeconds(duration);
    }

    // 현재 그룹과 겹치지 않는 다음 그룹 대상만 예고 상태로 전환
    private static void WarnNextCrossGroup(
        IReadOnlyList<IPatternTarget> currentGroup,
        IReadOnlyList<IPatternTarget> nextGroup)
    {
        HashSet<IPatternTarget> currentTargets = BuildGroupSet(currentGroup); // 현재 그룹의 빠른 포함 검사용 집합

        foreach (IPatternTarget target in nextGroup)
        {
            if (IsValidTarget(target) && !currentTargets.Contains(target))
                target.EnterPatternWarning();
        }
    }

    // 현재 그룹을 정지하고 다음 그룹을 같은 프레임에 활성화
    private static void TransitionCrossGroups(
        IReadOnlyList<IPatternTarget> currentGroup,
        IReadOnlyList<IPatternTarget> nextGroup)
    {
        HashSet<IPatternTarget> nextTargets = BuildGroupSet(nextGroup); // 두 그룹에 함께 들어간 대상의 불필요한 비활성 방지 집합

        foreach (IPatternTarget target in currentGroup)
        {
            if (IsValidTarget(target) && !nextTargets.Contains(target))
                target.EnterPatternInactive();
        }

        foreach (IPatternTarget target in nextGroup)
        {
            if (IsValidTarget(target))
                target.EnterPatternActive();
        }
    }

    // 그룹 전체에 예고 명령을 목록 순서대로 전달
    private static void SetGroupWarning(IReadOnlyList<IPatternTarget> group)
    {
        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target))
                target.EnterPatternWarning();
        }
    }

    // 그룹 전체에 활성 명령을 같은 프레임 안에서 전달
    private static void SetGroupActive(IReadOnlyList<IPatternTarget> group)
    {
        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target))
                target.EnterPatternActive();
        }
    }

    // 그룹 전체에 비활성 명령을 목록 순서대로 전달
    private static void SetGroupInactive(IReadOnlyList<IPatternTarget> group)
    {
        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target))
                target.EnterPatternInactive();
        }
    }

    // 그룹 목록을 중복 없는 빠른 검색 집합으로 변환
    private static HashSet<IPatternTarget> BuildGroupSet(IReadOnlyList<IPatternTarget> group)
    {
        HashSet<IPatternTarget> result = new HashSet<IPatternTarget>(); // 중복을 제거해 만든 검색 결과

        if (group == null)
            return result;

        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target))
                result.Add(target);
        }

        return result;
    }

    // 번호에 맞는 실제 제어 대상 그룹 반환
    private IReadOnlyList<IPatternTarget> GetRuntimeGroup(int groupIndex)
    {
        return groupIndex == 0 ? runtimeGroupA : runtimeCrossGroupB;
    }

    // 그룹 안에 파괴되지 않은 대상이 하나라도 있는지 확인
    private static bool HasAnyTarget(IReadOnlyList<IPatternTarget> group)
    {
        if (group == null)
            return false;

        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target))
                return true;
        }

        return false;
    }

    // Unity 오브젝트가 파괴된 인터페이스 참조까지 함께 제외
    private static bool IsValidTarget(IPatternTarget target)
    {
        return target != null && target.PatternTargetObject != null;
    }

    // 이전 확보 목록을 반환하고 현재 Inspector 설정을 기준으로 제어권 다시 확보
    private void ClaimPatternControl()
    {
        ReleasePatternControl();
        ClaimGroup(ConfiguredGroupA, runtimeGroupA);

        if (UsesCrossPattern)
            ClaimGroup(ConfiguredCrossGroupB, runtimeCrossGroupB);
    }

    // 설정 그룹에서 중복되지 않고 제어권 확보에 성공한 대상만 실행 목록에 추가
    private void ClaimGroup(
        IReadOnlyList<IPatternTarget> configuredGroup,
        List<IPatternTarget> runtimeGroup)
    {
        if (configuredGroup == null)
            return;

        foreach (IPatternTarget target in configuredGroup)
        {
            if (IsValidTarget(target) && !runtimeGroup.Contains(target) && target.ClaimPatternControl(this))
                runtimeGroup.Add(target);
        }
    }

    // 확보한 모든 대상의 제어권을 중복 없이 반환
    private void ReleasePatternControl()
    {
        uniqueTargets.Clear();
        ReleaseGroup(runtimeGroupA);
        ReleaseGroup(runtimeCrossGroupB);
        runtimeGroupA.Clear();
        runtimeCrossGroupB.Clear();
    }

    // 같은 대상이 두 그룹에 있어도 제어권 반환을 한 번만 실행
    private void ReleaseGroup(IReadOnlyList<IPatternTarget> group)
    {
        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target) && uniqueTargets.Add(target))
                target.ReleasePatternControl(this);
        }
    }

    // 확보한 모든 대상 상태를 중복 없이 완전 초기화
    private void ResetAllTargets()
    {
        uniqueTargets.Clear();
        ResetGroup(runtimeGroupA);
        ResetGroup(runtimeCrossGroupB);
    }

    // 같은 대상이 두 그룹에 있어도 초기화를 한 번만 실행
    private void ResetGroup(IReadOnlyList<IPatternTarget> group)
    {
        foreach (IPatternTarget target in group)
        {
            if (IsValidTarget(target) && uniqueTargets.Add(target))
                target.ResetPatternTarget();
        }
    }

    // 음수 시작 지연시간을 실행 직전에도 0으로 보정
    private float GetSafeStartDelay()
    {
        return Mathf.Max(0f, PatternStartDelay);
    }

    // 음수 예고시간을 실행 직전에도 0으로 보정
    private float GetSafeWarningDuration()
    {
        return Mathf.Max(0f, InitialWarningDuration);
    }

    // 무한 0초 반복을 막기 위해 활성시간에 최소값 적용
    private float GetSafeActiveDuration()
    {
        return Mathf.Max(0.01f, ActiveDuration);
    }

    // 음수 회복시간을 실행 직전에도 0으로 보정
    private float GetSafeRecoveryDuration()
    {
        return Mathf.Max(0f, AloneRecoveryDuration);
    }
}
