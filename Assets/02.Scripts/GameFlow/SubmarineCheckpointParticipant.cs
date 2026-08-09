using UnityEngine;

namespace Varco.GameFlow
{
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

        // 플레이어와 적보다 먼저 복원
        public override int RestoreOrder => 10;

        // 잠수함과 자식 복원 대상 참조 수집
        private void Awake()
        {
            controller = GetComponent<SubmarineController>();
            health = GetComponent<Health>();
            repairable = GetComponent<RepairableStructure>();
            body = GetComponent<Rigidbody>();
            doors = GetComponentsInChildren<SubmarineDoor>(true);
            seats = GetComponentsInChildren<CockpitSeat>(true);
        }

        // 위치 체력 부위 손상 문 상태 캡처
        public override object CaptureCheckpointState()
        {
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
                DoorStates = doorStates
            };
        }

        // 복원 전 모든 좌석 연결 해제
        public override void PrepareForCheckpointRestore()
        {
            foreach (CockpitSeat seat in seats)
            {
                if (seat != null && seat.Occupant != null)
                    seat.ForceExit(seat.Occupant);
            }
        }

        // 잠수함 기반 상태와 문 상태 복원
        public override void RestoreCheckpointState(object state)
        {
            if (state is not SubmarineState submarineState)
                return;

            // Rigidbody가 있으면 물리 위치를 직접 적용
            if (body != null)
            {
                body.position = submarineState.Position;
                body.rotation = submarineState.Rotation;
            }
            else
            {
                transform.SetPositionAndRotation(submarineState.Position, submarineState.Rotation);
            }

            // 저장하지 않는 이동 속도와 외력 초기화
            controller.ResetMotionState();
            // 공개 API를 통한 체력과 부위 손상 복원
            GameFlowHealthUtility.Restore(health, submarineState.Health);
            repairable?.RestoreCheckpointDamage(
                submarineState.AccumulatedDamage,
                submarineState.RepairProgress);

            // 저장된 배열 범위 안에서 문 상태 복원
            int doorCount = Mathf.Min(doors.Length, submarineState.DoorStates?.Length ?? 0);
            for (int i = 0; i < doorCount; i++)
                doors[i]?.RestoreCheckpointState(submarineState.DoorStates[i]);
        }

        // 터미널 상태와 복원 중 잠수함 이동 시뮬레이션 제어
        public override void SetGameplayEnabled(bool enabled)
        {
            if (controller != null)
                controller.enabled = enabled;
        }

        // 잠수함 전체 복원 데이터
        private sealed class SubmarineState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public HealthCheckpointState Health;
            public float[] AccumulatedDamage;
            public float[] RepairProgress;
            public bool[] DoorStates;
        }
    }
}
