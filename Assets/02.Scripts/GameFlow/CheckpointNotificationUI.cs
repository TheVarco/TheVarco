using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Varco.GameFlow
{
    // 체크포인트 전진 시 우측 중앙에 급정지 모션 알림을 표시한다.
    [DisallowMultipleComponent]
    public sealed class CheckpointNotificationUI : MonoBehaviour
    {
        private const string NotificationText = "Check Point!";

        private const float TargetX = -72f;
        private const float StartX = 420f;
        private const float OvershootX = TargetX - 46f;
        private const float ReboundX = TargetX + 14f;
        private const float SecondOvershootX = TargetX - 6f;

        private const float EnterDuration = 0.22f;
        private const float ReboundDuration = 0.20f;
        private const float SettleDuration = 0.16f;
        private const float HoldDuration = 0.77f;
        private const float ExitDuration = 0.30f;

        [SerializeField] private Font checkpointFont;

        private GameFlowCoordinator coordinator;
        private RectTransform notificationTransform;
        private CanvasGroup canvasGroup;
        private GameObject notificationObject;
        private Coroutine animationRoutine;
        private bool hasReceivedInitialState;
        private int highestObservedCheckpointZone = CheckpointZoneProgression.FirstZone;

        // 게임 흐름 상태를 구독하고 첫 상태를 표시 기준으로 저장한다.
        public void Initialize(GameFlowCoordinator targetCoordinator)
        {
            Unsubscribe();
            coordinator = targetCoordinator;
            hasReceivedInitialState = false;
            highestObservedCheckpointZone = CheckpointZoneProgression.FirstZone;

            EnsureView();
            HideImmediately();

            if (coordinator != null)
                coordinator.FlowStateChanged += HandleFlowStateChanged;
        }

        // 컴포넌트가 제거될 때 이벤트와 실행 중인 모션을 정리한다.
        private void OnDestroy()
        {
            Unsubscribe();
            StopAnimation();
        }

        // 비활성 Canvas에서는 표시를 중단하되 진행 구역 추적은 유지한다.
        private void OnDisable()
        {
            StopAnimation();
            HideImmediately();
        }

        // 네트워크로 수신한 첫 상태는 기준값으로만 사용하고 이후 전진만 표시한다.
        private void HandleFlowStateChanged(GameFlowReplicatedState state)
        {
            int receivedZone = Mathf.Max(CheckpointZoneProgression.FirstZone, state.CheckpointZone);
            if (!hasReceivedInitialState)
            {
                hasReceivedInitialState = true;
                highestObservedCheckpointZone = receivedZone;
                return;
            }

            bool advanced = receivedZone > highestObservedCheckpointZone;
            highestObservedCheckpointZone = Mathf.Max(highestObservedCheckpointZone, receivedZone);

            if (advanced && state.State == GameFlowState.Playing && isActiveAndEnabled)
                PlayNotification();
        }

        // 기존 재생을 취소하고 급브레이크 알림을 처음부터 다시 재생한다.
        private void PlayNotification()
        {
            if (!EnsureView())
                return;

            StopAnimation();
            notificationObject.SetActive(true);
            animationRoutine = StartCoroutine(PlayNotificationRoutine());
        }

        // 진입, 압축, 두 번의 반동, 유지, 퇴장을 순서대로 처리한다.
        private IEnumerator PlayNotificationRoutine()
        {
            SetVisualState(StartX, Vector2.one, 0f);

            yield return AnimateSegment(EnterDuration, progress =>
            {
                float eased = EaseOutQuart(progress);
                float alpha = Mathf.Clamp01(progress * 3f);
                SetVisualState(
                    Mathf.LerpUnclamped(StartX, OvershootX, eased),
                    Vector2.LerpUnclamped(Vector2.one, new Vector2(0.88f, 1.10f), eased),
                    alpha);
            });

            yield return AnimateSegment(ReboundDuration, progress =>
            {
                float eased = EaseOutCubic(progress);
                SetVisualState(
                    Mathf.LerpUnclamped(OvershootX, ReboundX, eased),
                    Vector2.LerpUnclamped(new Vector2(0.88f, 1.10f), new Vector2(1.04f, 0.97f), eased),
                    1f);
            });

            float secondBounceDuration = SettleDuration * 0.5f;
            yield return AnimateSegment(secondBounceDuration, progress =>
            {
                float eased = EaseOutCubic(progress);
                SetVisualState(
                    Mathf.LerpUnclamped(ReboundX, SecondOvershootX, eased),
                    Vector2.LerpUnclamped(new Vector2(1.04f, 0.97f), new Vector2(0.98f, 1.02f), eased),
                    1f);
            });

            yield return AnimateSegment(secondBounceDuration, progress =>
            {
                float eased = EaseOutCubic(progress);
                SetVisualState(
                    Mathf.LerpUnclamped(SecondOvershootX, TargetX, eased),
                    Vector2.LerpUnclamped(new Vector2(0.98f, 1.02f), Vector2.one, eased),
                    1f);
            });

            yield return WaitUnscaled(HoldDuration);

            yield return AnimateSegment(ExitDuration, progress =>
            {
                float eased = EaseInCubic(progress);
                SetVisualState(
                    Mathf.LerpUnclamped(TargetX, TargetX + 40f, eased),
                    Vector2.one,
                    1f - eased);
            });

            HideImmediately();
            animationRoutine = null;
        }

        // 현재 Canvas 아래에 텍스트와 그림자 표시 오브젝트를 한 번만 생성한다.
        private bool EnsureView()
        {
            if (notificationObject != null)
                return true;

            if (checkpointFont == null)
            {
                Debug.LogError("[GameFlow] CheckpointNotificationUI requires the CookieRun Bold font.", this);
                return false;
            }

            notificationObject = new GameObject(
                "CheckpointNotification",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup),
                typeof(Text),
                typeof(Shadow));
            notificationObject.layer = gameObject.layer;
            notificationObject.transform.SetParent(transform, false);
            notificationObject.transform.SetAsLastSibling();

            notificationTransform = notificationObject.GetComponent<RectTransform>();
            notificationTransform.anchorMin = new Vector2(1f, 0.5f);
            notificationTransform.anchorMax = new Vector2(1f, 0.5f);
            notificationTransform.pivot = new Vector2(1f, 0.5f);
            notificationTransform.sizeDelta = new Vector2(420f, 96f);

            canvasGroup = notificationObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Text text = notificationObject.GetComponent<Text>();
            text.text = NotificationText;
            text.font = checkpointFont;
            text.fontSize = 56;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleRight;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            Shadow shadow = notificationObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0.025f, 0.10f, 0.18f, 0.78f);
            shadow.effectDistance = new Vector2(4f, -4f);
            shadow.useGraphicAlpha = true;
            return true;
        }

        // 위치, 압축 비율, 투명도를 한 프레임에 함께 적용한다.
        private void SetVisualState(float anchoredX, Vector2 scale, float alpha)
        {
            notificationTransform.anchoredPosition = new Vector2(anchoredX, 0f);
            notificationTransform.localScale = new Vector3(scale.x, scale.y, 1f);
            canvasGroup.alpha = alpha;
        }

        private void HideImmediately()
        {
            if (notificationObject == null)
                return;

            SetVisualState(StartX, Vector2.one, 0f);
            notificationObject.SetActive(false);
        }

        private void StopAnimation()
        {
            if (animationRoutine == null)
                return;

            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        private void Unsubscribe()
        {
            if (coordinator != null)
                coordinator.FlowStateChanged -= HandleFlowStateChanged;

            coordinator = null;
        }

        private static IEnumerator AnimateSegment(float duration, Action<float> apply)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                apply(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            apply(1f);
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static float EaseOutQuart(float value)
        {
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse * inverse;
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInCubic(float value)
        {
            return value * value * value;
        }
    }
}
