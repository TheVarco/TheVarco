using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Varco.GameFlow
{
    /// <summary>
    /// 싱글플레이와 코어 검증용 로컬 브리지
    /// Fusion 멀티플레이 감지 시 판정 권한 비활성
    /// </summary>
    public sealed class LocalGameFlowBridge : MonoBehaviour, IGameFlowNetworkBridge
    {
        private const string IntroSceneName = "IntroScene_final";
        private const float ShutdownTimeoutSeconds = 5f;

        // 플레이어 인스턴스별 레거시 참가자 캐시
        private readonly Dictionary<int, LegacyPlayerCheckpointParticipant> playerByInstanceId = new();
        // 현재 활성 플레이어 참가자 목록
        private readonly List<IPlayerCheckpointParticipant> players = new();
        // 멀티플레이 경고 중복 방지
        private bool warnedAboutMultiplayer;
        // 중복 시작 화면 이동 방지
        private bool returningToStart;

        // 실행 중인 멀티플레이 Runner 존재 여부
        public bool IsMultiplayer => FindRunningMultiplayerRunner() != null;
        // 로컬 판정 사용 가능 여부
        public bool IsReady => !IsMultiplayer;
        // 싱글플레이에서만 로컬 권한 허용
        public bool IsAuthority => IsReady;
        // 조회 시점의 활성 플레이어 목록 제공
        public IReadOnlyList<IPlayerCheckpointParticipant> Players
        {
            get
            {
                RefreshPlayers();
                return players;
            }
        }

        // 로컬 상태 전파 이벤트
        public event Action<GameFlowReplicatedState> ReplicatedStateReceived;

        // 팀 브리지 없이 멀티플레이가 실행되면 명확한 경고 출력
        private void Update()
        {
            if (IsMultiplayer && !warnedAboutMultiplayer)
            {
                warnedAboutMultiplayer = true;
                Debug.LogWarning(
                    "[GameFlow] Fusion multiplayer is running without an IGameFlowNetworkBridge. " +
                    "Local success/failure evaluation is disabled to prevent peer divergence.",
                    this);
            }
        }

        // 싱글플레이에서는 동일 인스턴스에 상태 즉시 전달
        public void PublishState(GameFlowReplicatedState state)
        {
            if (IsReady)
                ReplicatedStateReceived?.Invoke(state);
        }

        // 프리팹 식별 정보가 없는 로컬 생성 요청 거부
        public bool TrySpawnCheckpointEntity(
            string checkpointEntityKey,
            Vector3 position,
            Quaternion rotation,
            out GameObject entity)
        {
            entity = null;
            Debug.LogWarning(
                $"[GameFlow] Local bridge cannot recreate '{checkpointEntityKey}' without a registered prefab.",
                this);
            return false;
        }

        // 로컬 체크포인트 엔티티 제거
        public bool TryDespawnCheckpointEntity(string checkpointEntityKey, GameObject entity)
        {
            if (entity == null)
                return true;

            Destroy(entity);
            return true;
        }

        // 영상 완료 콜백을 빠져나온 뒤 Runner를 정리하고 시작 화면 이동
        public void ReturnToStartScene()
        {
            if (returningToStart)
                return;

            StartCoroutine(ReturnToStartRoutine());
        }

        private IEnumerator ReturnToStartRoutine()
        {
            returningToStart = true;
            Debug.Log("[GameFlow] 시작 화면 복귀를 시작합니다.", this);

            // VideoPlayer.loopPointReached 네이티브 콜백 안에서 씬을 언로드하지 않는다.
            yield return null;

            NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
            List<Task> shutdownTasks = new();
            List<string> runnerNames = new();
            foreach (NetworkRunner activeRunner in runners)
            {
                if (activeRunner == null || !activeRunner.IsRunning)
                    continue;

                string runnerName = activeRunner.name;
                try
                {
                    Debug.Log($"[GameFlow] NetworkRunner Shutdown을 요청합니다: {runnerName}", this);
                    Task shutdownTask = activeRunner.Shutdown(forceShutdownProcedure: true);
                    if (shutdownTask != null)
                    {
                        shutdownTasks.Add(shutdownTask);
                        runnerNames.Add(runnerName);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[GameFlow] NetworkRunner Shutdown 요청 중 예외가 발생했습니다: " +
                        $"{runnerName}\n{exception}",
                        this);
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
            while (Time.realtimeSinceStartup < shutdownDeadline)
            {
                bool allCompleted = true;
                foreach (Task shutdownTask in shutdownTasks)
                {
                    if (!shutdownTask.IsCompleted)
                    {
                        allCompleted = false;
                        break;
                    }
                }

                if (allCompleted)
                    break;

                yield return null;
            }

            for (int i = 0; i < shutdownTasks.Count; i++)
            {
                Task shutdownTask = shutdownTasks[i];
                string runnerName = runnerNames[i];
                if (!shutdownTask.IsCompleted)
                {
                    Debug.LogWarning(
                        $"[GameFlow] NetworkRunner Shutdown이 {ShutdownTimeoutSeconds:0.#}초 안에 " +
                        $"끝나지 않았습니다: {runnerName}",
                        this);
                }
                else if (shutdownTask.IsFaulted)
                {
                    Debug.LogError(
                        $"[GameFlow] NetworkRunner Shutdown이 실패했습니다: {runnerName}\n" +
                        shutdownTask.Exception,
                        this);
                }
                else if (shutdownTask.IsCanceled)
                {
                    Debug.LogWarning(
                        $"[GameFlow] NetworkRunner Shutdown이 취소되었습니다: {runnerName}",
                        this);
                }
                else
                {
                    Debug.Log($"[GameFlow] NetworkRunner Shutdown이 완료되었습니다: {runnerName}", this);
                }
            }

            // Shutdown이 예약한 Destroy가 프레임 끝에 반영된 후 비동기 씬 로드를 시작한다.
            yield return null;
            Debug.Log($"[GameFlow] {IntroSceneName} 비동기 로드를 시작합니다.", this);
            if (SceneManager.LoadSceneAsync(IntroSceneName, LoadSceneMode.Single) == null)
                Debug.LogError($"[GameFlow] {IntroSceneName} 비동기 로드를 시작하지 못했습니다.", this);
        }

        // 활성 플레이어를 탐색하고 레거시 참가자 목록 갱신
        private void RefreshPlayers()
        {
            players.Clear();
            PlayerDownedState[] downedStates = FindObjectsByType<PlayerDownedState>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            // 현재 탐색에서 발견된 플레이어 키 수집
            HashSet<int> activeIds = new();
            foreach (PlayerDownedState downedState in downedStates)
            {
                if (downedState == null)
                    continue;

                int id = downedState.gameObject.GetInstanceID();
                activeIds.Add(id);
                if (!playerByInstanceId.TryGetValue(id, out LegacyPlayerCheckpointParticipant player))
                {
                    player = new LegacyPlayerCheckpointParticipant(downedState);
                    playerByInstanceId.Add(id, player);
                }

                players.Add(player);
            }

            // 사라진 플레이어 캐시 키 수집
            List<int> staleIds = null;
            foreach (int id in playerByInstanceId.Keys)
            {
                if (activeIds.Contains(id))
                    continue;

                staleIds ??= new List<int>();
                staleIds.Add(id);
            }

            if (staleIds == null)
                return;

            foreach (int id in staleIds)
                playerByInstanceId.Remove(id);
        }

        // Single 모드를 제외한 실행 중 Runner 탐색
        private static NetworkRunner FindRunningMultiplayerRunner()
        {
            NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
            foreach (NetworkRunner runner in runners)
            {
                if (runner != null && runner.IsRunning && runner.GameMode != GameMode.Single)
                    return runner;
            }

            return null;
        }

        // 팀 플레이어 어댑터 연결 전 사용하는 제한형 로컬 참가자
        private sealed class LegacyPlayerCheckpointParticipant : IPlayerCheckpointParticipant
        {
            // 다운 상태와 플레이어 루트 참조
            private readonly PlayerDownedState downedState;
            // 기본 생존 수치 참조
            private readonly Health health;
            private readonly OxygenStat oxygen;
            private readonly HungerStat hunger;
            // 위치 복원 후 속도 초기화 대상
            private readonly Rigidbody body;
            // 복원 전 좌석 해제 대상
            private readonly PlayerSeatController seatController;
            // 게임 정지 전 입력 컴포넌트 활성 상태
            private readonly Dictionary<MonoBehaviour, bool> inputStates = new();
            // Failed -> Restoring 전환처럼 false가 연속 적용되어 원래 입력 상태를 잃는 것을 방지
            private bool gameplayEnabled = true;

            // 기존 플레이어 컴포넌트를 읽기 전용으로 연결
            public LegacyPlayerCheckpointParticipant(PlayerDownedState downedState)
            {
                this.downedState = downedState;
                GameObject player = downedState.gameObject;
                health = player.GetComponent<Health>();
                oxygen = player.GetComponentInChildren<OxygenStat>(true);
                hunger = player.GetComponentInChildren<HungerStat>(true);
                body = player.GetComponent<Rigidbody>();
                seatController = player.GetComponent<PlayerSeatController>();
                PlayerKey = player.GetInstanceID().ToString();
                CheckpointId = $"player:{PlayerKey}";
            }

            public string PlayerKey { get; }
            public string CheckpointId { get; }
            public int RestoreOrder => 100;
            public bool IsDowned => downedState != null && downedState.IsDowned;
            // Carryable 핫바는 items:session이 함께 저장하며 수량형 PlayerInventory는 범위에서 제외한다.
            public bool SupportsCompleteSnapshot => true;

            // 위치 생존 수치 다운 상태 캡처
            public object CaptureCheckpointState()
            {
                Transform transform = downedState.transform;
                return new PlayerState
                {
                    Position = transform.position,
                    Rotation = transform.rotation,
                    Health = GameFlowHealthUtility.Capture(health),
                    Oxygen = oxygen != null ? oxygen.CurrentValue : 0f,
                    OxygenMax = oxygen != null ? oxygen.maxValue : 0f,
                    Hunger = hunger != null ? hunger.CurrentValue : 0f,
                    HungerMax = hunger != null ? hunger.maxValue : 0f,
                    IsDowned = IsDowned
                };
            }

            // 현재 좌석 연결 선해제
            public void PrepareForCheckpointRestore()
            {
                if (seatController != null && seatController.CurrentSeat != null)
                    seatController.CurrentSeat.ForceExit(seatController);
            }

            // 위치 속도 생존 수치 다운 상태 복원
            public void RestoreCheckpointState(object state)
            {
                if (state is not PlayerState playerState || downedState == null)
                    return;

                Transform transform = downedState.transform;
                transform.SetPositionAndRotation(playerState.Position, playerState.Rotation);

                // 물리 속도는 스냅샷에 포함하지 않고 초기화
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                // 생존 스냅샷이면 체력 복원 전 다운 상태 해제
                if (!playerState.IsDowned && downedState.IsDowned)
                    downedState.Revive();

                GameFlowHealthUtility.Restore(health, playerState.Health);
                GameFlowHealthUtility.RestoreStat(oxygen, playerState.Oxygen, playerState.OxygenMax);
                GameFlowHealthUtility.RestoreStat(hunger, playerState.Hunger, playerState.HungerMax);
            }

            // 현재 좌석을 해제하고 플레이어를 내부 스폰 위치로 이동
            public bool TrySetCheckpointSpawnPose(Vector3 position, Quaternion rotation)
            {
                if (downedState == null)
                    return false;

                if (seatController != null && seatController.CurrentSeat != null)
                    seatController.CurrentSeat.ForceExit(seatController);

                downedState.transform.SetPositionAndRotation(position, rotation);
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                return true;
            }

            // 내부 스폰 방식은 기존 좌석을 다시 연결하지 않음
            public void CompleteCheckpointRestore() { }

            // 결과 화면과 복원 중 입력 컴포넌트 게이트 처리
            public void SetGameplayEnabled(bool enabled)
            {
                if (downedState == null)
                    return;
                if (gameplayEnabled == enabled)
                    return;

                gameplayEnabled = enabled;

                MonoBehaviour[] behaviours = downedState.GetComponents<MonoBehaviour>();
                // 정지 직전 활성 상태를 저장하고 입력 차단
                if (!enabled)
                {
                    inputStates.Clear();
                    foreach (MonoBehaviour behaviour in behaviours)
                    {
                        if (!IsInputBehaviour(behaviour))
                            continue;

                        inputStates[behaviour] = behaviour.enabled;
                        behaviour.enabled = false;
                    }
                    return;
                }

                // 생존 플레이어의 기존 입력 상태 복구
                foreach (KeyValuePair<MonoBehaviour, bool> entry in inputStates)
                {
                    if (entry.Key != null && entry.Value && !IsDowned)
                        entry.Key.enabled = true;
                }
                inputStates.Clear();
            }

            // 플레이어 조작에 직접 관여하는 컴포넌트 판정
            private static bool IsInputBehaviour(MonoBehaviour behaviour)
            {
                return behaviour is PlayerController
                    || behaviour is PlayerGrabber
                    || behaviour is PlayerHotbar
                    || behaviour is MeleeAttack
                    || behaviour is PlayerItemGiver
                    || behaviour is PlayerReviver
                    || behaviour is PlayerInteractor
                    || behaviour is PlayerSeatController
                    || behaviour != null && behaviour.GetType().Name == "CaveSwimController";
            }

            // 로컬 플레이어 복원용 세션 메모리 데이터
            private sealed class PlayerState
            {
                public Vector3 Position;
                public Quaternion Rotation;
                public HealthCheckpointState Health;
                public float Oxygen;
                public float OxygenMax;
                public float Hunger;
                public float HungerMax;
                public bool IsDowned;
            }
        }
    }
}
