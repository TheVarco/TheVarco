using System.Collections.Generic;
using UnityEngine;

namespace Varco.GameFlow
{
    // 특정 구역에서 캡처된 전체 참가자 상태
    public sealed class CheckpointSnapshot
    {
        public CheckpointSnapshot(int zone, Dictionary<string, object> participantStates)
        {
            Zone = zone;
            ParticipantStates = participantStates;
        }

        public int Zone { get; } // 캡처 구역
        public IReadOnlyDictionary<string, object> ParticipantStates { get; } // 참가자별 상태
    }

    // 메모리 기반 체크포인트 캡처와 복원 실행기
    public sealed class CheckpointSnapshotService
    {
        // 복원 작업에 사용할 임시 참가자 목록
        private readonly List<ICheckpointParticipant> participants = new();
        // 캡처 중 고유 키 중복 검사 저장소
        private readonly HashSet<string> participantIds = new();

        // 현재 세션의 최신 체크포인트
        public CheckpointSnapshot CurrentSnapshot { get; private set; }

        // 모든 활성 참가자의 현재 상태 캡처
        public bool Capture(int zone, IReadOnlyList<IPlayerCheckpointParticipant> players)
        {
            RefreshParticipants(players);
            Dictionary<string, object> states = new(participants.Count);

            foreach (ICheckpointParticipant participant in participants)
            {
                string id = participant.CheckpointId;
                // 빈 키와 중복 키는 잘못된 복원 방지를 위해 캡처 중단
                if (string.IsNullOrWhiteSpace(id) || !participantIds.Add(id))
                {
                    Debug.LogError($"[GameFlow] Duplicate or empty checkpoint id: {id}");
                    participantIds.Clear();
                    return false;
                }

                states[id] = participant.CaptureCheckpointState();
            }

            participantIds.Clear();
            CurrentSnapshot = new CheckpointSnapshot(zone, states);
            return true;
        }

        // 결과 화면과 복원 중 입력과 시뮬레이션 제어
        public void SetGameplayEnabled(bool enabled, IReadOnlyList<IPlayerCheckpointParticipant> players)
        {
            RefreshParticipants(players);
            foreach (ICheckpointParticipant participant in participants)
                participant.SetGameplayEnabled(enabled);
        }

        // 준비 복원 완료 순서로 스냅샷 적용
        public bool Restore(IReadOnlyList<IPlayerCheckpointParticipant> players)
        {
            if (CurrentSnapshot == null)
                return false;

            RefreshParticipants(players);
            // 낮은 순서의 기반 오브젝트부터 복원
            participants.Sort((left, right) => left.RestoreOrder.CompareTo(right.RestoreOrder));

            // 좌석과 부착 관계 선해제
            foreach (ICheckpointParticipant participant in participants)
                participant.PrepareForCheckpointRestore();

            // 참가자 키와 캡처 데이터 매칭
            foreach (ICheckpointParticipant participant in participants)
            {
                if (CurrentSnapshot.ParticipantStates.TryGetValue(participant.CheckpointId, out object state))
                    participant.RestoreCheckpointState(state);
                else
                    Debug.LogWarning($"[GameFlow] No checkpoint state for {participant.CheckpointId}");
            }

            // 좌석과 부착 관계 후연결
            foreach (ICheckpointParticipant participant in participants)
                participant.CompleteCheckpointRestore();

            // Transform 변경 결과를 물리 월드에 즉시 반영
            Physics.SyncTransforms();
            return true;
        }

        // 컴포넌트 참가자와 네트워크 플레이어 목록 병합
        private void RefreshParticipants(IReadOnlyList<IPlayerCheckpointParticipant> players)
        {
            CheckpointParticipantRegistry.CopyTo(participants);
            if (players == null)
                return;

            foreach (IPlayerCheckpointParticipant player in players)
            {
                if (player != null && !participants.Contains(player))
                    participants.Add(player);
            }
        }
    }
}
