using System;
using System.Collections.Generic;
using UnityEngine;

namespace Varco.GameFlow
{
    // 체크포인트 저장과 복원에 참여하는 공통 계약
    public interface ICheckpointParticipant
    {
        // 스냅샷 데이터 매칭용 세션 고유 키
        string CheckpointId { get; }
        // 낮은 값부터 실행되는 복원 순서
        int RestoreOrder { get; }
        // 현재 런타임 상태 캡처
        object CaptureCheckpointState();
        // 좌석과 부착 상태 해제 단계
        void PrepareForCheckpointRestore();
        // 캡처 데이터 적용 단계
        void RestoreCheckpointState(object state);
        // 좌석과 부착 상태 재연결 단계
        void CompleteCheckpointRestore();
        // 입력과 시뮬레이션 활성 제어
        void SetGameplayEnabled(bool enabled);
    }

    // Transform이나 점유 관계를 변경하기 전에 스냅샷을 검증하는 선택 계약.
    // 구현하지 않은 기존 참가자는 이전과 동일하게 복원된다.
    public interface ICheckpointRestoreValidator
    {
        bool ValidateCheckpointState(object state, out string error);
    }

    // void 복원 계약을 유지하면서 참가자 내부 복원 실패를 서비스에 알리는 선택 계약.
    public interface ICheckpointRestoreStatus
    {
        bool CheckpointRestoreSucceeded { get; }
        string CheckpointRestoreError { get; }
    }

    // 팀 네트워크 플레이어가 구현할 체크포인트 계약
    public interface IPlayerCheckpointParticipant : ICheckpointParticipant
    {
        // 네트워크 전 구간에서 유지되는 플레이어 키
        string PlayerKey { get; }
        // 전원 다운 판정용 상태
        bool IsDowned { get; }
        // 승인된 플레이어 스냅샷 범위 지원 여부. Carryable 핫바는 items:session이 담당한다.
        bool SupportsCompleteSnapshot { get; }
        // 잠수함 내부의 지정 위치를 이번 복원 위치로 요청
        bool TrySetCheckpointSpawnPose(Vector3 position, Quaternion rotation);
    }

    // 게임 흐름 코어와 팀 네트워크 계층의 경계
    public interface IGameFlowNetworkBridge
    {
        // 플레이어 목록과 상태 송수신 준비 여부
        bool IsReady { get; }
        // 현재 인스턴스의 판정 권한 여부
        bool IsAuthority { get; }
        // 멀티플레이 세션 여부
        bool IsMultiplayer { get; }
        // 현재 참가 플레이어 목록
        IReadOnlyList<IPlayerCheckpointParticipant> Players { get; }

        // 원격 상태 수신 이벤트
        event Action<GameFlowReplicatedState> ReplicatedStateReceived;

        // 권한 인스턴스의 상태 전파 요청
        void PublishState(GameFlowReplicatedState state);
        // 시작 화면 이동 전파 요청
        void ReturnToStartScene();

        // 네트워크 식별자를 이용한 엔티티 생성 요청
        bool TrySpawnCheckpointEntity(
            string checkpointEntityKey,
            Vector3 position,
            Quaternion rotation,
            out GameObject entity);
        // 네트워크 식별자를 이용한 엔티티 제거 요청
        bool TryDespawnCheckpointEntity(string checkpointEntityKey, GameObject entity);
    }

    // 컴포넌트 기반 참가자의 등록 수명주기 제공
    public abstract class CheckpointParticipantBehaviour : MonoBehaviour, ICheckpointParticipant
    {
        // 인스펙터 또는 부트스트랩에서 설정하는 고유 키
        [SerializeField] private string checkpointId;

        public string CheckpointId
        {
            get
            {
                // 별도 키가 없을 때 현재 세션용 임시 키 생성
                if (string.IsNullOrWhiteSpace(checkpointId))
                    checkpointId = $"{GetType().Name}:{gameObject.scene.handle}:{GetInstanceID()}";
                return checkpointId;
            }
        }

        // 기본 복원 순서
        public virtual int RestoreOrder => 0;

        // 런타임 부트스트랩에서 안정적인 키 설정
        public void InitializeCheckpointId(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                checkpointId = value;
        }

        // 활성 참가자 자동 등록
        protected virtual void OnEnable()
        {
            CheckpointParticipantRegistry.Register(this);
        }

        // 비활성 참가자 자동 해제
        protected virtual void OnDisable()
        {
            CheckpointParticipantRegistry.Unregister(this);
        }

        public abstract object CaptureCheckpointState();
        public virtual void PrepareForCheckpointRestore() { }
        public abstract void RestoreCheckpointState(object state);
        public virtual void CompleteCheckpointRestore() { }
        public virtual void SetGameplayEnabled(bool enabled) { }
    }

    // 현재 활성 체크포인트 참가자 저장소
    public static class CheckpointParticipantRegistry
    {
        private static readonly HashSet<ICheckpointParticipant> Participants = new();

        // 중복 없이 참가자 등록
        public static void Register(ICheckpointParticipant participant)
        {
            if (participant != null)
                Participants.Add(participant);
        }

        // 비활성 참가자 해제
        public static void Unregister(ICheckpointParticipant participant)
        {
            if (participant != null)
                Participants.Remove(participant);
        }

        // 파괴된 참가자를 제거하고 현재 목록 복사
        public static void CopyTo(List<ICheckpointParticipant> target)
        {
            target.Clear();
            Participants.RemoveWhere(IsUnavailable);
            target.AddRange(Participants);
        }

        // 일반 null과 Unity 파괴 객체 판정
        private static bool IsUnavailable(ICheckpointParticipant participant)
        {
            return participant == null
                || participant is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}
