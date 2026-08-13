using System.Collections;
using Fusion;
using UnityEngine;

// 잠수함 문 상태 동기화
// 클라이언트 상호작용 요청을 호스트가 검증
// 열림 상태와 전환 틱과 자동 닫힘 타이머 복제
// 통로에 플레이어가 있으면 자동 닫힘 연기
// 로컬 테스트에서는 코루틴 기반 동작 유지

public class SubmarineDoor : NetworkBehaviour, Interactable
{
    // 애니메이터 상태와 자동 닫힘 시간 설정
    [SerializeField] private string doorOpen = "IsOpen";
    [SerializeField] private string openAnimationState = "DoorOpen";
    [SerializeField] private string closeAnimationState = "DoorClose";
    [SerializeField, Min(0.05f)] private float transitionDuration = 1.5f;
    [SerializeField] private float autoCloseDelay = 5f;
    [SerializeField, Min(0.05f)] private float blockedRetryDelay = 0.5f;
    [SerializeField] private Transform obstructionCenter;
    [SerializeField] private Vector3 obstructionHalfExtents = new Vector3(0.75f, 1f, 0.5f);

    [Header("퇴장 감지 및 오디오")]
    [SerializeField] private Transform insidePoint;
    [SerializeField] private Transform outsidePoint;
    [SerializeField, Min(0.01f)] private float passageSideThreshold = 0.05f;
    [SerializeField] private AudioSource exitAudioSource;
    [SerializeField] private AudioClip exitWaterClip;
    [SerializeField] private Vector3 exitTriggerSize = new Vector3(2.5f, 3f, 0.8f);

    // 모든 피어가 공유하는 권위 상태
    [Networked] private NetworkBool NetworkedIsOpen { get; set; }
    [Networked] private TickTimer AutoCloseTimer { get; set; }
    [Networked] private int TransitionTickRaw { get; set; }

    // 로컬 테스트 상태와 렌더 적용 캐시
    private Animator doorAnimator;
    private bool localIsOpen;
    private Coroutine autoCloseRoutine;
    private bool lastAppliedOpen;
    private bool hasAppliedState;
    private Vector3 passageCenter;
    private Vector3 passageDirection;
    private Transform passageFrame;

    private bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;
    public bool IsOpen => IsNetworkActive ? NetworkedIsOpen : localIsOpen;

    // 애니메이터와 통로 검사 기준 준비
    private void Awake()
    {
        // Animator를 수집하고 장애물 검사 기준이 없으면 문 Transform 사용
        doorAnimator = GetComponent<Animator>();
        if (obstructionCenter == null)
            obstructionCenter = transform;
        CacheExitAudioSource();
        CachePassageAxis();
        EnsureExitTrigger();
    }

    // 호스트 초기값 기록과 첫 애니메이션 적용
    public override void Spawned()
    {
        // 호스트는 씬의 초기 문 상태를 네트워크 기준값으로 기록
        if (Object.HasStateAuthority)
        {
            NetworkedIsOpen = localIsOpen;
            AutoCloseTimer = TickTimer.None;
            TransitionTickRaw = Runner.Tick.Raw;
        }
        // 모든 피어가 현재 권위 상태로 애니메이터를 즉시 맞춤
        ApplyAnimatorState(true);
    }

    // 호스트만 자동 닫힘 만료와 통로 점유 검사
    public override void FixedUpdateNetwork()
    {
        // 호스트와 열린 상태와 자동 닫힘 만료 조건을 먼저 확인
        if (!Object.HasStateAuthority || !NetworkedIsOpen || !AutoCloseTimer.Expired(Runner))
            return;

        if (IsDoorwayOccupied())
        {
            AutoCloseTimer = TickTimer.CreateFromSeconds(Runner, blockedRetryDelay);
            return;
        }

        SetNetworkOpen(false);
    }

    // 복제된 열림 상태를 화면에 반영
    public override void Render()
    {
        // 마지막 적용 상태와 비교해 필요한 애니메이션만 갱신
        ApplyAnimatorState(false);
    }

    // 로컬 요청과 네트워크 요청 경로 분리
    public void Interact(GameObject interactor)
    {
        // 로컬 모드면 즉시 전환하고 네트워크 모드면 소유자 RPC 요청
        if (!IsNetworkActive)
        {
            SetLocalOpen(!localIsOpen);
            return;
        }

        NetworkObject playerObject = interactor != null
            ? interactor.GetComponentInParent<NetworkObject>()
            : null;
        if (playerObject == null || !playerObject.IsValid || !playerObject.HasInputAuthority)
            return;

        RPC_RequestToggle(playerObject.Id);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    // 요청자 권한과 상호작용 거리 검증
    private void RPC_RequestToggle(NetworkId playerId, RpcInfo info = default)
    {
        // 요청 PlayerRef와 NetworkId와 문까지의 거리를 호스트에서 검증
        if (!Runner.TryFindObject(playerId, out NetworkObject playerObject)
            || playerObject.InputAuthority != info.Source
            || Vector3.Distance(playerObject.transform.position, transform.position) > 3.5f)
        {
            return;
        }

        SetNetworkOpen(!NetworkedIsOpen);
    }

    // 현재 문 상태에 맞는 상호작용 안내 반환
    public string GetInteractionPrompt()
    {
        // 열린 문에는 닫기 문구를 닫힌 문에는 열기 문구를 선택
        return IsOpen ? "E : 잠수함 출입구 닫기" : "E : 잠수함 출입구 열기";
    }

    // 애니메이터와 상호작용자 존재 확인
    public bool CanInteract(GameObject interactor)
    {
        // Animator와 상호작용자가 모두 있을 때만 문 사용 허용
        return doorAnimator != null && interactor != null;
    }

    // 체크포인트 문 상태를 권위 경로로 복원
    public void RestoreCheckpointState(bool open)
    {
        // 네트워크 권위 상태와 로컬 코루틴 상태를 실행 모드에 맞게 복원
        if (IsNetworkActive)
        {
            if (Object.HasStateAuthority)
            {
                NetworkedIsOpen = open;
                TransitionTickRaw = Runner.Tick.Raw;
                AutoCloseTimer = open
                    ? TickTimer.CreateFromSeconds(Runner, autoCloseDelay)
                    : TickTimer.None;
                ApplyAnimatorState(true);
            }
            return;
        }

        SetLocalOpen(open);
        if (open && autoCloseRoutine == null)
            autoCloseRoutine = StartCoroutine(AutoCloseAfterDelay());
    }

    // 열림 애니메이션 종료 뒤 자동 닫힘 시작
    public void OnOpenAnimationFinished()
    {
        // 문이 열린 상태에서만 자동 닫힘 타이머 시작
        if (!IsOpen)
            return;

        if (IsNetworkActive)
        {
            if (Object.HasStateAuthority)
                AutoCloseTimer = TickTimer.CreateFromSeconds(Runner, autoCloseDelay);
            return;
        }

        if (autoCloseRoutine == null)
            autoCloseRoutine = StartCoroutine(AutoCloseAfterDelay());
    }

    // 호스트 권위 상태 전환 기록
    private void SetNetworkOpen(bool open)
    {
        // 상태가 실제로 바뀔 때 전환 틱과 타이머와 애니메이션 갱신
        if (!Object.HasStateAuthority || NetworkedIsOpen == open)
            return;

        NetworkedIsOpen = open;
        TransitionTickRaw = Runner.Tick.Raw;
        AutoCloseTimer = TickTimer.None;
        ApplyAnimatorState(true);
    }

    // 네트워크가 없는 씬의 문 상태 전환
    private void SetLocalOpen(bool open)
    {
        // 기존 자동 닫힘 코루틴을 중단하고 로컬 상태 적용
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }

        localIsOpen = open;
        ApplyAnimatorState(true);
    }

    // 전환 틱을 기준으로 애니메이션 진행 위치 보정
    private void ApplyAnimatorState(bool force)
    {
        // 복제 상태 변화 여부를 확인하고 전환 틱 기반 진행률 계산
        bool open = IsOpen;
        bool shouldPlayHatch = hasAppliedState && lastAppliedOpen != open;
        if (!force && hasAppliedState && lastAppliedOpen == open)
            return;

        hasAppliedState = true;
        lastAppliedOpen = open;
        if (doorAnimator == null)
            return;

        doorAnimator.SetBool(doorOpen, open);
        if (shouldPlayHatch && open)
            PlayHatchAudio();
        if (!IsNetworkActive)
            return;

        // 문 애니메이션 틱 보정
        float elapsed = Mathf.Max(0f, (Runner.Tick.Raw - TransitionTickRaw) * Runner.DeltaTime);
        float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, transitionDuration));
        string stateName = open ? openAnimationState : closeAnimationState;
        if (!string.IsNullOrEmpty(stateName))
            doorAnimator.Play(stateName, 0, normalizedTime);
    }

    // 통로 검사 상자에서 플레이어 탐색
    private bool IsDoorwayOccupied()
    {
        // 설정한 상자 범위의 Collider에서 PlayerController 검색
        Vector3 center = obstructionCenter != null ? obstructionCenter.position : transform.position;
        Quaternion rotation = obstructionCenter != null ? obstructionCenter.rotation : transform.rotation;
        Collider[] hits = Physics.OverlapBox(
            center,
            obstructionHalfExtents,
            rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit != null && hit.GetComponentInParent<PlayerController>() != null)
                return true;
        }
        return false;
    }

    // InsidePoint에서 OutsidePoint로 향하는 문의 통과 축을 기준으로 위치의 부호를 반환한다.
    internal float GetPassageSide(Vector3 worldPosition)
    {
        if (insidePoint != null && outsidePoint != null)
        {
            Vector3 liveDirection = outsidePoint.position - insidePoint.position;
            if (liveDirection.sqrMagnitude <= 0.0001f)
                return 0f;

            Vector3 liveCenter = (insidePoint.position + outsidePoint.position) * 0.5f;
            return Vector3.Dot(worldPosition - liveCenter, liveDirection.normalized);
        }

        // 런타임 감지 프레임은 잠수함 루트의 자식이라 이동·회전을 그대로 따라간다.
        if (passageFrame != null)
            return Vector3.Dot(worldPosition - passageFrame.position, passageFrame.forward);

        if (passageDirection.sqrMagnitude <= 0.0001f)
            return 0f;

        return Vector3.Dot(worldPosition - passageCenter, passageDirection);
    }

    internal bool CanProcessExitDetection =>
        !IsNetworkActive || Object.HasStateAuthority;

    internal float PassageSideThreshold => passageSideThreshold;

    // 방향 감지기는 권위 피어에서만 이 메서드를 호출한다.
    // 네트워크 세션에서는 한 번의 RPC로 모든 피어의 문 오디오를 동기화한다.
    internal void NotifyPlayerExited()
    {
        if (!IsOpen || !CanProcessExitDetection)
            return;

        if (IsNetworkActive)
        {
            RPC_PlayExitWaterAudio();
            return;
        }

        PlayExitWaterAudio();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayExitWaterAudio()
    {
        PlayExitWaterAudio();
    }

    private void PlayExitWaterAudio()
    {
        CacheExitAudioSource();
        if (exitAudioSource != null && exitWaterClip != null)
            exitAudioSource.PlayOneShot(exitWaterClip);
    }

    private void CacheExitAudioSource()
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (exitWaterClip == null && library != null)
            exitWaterClip = library.submarineExit;

        if (exitAudioSource == null)
            exitAudioSource = GetComponent<AudioSource>();
        if (exitAudioSource == null)
            exitAudioSource = gameObject.AddComponent<AudioSource>();

        exitAudioSource.playOnAwake = false;
        exitAudioSource.loop = false;
        exitAudioSource.spatialBlend = 1f;
        exitAudioSource.minDistance = 1.5f;
        exitAudioSource.maxDistance = 25f;
    }

    private void PlayHatchAudio()
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library != null)
            VarcoAudio.PlayOneShotAt(transform, library.submarineHatch, 0.72f, 1.5f, 26f);
    }

    private void CachePassageAxis()
    {
        if (insidePoint != null && outsidePoint != null)
        {
            passageCenter = (insidePoint.position + outsidePoint.position) * 0.5f;
            passageDirection = (outsidePoint.position - insidePoint.position).normalized;
            return;
        }

        // 별도 기준점이 없는 기존 프리팹은 내부 WalkZone의 중심에서 문 쪽을 향하는 방향을
        // Inside -> Outside 축으로 사용한다. 문 애니메이션으로 Transform이 회전하기 전에 캐시한다.
        PlayerWalkZone walkZone = GetComponentInParent<SubmarineController>()
            ?.GetComponentInChildren<PlayerWalkZone>(true);
        Collider walkCollider = walkZone != null ? walkZone.GetComponent<Collider>() : null;
        Vector3 insidePosition = walkCollider != null
            ? walkCollider.bounds.center
            : transform.position - transform.forward;

        passageCenter = transform.position;
        passageDirection = (transform.position - insidePosition).normalized;
        if (passageDirection.sqrMagnitude <= 0.0001f)
            passageDirection = transform.forward;
    }

    private void EnsureExitTrigger()
    {
        if (GetComponentInChildren<SubmarineExitAudioTrigger>(true) != null)
            return;

        SubmarineController submarine = GetComponentInParent<SubmarineController>();
        Transform triggerParent = submarine != null ? submarine.transform : transform.parent;
        GameObject triggerObject = new GameObject("Submarine Exit Audio Trigger");
        triggerObject.layer = gameObject.layer;
        triggerObject.transform.SetParent(triggerParent, true);
        triggerObject.transform.position = passageCenter;
        triggerObject.transform.rotation = Quaternion.LookRotation(
            passageDirection,
            triggerParent != null ? triggerParent.up : Vector3.up);
        passageFrame = triggerObject.transform;

        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = exitTriggerSize;

        SubmarineExitAudioTrigger detector =
            triggerObject.AddComponent<SubmarineExitAudioTrigger>();
        detector.Initialize(this);
    }

    // 로컬 테스트 자동 닫힘 처리
    private IEnumerator AutoCloseAfterDelay()
    {
        // 닫힘 시간을 기다리고 통로가 비워질 때까지 재검사
        yield return new WaitForSeconds(autoCloseDelay);
        while (IsDoorwayOccupied())
            yield return new WaitForSeconds(blockedRetryDelay);

        autoCloseRoutine = null;
        SetLocalOpen(false);
    }

    // 비활성화 시 남은 코루틴 정리
    private void OnDisable()
    {
        // 실행 중인 로컬 자동 닫힘 코루틴만 중단
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }
    }
}
