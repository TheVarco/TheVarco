using System;

namespace Varco.GameFlow
{
    // 게임 전체 진행 상태
    public enum GameFlowState
    {
        // 정상 플레이 상태
        Playing,
        // 실패 결과 상태
        Failed,
        // 성공 결과 상태
        Succeeded,
        // 체크포인트 복원 상태
        Restoring
    }

    // 실패 화면에 표시할 실패 원인
    public enum GameFailureReason
    {
        // 실패 원인 없음
        None,
        // 잠수함 체력 소진
        SubmarineDestroyed,
        // 모든 참가 플레이어 다운
        PartyWiped
    }

    // 같은 프레임에 발생한 결과 후보
    // 숫자가 클수록 높은 판정 우선순위
    public enum GameFlowOutcomeCandidate
    {
        // 대기 결과
        None = 0,
        // 출구 도달 결과
        Success = 1,
        // 파티 전멸 결과
        PartyWiped = 2,
        // 잠수함 파괴 결과
        SubmarineDestroyed = 3
    }

    // 네트워크로 전파할 최소 게임 흐름 데이터
    [Serializable]
    public readonly struct GameFlowReplicatedState
    {
        public GameFlowReplicatedState(
            GameFlowState state,
            GameFailureReason failureReason,
            int checkpointZone,
            int revision)
        {
            State = state;
            FailureReason = failureReason;
            CheckpointZone = checkpointZone;
            Revision = revision;
        }

        public GameFlowState State { get; } // 현재 게임 상태
        public GameFailureReason FailureReason { get; } // 현재 실패 원인
        public int CheckpointZone { get; } // 현재 체크포인트 구역
        public int Revision { get; } // 오래된 수신 상태 차단용 버전
    }

    // 동시 결과의 우선순위 결정기
    public static class GameFlowOutcomeResolver
    {
        // 기존 후보와 새 후보 중 높은 우선순위 선택
        public static GameFlowOutcomeCandidate Choose(
            GameFlowOutcomeCandidate current,
            GameFlowOutcomeCandidate incoming)
        {
            return incoming > current ? incoming : current;
        }

        // 결과 후보를 실패 화면용 원인으로 변환
        public static GameFailureReason ToFailureReason(GameFlowOutcomeCandidate candidate)
        {
            return candidate switch
            {
                GameFlowOutcomeCandidate.SubmarineDestroyed => GameFailureReason.SubmarineDestroyed,
                GameFlowOutcomeCandidate.PartyWiped => GameFailureReason.PartyWiped,
                _ => GameFailureReason.None
            };
        }
    }

    // 체크포인트 구역 전진 규칙
    public static class CheckpointZoneProgression
    {
        // 최초 체크포인트 구역
        public const int FirstZone = 1;
        // 최종 체크포인트 구역
        public const int LastZone = 7;

        // 유효 범위이며 현재 구역보다 앞인지 확인
        public static bool CanAdvance(int currentZone, int reachedZone)
        {
            return reachedZone >= FirstZone
                && reachedZone <= LastZone
                && reachedZone > currentZone;
        }
    }
}
