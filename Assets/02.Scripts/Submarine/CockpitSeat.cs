using UnityEngine;

public class CockpitSeat : MonoBehaviour, Interactable
{
    [Header("착석/하차 위치")]
    private SubmarineController submarineController;
    private SubmarineSeatManager seatManager;
    [SerializeField] private SubmarineSeatType seatType;
    [SerializeField] private Transform seatPoint;

    [Header("하차 세이프가드")]
    // 정상 하차를 허용할 최대 이동/회전 속도와 공간 검사
    // 플레이어가 튕겨나는 거 방지용
    [SerializeField] private float maxExitSpeed = 0.5f;
    [SerializeField] private float maxExitYawSpeed = 5f;
    [SerializeField] private LayerMask exitBlockerMask = ~0;
    
    public PlayerSeatController Occupant { get; private set; } // 좌석을 사용 중인 플레이어
    public SubmarineSeatType SeatType => seatType;
    public SubmarineController Controller => submarineController;

    private void Awake()
    {
        if (submarineController == null)
            submarineController = GetComponentInParent<SubmarineController>();
        if (seatManager == null)
            seatManager = GetComponentInParent<SubmarineSeatManager>();

        if (seatPoint == null)
            seatPoint = transform;

        if (submarineController == null)
            Debug.LogError("CockpitSeat: 부모에서 SubmarineController를 찾지 못했습니다.", this);
        if (seatManager == null)
            Debug.LogError("CockpitSeat: 부모에서 SubmarineSeatManager를 찾지 못했습니다.", this);
    }

    public void Interact(GameObject interactor)
    {
        PlayerSeatController player = interactor != null
            ? interactor.GetComponent<PlayerSeatController>()
            : null;

        if (player == null)
        {
            Debug.LogWarning("CockpitSeat: 착석을 시도한 플레이어 오브젝트에 PlayerSeatController 컴포넌트가 없습니다.", this);
            return;
        }

        TryEnter(player);
    }

    public string GetInteractionPrompt()
    {
        return "E : 조종석에 앉기";
    }

    // 좌석과 잠수함 상태를 확인해 현재 플레이어가 착석 가능한지 반환
    public bool CanInteract(GameObject interactor)
    {
        if (Occupant != null || seatManager == null || seatPoint == null
                             || interactor == null || !seatManager.IsSeatTypeAvailable(seatType))
            return false;

        PlayerSeatController player = interactor.GetComponent<PlayerSeatController>();
        return player != null && !player.IsSeated;
    }

    // 잠수함에 운전자를 먼저 등록한 뒤 실제 플레이어 착석을 수행
    public bool TryEnter(PlayerSeatController player)
    {
        if (player == null || Occupant != null || player.IsSeated || seatManager == null)
            return false;

        // 이미 다른 운전자가 있다면 조종권을 받을 수 없음
        if (!seatManager.TryAssignDriver(seatType, player))
            return false;

        Occupant = player;
        if (player.EnterSeat(this, seatPoint))
            return true;

        // 플레이어 착석이 실패하면 좌석 점유와 조종권을 모두 되돌림
        Occupant = null;
        seatManager.ReleaseDriver(seatType, player);
        return false;
    }

    // 하차 처리
    // 플레이어가 튕겨나가는 걸 방지하기 위해서 속도와 하차 공간을 모두 검사
    public bool TryExit(PlayerSeatController player)
    {
        if (player == null || Occupant != player)
            return false;

        // 빠르게 이동하거나 회전 중일 때는 하차를 거부
        if (submarineController != null
            && (submarineController.CurrentSpeed > maxExitSpeed
                || submarineController.CurrentYawSpeed > maxExitYawSpeed))
        {
            player.ShowSeatMessage("잠수함이 너무 빠릅니다");
            return false;
        }

        // 플레이어가 하차 할 공간이 막혀 있으면 하차 거부
        if (!IsSeatPointClear(player))
        {
            player.ShowSeatMessage("내릴 공간이 없습니다");
            return false;
        }

        CompleteExit(player);
        return true;
    }

    // 사망이나 기절 상황에서는 검사 없이 하차
    // 잠수함 조종권과 좌석 점유를 해제
    // 플레이어의 실제 하차 처리 호출
    public void ForceExit(PlayerSeatController player)
    {
        if (player == null || Occupant != player)
            return;

        CompleteExit(player);
    }

    public void SubmitInput(PlayerSeatController player, float throttle, float steering, bool ascend, bool descend)
    {
        if (player == null || Occupant != player || seatManager == null)
            return;

        seatManager.SubmitDriverInput(seatType, player, throttle, steering, ascend, descend);
    }

    private void CompleteExit(PlayerSeatController player)
    {
        // 플레이어가 하차할 때 사용할 잠수함의 현재 월드 속도
        Vector3 inheritedVelocity = submarineController != null
            ? submarineController.CurrentWorldVelocity
            : Vector3.zero;

        seatManager?.ReleaseDriver(seatType, player);
        Occupant = null;
        player.ExitSeat(seatPoint, inheritedVelocity);
    }

    // SeatPoint 위치에 플레이어 캡슐을 놓을 수 있는지 검사
    private bool IsSeatPointClear(PlayerSeatController player)
    {
        if (seatPoint == null)
            return false;

        // 플레이어의 실제 CapsuleCollider 크기를 월드 공간 캡슐로 변환
        player.TryGetCapsuleAt(seatPoint.position, seatPoint.rotation, out Vector3 a, out Vector3 b, out float radius);
        Collider[] hits = Physics.OverlapCapsule(a, b, radius, exitBlockerMask, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            // 플레이어 자신의 Collider는 하차 방해물에서 제외
            if (hit == null
                || hit.transform == player.transform
                || hit.transform.IsChildOf(player.transform))
                continue;
            // 잠수함 내부에서 내리는 구조이므로 현재 잠수함의 Collider도 제외
            if (submarineController != null
                && (hit.transform == submarineController.transform
                    || hit.transform.IsChildOf(submarineController.transform)))
                continue;
            return false;
        }

        return true;
    }

    private void OnDisable()
    {
        if (Application.isPlaying && Occupant != null)
            ForceExit(Occupant);
    }
}
