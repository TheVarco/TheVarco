using UnityEngine;
using UnityEngine.UI;

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

        // 버튼 명령과 상태 변경 이벤트를 제공하는 게임 흐름 관리자
        private GameFlowCoordinator coordinator;
        // 결과 화면 진입 전 커서 잠금 상태
        private CursorLockMode previousLockMode;
        // 결과 화면 진입 전 커서 표시 상태
        private bool previousCursorVisible;
        // 커서 상태 중복 저장을 막는 값
        private bool cursorStateCaptured;

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
            RestoreCursor();
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
            startButton.gameObject.SetActive(
                state.State is GameFlowState.Failed or GameFlowState.Succeeded);
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

                case GameFlowState.Succeeded:
                    titleText.text = "임무 성공";
                    titleText.color = new Color(0.45f, 1f, 0.72f);
                    reasonText.text = "최종 구역의 출구에 도달했습니다.";
                    waitingText.text = "호스트의 결정을 기다리는 중...";
                    break;

                case GameFlowState.Restoring:
                    titleText.text = "체크포인트 복원 중";
                    titleText.color = new Color(0.55f, 0.9f, 1f);
                    reasonText.text = "잠시만 기다려 주세요.";
                    break;
            }
        }

        // 결과 화면 진입 전 커서 상태를 한 번만 저장
        private void CaptureAndReleaseCursor()
        {
            if (!cursorStateCaptured)
            {
                cursorStateCaptured = true;
                previousLockMode = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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
