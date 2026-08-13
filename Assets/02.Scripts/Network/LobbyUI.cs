using Fusion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// 인트로 씬의 대기방 화면. 방을 만들거나 참가하면 뜨고, 게임 씬으로 넘어가면 사라진다.
//
// 네트워크는 NetworkTestStarter가 전부 담당하고, 여기서는 그쪽이 알려주는 이벤트를 듣고
// 화면만 바꾼다. 한 파일에 두면 UI를 고칠 때마다 네트워크 코드를 열어야 하고,
// 팀원들이 각자 테스트 씬에서 쓰는 스크립트라 건드릴수록 그쪽이 깨질 위험이 커진다.
public class LobbyUI : MonoBehaviour
{
    [Tooltip("비워두면 같은 씬에서 자동으로 찾는다")]
    [SerializeField] private NetworkTestStarter starter;

    [Header("대기방")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private Text roomNameText;
    [Tooltip("호스트만 누를 수 있다. 참가자에게는 회색으로 보인다")]
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button backButton;
    [Tooltip("PlayerSlot_1 ~ 4 오브젝트를 순서대로 연결. 안의 Text는 알아서 찾는다")]
    [SerializeField] private GameObject[] playerSlots = new GameObject[4];

    [Header("로딩 화면 (씬 이동 중 오프닝 재생)")]
    [SerializeField] private GameObject loadingOverlay;
    [Tooltip("비워두면 검은 화면만 표시된다")]
    [SerializeField] private VideoPlayer openingVideo;
    [Tooltip("영상이 끝나지 않아도 이 시간이 지나면 걷어낸다 (재생 실패로 영원히 막히는 걸 방지)")]
    [SerializeField] private float maxOverlaySeconds = 40f;

    // 씬을 넘어 들고 간 로딩 화면. 로드가 끝나면 이걸로 찾아 지운다
    private GameObject carriedOverlay;

    // 오버레이는 "씬 로드 완료"와 "영상 재생 완료"가 둘 다 돼야 걷힌다.
    // 로드가 먼저 끝나도 영상을 자르지 않고, 영상이 먼저 끝나도 반쯤 로드된 맵을 보여주지 않는다
    private bool sceneReady;
    private bool videoDone;
    private float overlayTimer;
    private bool overlayUp;

    void Awake()
    {
        if (starter == null) starter = FindFirstObjectByType<NetworkTestStarter>();
        if (starter == null)
        {
            Debug.LogWarning("[LobbyUI] NetworkTestStarter를 찾지 못했습니다. 대기방이 동작하지 않습니다.");
            return;
        }

        starter.LobbyEntered += ShowLobby;
        starter.PlayerListChanged += RefreshPlayerSlots;
        starter.SceneLoadStarted += ShowLoadingOverlay;
        starter.GameSceneLoaded += OnGameSceneLoaded;

        if (gameStartButton != null) gameStartButton.onClick.AddListener(OnClickGameStart);
        if (backButton != null) backButton.onClick.AddListener(OnClickBack);

        WarnIfMissing(lobbyPanel, "Lobby Panel");
        WarnIfMissing(roomNameText, "Room Name Text");
        WarnIfMissing(gameStartButton, "Game Start Button");
        WarnIfMissing(backButton, "Back Button");
        WarnIfMissing(loadingOverlay, "Loading Overlay");
        if (playerSlots == null || playerSlots.Length == 0)
            Debug.LogWarning("[LobbyUI] 'Player Slots'가 비어 있습니다.", this);
        else
            for (int i = 0; i < playerSlots.Length; i++)
                WarnIfMissing(playerSlots[i], $"Player Slots[{i}]");

        HideAll();
    }

    // 비어 있으면 그 부분만 조용히 동작을 안 해서 원인 찾는 데 오래 걸린다
    private void WarnIfMissing(UnityEngine.Object reference, string fieldName)
    {
        if (reference == null)
            Debug.LogWarning($"[LobbyUI] '{fieldName}' 칸이 비어 있습니다. 인스펙터에서 연결해주세요.", this);
    }

    void OnDestroy()
    {
        if (starter == null) return;

        starter.LobbyEntered -= ShowLobby;
        starter.PlayerListChanged -= RefreshPlayerSlots;
        starter.SceneLoadStarted -= ShowLoadingOverlay;
        starter.GameSceneLoaded -= OnGameSceneLoaded;
    }

    // 방을 만들거나 참가한 직후. 아직 게임 씬으로 안 넘어간 상태다
    private void ShowLobby()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        if (roomNameText != null) roomNameText.text = $"방 이름: {starter.RoomName}";

        // 참가자에게 버튼을 숨기지 않고 회색으로 남겨두면 자리가 안 움직이고,
        // "호스트가 누르는 것"임이 드러난다
        if (gameStartButton != null) gameStartButton.interactable = starter.IsHost;

        RefreshPlayerSlots();
    }

    private void RefreshPlayerSlots()
    {
        if (playerSlots == null || starter == null) return;

        int index = 0;
        foreach (PlayerRef player in starter.ActivePlayers)
        {
            if (index >= playerSlots.Length) break;

            SetSlotText(index, player == starter.LocalPlayer
                ? $"플레이어 {player.PlayerId} (나)"
                : $"플레이어 {player.PlayerId}");
            index++;
        }

        for (; index < playerSlots.Length; index++)
            SetSlotText(index, "비어있음");
    }

    private void SetSlotText(int index, string value)
    {
        if (playerSlots[index] == null) return;

        Text label = playerSlots[index].GetComponentInChildren<Text>(true);
        if (label != null) label.text = value;
    }

    private void OnClickGameStart()
    {
        if (!starter.IsHost) return;

        // 로딩 화면은 SceneLoadStarted가 띄운다. 여기서 직접 띄우면 누른 사람 화면만 덮인다
        starter.RequestStartGame();
    }

    private void OnClickBack()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        starter.LeaveSession();
    }

    // 로딩 화면은 씬이 바뀌는 동안 화면을 덮는 게 일인데, 인트로 씬과 함께 파괴되면 의미가 없다.
    // DontDestroyOnLoad로 들고 넘어갔다가 로드가 끝나면 지운다
    private void ShowLoadingOverlay()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadingOverlay == null)
        {
            videoDone = true; // 덮을 화면이 없으면 기다릴 이유도 없다
            return;
        }

        loadingOverlay.SetActive(true);
        overlayUp = true;
        overlayTimer = 0f;

        if (openingVideo != null && openingVideo.clip != null)
        {
            openingVideo.isLooping = false; // 반복이면 끝나는 시점이 영영 안 온다
            openingVideo.loopPointReached += OnVideoFinished;
            openingVideo.Play();
        }
        else
        {
            videoDone = true; // 영상이 없으면 로드가 끝나는 대로 넘어간다
        }

        // DontDestroyOnLoad는 루트 오브젝트에만 걸린다. 자식에 걸면 경고만 뜨고 그대로 사라진다
        carriedOverlay = loadingOverlay.transform.root.gameObject;
        DontDestroyOnLoad(carriedOverlay);
        DontDestroyOnLoad(transform.root.gameObject); // 나도 살아야 로드 완료를 받고 걷어낸다
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        videoDone = true;
        TryFinish();
    }

    // 게임 씬 로드 완료
    private void OnGameSceneLoaded()
    {
        sceneReady = true;
        TryFinish();
    }

    // 영상 재생에 실패하면 loopPointReached가 영영 안 온다.
    // 그러면 게임 화면이 덮인 채로 아무것도 못 하게 되므로 시간 상한을 둔다
    void Update()
    {
        if (!overlayUp || videoDone) return;

        overlayTimer += Time.unscaledDeltaTime;
        if (overlayTimer < maxOverlaySeconds) return;

        Debug.LogWarning($"[LobbyUI] 오프닝이 {maxOverlaySeconds}초 안에 끝나지 않아 강제로 넘어갑니다.", this);
        videoDone = true;
        TryFinish();
    }

    // 씬 로드와 영상이 둘 다 끝나야 걷는다. 안 지우면 게임 화면 위에 그대로 남는다
    private void TryFinish()
    {
        if (!sceneReady || !videoDone) return;

        if (openingVideo != null) openingVideo.loopPointReached -= OnVideoFinished;

        starter.ReleasePlayerSpawn(); // 화면이 열린 다음에 캐릭터가 나온다

        HideAll();
        if (carriedOverlay != null) Destroy(carriedOverlay);
        Destroy(transform.root.gameObject);
    }

    private void HideAll()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
    }
}
