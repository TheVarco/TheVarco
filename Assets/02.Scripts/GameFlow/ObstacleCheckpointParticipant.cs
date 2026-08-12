using UnityEngine;

namespace Varco.GameFlow
{
    // Vent와 Rock 패턴 상태를 한 번에 저장하는 참가자
    [DisallowMultipleComponent]
    public sealed class ObstacleCheckpointParticipant : CheckpointParticipantBehaviour
    {
        // 실행 상태와 그룹을 저장할 패턴 목록
        private ObstaclePatternBase[] patterns;
        // 현재 단계 상태를 저장할 Vent 목록
        private VentController[] vents;
        // 활성 낙석 제거와 풀 초기화 대상
        private RockSpawner[] rockSpawners;
        // 전체 물리 복원이 끝난 뒤 다시 켤 장애물 상태
        private ObstacleState pendingRestoreState;

        // 잠수함 이후 적보다 먼저 복원
        public override int RestoreOrder => 20;

        // 현재 씬의 허용 장애물 참조 수집
        public void InitializeFromScene()
        {
            patterns = FindObjectsByType<ObstaclePatternBase>(FindObjectsSortMode.None);
            vents = FindObjectsByType<VentController>(FindObjectsSortMode.None);
            rockSpawners = FindObjectsByType<RockSpawner>(FindObjectsSortMode.None);
        }

        // 패턴 실행 여부와 그룹 및 Vent 상태 캡처
        public override object CaptureCheckpointState()
        {
            EnsureReferences();
            // 각 패턴의 실행 여부와 현재 그룹 저장
            PatternState[] patternStates = new PatternState[patterns.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                patternStates[i] = new PatternState
                {
                    WasRunning = patterns[i] != null && patterns[i].IsRunning,
                    GroupIndex = patterns[i] != null ? patterns[i].CurrentGroupIndex : -1
                };
            }

            // 각 Vent의 현재 상태 저장
            VentState[] ventStates = new VentState[vents.Length];
            for (int i = 0; i < vents.Length; i++)
                ventStates[i] = vents[i] != null ? vents[i].CurrentState : VentState.Inactive;

            return new ObstacleState
            {
                Patterns = patternStates,
                Vents = ventStates
            };
        }

        // 세부 타이머를 버리고 저장된 실행 상태를 재개 대기열에 저장
        public override void RestoreCheckpointState(object state)
        {
            if (state is not ObstacleState obstacleState)
                return;

            EnsureReferences();
            // 활성 낙석과 스포너 내부 상태 초기화
            foreach (RockSpawner spawner in rockSpawners)
                spawner?.ResetRockSpawner();

            pendingRestoreState = obstacleState;
        }

        // 잠수함과 플레이어 위치 복원이 끝난 뒤 저장된 장애물 상태 재개
        private void ApplyPendingRestoreState()
        {
            if (pendingRestoreState == null)
                return;

            // 실행 중이던 패턴은 저장 그룹부터 새 타이머로 시작
            int patternCount = Mathf.Min(
                patterns.Length,
                pendingRestoreState.Patterns?.Length ?? 0);
            for (int i = 0; i < patternCount; i++)
            {
                ObstaclePatternBase pattern = patterns[i];
                if (pattern == null)
                    continue;

                PatternState patternState = pendingRestoreState.Patterns[i];
                if (patternState.WasRunning)
                    pattern.RestartPatternAtGroup(Mathf.Max(0, patternState.GroupIndex));
                else
                    pattern.StopAndReset();
            }

            // Vent는 저장된 단계 상태로 복원
            int ventCount = Mathf.Min(
                vents.Length,
                pendingRestoreState.Vents?.Length ?? 0);
            for (int i = 0; i < ventCount; i++)
                vents[i]?.SetState(pendingRestoreState.Vents[i]);

            pendingRestoreState = null;
        }

        // 게임 정지 시 모든 장애물 실행과 활성 낙석 초기화
        public override void SetGameplayEnabled(bool enabled)
        {
            if (enabled)
            {
                ApplyPendingRestoreState();
                return;
            }

            EnsureReferences();
            pendingRestoreState = null;
            foreach (ObstaclePatternBase pattern in patterns)
                pattern?.StopAndReset();
            foreach (VentController vent in vents)
                vent?.ResetVent();
            foreach (RockSpawner spawner in rockSpawners)
                spawner?.ResetRockSpawner();
        }

        // 씬 참조가 없는 경우 지연 초기화
        private void EnsureReferences()
        {
            if (patterns == null || vents == null || rockSpawners == null)
                InitializeFromScene();
        }

        // 장애물 전체 복원 데이터
        private sealed class ObstacleState
        {
            public PatternState[] Patterns;
            public VentState[] Vents;
        }

        // 개별 패턴 복원 데이터
        private struct PatternState
        {
            public bool WasRunning;
            public int GroupIndex;
        }
    }
}
