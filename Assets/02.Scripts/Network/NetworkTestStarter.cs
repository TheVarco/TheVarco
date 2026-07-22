using Fusion;
using Fusion.Sockets;
using UnityEngine;

// 최소 접속 테스트용 스크립트. Host로 세션을 시작하고, 접속한 플레이어마다
// playerPrefab을 스폰한다. NetworkManager 오브젝트에 부착.
public class NetworkTestStarter : MonoBehaviour, INetworkRunnerCallbacks
{
    [Tooltip("접속 시 스폰할 플레이어 프리팹 (Network Object + Network Transform 붙어있어야 함)")]
    public NetworkPrefabRef playerPrefab;

    private NetworkRunner runner;

    // 화면에 임시로 띄울 시작 버튼 (테스트용, 나중에 제대로 된 UI로 교체)
    void OnGUI()
    {
        if (runner != null) return; // 이미 시작했으면 버튼 숨김

        if (GUI.Button(new Rect(20, 20, 200, 40), "Host로 시작"))
        {
            StartGame(GameMode.Host);
        }

        if (GUI.Button(new Rect(20, 70, 200, 40), "참가로 시작"))
        {
            StartGame(GameMode.Client);
        }
    }

    async void StartGame(GameMode mode)
    {
        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true; // 이 클라이언트가 입력을 보낼 수 있게 함

        await runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = "TestRoom", // 같은 이름을 쓰는 사람끼리 같은 방에 모임
            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }

    // 새 플레이어가 접속하면 Fusion이 자동으로 호출해주는 콜백
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer) // Host(서버 역할)만 스폰을 실행 (모두가 각자 스폰하면 중복 생성됨)
        {
            runner.Spawn(playerPrefab, Vector3.zero, Quaternion.identity, player);
        }
    }

    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
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