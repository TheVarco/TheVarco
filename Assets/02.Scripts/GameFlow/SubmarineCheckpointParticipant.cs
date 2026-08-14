using UnityEngine;

namespace Varco.GameFlow
{
    // 잠수함 체크포인트 상태 저장
    // 위치와 회전과 이동 상태를 하나의 스냅샷으로 보관
    // 체력과 손상 슬롯과 문 상태를 함께 복원
    // 복원 전 좌석 점유와 조종 입력을 먼저 해제
    // 잠수함 본체 손상 문 상태를 저장하는 참가자
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SubmarineController))]
    public sealed class SubmarineCheckpointParticipant : CheckpointParticipantBehaviour
    {
        // 이동과 입력 초기화 대상
        private SubmarineController controller;
        // 잠수함 전체 체력
        private Health health;
        // 부위별 손상과 수리 진행 상태
        private RepairableStructure repairable;
        // 위치 회전과 속도 초기화 대상
        private Rigidbody body;
        // 자식 문 상태 목록
        private SubmarineDoor[] doors;
        // 자식 조종석 목록
        private CockpitSeat[] seats;
        // 네트워크 좌석 점유와 조종 입력을 한 번에 해제할 관리자
        private SubmarineSeatManager seatManager;

        // 플레이어와 적보다 먼저 복원
        public override int RestoreOrder => 10;

        // 잠수함과 자식 복원 대상 참조 수집
    private void Awake()
    {
        // 잠수함 이동과 체력과 수리와 문과 좌석 참조를 한 번에 수집
            controller = GetComponent<SubmarineController>();
            health = GetComponent<Health>();
            repairable = GetComponent<RepairableStructure>();
            body = GetComponent<Rigidbody>();
            doors = GetComponentsInChildren<SubmarineDoor>(true);
            seats = GetComponentsInChildren<CockpitSeat>(true);
            seatManager = GetComponent<SubmarineSeatManager>();
        }

        // 위치 체력 부위 손상 문 상태 캡처
    public override object CaptureCheckpointState()
    {
        // 현재 문 상태 배열과 잠수함 전체 상태 구조체 생성
            // 문 배열 순서에 맞춰 개폐 상태 저장
            bool[] doorStates = new bool[doors.Length];
            for (int i = 0; i < doors.Length; i++)
                doorStates[i] = doors[i] != null && doors[i].IsOpen;

            return new SubmarineState
            {
                Position = transform.position,
                Rotation = transform.rotation,
                Health = GameFlowHealthUtility.Capture(health),
                AccumulatedDamage = repairable != null ? repairable.CaptureCheckpointDamage() : null,
                RepairProgress = repairable != null ? repairable.CaptureCheckpointRepairProgress() : null,
                DamageRegionOrders = repairable != null ? repairable.CaptureCheckpointDamageOrders() : null,
                DamageSequence = repairable != null ? repairable.CaptureCheckpointDamageSequence() : 0,
                DoorStates = doorStates
            };
        }

        // 복원 전 모든 좌석 연결 해제
    public override void PrepareForCheckpointRestore()
    {
        // 호스트 권한을 확인하고 좌석 점유와 입력부터 해제
            if (controller != null && controller.IsNetworkActive && !controller.Object.HasStateAuthority)
                return;

            if (seatManager != null)
            {
                seatManager.ClearForCheckpointRestore();
            }
            else
            {
                foreach (CockpitSeat seat in seats)
                {
                    if (seat != null && seat.Occupant != null)
                        seat.ForceExit(seat.Occupant);
                }
            }
        }

        // 잠수함 기반 상태와 문 상태 복원
    public override void RestoreCheckpointState(object state)
    {
        // 저장 형식과 권한을 확인한 뒤 위치와 게임 상태를 순서대로 적용
            if (controller != null && controller.IsNetworkActive && !controller.Object.HasStateAuthority)
                return;

            if (state is not SubmarineState submarineState)
                return;

            // 체크포인트 재시작 시 정지 상태 적용
            // 자세 예약 전 저장 속도와 잔여 속도 제거
            controller?.ResetMotionState();

            // State Authority Simulation Tick에서 네트워크 물리 자세 적용
            // 로컬 및 비네트워크 실행은 자세 즉시 적용
            bool queuedNetworkTeleport = controller != null
                && controller.QueueCheckpointTeleport(
                    submarineState.Position,
                    submarineState.Rotation);
            if (!queuedNetworkTeleport && body != null)
            {
                body.position = submarineState.Position;
                body.rotation = submarineState.Rotation;
                transform.SetPositionAndRotation(
                    submarineState.Position,
                    submarineState.Rotation);
            }
            else if (!queuedNetworkTeleport)
            {
                transform.SetPositionAndRotation(submarineState.Position, submarineState.Rotation);
            }

            // 공개 API 기반 체력과 부위 손상 복원
            GameFlowHealthUtility.Restore(health, submarineState.Health);
            repairable?.RestoreCheckpointDamage(
                submarineState.AccumulatedDamage,
                submarineState.RepairProgress,
                submarineState.DamageRegionOrders,
                submarineState.DamageSequence);

            // 저장된 배열 범위 안에서 문 상태 복원
            int doorCount = Mathf.Min(doors.Length, submarineState.DoorStates?.Length ?? 0);
            for (int i = 0; i < doorCount; i++)
                doors[i]?.RestoreCheckpointState(submarineState.DoorStates[i]);
        }

        // 터미널 상태와 복원 중 잠수함 이동 시뮬레이션 제어
    public override void SetGameplayEnabled(bool enabled)
    {
        // 복원과 종료 화면 동안 잠수함 이동 시뮬레이션 활성 상태 변경
            if (controller != null)
                controller.SetCheckpointGameplayEnabled(enabled);
        }

        // 잠수함 전체 복원 데이터
        private sealed class SubmarineState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public HealthCheckpointState Health;
            public float[] AccumulatedDamage;
            public float[] RepairProgress;
            public int[] DamageRegionOrders;
            public int DamageSequence;
            public bool[] DoorStates;
        }
    }
}
