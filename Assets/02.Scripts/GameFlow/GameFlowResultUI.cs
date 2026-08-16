using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Varco.GameFlow
{
    // 게임 결과 상태를 표시하는 화면 UI
    [DisallowMultipleComponent]
    public sealed class GameFlowResultUI : MonoBehaviour
    {
        // 씬에 배치된 결과 화면 전체 패널
        [SerializeField] private GameObject panel;
        // 성공 실패 복원 상태를 표시하는 제목
        [SerializeField] private Text titleText;
        // 실패 원인 또는 성공 내용을 표시하는 본문
        [SerializeField] private Text reasonText;
        // 현재 저장된 체크포인트 구역을 표시하는 문구
        [SerializeField] private Text checkpointText;
        // 클라이언트에게 호스트 대기 상태를 표시하는 문구
        [SerializeField] private Text waitingText;
        // 실패 후 체크포인트 복원을 요청하는 버튼
        [SerializeField] private Button restartButton;
        // 결과 화면에서 시작 화면 이동을 요청하는 버튼
        [SerializeField] private Button startButton;

        [Header("성공 영상")]
        // 성공 시 결과 UI 대신 재생할 엔딩 영상
        [SerializeField] private VideoClip successMovie;
        // 영상 디코더 준비가 끝나지 않을 때의 안전 제한 시간
        [SerializeField, Min(1f)] private float prepareTimeoutSeconds = 10f;
        // 알려진 영상 길이에 더하는 재생 정지 감지 여유 시간
        [SerializeField, Min(1f)] private float playbackTimeoutPaddingSeconds = 10f;
        // 플랫폼에서 영상 길이를 읽지 못할 때 사용할 전체 재생 제한 시간
        [SerializeField, Min(1f)] private float unknownDurationTimeoutSeconds = 180f;

        // 버튼 명령과 상태 변경 이벤트를 제공하는 게임 흐름 관리자
        private GameFlowCoordinator coordinator;
        // 결과 화면 진입 전 커서 잠금 상태
        private CursorLockMode previousLockMode;
        // 결과 화면 진입 전 커서 표시 상태
        private bool previousCursorVisible;
        // 커서 상태 중복 저장을 막는 값
        private bool cursorStateCaptured;
        // 모든 기존 HUD보다 위에 표시할 성공 영상 오버레이
        private GameObject successMovieOverlay;
        // 엔딩 영상 재생기
        private VideoPlayer successVideoPlayer;
        // 영상 출력용 런타임 텍스처
        private RenderTexture successMovieTexture;
        // 원본 영상 비율을 유지하는 화면 요소
        private AspectRatioFitter successMovieAspectRatio;
        // 준비 또는 재생 정지를 감지할 실시간 마감 시각
        private float successMovieDeadline;
        // 같은 성공 상태에서 영상이 중복 실행되는 것을 막는 상태
        private SuccessMovieState successMovieState;

        private enum SuccessMovieState
        {
            None,
            Preparing,
            Playing,
            Finished
        }

        private void Awake()
        {
            GameUIFont.Apply(titleText);
            GameUIFont.Apply(reasonText);
            GameUIFont.Apply(checkpointText);
            GameUIFont.Apply(waitingText);
        }

        // 게임 흐름 관리자 연결과 버튼 이벤트 등록
        public void Initialize(GameFlowCoordinator targetCoordinator)
        {
            Unsubscribe();
            coordinator = targetCoordinator;

            if (!HasRequiredReferences())
            {
                Debug.LogError("[GameFlow] GameFlowResultUI references are not configured.", this);
                return;
            }

            restartButton.onClick.RemoveListener(HandleRestartClicked);
            restartButton.onClick.AddListener(HandleRestartClicked);
            startButton.onClick.RemoveListener(HandleStartClicked);
            startButton.onClick.AddListener(HandleStartClicked);

            if (coordinator == null)
                return;

            coordinator.FlowStateChanged += HandleFlowStateChanged;
            HandleFlowStateChanged(new GameFlowReplicatedState(
                coordinator.State,
                coordinator.FailureReason,
                coordinator.CurrentCheckpointZone,
                0));
        }

        // 오브젝트 제거 전 이벤트와 커서 상태 정리
        private void OnDestroy()
        {
            Unsubscribe();
            TearDownSuccessMovie();
            RestoreCursor();
        }

        // Time.timeScale과 무관하게 영상 준비 및 재생 정지 상태 감시
        private void Update()
        {
            if (successMovieState is not (SuccessMovieState.Preparing or SuccessMovieState.Playing)
                || Time.realtimeSinceStartup < successMovieDeadline)
            {
                return;
            }

            string phase = successMovieState == SuccessMovieState.Preparing ? "준비" : "재생";
            Debug.LogWarning($"[GameFlow] 성공 영상 {phase} 시간이 제한을 초과했습니다.", this);
            CompleteSuccessMovie();
        }

        // 체크포인트 복원 명령 전달
        private void HandleRestartClicked()
        {
            coordinator?.RequestRestartFromCheckpoint();
        }

        // 시작 화면 이동 명령 전달
        private void HandleStartClicked()
        {
            coordinator?.RequestReturnToStart();
        }

        // 등록된 이벤트와 버튼 명령 해제
        private void Unsubscribe()
        {
            if (coordinator != null)
                coordinator.FlowStateChanged -= HandleFlowStateChanged;

            if (restartButton != null)
                restartButton.onClick.RemoveListener(HandleRestartClicked);
            if (startButton != null)
                startButton.onClick.RemoveListener(HandleStartClicked);
        }

        // 인스펙터에서 연결해야 하는 필수 참조 검사
        private bool HasRequiredReferences()
        {
            return panel != null
                && titleText != null
                && reasonText != null
                && checkpointText != null
                && waitingText != null
                && restartButton != null
                && startButton != null;
        }

        // 수신한 게임 상태에 맞춰 화면 문구와 버튼 권한 갱신
        private void HandleFlowStateChanged(GameFlowReplicatedState state)
        {
            if (!HasRequiredReferences())
                return;

            // 성공 상태에서는 기존 결과 UI를 띄우지 않고 모든 HUD를 덮는 영상을 재생
            if (state.State == GameFlowState.Succeeded)
            {
                panel.SetActive(false);
                CaptureAndHideCursor();
                PlaySuccessMovieOnce();
                return;
            }

            // 성공 외 상태로 돌아온 경우 런타임 영상 자원을 정리
            if (successMovieState != SuccessMovieState.None)
                TearDownSuccessMovie();

            // 정상 플레이 상태에서는 결과 화면 숨김
            bool visible = state.State != GameFlowState.Playing;
            panel.SetActive(visible);
            if (!visible)
            {
                RestoreCursor();
                return;
            }

            // 결과 화면에서 마우스로 버튼을 누를 수 있도록 커서 해제
            CaptureAndReleaseCursor();

            // 게임 판정 권한을 가진 호스트만 명령 버튼 활성화
            bool isAuthority = coordinator != null && coordinator.IsAuthority;
            restartButton.gameObject.SetActive(state.State == GameFlowState.Failed);
            startButton.gameObject.SetActive(state.State == GameFlowState.Failed);
            restartButton.interactable = isAuthority && state.State == GameFlowState.Failed;
            startButton.interactable = isAuthority;
            waitingText.gameObject.SetActive(!isAuthority && state.State != GameFlowState.Restoring);

            // 현재 복원 기준이 되는 체크포인트 구역 표시
            checkpointText.text =
                $"현재 체크포인트: Z{Mathf.Clamp(state.CheckpointZone, 1, 7)}";

            // 실패 성공 복원 상태별 화면 문구 적용
            switch (state.State)
            {
                case GameFlowState.Failed:
                    titleText.text = "임무 실패";
                    titleText.color = new Color(1f, 0.38f, 0.32f);
                    reasonText.text = state.FailureReason == GameFailureReason.SubmarineDestroyed
                        ? "잠수함이 파괴되었습니다."
                        : "모든 플레이어가 행동 불능 상태입니다.";
                    waitingText.text = "호스트의 결정을 기다리는 중...";
                    break;

                case GameFlowState.Restoring:
                    titleText.text = "체크포인트 복원 중";
                    titleText.color = new Color(0.55f, 0.9f, 1f);
                    reasonText.text = "잠시만 기다려 주세요.";
                    break;
            }
        }

        // 성공 상태당 엔딩 영상을 한 번만 준비하고 재생
        private void PlaySuccessMovieOnce()
        {
            if (successMovieState != SuccessMovieState.None)
                return;

            CreateSuccessMovieOverlay();
            if (successMovie == null)
            {
                Debug.LogError("[GameFlow] 성공 영상이 연결되지 않아 시작 화면으로 복귀합니다.", this);
                successMovieState = SuccessMovieState.Finished;
                RequestReturnToStartIfAuthority();
                return;
            }

            successVideoPlayer.clip = successMovie;
            successVideoPlayer.prepareCompleted += HandleSuccessMoviePrepared;
            successVideoPlayer.loopPointReached += HandleSuccessMovieFinished;
            successVideoPlayer.errorReceived += HandleSuccessMovieError;

            successMovieState = SuccessMovieState.Preparing;
            successMovieDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, prepareTimeoutSeconds);
            successVideoPlayer.Prepare();
        }

        // 검은 배경과 비율 유지 RawImage를 가진 최상위 오버레이 생성
        private void CreateSuccessMovieOverlay()
        {
            successMovieOverlay = new GameObject(
                "[GameFlow Success Movie]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = successMovieOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;

            CanvasScaler scaler = successMovieOverlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject backdrop = new("Black Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(successMovieOverlay.transform, false);
            StretchToParent(backdrop.GetComponent<RectTransform>());
            Image backdropImage = backdrop.GetComponent<Image>();
            backdropImage.color = Color.black;
            backdropImage.raycastTarget = true;

            GameObject movieImageObject = new(
                "Movie",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            movieImageObject.transform.SetParent(successMovieOverlay.transform, false);
            StretchToParent(movieImageObject.GetComponent<RectTransform>());
            RawImage movieImage = movieImageObject.GetComponent<RawImage>();
            movieImage.color = Color.white;
            movieImage.raycastTarget = false;
            successMovieAspectRatio = movieImageObject.GetComponent<AspectRatioFitter>();
            successMovieAspectRatio.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            int width = successMovie != null && successMovie.width > 0
                ? (int)successMovie.width
                : 1920;
            int height = successMovie != null && successMovie.height > 0
                ? (int)successMovie.height
                : 1080;
            successMovieAspectRatio.aspectRatio = (float)width / height;

            successMovieTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "GameFlow Success Movie",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            successMovieTexture.Create();
            movieImage.texture = successMovieTexture;

            successVideoPlayer = successMovieOverlay.AddComponent<VideoPlayer>();
            successVideoPlayer.playOnAwake = false;
            successVideoPlayer.isLooping = false;
            successVideoPlayer.waitForFirstFrame = true;
            successVideoPlayer.skipOnDrop = true;
            successVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            successVideoPlayer.targetTexture = successMovieTexture;
            successVideoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        }

        // 영상 준비 완료 후 실제 크기를 반영하고 재생 시작
        private void HandleSuccessMoviePrepared(VideoPlayer source)
        {
            if (source != successVideoPlayer || successMovieState != SuccessMovieState.Preparing)
                return;

            if (source.width > 0 && source.height > 0 && successMovieAspectRatio != null)
                successMovieAspectRatio.aspectRatio = (float)source.width / source.height;

            successMovieState = SuccessMovieState.Playing;
            float playbackLimit = successMovie != null && successMovie.length > 0d
                ? (float)successMovie.length + Mathf.Max(1f, playbackTimeoutPaddingSeconds)
                : Mathf.Max(1f, unknownDurationTimeoutSeconds);
            successMovieDeadline = Time.realtimeSinceStartup + playbackLimit;
            source.Play();
        }

        // 정상 재생 완료 처리
        private void HandleSuccessMovieFinished(VideoPlayer source)
        {
            if (source == successVideoPlayer)
                CompleteSuccessMovie();
        }

        // 디코더 오류 시 호스트가 시작 화면으로 복귀하도록 안전 처리
        private void HandleSuccessMovieError(VideoPlayer source, string message)
        {
            if (source != successVideoPlayer)
                return;

            Debug.LogError($"[GameFlow] 성공 영상 재생 오류: {message}", this);
            CompleteSuccessMovie();
        }

        // 영상 완료 후 마지막 화면을 유지한 채 호스트만 복귀 명령 실행
        private void CompleteSuccessMovie()
        {
            if (successMovieState == SuccessMovieState.Finished)
                return;

            successMovieState = SuccessMovieState.Finished;
            if (successVideoPlayer != null && successVideoPlayer.isPlaying)
                successVideoPlayer.Pause();
            RestoreCursor();
            RequestReturnToStartIfAuthority();
        }

        private void RequestReturnToStartIfAuthority()
        {
            if (coordinator != null && coordinator.IsAuthority)
                coordinator.RequestReturnToStart();
        }

        // 씬 전환 또는 상태 변경 시 영상 이벤트와 런타임 리소스 정리
        private void TearDownSuccessMovie()
        {
            if (successVideoPlayer != null)
            {
                successVideoPlayer.prepareCompleted -= HandleSuccessMoviePrepared;
                successVideoPlayer.loopPointReached -= HandleSuccessMovieFinished;
                successVideoPlayer.errorReceived -= HandleSuccessMovieError;
                successVideoPlayer.Stop();
                successVideoPlayer.targetTexture = null;
            }

            if (successMovieTexture != null)
            {
                successMovieTexture.Release();
                Destroy(successMovieTexture);
            }

            if (successMovieOverlay != null)
                Destroy(successMovieOverlay);

            successMovieOverlay = null;
            successVideoPlayer = null;
            successMovieTexture = null;
            successMovieAspectRatio = null;
            successMovieDeadline = 0f;
            successMovieState = SuccessMovieState.None;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        // 결과 화면 진입 전 커서 상태를 한 번만 저장
        private void CaptureAndReleaseCursor()
        {
            CaptureCursorState();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 엔딩 영상 위에 마우스 포인터가 표시되지 않도록 숨김
        private void CaptureAndHideCursor()
        {
            CaptureCursorState();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
        }

        private void CaptureCursorState()
        {
            if (cursorStateCaptured)
                return;

            cursorStateCaptured = true;
            previousLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
        }

        // 플레이 재개 시 기존 커서 상태 복원
        private void RestoreCursor()
        {
            if (!cursorStateCaptured)
                return;

            Cursor.lockState = previousLockMode;
            Cursor.visible = previousCursorVisible;
            cursorStateCaptured = false;
        }
    }
}
