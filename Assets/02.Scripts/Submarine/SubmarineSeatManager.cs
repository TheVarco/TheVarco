using Fusion;
using UnityEngine;

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
/// <summary>
/// 싱글/멀티 플레이에 따른 좌석 구성, 좌석 점유, 역할별 조작 입력 관리
/// </summary>
public class SubmarineSeatManager : MonoBehaviour
{
    [Header("Seat Mode Objects")]
    [SerializeField] private GameObject chairSolo;
    [SerializeField] private GameObject chairLeft;
    [SerializeField] private GameObject chairRight;

    [SerializeField] private GameObject seatPointSolo;
    [SerializeField] private GameObject seatPointWSS;
    [SerializeField] private GameObject seatPointADC;

    // 현재 조종 상태를 읽을 때 사용하는 값들
    public bool HasDriver => CurrentPlayMode == SubmarinePlayMode.Solo ? soloDriver != null
        : wssDriver != null || adcDriver != null;
    public SubmarinePlayMode CurrentPlayMode { get; private set; }

    // 잠수함 이동과 조이스틱 스크립트에서 읽을 수 있는 현재 입력값
    public float ThrottleInput { get; private set; }
    public float SteeringInput { get; private set; }
    public float VerticalInput { get; private set; }

    private PlayerSeatController soloDriver;
    private PlayerSeatController wssDriver;
    private PlayerSeatController adcDriver;

    private bool isPlayMode; // 싱글/멀티 모드가 확정됐는지 표시용
    private float moveInput; // 앞뒤
    private float turnInput; // 좌우
    private bool upPressed; // 위
    private bool downPressed; // 아래

    private void Awake()
    {
        SubmarinePlayMode detectedPlayMode = HasRunningMultiplayerRunner() ? SubmarinePlayMode.Multiplayer : SubmarinePlayMode.Solo;
        SetPlayMode(detectedPlayMode);
        isPlayMode = detectedPlayMode == SubmarinePlayMode.Multiplayer;
    }

    private void Update()
    {
        if (!isPlayMode && HasRunningMultiplayerRunner())
        {
            SetPlayMode(SubmarinePlayMode.Multiplayer);
            isPlayMode = true;
        }

        // 운전자가 없으면 입력만 즉시 0으로
        // 이미 쌓인 속도는 SubmarineController에서 자연 감속하므로 바로 멈추지 않음
        RebuildCombinedInput();
    }

    /// <summary>
    /// 지정한 좌석에 플레이어를 운전자로 등록
    /// </summary>
    public bool TryAssignDriver(SubmarineSeatType seatType, PlayerSeatController driver)
    {
        if (driver == null || !IsSeatTypeAvailable(seatType))
            return false;

        if (IsAssignedToAnotherSeat(seatType, driver))
            return false;

        switch (seatType)
        {
            case SubmarineSeatType.Solo:
                if (soloDriver != null && soloDriver != driver) return false;
                soloDriver = driver;
                break;
            case SubmarineSeatType.WSS:
                if (wssDriver != null && wssDriver != driver) return false;
                wssDriver = driver;
                break;
            case SubmarineSeatType.ADC:
                if (adcDriver != null && adcDriver != driver) return false;
                adcDriver = driver;
                break;
            default:
                return false;
        }

        isPlayMode = true;
        ClearDriverInput(seatType);
        RebuildCombinedInput();
        return true;
    }

    /// <summary>
    /// 해당 좌석을 사용 중인 플레이어가 맞을 때 운전자 등록과 입력 해제
    /// </summary>
    public void ReleaseDriver(SubmarineSeatType seatType, PlayerSeatController driver)
    {
        bool released = false;

        switch (seatType)
        {
            case SubmarineSeatType.Solo when soloDriver == driver:
                soloDriver = null;
                released = true;
                break;
            case SubmarineSeatType.WSS when wssDriver == driver:
                wssDriver = null;
                released = true;
                break;
            case SubmarineSeatType.ADC when adcDriver == driver:
                adcDriver = null;
                released = true;
                break;
        }

        if (!released)
            return;

        ClearDriverInput(seatType);
        RebuildCombinedInput();
    }

    /// <summary>
    /// 현재 플레이 모드에서 사용할 수 있는 좌석 타입인지 확인
    /// </summary>
    public bool IsSeatTypeAvailable(SubmarineSeatType seatType)
    {
        return CurrentPlayMode == SubmarinePlayMode.Solo ?
            seatType == SubmarineSeatType.Solo : seatType == SubmarineSeatType.WSS || seatType == SubmarineSeatType.ADC;
    }

    /// <summary>
    /// 좌석 타입에 허용된 입력만 받아 잠수함의 공용 입력 상태에 반영
    /// </summary>
    public void SubmitDriverInput(SubmarineSeatType seatType, PlayerSeatController driver,
        float throttle, float steering, bool ascend, bool descend)
    {
        throttle = Mathf.Clamp(throttle, -1f, 1f);
        steering = Mathf.Clamp(steering, -1f, 1f);

        switch (seatType)
        {
            case SubmarineSeatType.Solo when soloDriver == driver:
                moveInput = throttle;
                turnInput = steering;
                upPressed = ascend;
                downPressed = descend;
                break;
            case SubmarineSeatType.WSS when wssDriver == driver:
                moveInput = throttle;
                upPressed = ascend;
                break;
            case SubmarineSeatType.ADC when adcDriver == driver:
                turnInput = steering;
                downPressed = descend;
                break;
            default:
                return;
        }

        RebuildCombinedInput();
    }

    /// <summary>
    /// 같은 플레이어가 요청한 좌석 이외의 좌석에 이미 등록되어 있는지 확인
    /// </summary>
    private bool IsAssignedToAnotherSeat(SubmarineSeatType requestedType, PlayerSeatController driver)
    {
        return (requestedType != SubmarineSeatType.Solo && soloDriver == driver)
            || (requestedType != SubmarineSeatType.WSS && wssDriver == driver)
            || (requestedType != SubmarineSeatType.ADC && adcDriver == driver);
    }

    /// <summary>
    /// 좌석별로 저장된 입력을 실제 이동에 사용하는 최종 입력값으로 합성
    /// </summary>
    private void RebuildCombinedInput()
    {
        ThrottleInput = moveInput;
        SteeringInput = turnInput;
        VerticalInput = (upPressed ? 1f : 0f) - (downPressed ? 1f : 0f);
    }

    /// <summary>
    /// 지정한 좌석이 담당하는 입력만 중립 상태로 초기화
    /// </summary>
    private void ClearDriverInput(SubmarineSeatType seatType)
    {
        switch (seatType)
        {
            case SubmarineSeatType.Solo:
                moveInput = 0f;
                turnInput = 0f;
                upPressed = false;
                downPressed = false;
                break;
            case SubmarineSeatType.WSS:
                moveInput = 0f;
                upPressed = false;
                break;
            case SubmarineSeatType.ADC:
                turnInput = 0f;
                downPressed = false;
                break;
        }
    }

    /// <summary>
    /// 플레이 모드에 맞는 좌석 오브젝트를 활성화하고 기존 점유와 입력 초기화
    /// </summary>
    private void SetPlayMode(SubmarinePlayMode mode)
    {
        CurrentPlayMode = mode;
        bool solo = mode == SubmarinePlayMode.Solo;

        SetActiveIfAssigned(chairSolo, solo);
        SetActiveIfAssigned(seatPointSolo, solo);
        SetActiveIfAssigned(chairLeft, !solo);
        SetActiveIfAssigned(chairRight, !solo);
        SetActiveIfAssigned(seatPointWSS, !solo);
        SetActiveIfAssigned(seatPointADC, !solo);

        ClearAllDriversAndInput();
    }

    /// <summary>
    /// 오브젝트 활성 상태를 변경
    /// </summary>
    private static void SetActiveIfAssigned(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    /// <summary>
    /// Fusion Runner가 있는지 확인
    /// </summary>
    private static bool HasRunningMultiplayerRunner()
    {
        NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (NetworkRunner runner in runners)
        {
            if (runner != null && runner.IsRunning && runner.GameMode != GameMode.Single)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 모든 좌석의 운전자 등록과 저장된 조작 입력 초기화
    /// </summary>
    private void ClearAllDriversAndInput()
    {
        soloDriver = null;
        wssDriver = null;
        adcDriver = null;
        moveInput = 0f;
        turnInput = 0f;
        upPressed = false;
        downPressed = false;
        ThrottleInput = 0f;
        SteeringInput = 0f;
        VerticalInput = 0f;
    }

    private void OnDisable()
    {
        ClearAllDriversAndInput();
    }
}
