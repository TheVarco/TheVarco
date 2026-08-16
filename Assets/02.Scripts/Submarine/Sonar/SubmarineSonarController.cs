using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

// 잠수함 소나 상태 동기화
// 호스트가 소나 대상 탐지와 접촉점 정렬 수행
// 가까운 접촉점 최대 서른두 개 복제
// 핑 순번과 공개 틱과 만료 틱으로 표시 시점 통일
// 각 피어는 복제 결과로 파동과 잔상과 소리 재생

public struct SonarEchoNetworkData : INetworkStruct
{
    public Vector2 NormalizedPosition;
    public int Category;
    public int VerticalDirection;
    // Fusion 구조 제한 대응
    public int RevealTickRaw;
    public int ExpireTickRaw;
}

[DisallowMultipleComponent]
/// <summary>
/// 일정 간격으로 소나 핑을 발생시키고 탐지된 대상의 월드 위치를 소나 화면 좌표로 변환
/// </summary>
public sealed class SubmarineSonarController : NetworkBehaviour
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

    [Header("Audio")]
    [SerializeField] private AudioSource sonarAudioSource;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.004f, 0.035f, 0.025f, 0.98f);
    [SerializeField] private Color gridColor = new Color(0.12f, 0.55f, 0.32f, 0.45f);
    [SerializeField] private Color pulseColor = new Color(0.36f, 1f, 0.62f, 0.95f);
    [SerializeField] private Color creatureColor = new Color(1f, 0.28f, 0.08f, 1f);
    [SerializeField] private Color itemColor = new Color(0.35f, 1f, 0.32f, 1f);
    [SerializeField] private Color pointOfInterestColor = new Color(0.12f, 0.95f, 0.9f, 1f);
    [SerializeField] private Color equipmentColor = new Color(1f, 0.82f, 0.12f, 1f);

    [Header("Events")]
    [Tooltip("각 핑이 시작될 때 호출")]
    // TODO : 소나 사운드
    [SerializeField] private UnityEvent onPing = new UnityEvent();

    // 한 번의 핑에서 위치를 스냅샷으로 저장한 접촉점들
    private readonly List<SonarEchoVisual> echoes = new List<SonarEchoVisual>();
    private readonly List<SonarEchoNetworkData> networkEchoBuffer = new List<SonarEchoNetworkData>(32);
    private float nextPingTime;
    private float pingStartedAt = float.NegativeInfinity;
    private int lastRenderedPingSequence = -1;

    [Networked] private TickTimer NetworkedPingTimer { get; set; }
    [Networked] private int NetworkedPingStartedTickRaw { get; set; }
    [Networked] private int NetworkedPingSequence { get; set; }
    [Networked] private int NetworkedEchoCount { get; set; }
    [Networked, Capacity(32)] private NetworkArray<SonarEchoNetworkData> NetworkedEchoes => default;

    private bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

    public float DetectionRange => detectionRange;
    public UnityEvent OnPing => onPing;
    public SubmarineSonarGraphic Display => display;

    // 소나 표시 색상과 UI 참조 초기화
    private void Awake()
    {
        CacheSonarAudioSource();

        // 표시 컴포넌트를 찾고 카테고리별 색상 팔레트 전달
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
            pointOfInterestColor,
            equipmentColor);
    }

    // 로컬 소나 첫 핑 시점 초기화
    private void OnEnable()
    {
        // 활성화 직후 첫 Update에서 핑을 시작하도록 현재 시간 저장
        nextPingTime = Time.time;
    }

    // 소나 네트워크 배열과 첫 핑 타이머 초기화
    public override void Spawned()
    {
        // 호스트만 접촉점 수와 핑 순번과 시작 틱을 기록
        if (Object.HasStateAuthority)
        {
            NetworkedEchoCount = 0;
            NetworkedPingSequence = 0;
            NetworkedPingStartedTickRaw = Runner.Tick.Raw;
            NetworkedPingTimer = TickTimer.CreateFromSeconds(Runner, 0f);
        }
        // 각 피어가 첫 핑 이벤트를 한 번 재생하도록 렌더 캐시 초기화
        lastRenderedPingSequence = -1;
    }

    // 호스트 틱에서 핑 타이머 만료와 다음 핑 예약 처리
    public override void FixedUpdateNetwork()
    {
        // 권한과 타이머를 확인한 뒤 네트워크 접촉점 갱신
        if (!Object.HasStateAuthority || !NetworkedPingTimer.ExpiredOrNotRunning(Runner))
            return;

        BeginNetworkPing();
        NetworkedPingTimer = TickTimer.CreateFromSeconds(Runner, pingInterval);
    }

    // 네트워크 소나 프레임 렌더
    public override void Render()
    {
        // 복제 상태를 각 피어의 UI 데이터로 변환
        RenderNetworkSonar();
    }

    // 네트워크가 없는 씬의 로컬 소나 갱신
    private void Update()
    {
        // 핑 간격과 파동 진행률과 접촉점 잔상 계산
        if (IsNetworkActive)
            return;

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

    // 로컬 씬에서 현재 탐지 가능한 소나 대상 수집
    private void BeginPing(float now)
    {
        // 소나 원점에서 활성 대상을 검사해 공개 시간과 만료 시간 생성
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
        PlaySonarAudio();
    }

    // 호스트가 현재 소나 접촉점을 수집해 네트워크 배열 갱신
    private void BeginNetworkPing()
    {
        // 현재 틱과 틱 간격과 소나 원점을 이번 핑의 공통 기준으로 고정
        Tick nowTick = Runner.Tick;
        float tickDelta = Runner.DeltaTime;
        Vector3 origin = sonarOrigin != null ? sonarOrigin.position : transform.position;

        // 이전 계산 결과를 제거하고 아직 유효한 잔상부터 다시 수집
        networkEchoBuffer.Clear();

        // 이전 핑 잔상 보존
        int existingCount = Mathf.Min(NetworkedEchoCount, NetworkedEchoes.Length);
        for (int i = 0; i < existingCount; i++)
        {
            SonarEchoNetworkData existing = NetworkedEchoes.Get(i);
            if (existing.ExpireTickRaw > nowTick.Raw)
                networkEchoBuffer.Add(existing);
        }

        foreach (SonarTarget target in SonarTarget.ActiveTargets)
        {
            // 탐지 범위와 대상 활성 조건을 통과한 접촉점만 처리
            if (!ShouldDetect(target, origin, out Vector3 worldOffset, out float distance))
                continue;

            // 월드 오프셋을 잠수함 로컬 평면 좌표와 공개 시점으로 변환
            Vector3 localOffset = Quaternion.Inverse(transform.rotation) * worldOffset;
            int revealTicks = Mathf.Max(0, Mathf.RoundToInt(
                pulseDuration * Mathf.Clamp01(distance / detectionRange) / tickDelta));
            Tick revealTick = nowTick.Next(revealTicks);
            Tick expireTick = revealTick.Next(Mathf.Max(1, Mathf.CeilToInt(echoLingerDuration / tickDelta)));

            networkEchoBuffer.Add(new SonarEchoNetworkData
            {
                NormalizedPosition = new Vector2(localOffset.x, localOffset.z) / detectionRange,
                Category = (int)target.Category,
                VerticalDirection = (int)GetVerticalDirection(localOffset.y, verticalThreshold),
                RevealTickRaw = revealTick.Raw,
                ExpireTickRaw = expireTick.Raw
            });
        }

        // 가까운 접촉점 우선
        networkEchoBuffer.Sort((left, right) =>
            left.NormalizedPosition.sqrMagnitude.CompareTo(right.NormalizedPosition.sqrMagnitude));

        // 가까운 순서대로 배열 용량까지만 네트워크 상태에 기록
        int count = Mathf.Min(networkEchoBuffer.Count, NetworkedEchoes.Length);
        for (int i = 0; i < count; i++)
            NetworkedEchoes.Set(i, networkEchoBuffer[i]);

        // 접촉점 수와 핑 시작 틱과 순번을 마지막에 함께 갱신
        NetworkedEchoCount = count;
        NetworkedPingStartedTickRaw = nowTick.Raw;
        NetworkedPingSequence++;
    }

    // 복제된 소나 상태를 현재 피어의 UI 프레임으로 변환
    private void RenderNetworkSonar()
    {
        // 네트워크와 표시 대상이 준비되지 않으면 렌더 작업 생략
        if (!IsNetworkActive || display == null)
            return;

        // Fusion 틱을 초 단위 시간과 파동 진행률로 변환
        float tickDelta = Runner.DeltaTime;
        float now = Runner.Tick.Raw * tickDelta;
        float startedAt = NetworkedPingStartedTickRaw * tickDelta;
        float elapsed = now - startedAt;
        float normalizedPulse = elapsed >= 0f && elapsed <= pulseDuration
            ? Mathf.Clamp01(elapsed / pulseDuration)
            : -1f;

        // 만료되지 않은 복제 접촉점만 시각 데이터로 재구성
        echoes.Clear();
        int count = Mathf.Min(NetworkedEchoCount, NetworkedEchoes.Length);
        for (int i = 0; i < count; i++)
        {
            SonarEchoNetworkData echo = NetworkedEchoes.Get(i);
            if (echo.ExpireTickRaw <= Runner.Tick.Raw)
                continue;

            echoes.Add(new SonarEchoVisual(
                echo.NormalizedPosition,
                (SonarTargetCategory)echo.Category,
                (SonarVerticalDirection)echo.VerticalDirection,
                echo.RevealTickRaw * tickDelta,
                echo.ExpireTickRaw * tickDelta));
        }

        // 새 핑 순번을 처음 본 프레임에서만 핑 이벤트 실행
        if (lastRenderedPingSequence != NetworkedPingSequence)
        {
            lastRenderedPingSequence = NetworkedPingSequence;
            if (NetworkedPingSequence > 0)
            {
                onPing.Invoke();
                PlaySonarAudio();
            }
        }

        // 파동 진행률과 현재 시간과 접촉점 목록을 소나 UI에 전달
        display.SetFrame(normalizedPulse, now, echoes);
    }

    private void PlaySonarAudio()
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library == null || library.sonarPing == null)
            return;

        CacheSonarAudioSource();
        sonarAudioSource.PlayOneShot(library.sonarPing, 0.65f);
    }

    private void CacheSonarAudioSource()
    {
        if (sonarAudioSource == null)
            sonarAudioSource = gameObject.AddComponent<AudioSource>();

        sonarAudioSource.playOnAwake = false;
        sonarAudioSource.loop = false;
        sonarAudioSource.spatialBlend = 1f;
        sonarAudioSource.rolloffMode = AudioRolloffMode.Linear;
        sonarAudioSource.minDistance = 2f;
        sonarAudioSource.maxDistance = 10f;
    }

    // 소나 대상이 현재 핑에 포함될 수 있는지 판정
    private bool ShouldDetect(
        SonarTarget target,
        Vector3 origin,
        out Vector3 worldOffset,
        out float distance)
    {
        // 대상 활성 상태와 거리와 범주 조건으로 탐지 여부 결정
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
        // 월드 위치 차이를 기준 회전의 로컬 평면 좌표로 정규화
        if (range <= 0f)
            return Vector2.zero;

        Vector3 localOffset = Quaternion.Inverse(sourceRotation) * (targetPosition - sourcePosition);
        return new Vector2(localOffset.x, localOffset.z) / range;
    }

    /// <summary>높이 차이를 위, 같은 높이, 아래 중 하나로 분류</summary>
    public static SonarVerticalDirection GetVerticalDirection(float localHeight, float threshold)
    {
        // 높이 임계값을 기준으로 위와 아래와 같은 높이 분류
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
        // 공개 전과 만료 후를 제외하고 남은 시간 비율 계산
        if (now < revealTime || now >= expireTime || expireTime <= revealTime)
            return 0f;

        return 1f - Mathf.InverseLerp(revealTime, expireTime, now);
    }

    // 인스펙터 소나 시간과 거리 설정값 보정
    private void OnValidate()
    {
        // 음수와 영 값이 들어오지 않도록 최소 범위 적용
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
