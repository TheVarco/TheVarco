using Fusion;
using UnityEngine;

// 잠수함 좌석 점유 동기화
// 호스트가 좌석별 PlayerRef 점유 상태 관리
// WSS 입력과 ADC 입력을 하나의 조종 상태로 합성
// 연결 종료와 체크포인트 복원 시 점유와 입력 해제
// 네트워크가 없는 씬에서는 로컬 운전자 참조 사용

public enum SubmarinePlayMode
{
    Solo,
    Multiplayer
}

public enum SubmarineSeatType
{
    Solo,
    WSS,
    ADC
}

[RequireComponent(typeof(SubmarineController))]
// 좌석 점유 권한 관리
// 분할 조종 입력 합성
// 좌석 프리팹 활성 상태와 플레이 모드 일치
public class SubmarineSeatManager : NetworkBehaviour
{
    public const int SoloSeatMask = 1 << (int)SubmarineSeatType.Solo;
    public const int WssSeatMask = 1 << (int)SubmarineSeatType.WSS;
    public const int AdcSeatMask = 1 << (int)SubmarineSeatType.ADC;

    [Header("Seat Mode Objects")]
    [SerializeField] private GameObject chairSolo;
    [SerializeField] private GameObject chairLeft;
    [SerializeField] private GameObject chairRight;

    [SerializeField] private GameObject seatPointSolo;
    [SerializeField] private GameObject seatPointWSS;
    [SerializeField] private GameObject seatPointADC;

    [Networked] private SubmarinePlayMode NetworkedPlayMode { get; set; }
    [Networked] private PlayerRef SoloOccupant { get; set; }
    [Networked] private PlayerRef WssOccupant { get; set; }
    [Networked] private PlayerRef AdcOccupant { get; set; }
    [Networked] private float NetworkedThrottle { get; set; }
    [Networked] private float NetworkedSteering { get; set; }
    [Networked] private float NetworkedVertical { get; set; }

    private SubmarinePlayMode localPlayMode;
    private PlayerSeatController localSoloDriver;
    private PlayerSeatController localWssDriver;
    private PlayerSeatController localAdcDriver;
    private float localThrottle;
    private float localSteering;
    private bool localUp;
    private bool localDown;

    private CockpitSeat soloSeat;
    private CockpitSeat wssSeat;
    private CockpitSeat adcSeat;
    private SubmarinePlayMode lastAppliedMode = (SubmarinePlayMode)(-1);

    private bool IsNetworkActive => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;

    public SubmarinePlayMode CurrentPlayMode => IsNetworkActive ? NetworkedPlayMode : localPlayMode;
    public float ThrottleInput => IsNetworkActive ? NetworkedThrottle : localThrottle;
    public float SteeringInput => IsNetworkActive ? NetworkedSteering : localSteering;
    public float VerticalInput => IsNetworkActive
        ? NetworkedVertical
        : (localUp ? 1f : 0f) - (localDown ? 1f : 0f);
    public int ActiveSeatMask => CurrentPlayMode == SubmarinePlayMode.Solo
        ? SoloSeatMask
        : WssSeatMask | AdcSeatMask;

    public bool HasDriver => IsNetworkActive
        ? (CurrentPlayMode == SubmarinePlayMode.Solo
            ? !SoloOccupant.IsNone
            : !WssOccupant.IsNone || !AdcOccupant.IsNone)
        : (CurrentPlayMode == SubmarinePlayMode.Solo
            ? localSoloDriver != null
            : localWssDriver != null || localAdcDriver != null);

    // 좌석 참조와 로컬 플레이 모드 초기화
    private void Awake()
    {
        // 자식 좌석을 찾고 실행 중 Runner로 기본 모드 결정
        CacheSeats();
        localPlayMode = HasRunningMultiplayerRunner()
            ? SubmarinePlayMode.Multiplayer
            : SubmarinePlayMode.Solo;
        ApplyPlayMode(localPlayMode);
    }

    // 좌석 플레이 모드와 초기 점유 상태를 네트워크 기준으로 준비
    public override void Spawned()
    {
        // 프리팹 자식에서 좌석 종류별 컴포넌트를 다시 수집
        CacheSeats();
        if (Object.HasStateAuthority)
        {
            // 호스트가 실행 모드에 따라 Solo와 Multiplayer 결정
            NetworkedPlayMode = Runner.GameMode == GameMode.Single
                ? SubmarinePlayMode.Solo
                : SubmarinePlayMode.Multiplayer;
            // 이전 점유자와 남은 조종 입력을 모두 초기화
            ClearNetworkOccupantsAndInput();
        }

        // 모든 피어가 좌석 오브젝트와 점유 표현을 복제 상태에 맞춤
        ApplyPlayMode(CurrentPlayMode);
        ReconcileSeatVisuals();
    }

    // 로컬 테스트 실행 모드 변화 감지
    private void Update()
    {
        // 네트워크 상태가 아니면 Runner 상태에 맞춰 좌석 모드 전환
        if (IsNetworkActive)
            return;

        SubmarinePlayMode detected = HasRunningMultiplayerRunner()
            ? SubmarinePlayMode.Multiplayer
            : SubmarinePlayMode.Solo;
        if (detected != localPlayMode)
        {
            localPlayMode = detected;
            ClearLocalOccupantsAndInput();
            ApplyPlayMode(localPlayMode);
        }
    }

    // 호스트 틱 좌석 정리와 조종 입력 합성
    public override void FixedUpdateNetwork()
    {
        // StateAuthority만 연결 종료 처리와 입력 상태 변경
        if (!Object.HasStateAuthority)
            return;

        RemoveDisconnectedOccupants();
        RebuildNetworkInput();
    }

    // 복제된 좌석 모드와 점유 표현 적용
    public override void Render()
    {
        // 각 피어의 좌석 오브젝트와 플레이어 연결 상태 갱신
        ApplyPlayMode(CurrentPlayMode);
        ReconcileSeatVisuals();
    }

    // 현재 모드에서 지정 좌석이 활성 좌석인지 확인
    public bool IsSeatTypeAvailable(SubmarineSeatType seatType)
    {
        // 좌석 종류를 비트로 바꿔 활성 좌석 마스크와 비교
        int seatBit = 1 << (int)seatType;
        return (ActiveSeatMask & seatBit) != 0;
    }

    // 체크포인트 복원 전 좌석과 입력 전체 해제
    public void ClearForCheckpointRestore()
    {
        // 실행 모드와 권한에 맞는 로컬 또는 네트워크 초기화 경로 선택
        if (!IsNetworkActive)
        {
            soloSeat?.ApplyNetworkOccupant(null);
            wssSeat?.ApplyNetworkOccupant(null);
            adcSeat?.ApplyNetworkOccupant(null);
            ClearLocalOccupantsAndInput();
            return;
        }

        if (!Object.HasStateAuthority)
            return;

        ClearNetworkOccupantsAndInput();
        ReconcileSeatVisuals();
    }

    // 로컬 플레이어의 착석 요청 전달
    public void RequestEnter(SubmarineSeatType seatType, PlayerSeatController driver)
    {
        // 로컬 모드는 즉시 배정하고 네트워크 모드는 소유권 확인 후 RPC 전송
        if (driver == null)
            return;

        if (!IsNetworkActive)
        {
            TryAssignLocal(seatType, driver);
            return;
        }

        if (driver.Object == null || !driver.Object.IsValid || !driver.Object.HasInputAuthority)
            return;

        RPC_RequestEnter(driver.Object.Id, (byte)seatType);
    }

    // 로컬 플레이어의 일반 또는 강제 하차 요청 전달
    public void RequestExit(SubmarineSeatType seatType, PlayerSeatController driver, bool force = false)
    {
        // 강제 하차 권한과 입력 소유권을 확인한 뒤 적절한 해제 경로 선택
        if (driver == null)
            return;

        if (!IsNetworkActive)
        {
            ReleaseLocal(seatType, driver);
            return;
        }

        if (Object.HasStateAuthority && force)
        {
            ReleaseNetwork(seatType, driver.Object != null ? driver.Object.InputAuthority : PlayerRef.None);
            return;
        }

        if (driver.Object == null || !driver.Object.IsValid || !driver.Object.HasInputAuthority)
            return;

        RPC_RequestExit(driver.Object.Id, (byte)seatType, force);
    }

    // 호스트가 착석 요청자를 검증하고 좌석 점유 상태를 확정
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestEnter(NetworkId playerId, byte seatValue, RpcInfo info = default)
    {
        // RPC 출처와 거리와 좌석 활성 상태와 중복 점유 여부 검증
        if (!TryResolveRequester(playerId, info.Source, out PlayerSeatController driver))
            return;

        SubmarineSeatType seatType = (SubmarineSeatType)seatValue;
        CockpitSeat seat = GetSeat(seatType);
        if (seat == null || !seat.AuthorityCanEnter(driver) || !IsSeatTypeAvailable(seatType))
            return;

        PlayerRef player = driver.Object.InputAuthority;
        if (IsPlayerAssigned(player) || !GetOccupant(seatType).IsNone)
            return;

        SetOccupant(seatType, player);
        ReconcileSeatVisuals();
    }

    // 호스트가 하차 요청자를 검증하고 좌석 점유 상태를 해제
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestExit(NetworkId playerId, byte seatValue, NetworkBool force, RpcInfo info = default)
    {
        // 현재 점유자와 하차 가능 조건을 검증한 뒤 네트워크 점유 해제
        if (!TryResolveRequester(playerId, info.Source, out PlayerSeatController driver))
            return;

        SubmarineSeatType seatType = (SubmarineSeatType)seatValue;
        if (GetOccupant(seatType) != driver.Object.InputAuthority)
            return;

        CockpitSeat seat = GetSeat(seatType);
        if (!force && (seat == null || !seat.AuthorityCanExit(driver)))
            return;

        ReleaseNetwork(seatType, driver.Object.InputAuthority);
    }

    // 로컬 운전자 입력을 좌석 역할별 상태에 저장
    public void SubmitDriverInput(
        SubmarineSeatType seatType,
        PlayerSeatController driver,
        float throttle,
        float steering,
        bool ascend,
        bool descend)
    {
        // 호스트 입력 직접 수집
        if (IsNetworkActive || driver == null)
            return;

        switch (seatType)
        {
            case SubmarineSeatType.Solo when localSoloDriver == driver:
                localThrottle = Mathf.Clamp(throttle, -1f, 1f);
                localSteering = Mathf.Clamp(steering, -1f, 1f);
                localUp = ascend;
                localDown = descend;
                break;
            case SubmarineSeatType.WSS when localWssDriver == driver:
                localThrottle = Mathf.Clamp(throttle, -1f, 1f);
                localUp = ascend;
                break;
            case SubmarineSeatType.ADC when localAdcDriver == driver:
                localSteering = Mathf.Clamp(steering, -1f, 1f);
                localDown = descend;
                break;
        }
    }

    // 현재 점유자 입력을 역할별 잠수함 입력으로 합성
    private void RebuildNetworkInput()
    {
        // 네트워크 모드를 제외하고 현재 좌석 점유자 입력만 반영
        // Solo 또는 WSS ADC 모드에 맞춰 각 PlayerRef 입력 읽기
        float throttle = 0f;
        float steering = 0f;
        bool ascend = false;
        bool descend = false;

        if (NetworkedPlayMode == SubmarinePlayMode.Solo)
        {
            if (TryReadInput(SoloOccupant, out NetworkInputData input))
            {
                throttle = input.Vertical;
                steering = input.Horizontal;
                ascend = input.Up;
                descend = input.Down;
            }
        }
        else
        {
            if (TryReadInput(WssOccupant, out NetworkInputData wssInput))
            {
                throttle = wssInput.Vertical;
                ascend = wssInput.Up;
            }

            if (TryReadInput(AdcOccupant, out NetworkInputData adcInput))
            {
                steering = adcInput.Horizontal;
                descend = adcInput.Down;
            }
        }

        NetworkedThrottle = Mathf.Clamp(throttle, -1f, 1f);
        NetworkedSteering = Mathf.Clamp(steering, -1f, 1f);
        NetworkedVertical = (ascend ? 1f : 0f) - (descend ? 1f : 0f);
    }

    // 점유자의 현재 Fusion 입력 읽기
    private bool TryReadInput(PlayerRef player, out NetworkInputData input)
    {
        // 빈 PlayerRef를 제외하고 Runner 입력 조회 결과 반환
        input = default;
        return !player.IsNone && Runner.TryGetInputForPlayer(player, out input);
    }

    // RPC 출처를 플레이어 좌석 제어기로 검증
    private bool TryResolveRequester(
        NetworkId playerId,
        PlayerRef source,
        out PlayerSeatController driver)
    {
        // NetworkId 오브젝트와 InputAuthority와 RpcInfo 출처 일치 확인
        driver = null;
        if (!Runner.TryFindObject(playerId, out NetworkObject playerObject)
            || playerObject.InputAuthority != source)
        {
            return false;
        }

        driver = playerObject.GetComponent<PlayerSeatController>();
        return driver != null;
    }

    // 연결이 끊긴 좌석 점유자 제거
    private void RemoveDisconnectedOccupants()
    {
        // 세 좌석 PlayerRef를 활성 참가자 목록과 비교해 해제
        if (!IsPlayerAvailable(SoloOccupant)) SoloOccupant = PlayerRef.None;
        if (!IsPlayerAvailable(WssOccupant)) WssOccupant = PlayerRef.None;
        if (!IsPlayerAvailable(AdcOccupant)) AdcOccupant = PlayerRef.None;
    }

    // PlayerRef가 현재 활성 참가자인지 확인
    private bool IsPlayerAvailable(PlayerRef player)
    {
        // 빈 참조를 거부하고 ActivePlayers 목록에서 동일 값 검색
        return player.IsNone || Runner.TryGetPlayerObject(player, out NetworkObject playerObject) && playerObject != null;
    }

    // PlayerRef의 다른 좌석 중복 점유 확인
    private bool IsPlayerAssigned(PlayerRef player)
    {
        // 세 네트워크 점유자 중 하나와 일치하는지 반환
        return !player.IsNone && (SoloOccupant == player || WssOccupant == player || AdcOccupant == player);
    }

    // 좌석 종류에 대응하는 PlayerRef 반환
    private PlayerRef GetOccupant(SubmarineSeatType seatType)
    {
        // switch 식으로 Solo WSS ADC 점유 필드 선택
        return seatType switch
        {
            SubmarineSeatType.Solo => SoloOccupant,
            SubmarineSeatType.WSS => WssOccupant,
            SubmarineSeatType.ADC => AdcOccupant,
            _ => PlayerRef.None
        };
    }

    // 좌석 종류에 대응하는 PlayerRef 기록
    private void SetOccupant(SubmarineSeatType seatType, PlayerRef player)
    {
        // 좌석 종류별 네트워크 점유 필드 하나만 변경
        switch (seatType)
        {
            case SubmarineSeatType.Solo: SoloOccupant = player; break;
            case SubmarineSeatType.WSS: WssOccupant = player; break;
            case SubmarineSeatType.ADC: AdcOccupant = player; break;
        }
    }

    // 네트워크 좌석 점유와 관련 입력 해제
    private void ReleaseNetwork(SubmarineSeatType seatType, PlayerRef player)
    {
        // 현재 점유자가 요청자와 같을 때 점유를 비우고 표현 갱신
        if (player.IsNone || GetOccupant(seatType) != player)
            return;

        SetOccupant(seatType, PlayerRef.None);
        RebuildNetworkInput();
        ReconcileSeatVisuals();
    }

    // 로컬 테스트 좌석 배정
    private bool TryAssignLocal(SubmarineSeatType seatType, PlayerSeatController driver)
    {
        // 좌석 활성 상태와 중복 점유를 확인한 뒤 종류별 운전자 저장
        if (!IsSeatTypeAvailable(seatType) || driver.IsSeated || IsLocalPlayerAssigned(driver))
            return false;

        CockpitSeat seat = GetSeat(seatType);
        if (seat == null || !seat.AuthorityCanEnter(driver))
            return false;

        switch (seatType)
        {
            case SubmarineSeatType.Solo when localSoloDriver == null: localSoloDriver = driver; break;
            case SubmarineSeatType.WSS when localWssDriver == null: localWssDriver = driver; break;
            case SubmarineSeatType.ADC when localAdcDriver == null: localAdcDriver = driver; break;
            default: return false;
        }

        seat.ApplyNetworkOccupant(driver);
        return true;
    }

    // 로컬 테스트 좌석과 입력 해제
    private void ReleaseLocal(SubmarineSeatType seatType, PlayerSeatController driver)
    {
        // 좌석별 현재 운전자와 일치할 때 참조와 해당 입력 초기화
        CockpitSeat seat = GetSeat(seatType);
        switch (seatType)
        {
            case SubmarineSeatType.Solo when localSoloDriver == driver: localSoloDriver = null; break;
            case SubmarineSeatType.WSS when localWssDriver == driver: localWssDriver = null; break;
            case SubmarineSeatType.ADC when localAdcDriver == driver: localAdcDriver = null; break;
            default: return;
        }

        ClearLocalInput(seatType);
        seat?.ApplyNetworkOccupant(null);
    }

    // 로컬 운전자 중복 점유 확인
    private bool IsLocalPlayerAssigned(PlayerSeatController driver)
    {
        // 세 로컬 운전자 참조 중 하나와 같은지 반환
        return localSoloDriver == driver || localWssDriver == driver || localAdcDriver == driver;
    }

    // 네트워크 점유 상태를 좌석과 플레이어 표현에 적용
    private void ReconcileSeatVisuals()
    {
        // 각 PlayerRef를 PlayerSeatController로 변환해 좌석에 전달
        if (!IsNetworkActive)
            return;

        soloSeat?.ApplyNetworkOccupant(ResolvePlayer(SoloOccupant));
        wssSeat?.ApplyNetworkOccupant(ResolvePlayer(WssOccupant));
        adcSeat?.ApplyNetworkOccupant(ResolvePlayer(AdcOccupant));
    }

    // PlayerRef를 플레이어 좌석 제어기로 변환
    private PlayerSeatController ResolvePlayer(PlayerRef player)
    {
        // Runner의 PlayerObject를 찾고 같은 오브젝트의 컴포넌트 반환
        if (player.IsNone || !Runner.TryGetPlayerObject(player, out NetworkObject playerObject))
            return null;
        return playerObject != null ? playerObject.GetComponent<PlayerSeatController>() : null;
    }

    // 자식 좌석을 종류별로 캐시
    private void CacheSeats()
    {
        // 비활성 자식까지 검색해 Solo WSS ADC 필드에 저장
        CockpitSeat[] seats = GetComponentsInChildren<CockpitSeat>(true);
        foreach (CockpitSeat seat in seats)
        {
            if (seat == null) continue;
            switch (seat.SeatType)
            {
                case SubmarineSeatType.Solo: soloSeat = seat; break;
                case SubmarineSeatType.WSS: wssSeat = seat; break;
                case SubmarineSeatType.ADC: adcSeat = seat; break;
            }
        }
    }

    // 좌석 종류에 대응하는 CockpitSeat 반환
    private CockpitSeat GetSeat(SubmarineSeatType seatType)
    {
        // switch 식으로 캐시된 좌석 필드 선택
        return seatType switch
        {
            SubmarineSeatType.Solo => soloSeat,
            SubmarineSeatType.WSS => wssSeat,
            SubmarineSeatType.ADC => adcSeat,
            _ => null
        };
    }

    // 플레이 모드에 맞춰 좌석 오브젝트 활성 상태 전환
    private void ApplyPlayMode(SubmarinePlayMode mode)
    {
        // 이전 적용 모드와 다를 때 Solo와 분할 좌석 표시 교체
        if (lastAppliedMode == mode)
            return;

        lastAppliedMode = mode;
        bool solo = mode == SubmarinePlayMode.Solo;
        SetActiveIfAssigned(chairSolo, solo);
        SetActiveIfAssigned(seatPointSolo, solo);
        SetActiveIfAssigned(chairLeft, !solo);
        SetActiveIfAssigned(chairRight, !solo);
        SetActiveIfAssigned(seatPointWSS, !solo);
        SetActiveIfAssigned(seatPointADC, !solo);
    }

    // 선택 오브젝트 활성 상태 안전 변경
    private static void SetActiveIfAssigned(GameObject target, bool active)
    {
        // 참조가 있고 현재 값이 다를 때만 SetActive 호출
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    // 네트워크 점유자와 합성 입력 전체 초기화
    private void ClearNetworkOccupantsAndInput()
    {
        // 세 PlayerRef와 세 조종 입력 값을 기본값으로 변경
        SoloOccupant = PlayerRef.None;
        WssOccupant = PlayerRef.None;
        AdcOccupant = PlayerRef.None;
        NetworkedThrottle = 0f;
        NetworkedSteering = 0f;
        NetworkedVertical = 0f;
    }

    // 로컬 운전자와 입력 전체 초기화
    private void ClearLocalOccupantsAndInput()
    {
        // 세 운전자 참조와 방향 입력 불 값을 기본값으로 변경
        localSoloDriver = null;
        localWssDriver = null;
        localAdcDriver = null;
        localThrottle = 0f;
        localSteering = 0f;
        localUp = false;
        localDown = false;
    }

    // 지정한 로컬 좌석 역할 입력 초기화
    private void ClearLocalInput(SubmarineSeatType seatType)
    {
        // 좌석별 담당 축만 선택해 값 제거
        switch (seatType)
        {
            case SubmarineSeatType.Solo:
                localThrottle = 0f;
                localSteering = 0f;
                localUp = false;
                localDown = false;
                break;
            case SubmarineSeatType.WSS:
                localThrottle = 0f;
                localUp = false;
                break;
            case SubmarineSeatType.ADC:
                localSteering = 0f;
                localDown = false;
                break;
        }
    }

    // 실행 중 멀티플레이 Runner 확인
    private static bool HasRunningMultiplayerRunner()
    {
        // 씬의 모든 Runner에서 실행 상태와 게임 모드 검사
        NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (NetworkRunner runner in runners)
        {
            if (runner != null && runner.IsRunning && runner.GameMode != GameMode.Single)
                return true;
        }
        return false;
    }

    // 좌석 종류와 내부 스폰 포인트 구성 검증
    private void OnValidate()
    {
        // 프리팹 자식을 순회해 좌석별 수와 스폰 포인트 수 계산
        CockpitSeat[] seats = GetComponentsInChildren<CockpitSeat>(true);
        int soloCount = 0;
        int wssCount = 0;
        int adcCount = 0;
        foreach (CockpitSeat seat in seats)
        {
            if (seat == null) continue;
            switch (seat.SeatType)
            {
                case SubmarineSeatType.Solo: soloCount++; break;
                case SubmarineSeatType.WSS: wssCount++; break;
                case SubmarineSeatType.ADC: adcCount++; break;
            }
        }

        if (soloCount != 1 || wssCount != 1 || adcCount != 1)
            Debug.LogWarning("SubmarineSeatManager: Solo/WSS/ADC 좌석은 각각 정확히 하나여야 합니다.", this);

        int spawnPointCount = 0;
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant != null && descendant.name.StartsWith("PlayerSpawnPoint", System.StringComparison.Ordinal))
                spawnPointCount++;
        }

        if (spawnPointCount != 4)
            Debug.LogWarning($"SubmarineSeatManager: 4인용 내부 스폰 포인트가 {spawnPointCount}개입니다.", this);
    }

    // 비활성화 시 로컬 좌석 입력 정리
    private void OnDisable()
    {
        // 네트워크 상태가 아닐 때만 로컬 점유와 입력 초기화
        if (!IsNetworkActive)
            ClearLocalOccupantsAndInput();
    }
}
