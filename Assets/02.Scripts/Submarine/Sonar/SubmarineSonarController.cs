using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 일정 간격으로 소나 핑을 발생시키고 탐지된 대상의 월드 위치를 소나 화면 좌표로 변환
/// </summary>
[DisallowMultipleComponent]
public sealed class SubmarineSonarController : MonoBehaviour
{
    [Header("Sonar Timing")]
    [Tooltip("원 안에 표시할 최대 거리")]
    [SerializeField, Min(1f)] private float detectionRange = 30f;
    [Tooltip("핑을 시작하는 간격")]
    [SerializeField, Min(0.1f)] private float pingInterval = 2.5f;
    [Tooltip("파동이 화면 중심에서 최대 탐지 거리까지 도달하는 시간")]
    [SerializeField, Min(0.05f)] private float pulseDuration = 1.5f;
    [Tooltip("파동에 감지된 접촉점이 완전히 사라질 때까지 유지되는 시간")]
    [SerializeField, Min(0.05f)] private float echoLingerDuration = 2f;
    [Tooltip("잠수함과 대상의 높이 차이가 이 값 이상이면 위/아래 화살표를 표시")]
    [SerializeField, Min(0f)] private float verticalThreshold = 2f;

    [Header("Detection")]
    [Tooltip("탐지 거리와 장애물 검사를 시작할 위치")]
    [SerializeField] private Transform sonarOrigin;
    [Tooltip("대상과 잠수함 사이를 가리는 동굴 벽 등의 레이어")]
    [SerializeField] private LayerMask obstacleMask = 1 << 6;

    [Header("Monitor")]
    [SerializeField] private SubmarineSonarGraphic display;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.004f, 0.035f, 0.025f, 0.98f);
    [SerializeField] private Color gridColor = new Color(0.12f, 0.55f, 0.32f, 0.45f);
    [SerializeField] private Color pulseColor = new Color(0.36f, 1f, 0.62f, 0.95f);
    [SerializeField] private Color creatureColor = new Color(1f, 0.28f, 0.08f, 1f);
    [SerializeField] private Color itemColor = new Color(0.35f, 1f, 0.32f, 1f);
    [SerializeField] private Color pointOfInterestColor = new Color(0.12f, 0.95f, 0.9f, 1f);

    [Header("Events")]
    [Tooltip("각 핑이 시작될 때 호출")]
    // TODO : 소나 사운드
    [SerializeField] private UnityEvent onPing = new UnityEvent();

    // 한 번의 핑에서 위치를 스냅샷으로 저장한 접촉점들
    private readonly List<SonarEchoVisual> echoes = new List<SonarEchoVisual>();
    private float nextPingTime;
    private float pingStartedAt = float.NegativeInfinity;

    public float DetectionRange => detectionRange;
    public UnityEvent OnPing => onPing;
    public SubmarineSonarGraphic Display => display;

    private void Awake()
    {
        if (display == null)
        {
            Debug.LogError(
                "SubmarineSonarController: 소나 캔버스가 없습니다.",
                this);
            enabled = false;
            return;
        }

        display.ConfigureColors(
            backgroundColor,
            gridColor,
            pulseColor,
            creatureColor,
            itemColor,
            pointOfInterestColor);
    }

    private void OnEnable()
    {
        nextPingTime = Time.time;
    }

    private void Update()
    {
        float now = Time.time;

        // 핑 간격이 지나면 현재 대상 위치를 새로 스냅샷으로 저장
        if (now >= nextPingTime)
        {
            BeginPing(now);
            nextPingTime = now + pingInterval;
        }

        // 수명이 끝난 잔상은 역순으로 제거
        for (int i = echoes.Count - 1; i >= 0; i--)
        {
            if (now >= echoes[i].ExpireTime)
                echoes.RemoveAt(i);
        }

        if (display == null)
            return;

        // 파동이 얼마나 진행되었는지 나타내는 비율
        // 진행 중인 핑만 0~1 값을 전달하고, 파동이 끝난 동안에는 -1로 숨김
        float elapsed = now - pingStartedAt;
        float normalizedPulse = elapsed >= 0f && elapsed <= pulseDuration
            ? Mathf.Clamp01(elapsed / pulseDuration)
            : -1f;
        display.SetFrame(normalizedPulse, now, echoes);
    }

    private void BeginPing(float now)
    {
        pingStartedAt = now;
        Vector3 origin = sonarOrigin != null ? sonarOrigin.position : transform.position;

        // 이 시점의 위치를 저장하므로 대상이 움직여도 현재 핑의 점은 따라다니지 않음
        foreach (SonarTarget target in SonarTarget.ActiveTargets)
        {
            if (!ShouldDetect(target, origin, out Vector3 worldOffset, out float distance))
                continue;

            // 월드 방향을 잠수함 로컬 방향으로 바꿔 잠수함 전방(+Z)이 화면 위쪽이 되게
            Vector3 localOffset = Quaternion.Inverse(transform.rotation) * worldOffset;
            // 탐지 거리로 나누면 화면 중심 0, 가장자리 1인 정규화 좌표
            Vector2 normalizedPosition = new Vector2(localOffset.x, localOffset.z) / detectionRange;
            SonarVerticalDirection verticalDirection = GetVerticalDirection(localOffset.y, verticalThreshold);
            // 실제 거리 비율만큼 파동 도착 시간을 늦춰 파동보다 점이 먼저 보이지 않게
            float revealTime = now + pulseDuration * Mathf.Clamp01(distance / detectionRange);

            echoes.Add(new SonarEchoVisual(
                normalizedPosition,
                target.Category,
                verticalDirection,
                revealTime,
                revealTime + echoLingerDuration));
        }

        onPing.Invoke();
    }

    private bool ShouldDetect(
        SonarTarget target,
        Vector3 origin,
        out Vector3 worldOffset,
        out float distance)
    {
        worldOffset = default;
        distance = 0f;

        // 죽음/비활성 대상과 잠수함 내부에 붙은 대상은 자기 자신으로 간주해 제외
        if (target == null || !target.IsDetectable || target.transform.IsChildOf(transform))
            return false;

        worldOffset = target.Position - origin;
        distance = worldOffset.magnitude;
        if (distance > detectionRange)
            return false;

        // 장애물 레이어가 지정되어 있으면 직선 시야 검사를 수행
        if (obstacleMask.value == 0 || !Physics.Linecast(
                origin,
                target.Position,
                out RaycastHit obstacleHit,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
            return true;

        return obstacleHit.transform == target.transform
            || obstacleHit.transform.IsChildOf(target.transform);
    }

    /// <summary>
    /// 월드 위치를 잠수함 기준 -1~1 소나 좌표로 변환
    /// </summary>
    public static Vector2 WorldPositionToSonar(
        Vector3 sourcePosition,
        Quaternion sourceRotation,
        Vector3 targetPosition,
        float range)
    {
        if (range <= 0f)
            return Vector2.zero;

        Vector3 localOffset = Quaternion.Inverse(sourceRotation) * (targetPosition - sourcePosition);
        return new Vector2(localOffset.x, localOffset.z) / range;
    }

    /// <summary>높이 차이를 위, 같은 높이, 아래 중 하나로 분류</summary>
    public static SonarVerticalDirection GetVerticalDirection(float localHeight, float threshold)
    {
        threshold = Mathf.Max(0f, threshold);
        if (localHeight > threshold)
            return SonarVerticalDirection.Above;
        if (localHeight < -threshold)
            return SonarVerticalDirection.Below;
        return SonarVerticalDirection.Level;
    }

    /// <summary>접촉점이 공개된 뒤 잔상 시간 동안 1에서 0으로 감소하는 투명도를 계산.</summary>
    public static float EvaluateEchoAlpha(float now, float revealTime, float expireTime)
    {
        if (now < revealTime || now >= expireTime || expireTime <= revealTime)
            return 0f;

        return 1f - Mathf.InverseLerp(revealTime, expireTime, now);
    }

    private void OnValidate()
    {
        // Inspector나 YAML에서 잘못된 값이 들어와 0으로 나누거나 핑이 매 프레임 발생하는 것을 방지
        detectionRange = Mathf.Max(1f, detectionRange);
        pingInterval = Mathf.Max(0.1f, pingInterval);
        pulseDuration = Mathf.Max(0.05f, pulseDuration);
        echoLingerDuration = Mathf.Max(0.05f, echoLingerDuration);
        verticalThreshold = Mathf.Max(0f, verticalThreshold);
    }

    // private void OnDrawGizmosSelected()
    // {
    //     Gizmos.color = new Color(0.15f, 1f, 0.55f, 0.25f);
    //     Vector3 origin = sonarOrigin != null ? sonarOrigin.position : transform.position;
    //     Gizmos.DrawWireSphere(origin, detectionRange);
    // }
}
