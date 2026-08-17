using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Varco.GameFlow
{
    // Fusion Runner와 게임 흐름 코어를 연결하는 네트워크 브리지
    public sealed class FusionGameFlowNetworkBridge : MonoBehaviour,
        IGameFlowNetworkBridge,
        INetworkRunnerCallbacks
    {
        // 게임 상태 패킷을 구분하는 고정 키
        private static readonly ReliableKey StateKey =
            ReliableKey.FromInts(0x56415243, 0x4F464C4F, 0x57535441, 0x54450001);

        // 시작 화면 이동 패킷을 구분하는 고정 키
        private static readonly ReliableKey ReturnKey =
            ReliableKey.FromInts(0x56415243, 0x4F464C4F, 0x57524554, 0x55524E01);

        // 현재 참가자 목록 캐시
        private readonly List<IPlayerCheckpointParticipant> players = new();

        // 연결된 Fusion Runner
        private NetworkRunner runner;

        // 지연 참가자에게 전송할 마지막 게임 상태
        private GameFlowReplicatedState lastPublishedState;

        // 전파할 상태 존재 여부
        private bool hasPublishedState;

        // 중복 시작 화면 이동 방지
        private bool returningToStart;

        // Runner가 실행되고 있는지 확인
        public bool IsReady => runner != null && runner.IsRunning;

        // Host 또는 Single 인스턴스에 판정 권한 부여
        public bool IsAuthority => IsReady &&
            (runner.GameMode == GameMode.Single || runner.IsServer);

        // Single 이외 Fusion 세션 여부
        public bool IsMultiplayer => IsReady && runner.GameMode != GameMode.Single;

        // PlayerObject에 연결된 체크포인트 참가자 목록
        public IReadOnlyList<IPlayerCheckpointParticipant> Players
        {
            get
            {
                RefreshPlayers();
                return players;
            }
        }

        // 원격 게임 상태 수신 이벤트
        public event Action<GameFlowReplicatedState> ReplicatedStateReceived;

        // 실행 중 Runner가 생길 때까지 연결 시도
        private void Update()
        {
            if (runner == null || !runner.IsRunning)
                AttachRunningRunner();
        }

        // Runner 콜백 연결 해제
        private void OnDestroy()
        {
            if (runner != null)
                runner.RemoveCallbacks(this);
        }

        // 권한 인스턴스의 상태를 모든 원격 참가자에게 전송
        public void PublishState(GameFlowReplicatedState state)
        {
            lastPublishedState = state;
            hasPublishedState = true;

            if (IsAuthority)
                BroadcastState(state);
        }

        // 모든 참가자에게 시작 화면 이동 명령 전송
        public void ReturnToStartScene()
        {
            if (!IsAuthority || returningToStart)
                return;

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player != runner.LocalPlayer)
                    runner.SendReliableDataToPlayer(player, ReturnKey, new byte[] { 1 });
            }

            StartCoroutine(ReturnToStartRoutine());
        }

        // 프리팹 키 변환은 팀 네트워크 스폰 계층에서 구현
        public bool TrySpawnCheckpointEntity(
            string checkpointEntityKey,
            Vector3 position,
            Quaternion rotation,
            out GameObject entity)
        {
            entity = null;
            Debug.LogWarning(
                $"[GameFlow] No network prefab mapping for {checkpointEntityKey}",
                this);
            return false;
        }

        // NetworkObject는 Runner를 통해 제거하고 로컬 오브젝트는 직접 제거
        public bool TryDespawnCheckpointEntity(string checkpointEntityKey, GameObject entity)
        {
            if (entity == null)
                return true;

            NetworkObject networkObject = entity.GetComponentInParent<NetworkObject>();
            if (networkObject != null)
            {
                if (!IsAuthority)
                    return false;

                runner.Despawn(networkObject);
                return true;
            }

            Destroy(entity);
            return true;
        }

        // 현재 실행 중인 Runner를 찾아 콜백 연결
        private void AttachRunningRunner()
        {
            NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
            foreach (NetworkRunner candidate in runners)
            {
                if (candidate == null || !candidate.IsRunning)
                    continue;

                if (runner != null && runner != candidate)
                    runner.RemoveCallbacks(this);

                runner = candidate;
                runner.AddCallbacks(this);

                if (IsAuthority && hasPublishedState)
                    BroadcastState(lastPublishedState);
                return;
            }
        }

        // PlayerObject에서 참가자 컴포넌트 수집
        private void RefreshPlayers()
        {
            players.Clear();
            if (!IsReady)
                return;

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (!runner.TryGetPlayerObject(player, out NetworkObject playerObject))
                    continue;

                FusionPlayerCheckpointParticipant participant =
                    playerObject.GetComponent<FusionPlayerCheckpointParticipant>();
                if (participant != null)
                    players.Add(participant);
            }
        }

        // 현재 상태를 모든 원격 참가자에게 전송
        private void BroadcastState(GameFlowReplicatedState state)
        {
            byte[] data = SerializeState(state);
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player != runner.LocalPlayer)
                    runner.SendReliableDataToPlayer(player, StateKey, data);
            }
        }

        // 지연 참가자 한 명에게 현재 상태 전송
        private void SendCurrentState(PlayerRef player)
        {
            if (!IsAuthority || !hasPublishedState || player == runner.LocalPlayer)
                return;

            runner.SendReliableDataToPlayer(player, StateKey, SerializeState(lastPublishedState));
        }

        // 게임 상태를 고정 크기 바이트 데이터로 변환
        private static byte[] SerializeState(GameFlowReplicatedState state)
        {
            byte[] data = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes((int)state.State), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)state.FailureReason), 0, data, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(state.CheckpointZone), 0, data, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(state.Revision), 0, data, 12, 4);
            return data;
        }

        // 수신 바이트 데이터를 게임 상태로 변환
        private static bool TryDeserializeState(
            ArraySegment<byte> data,
            out GameFlowReplicatedState state)
        {
            state = default;
            if (data.Array == null || data.Count < 16)
                return false;

            int offset = data.Offset;
            state = new GameFlowReplicatedState(
                (GameFlowState)BitConverter.ToInt32(data.Array, offset),
                (GameFailureReason)BitConverter.ToInt32(data.Array, offset + 4),
                BitConverter.ToInt32(data.Array, offset + 8),
                BitConverter.ToInt32(data.Array, offset + 12));
            return true;
        }

        // Runner에 종료를 요청하고 기다리지 않고 시작 화면 로드
        private IEnumerator ReturnToStartRoutine()
        {
            returningToStart = true;
            yield return new WaitForSecondsRealtime(0.2f);

            NetworkRunner closingRunner = runner;
            if (closingRunner != null && closingRunner.IsRunning)
                _ = closingRunner.Shutdown();

            SceneManager.LoadScene("IntroScene_final");
        }

        // 새 참가자에게 최신 상태 전송
        public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
        {
            if (callbackRunner == runner)
                SendCurrentState(player);
        }

        // 종료된 Runner 참조 정리
        public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
        {
            if (callbackRunner == runner)
                runner = null;
        }

        // 게임 상태와 시작 화면 명령 수신
        public void OnReliableDataReceived(
            NetworkRunner callbackRunner,
            PlayerRef player,
            ReliableKey key,
            ArraySegment<byte> data)
        {
            if (callbackRunner != runner)
                return;

            if (key == StateKey
                && !IsAuthority
                && TryDeserializeState(data, out GameFlowReplicatedState state))
            {
                ReplicatedStateReceived?.Invoke(state);
                return;
            }

            if (key == ReturnKey && !IsAuthority && !returningToStart)
                StartCoroutine(ReturnToStartRoutine());
        }

        public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player) { }
        public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner callbackRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner callbackRunner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
        public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
        public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
    }
}
