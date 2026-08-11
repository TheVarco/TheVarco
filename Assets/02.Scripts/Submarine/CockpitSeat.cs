using UnityEngine;

// 잠수함 조종석 상호작용
// 로컬 플레이어의 착석과 하차 요청 전달
// 호스트가 거리와 속도와 하차 공간 검증
// 복제된 점유자를 좌석 앵커에 고정
// 점유 상태에 맞춰 안내 문구와 애니메이션 갱신

public class CockpitSeat : MonoBehaviour, Interactable
{
    [Header("착석/하차 위치")]
    private SubmarineController submarineController;
    private SubmarineSeatManager seatManager;
    [SerializeField] private SubmarineSeatType seatType;
    [SerializeField] private Transform seatPoint;
    [SerializeField, Min(0.1f)] private float maxEnterDistance = 3f;

    [Header("하차 세이프가드")]
    // 정상 하차를 허용할 최대 이동/회전 속도와 공간 검사
    // 플레이어가 튕겨나는 거 방지용
    [SerializeField] private float maxExitSpeed = 0.5f;
    [SerializeField] private float maxExitYawSpeed = 5f;
    [SerializeField] private LayerMask exitBlockerMask = ~0;
    
    public PlayerSeatController Occupant { get; private set; } // 좌석을 사용 중인 플레이어
    public SubmarineSeatType SeatType => seatType;
    public SubmarineController Controller => submarineController;

    // 잠수함 제어기와 좌석 관리자와 좌석 앵커 준비
    private void Awake()
    {
        // 부모 잠수함에서 필요한 컴포넌트를 찾고 누락 앵커 보정
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

    // 플레이어 상호작용을 착석 요청으로 변환
    public void Interact(GameObject interactor)
    {
        // 상호작용 오브젝트에서 좌석 제어기를 찾아 입장 요청
        PlayerSeatController player = interactor != null
            ? interactor.GetComponent<PlayerSeatController>()
            : null;

        if (player == null)
        {
            Debug.LogWarning("CockpitSeat: 착석을 시도한 플레이어 오브젝트에 PlayerSeatController 컴포넌트가 없습니다.", this);
            return;
        }

        seatManager?.RequestEnter(seatType, player);
    }

    // 좌석 종류와 점유 상태에 맞는 안내 문구 반환
    public string GetInteractionPrompt()
    {
        // 현재 점유 여부를 기준으로 착석 가능 문구 결정
        return "E : 조종석에 앉기";
    }

    // 좌석과 잠수함 상태를 확인해 현재 플레이어가 착석 가능한지 반환
    public bool CanInteract(GameObject interactor)
    {
        // 플레이어 좌석 제어기와 현재 좌석 사용 가능 상태 확인
        if (Occupant != null || seatManager == null || seatPoint == null
                             || interactor == null || !seatManager.IsSeatTypeAvailable(seatType))
            return false;

        PlayerSeatController player = interactor.GetComponent<PlayerSeatController>();
        return player != null && !player.IsSeated;
    }

    // 잠수함에 운전자를 먼저 등록한 뒤 실제 플레이어 착석을 수행
    public bool TryEnter(PlayerSeatController player)
    {
        // 로컬과 네트워크 실행 상태에 맞는 좌석 관리자 경로 호출
        if (player == null || seatManager == null || !AuthorityCanEnter(player))
            return false;

        seatManager.RequestEnter(seatType, player);
        return true;
    }

    // 하차 처리
    // 플레이어가 튕겨나가는 걸 방지하기 위해서 속도와 하차 공간을 모두 검사
    public bool TryExit(PlayerSeatController player)
    {
        // 현재 점유자만 일반 하차 요청을 보낼 수 있게 제한
        if (player == null || Occupant != player || seatManager == null)
            return false;

        seatManager.RequestExit(seatType, player);
        return true;
    }

    // 사망이나 기절 상황에서는 검사 없이 하차
    // 잠수함 조종권과 좌석 점유를 해제
    // 플레이어의 실제 하차 처리 호출
    public void ForceExit(PlayerSeatController player)
    {
        // 사망과 복원 경로에서 속도와 공간 검사 없이 하차 요청
        if (player == null || Occupant != player)
            return;

        seatManager?.RequestExit(seatType, player, true);
    }

    // 점유자의 조종 입력을 좌석 관리자에 전달
    public void SubmitInput(PlayerSeatController player, float throttle, float steering, bool ascend, bool descend)
    {
        // 좌석 종류와 입력 값을 그대로 관리자 합성 경로에 전달
        if (player == null || Occupant != player || seatManager == null)
            return;

        seatManager.SubmitDriverInput(seatType, player, throttle, steering, ascend, descend);
    }

    // 호스트가 착석 거리와 중복 상태와 좌석 공간 검증
    internal bool AuthorityCanEnter(PlayerSeatController player)
    {
        // 플레이어 유효성과 좌석 점유와 거리 조건을 순서대로 확인
        if (player == null || seatPoint == null)
            return false;

        Health playerHealth = player.GetComponent<Health>();
        PlayerDownedState downedState = player.GetComponent<PlayerDownedState>();
        return Occupant == null
            && !player.IsSeated
            && seatManager != null
            && (playerHealth == null || !playerHealth.IsDead)
            && (downedState == null || !downedState.IsDowned)
            && (player.transform.position - seatPoint.position).sqrMagnitude
                <= maxEnterDistance * maxEnterDistance
            && seatManager.IsSeatTypeAvailable(seatType);
    }

    // 호스트가 하차 속도와 하차 지점 공간 검증
    internal bool AuthorityCanExit(PlayerSeatController player)
    {
        // 잠수함 속도와 회전 속도와 플레이어 캡슐 공간 확인
        if (player == null || Occupant != player)
            return false;

        if (submarineController != null
            && (submarineController.CurrentSpeed > maxExitSpeed
                || submarineController.CurrentYawSpeed > maxExitYawSpeed))
        {
            player.ShowSeatMessage("잠수함이 너무 빠릅니다");
            return false;
        }

        if (!IsSeatPointClear(player))
        {
            player.ShowSeatMessage("내릴 공간이 없습니다");
            return false;
        }

        return true;
    }

    // 복제된 좌석 점유 적용
    // 좌석 점유는 호스트만 변경
    internal void ApplyNetworkOccupant(PlayerSeatController player)
    {
        // 기존 점유 표현을 정리한 뒤 새 점유자를 좌석에 연결
        if (Occupant == player)
            return;

        PlayerSeatController previous = Occupant;
        Occupant = null;

        // 플레이어가 하차할 때 사용할 잠수함의 현재 월드 속도
        Vector3 inheritedVelocity = submarineController != null
            ? submarineController.CurrentWorldVelocity
            : Vector3.zero;

        if (previous != null && previous.CurrentSeat == this)
            previous.ExitSeat(seatPoint, inheritedVelocity);

        if (player == null)
            return;

        if (player.EnterSeat(this, seatPoint))
            Occupant = player;
    }

    // SeatPoint 위치에 플레이어 캡슐을 놓을 수 있는지 검사
    private bool IsSeatPointClear(PlayerSeatController player)
    {
        // 플레이어 캡슐 크기로 하차 위치의 장애물 겹침 검사
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

    // 로컬 좌석 비활성화 시 남은 점유 상태 해제
    private void OnDisable()
    {
        // 네트워크 권위 상태는 유지하고 로컬 점유 표현만 정리
        if (Application.isPlaying && Occupant != null)
            ForceExit(Occupant);
    }
}
