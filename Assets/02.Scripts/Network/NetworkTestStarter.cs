using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 네트워크 테스트 세션 시작
// Host Client Single 실행 모드 생성
// 호스트가 참가자별 플레이어 오브젝트 생성
// 잠수함 내부 스폰 슬롯을 참가 순서대로 배정
// 연결 종료 시 플레이어와 스폰 슬롯 정리

// 최소 접속 테스트용 스크립트. Host로 세션을 시작하고, 접속한 플레이어마다
// playerPrefab을 스폰한다. NetworkManager 오브젝트에 부착.
public class NetworkTestStarter : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Prefabs & Scene")]
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

    [Header("UI Settings")]
    [Tooltip("시작 UI 패널 (StartPanel). 비워두면 씬에서 자동으로 검색합니다.")]
    public GameObject startPanel;
    [Tooltip("startPanel이 비어있을 때 씬에서 검색할 패널의 이름")]
    public string startPanelName = "StartPanel";

    [Tooltip("Host 시작 버튼 이름 (기본값: Host)")]
    public string hostButtonName = "Host";
    [Tooltip("Client/Guest 참가 버튼 이름 (기본값: Guest)")]
    public string clientButtonName = "Guest";
    [Tooltip("Single 플레이 버튼 이름 (기본값: Single)")]
    public string singleButtonName = "Single";
    [Tooltip("방 이름 입력 필드 이름 (기본값: InputField)")]
    public string sessionNameInputName = "InputField";

    [Header("Gameplay HUD Settings (게임 시작 전 숨김 대상)")]
    [Tooltip("게임 시작 전 숨길 UI 오브젝트 목록. 비워두면 씬에서 기본 이름으로 자동 탐색합니다.")]
    public List<GameObject> gameplayUIElements = new List<GameObject>();
    [Tooltip("자동 탐색할 인게임 UI 오브젝트 이름 목록 (HealthBar, OxygenBar, HotBarUI 등)")]
    public string[] defaultGameplayUINames = new string[] { "HealthBar", "OxygenBar", "HotBarUI", "HotbarUI" };

    [Header("대기방")]
    [Tooltip("체크하면 방을 만든 뒤 바로 게임 씬으로 가지 않고, 호스트가 시작을 누를 때까지 이 씬에서 대기한다.\n" +
             "팀원 테스트 씬은 꺼두면 지금까지와 똑같이 동작한다")]
    public bool useLobby;
    [Tooltip("오프닝이 끝났다는 신호가 이 시간 안에 안 오면 스스로 스폰한다.\n" +
             "UI 연결 실수로 캐릭터 없는 맵에 갇히는 걸 막는 안전망")]
    public float spawnReleaseTimeout = 60f;

    // 화면 전환은 LobbyUI가 맡는다. 여기서는 "무슨 일이 일어났는지"만 알린다
    // (네트워크와 UI를 한 파일에 두면 UI를 고칠 때마다 네트워크 코드를 열어야 한다)
    public event System.Action LobbyEntered;       // 세션이 열려 대기 상태가 됨
    public event System.Action PlayerListChanged;  // 누가 들어오거나 나감
    public event System.Action SceneLoadStarted;   // 게임 씬 로드 시작 (접속한 전원에게 옴)
    public event System.Action GameSceneLoaded;    // 게임 씬 로드 완료

    public bool IsHost => runner != null && runner.IsServer;
    public string RoomName => sessionName;

    private NetworkRunner runner;
    private PlayerCameraRig localCameraRig;
    private bool inLobby;
    private bool spawnReleased;
    private float spawnReleaseTimer = -1f; // 음수면 대기 중 아님

    // targetScenePath가 비어 있으면 옮겨갈 씬이 없어서 대기 자체가 의미 없다
    private bool UsesLobby => useLobby && !string.IsNullOrEmpty(targetScenePath);

    // 누가 나갔을 때 그 사람 캐릭터만 골라 지우기 위해 기억해둔다
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    // 내부 스폰 슬롯 배정
    private readonly Dictionary<PlayerRef, int> assignedSpawnSlots = new Dictionary<PlayerRef, int>();
    private SubmarinePlayerSpawnPoints submarineSpawnPoints;

    // 인트로에서 넘어온 스타터를 기억해둔다 (러너가 DontDestroyOnLoad라 씬을 넘어 살아있음)
    private static NetworkTestStarter instance;

    private void Awake()
    {
        // 게임 씬에도 팀원들이 각자 테스트하려고 배치해둔 NetworkManager가 있다.
        // 그냥 두면 얘가 자기 StartPanel을 붙잡고 인게임 HUD를 숨겨버린다.
        // (그 씬을 직접 Play하면 instance가 비어 있어서 평소대로 동작한다 — 팀원 테스트엔 영향 없음)
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        BindStartPanelUI();
        InitializeGameplayUI();
        SetGameplayUIVisible(false); // 게임 시작 전이므로 숨김
    }

    // 스폰을 LobbyUI가 열어주게 맡겨뒀는데, LobbyUI가 씬 전환에서 살아남지 못하면
    // 그 신호가 영영 안 온다. 그러면 캐릭터 없는 맵에 갇히므로 여기서 대신 연다
    private void Update()
    {
        if (spawnReleaseTimer < 0f || spawnReleased) return;

        spawnReleaseTimer += Time.unscaledDeltaTime;
        if (spawnReleaseTimer < spawnReleaseTimeout) return;

        Debug.LogWarning("[NetworkTestStarter] 오프닝 종료 신호가 오지 않아 플레이어를 강제로 스폰합니다. " +
                         "LobbyUI의 Loading Overlay 연결과, 그 오브젝트가 하이어라키 루트인지 확인하세요.");
        ReleasePlayerSpawn();
    }

    private void InitializeGameplayUI()
    {
        if (gameplayUIElements == null)
            gameplayUIElements = new List<GameObject>();

        // 씬에서 해당 이름의 UI 오브젝트들을 탐색하여 등록
        foreach (string uiName in defaultGameplayUINames)
        {
            if (string.IsNullOrEmpty(uiName)) continue;
            GameObject found = FindObjectInScene(uiName);
            if (found != null && !gameplayUIElements.Contains(found))
            {
                gameplayUIElements.Add(found);
            }
        }
    }

    public void SetGameplayUIVisible(bool visible)
    {
        if (gameplayUIElements == null) return;
        foreach (GameObject ui in gameplayUIElements)
        {
            if (ui != null)
            {
                ui.SetActive(visible);
            }
        }
    }

    private void BindStartPanelUI()
    {
        // 1. startPanel이 할당되지 않은 경우 씬에서 이름으로 검색 (비활성 오브젝트 포함)
        if (startPanel == null)
        {
            startPanel = FindObjectInScene(startPanelName);
        }

        if (startPanel == null)
        {
            Debug.LogWarning($"[NetworkTestStarter] '{startPanelName}'을(를) 씬에서 찾을 수 없습니다.");
            return;
        }

        // 2. StartPanel 내부의 버튼들을 이름으로 검색 후 이벤트 연결
        Button hostBtn = FindButtonInPanel(startPanel, hostButtonName, "Host", "HostButton", "BtnHost", "StartHost");
        if (hostBtn != null)
        {
            hostBtn.onClick.RemoveListener(OnClickStartHost);
            hostBtn.onClick.AddListener(OnClickStartHost);
        }
        else
        {
            Debug.LogWarning($"[NetworkTestStarter] Host 버튼을 '{startPanel.name}' 내부에서 찾지 못했습니다.");
        }

        Button clientBtn = FindButtonInPanel(startPanel, clientButtonName, "Guest", "GuestButton", "Client", "ClientButton", "BtnClient", "Join", "JoinButton", "StartClient");
        if (clientBtn != null)
        {
            clientBtn.onClick.RemoveListener(OnClickStartClient);
            clientBtn.onClick.AddListener(OnClickStartClient);
        }
        else
        {
            Debug.LogWarning($"[NetworkTestStarter] Guest/Client 버튼을 '{startPanel.name}' 내부에서 찾지 못했습니다.");
        }

        Button singleBtn = FindButtonInPanel(startPanel, singleButtonName, "Single", "SingleButton", "BtnSingle", "SinglePlayer", "StartSingle");
        if (singleBtn != null)
        {
            singleBtn.onClick.RemoveListener(OnClickStartSingle);
            singleBtn.onClick.AddListener(OnClickStartSingle);
        }
        else
        {
            Debug.LogWarning($"[NetworkTestStarter] Single 버튼을 '{startPanel.name}' 내부에서 찾지 못했습니다.");
        }

        // 3. StartPanel 내부의 InputField(방 이름 입력) 검색 및 연결
        BindInputFieldInPanel(startPanel);
    }

    private GameObject FindObjectInScene(string objName)
    {
        if (string.IsNullOrEmpty(objName)) return null;

        // 활성 오브젝트 우선 검색
        GameObject found = GameObject.Find(objName);
        if (found != null) return found;

        // 비활성 오브젝트 포함 전체 검색
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform t in allTransforms)
        {
            if (t != null && t.name.Equals(objName, System.StringComparison.OrdinalIgnoreCase))
            {
                return t.gameObject;
            }
        }

        return null;
    }

    private Button FindButtonInPanel(GameObject panel, params string[] candidateNames)
    {
        if (panel == null) return null;

        Button[] buttons = panel.GetComponentsInChildren<Button>(true);

        // 1순위: 후보 이름들과 정확히 일치(대소문자 무시)하는 버튼
        foreach (string targetName in candidateNames)
        {
            if (string.IsNullOrEmpty(targetName)) continue;
            foreach (Button btn in buttons)
            {
                if (btn != null && btn.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                    return btn;
            }
        }

        // 2순위: 후보 이름이 버튼 이름에 포함되어 있는 버튼 (예: "Btn_Host", "Host_Btn")
        foreach (string targetName in candidateNames)
        {
            if (string.IsNullOrEmpty(targetName)) continue;
            foreach (Button btn in buttons)
            {
                if (btn != null && btn.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return btn;
            }
        }

        return null;
    }

    private void BindInputFieldInPanel(GameObject panel)
    {
        if (panel == null) return;

        // 1. UnityEngine.UI.InputField
        InputField[] legacyInputs = panel.GetComponentsInChildren<InputField>(true);
        foreach (InputField input in legacyInputs)
        {
            if (input != null && (string.IsNullOrEmpty(sessionNameInputName) || input.name.IndexOf(sessionNameInputName, System.StringComparison.OrdinalIgnoreCase) >= 0 || legacyInputs.Length == 1))
            {
                if (string.IsNullOrEmpty(input.text))
                    input.text = sessionName;
                else
                    sessionName = input.text;

                input.onValueChanged.RemoveListener(OnSessionNameChanged);
                input.onValueChanged.AddListener(OnSessionNameChanged);
                return;
            }
        }

        // 2. TMPro.TMP_InputField
        TMP_InputField[] tmpInputs = panel.GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField input in tmpInputs)
        {
            if (input != null && (string.IsNullOrEmpty(sessionNameInputName) || input.name.IndexOf(sessionNameInputName, System.StringComparison.OrdinalIgnoreCase) >= 0 || tmpInputs.Length == 1))
            {
                if (string.IsNullOrEmpty(input.text))
                    input.text = sessionName;
                else
                    sessionName = input.text;

                input.onValueChanged.RemoveListener(OnSessionNameChanged);
                input.onValueChanged.AddListener(OnSessionNameChanged);
                return;
            }
        }
    }

    private void OnSessionNameChanged(string newName)
    {
        sessionName = newName;
    }

    // UI 버튼에서 직접 호출할 수 있는 public 메서드들
    public void OnClickStartHost() => StartGame(GameMode.Host);
    public void OnClickStartClient() => StartGame(GameMode.Client);
    public void OnClickStartSingle() => StartGame(GameMode.Single);

    // 선택한 모드로 Runner와 물리 예측과 씬 관리자를 구성
    async void StartGame(GameMode mode)
    {
        // 게임 시작 시 StartPanel 비활성화
        if (startPanel != null)
        {
            startPanel.SetActive(false);
        }

        // 입력 제공이 가능한 NetworkRunner를 현재 오브젝트에 생성
        CleanupRunner(); // 이전 세션에서 남은 컴포넌트를 걷는다. 두 개 붙으면 Fusion이 꼬인다
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

        // 대기방을 쓰면 세션만 먼저 열고 인트로 씬에 머무른다. 호스트가 시작을 누를 때 다 같이 이동.
        // 혼자 하기는 기다릴 사람이 없으니 바로 게임 씬으로 간다
        if (!UsesLobby || mode == GameMode.Single)
        {
            SceneRef gameScene = GetGameSceneRef();
            if (gameScene.IsValid) args.Scene = gameScene;
        }

        StartGameResult result = await runner.StartGame(args);

        // 결과를 안 보면 접속 실패가 성공과 똑같이 흘러가서, 존재하지도 않는 세션의
        // 빈 대기방이 떠버린다 (사람을 기다리는 줄 알고 계속 서 있게 됨)
        if (!result.Ok)
        {
            Debug.LogError($"[NetworkTestStarter] 세션 시작 실패: {result.ShutdownReason} {result.ErrorMessage}");
            if (startPanel != null) startPanel.SetActive(true); // 갇히지 않게 시작 화면으로 되돌린다
            return;
        }

        if (UsesLobby && mode != GameMode.Single)
        {
            inLobby = true;
            LobbyEntered?.Invoke();
        }
        else
            SetGameplayUIVisible(true); // 게임 시작 시 인게임 HUD(체력바, 산소바, 핫바 등) 활성화
    }

    // Build Settings에 없거나 체크가 꺼져 있으면 -1이 온다.
    // 그대로 SceneRef.FromIndex에 넘기면 예외로 죽으므로 경고만 남기고 현재 씬을 유지한다
    private SceneRef GetGameSceneRef()
    {
        if (string.IsNullOrEmpty(targetScenePath)) return default;

        int buildIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(targetScenePath);
        if (buildIndex < 0)
        {
            Debug.LogWarning($"[NetworkTestStarter] '{targetScenePath}'가 Build Settings에 없거나 체크가 꺼져 있습니다. " +
                             "File > Build Settings에서 씬을 켜주세요.");
            return default;
        }

        return SceneRef.FromIndex(buildIndex);
    }

    // 방을 만들거나 참가한 직후. 이때는 아직 게임 씬으로 안 넘어간 상태다
    // 대기방에서 호스트가 시작을 눌렀을 때. Fusion이 접속한 전원을 동기화해서 같이 옮긴다
    public void RequestStartGame()
    {
        if (runner == null || !runner.IsServer) return;

        SceneRef gameScene = GetGameSceneRef();
        if (gameScene.IsValid) runner.LoadScene(gameScene);
    }

    // 대기방에서 나갈 때. 세션을 닫고 시작 화면으로 되돌린다
    public void LeaveSession()
    {
        inLobby = false;
        spawnReleased = false;
        spawnReleaseTimer = -1f;

        // destroyGameObject 기본값이 true라 그냥 부르면 이 컴포넌트가 붙은 NetworkManager까지
        // 통째로 사라져서, 되돌아간 시작 화면의 버튼이 아무 반응도 안 하게 된다
        if (runner != null) runner.Shutdown(destroyGameObject: false);

        if (startPanel != null) startPanel.SetActive(true);
    }

    // 러너와 함께 붙였던 애드온들을 걷어낸다. 다음 시작 때 AddComponent가 겹치는 걸 막는다
    private void CleanupRunner()
    {
        foreach (RunnerSimulatePhysics3D physics in GetComponents<RunnerSimulatePhysics3D>()) Destroy(physics);
        foreach (NetworkSceneManagerDefault sceneManager in GetComponents<NetworkSceneManagerDefault>()) Destroy(sceneManager);
        foreach (NetworkRunner oldRunner in GetComponents<NetworkRunner>()) Destroy(oldRunner);

        runner = null;
    }

    // 슬롯을 그리는 쪽(LobbyUI)이 참가자 목록을 읽어갈 수 있게
    public IEnumerable<PlayerRef> ActivePlayers =>
        runner != null && runner.IsRunning ? runner.ActivePlayers : System.Array.Empty<PlayerRef>();

    public PlayerRef LocalPlayer => runner != null ? runner.LocalPlayer : default;

    // 새 플레이어가 접속하면 Fusion이 자동으로 호출해주는 콜백
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        PlayerListChanged?.Invoke();

        // 서버만 내부 스폰 위치를 정하고 플레이어 오브젝트 생성
        if (!runner.IsServer) return; // Host(서버 역할)만 스폰을 실행 (모두가 각자 스폰하면 중복 생성됨)

        // 대기방에 있는 동안은 스폰하지 않는다. 안 그러면 인트로 씬에 캐릭터가 생기고
        // 카메라가 그쪽으로 붙어서 대기 화면이 가려진다.
        // 오프닝이 도는 동안도 마찬가지 — 화면을 못 보는 채로 적에게 맞는다
        if (inLobby && !spawnReleased) return;

        SpawnPlayer(player);
    }

    // 오프닝이 끝나 화면이 열리면 LobbyUI가 부른다
    public void ReleasePlayerSpawn()
    {
        if (spawnReleased) return;
        spawnReleased = true;
        spawnReleaseTimer = -1f;

        if (runner == null || !runner.IsServer) return;
        foreach (PlayerRef player in runner.ActivePlayers) SpawnPlayer(player);
    }

    private void SpawnPlayer(PlayerRef player)
    {
        if (spawnedPlayers.ContainsKey(player)) return; // 중복 스폰 방지

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
        PlayerListChanged?.Invoke();

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

    // 게임 씬 로드가 끝난 시점. 여기서부터 실제 게임이 시작된다
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (!IsInGameScene()) return;

        GameSceneLoaded?.Invoke(); // LobbyUI가 대기방과 로딩 화면을 걷어낸다

        // 여기까지 들고 온 UI 참조는 전부 인트로 씬 것이라 이미 파괴됐다.
        // 게임 씬에도 팀원이 각자 테스트하던 시작 화면과 HUD가 그대로 있어서 다시 찾는다
        startPanel = FindObjectInScene(startPanelName);
        if (startPanel != null) startPanel.SetActive(false);

        gameplayUIElements.Clear();
        InitializeGameplayUI();
        SetGameplayUIVisible(true);

        if (!runner.IsServer) return;

        // 대기방을 거쳐 왔으면 오프닝이 끝날 때까지 기다린다 (LobbyUI가 ReleasePlayerSpawn을 부름).
        // 신호가 안 오는 경우를 대비해 여기서 안전망 타이머를 켠다
        if (inLobby)
        {
            spawnReleaseTimer = 0f;
            return;
        }

        foreach (PlayerRef player in runner.ActivePlayers) SpawnPlayer(player);
    }

    // 클라이언트가 세션에 붙을 때 일어나는 초기 씬 동기화(인트로 씬)와 구분한다.
    // 경로 문자열을 직접 비교하면 "MainScene_final"처럼 짧게 써도 씬은 로드되는데
    // 여기서만 안 맞아서, 스폰과 UI 정리가 통째로 조용히 건너뛰어진다.
    // 씬을 고르는 쪽(GetGameSceneRef)과 똑같은 함수로 인덱스를 뽑아 비교한다
    private bool IsInGameScene()
    {
        if (string.IsNullOrEmpty(targetScenePath)) return false;

        int target = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(targetScenePath);
        return target >= 0 &&
               UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == target;
    }

    // 호스트가 LoadScene을 부르면 Fusion이 접속한 전원의 이 콜백을 부른다.
    // 버튼을 누른 사람만 로딩 화면을 띄우면 나머지는 인트로 씬이 사라지는 걸 그대로 보게 된다
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // 세션에 붙는 중 일어나는 초기 씬 동기화까지 잡으면 대기방이 뜨기도 전에 로딩 화면이 깜빡인다
        if (!inLobby) return;

        SceneLoadStarted?.Invoke();
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
