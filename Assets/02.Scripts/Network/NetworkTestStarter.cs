using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine;

// 네트워크 테스트 세션 시작
// Host Client Single 실행 모드 생성
// 호스트가 참가자별 플레이어 오브젝트 생성
// 잠수함 내부 스폰 슬롯을 참가 순서대로 배정
// 연결 종료 시 플레이어와 스폰 슬롯 정리

// 최소 접속 테스트용 스크립트. Host로 세션을 시작하고, 접속한 플레이어마다
// playerPrefab을 스폰한다. NetworkManager 오브젝트에 부착.
public class NetworkTestStarter : MonoBehaviour, INetworkRunnerCallbacks
{
    [Tooltip("접속 시 스폰할 플레이어 프리팹 (Network Object + Network Transform 붙어있어야 함)")]
    public NetworkPrefabRef playerPrefab;
    [Tooltip("시작 시 불러올 게임 씬 경로 (Build Settings에 포함되어 있어야 함).\n" +
             "비워두면 씬 전환 없이 지금 이 씬에서 그대로 시작한다 (팀원 각자 테스트 씬용)")]
    public string targetScenePath = "";
    [Tooltip("같은 이름을 쓰는 사람끼리 같은 방에 모인다. 팀원끼리 겹치지 않게 각자 다르게 둘 것")]
    public string sessionName = "TestRoom";
    [Tooltip("스폰 기준 위치. 비워두면 원점(0,0,0) 기준")]
    public Transform spawnOrigin;
    [Tooltip("여러 명이 겹쳐서 스폰되지 않게 이 반지름으로 원형 배치")]
    public float spawnSpreadRadius = 2f;

    private NetworkRunner runner;
    private PlayerCameraRig localCameraRig;

    // 누가 나갔을 때 그 사람 캐릭터만 골라 지우기 위해 기억해둔다
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    // 내부 스폰 슬롯 배정
    private readonly Dictionary<PlayerRef, int> assignedSpawnSlots = new Dictionary<PlayerRef, int>();
    private SubmarinePlayerSpawnPoints submarineSpawnPoints;

    // 화면에 임시로 띄울 시작 버튼 (테스트용, 나중에 제대로 된 UI로 교체)
    void OnGUI()
    {
        // Runner 시작 전 방 이름과 실행 모드 선택 버튼 표시
        if (runner != null) return; // 이미 시작했으면 버튼 숨김

        // 방 이름을 실행 중에 바꿀 수 있게 함 (씬을 고쳐 저장하지 않고도 서로 이름을 맞출 수 있음)
        GUI.Label(new Rect(20, 175, 200, 20), "방 이름");
        sessionName = GUI.TextField(new Rect(20, 195, 200, 25), sessionName);

        if (GUI.Button(new Rect(20, 20, 200, 40), "Host로 시작"))
        {
            StartGame(GameMode.Host);
        }

        if (GUI.Button(new Rect(20, 70, 200, 40), "참가로 시작"))
        {
            StartGame(GameMode.Client);
        }

        if (GUI.Button(new Rect(20, 120, 200, 40), "싱글플레이로 시작"))
        {
            StartGame(GameMode.Single);
        }
    }

    // 선택한 모드로 Runner와 물리 예측과 씬 관리자를 구성
    async void StartGame(GameMode mode)
    {
        // 입력 제공이 가능한 NetworkRunner를 현재 오브젝트에 생성
        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true; // 이 클라이언트가 입력을 보낼 수 있게 함

        // NetworkRigidbody3D가 자동으로 붙여주는 기본값은 ClientPhysicsSimulation.Disabled 라서,
        // 클라이언트가 자기 움직임을 예측하지 못하고 호스트가 돌려준 결과를 기다리게 된다(입력 지연).
        // 직접 붙여서 클라이언트에서도 물리를 시뮬레이션(예측)하도록 설정한다.
        var physicsSimulation = gameObject.AddComponent<RunnerSimulatePhysics3D>();
        physicsSimulation.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;

        var args = new StartGameArgs
        {
            GameMode = mode,
            SessionName = sessionName,
            PlayerCount = 4,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        // 경로를 지정한 경우에만 씬을 전환한다. 비어있으면 지금 씬에서 그대로 시작
        if (!string.IsNullOrEmpty(targetScenePath))
        {
            int buildIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(targetScenePath);
            args.Scene = SceneRef.FromIndex(buildIndex);
        }

        await runner.StartGame(args);
    }

    // 새 플레이어가 접속하면 Fusion이 자동으로 호출해주는 콜백
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 서버만 내부 스폰 위치를 정하고 플레이어 오브젝트 생성
        if (!runner.IsServer) return; // Host(서버 역할)만 스폰을 실행 (모두가 각자 스폰하면 중복 생성됨)

        // 스폰 지점을 씬마다 따로 배치하지 않아도 겹치지 않도록 원형으로 흩어놓는다
        // 잠수함 내부 스폰 위치 선택
        bool hasSubmarineSpawn = TryGetSubmarineSpawnPose(player, out Vector3 spawnPosition, out Quaternion spawnRotation);
        if (!hasSubmarineSpawn)
        {
            Vector3 basePosition = spawnOrigin != null ? spawnOrigin.position : Vector3.zero;
            Vector3 offset = Quaternion.Euler(0f, player.PlayerId * 90f, 0f) * Vector3.forward * spawnSpreadRadius;
            spawnPosition = basePosition + offset;
            spawnRotation = Quaternion.identity;
        }

        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            spawnPosition,
            spawnRotation,
            player);
        spawnedPlayers[player] = playerObject;
        runner.SetPlayerObject(player, playerObject);

        // 시작 체크포인트 참가자 갱신
        Varco.GameFlow.GameFlowCoordinator coordinator =
            FindFirstObjectByType<Varco.GameFlow.GameFlowCoordinator>(FindObjectsInactive.Include);
        coordinator?.RefreshStartupPlayersAfterJoin();
    }

    // 나간 사람 캐릭터를 지우지 않으면 월드에 유령처럼 남아서 길을 막고,
    // 재접속하면 새 캐릭터가 또 스폰돼 하나씩 쌓인다
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 서버에서 나간 플레이어 오브젝트와 슬롯 배정 제거
        if (!runner.IsServer) return;

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            if (playerObject != null) runner.Despawn(playerObject);
            spawnedPlayers.Remove(player);
        }

        assignedSpawnSlots.Remove(player);
    }

    // 비어 있는 내부 스폰 슬롯 선택
    private bool TryGetSubmarineSpawnPose(
        PlayerRef player,
        out Vector3 position,
        out Quaternion rotation)
    {
        // 잠수함 스폰 지점 목록을 찾고 참가자의 슬롯 번호 확보
        if (submarineSpawnPoints == null)
        {
            submarineSpawnPoints =
                FindFirstObjectByType<SubmarinePlayerSpawnPoints>(FindObjectsInactive.Include);
        }

        if (submarineSpawnPoints == null || submarineSpawnPoints.Count == 0)
        {
            position = default;
            rotation = default;
            return false;
        }

        if (!assignedSpawnSlots.TryGetValue(player, out int slotIndex))
        {
            slotIndex = FindFreeSpawnSlot(submarineSpawnPoints.Count);
            if (slotIndex < 0)
            {
                position = default;
                rotation = default;
                return false;
            }

            assignedSpawnSlots[player] = slotIndex;
        }

        return submarineSpawnPoints.TryGetSpawnPose(slotIndex, out position, out rotation);
    }

    // 가장 낮은 빈 슬롯 검색
    private int FindFreeSpawnSlot(int slotCount)
    {
        // 이미 배정된 번호를 건너뛰며 가장 작은 슬롯 번호 반환
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            bool occupied = false;
            foreach (int assignedSlot in assignedSpawnSlots.Values)
            {
                if (assignedSlot != slotIndex)
                    continue;

                occupied = true;
                break;
            }

            if (!occupied)
                return slotIndex;
        }

        return -1;
    }

    // 로컬 키보드와 카메라 방향을 Fusion 입력 구조체로 제출
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // 로컬 카메라를 찾은 뒤 현재 프레임 입력값 구성
        if (localCameraRig == null)
            localCameraRig = FindFirstObjectByType<PlayerCameraRig>();

        var data = new NetworkInputData
        {
            Horizontal = Input.GetAxisRaw("Horizontal"),
            Vertical = Input.GetAxisRaw("Vertical"),
            Up = Input.GetKey(KeyCode.Space),
            Down = Input.GetKey(KeyCode.LeftControl),
            Dash = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            Yaw = localCameraRig != null ? localCameraRig.Yaw : 0f,
            Pitch = localCameraRig != null ? localCameraRig.Pitch : 0f
        };
        input.Set(data);
    }
    // 입력 누락 콜백 자리 유지
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        // 현재 테스트 흐름에서는 누락 입력을 별도 보정하지 않음
    }

    // Runner 종료 콜백 자리 유지
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        // 현재 테스트 흐름에서는 종료 후 추가 정리를 하지 않음
    }

    // 서버 연결 완료 콜백 자리 유지
    public void OnConnectedToServer(NetworkRunner runner)
    {
        // 현재 테스트 흐름에서는 연결 완료 후 추가 처리를 하지 않음
    }

    // 서버 연결 종료 콜백 자리 유지
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        // 현재 테스트 흐름에서는 연결 종료 안내를 처리하지 않음
    }

    // 접속 요청 콜백 자리 유지
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // 현재 테스트 흐름에서는 별도 접속 승인 규칙을 사용하지 않음
    }

    // 접속 실패 콜백 자리 유지
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        // 현재 테스트 흐름에서는 접속 실패 UI를 처리하지 않음
    }

    // 사용자 메시지 콜백 자리 유지
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
        // 현재 테스트 흐름에서는 사용자 시뮬레이션 메시지를 사용하지 않음
    }

    // 세션 목록 콜백 자리 유지
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList)
    {
        // 현재 테스트 흐름에서는 고정 방 이름을 사용해 목록을 소비하지 않음
    }

    // 인증 응답 콜백 자리 유지
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data)
    {
        // 현재 테스트 흐름에서는 사용자 인증 데이터를 사용하지 않음
    }

    // 호스트 마이그레이션 콜백 자리 유지
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        // 현재 범위에서는 호스트 마이그레이션을 지원하지 않음
    }

    // 신뢰 데이터 수신 콜백 자리 유지
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data)
    {
        // 현재 테스트 흐름에서는 신뢰 데이터 채널을 사용하지 않음
    }

    // 신뢰 데이터 진행 콜백 자리 유지
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        // 현재 테스트 흐름에서는 전송 진행률을 표시하지 않음
    }

    // 씬 로드 완료 콜백 자리 유지
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // 플레이어 생성은 참가 콜백에서 처리하므로 여기서는 작업하지 않음
    }

    // 씬 로드 시작 콜백 자리 유지
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // 현재 테스트 흐름에서는 로딩 화면을 별도로 제어하지 않음
    }

    // 관심 영역 이탈 콜백 자리 유지
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // 현재 테스트 흐름에서는 관심 영역 이탈 표시를 사용하지 않음
    }

    // 관심 영역 진입 콜백 자리 유지
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // 현재 테스트 흐름에서는 관심 영역 진입 표시를 사용하지 않음
    }
}
