using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine;

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

    // 화면에 임시로 띄울 시작 버튼 (테스트용, 나중에 제대로 된 UI로 교체)
    void OnGUI()
    {
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

    async void StartGame(GameMode mode)
    {
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
        if (!runner.IsServer) return; // Host(서버 역할)만 스폰을 실행 (모두가 각자 스폰하면 중복 생성됨)

        // 스폰 지점을 씬마다 따로 배치하지 않아도 겹치지 않도록 원형으로 흩어놓는다
        Vector3 basePosition = spawnOrigin != null ? spawnOrigin.position : Vector3.zero;
        Vector3 offset = Quaternion.Euler(0f, player.PlayerId * 90f, 0f) * Vector3.forward * spawnSpreadRadius;

        spawnedPlayers[player] = runner.Spawn(playerPrefab, basePosition + offset, Quaternion.identity, player);
    }

    // 나간 사람 캐릭터를 지우지 않으면 월드에 유령처럼 남아서 길을 막고,
    // 재접속하면 새 캐릭터가 또 스폰돼 하나씩 쌓인다
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            if (playerObject != null) runner.Despawn(playerObject);
            spawnedPlayers.Remove(player);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
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
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}