using UnityEngine;

namespace Varco.GameFlow
{
    // 상어 문어 성게 채집 생물 상태를 저장하는 참가자
    [DisallowMultipleComponent]
    public sealed class EnemyCheckpointParticipant : CheckpointParticipantBehaviour
    {
        // 체력과 물리 상태 참조
        private Health health;
        private Rigidbody body;
        // 종류별 AI 복원 대상
        private SharkController shark;
        private OctopusController octopus;
        private UrchinController urchin;
        // 채집과 부착 단계 복원 대상
        private HarvestableCreature harvestable;
        // 살아 있던 체크포인트의 상어 제거 방지 여부
        private bool preserveAfterDeath;

        // 잠수함과 장애물 이후 플레이어 이전 복원
        public override int RestoreOrder => 50;

        // 현재 적 종류와 공통 상태 참조 수집
        private void Awake()
        {
            health = GetComponent<Health>();
            body = GetComponent<Rigidbody>();
            shark = GetComponent<SharkController>();
            octopus = GetComponent<OctopusController>();
            urchin = GetComponent<UrchinController>();
            harvestable = GetComponent<HarvestableCreature>();
        }

        // 레지스트리 등록과 사망 보존 이벤트 연결
        protected override void OnEnable()
        {
            base.OnEnable();
            if (health != null)
                health.OnDeath.AddListener(HandleDeathForCheckpoint);
        }

        // 사망 보존 이벤트와 레지스트리 연결 해제
        protected override void OnDisable()
        {
            if (health != null)
                health.OnDeath.RemoveListener(HandleDeathForCheckpoint);
            base.OnDisable();
        }

        // 위치 체력 채집 단계 부착 슬롯 상태 캡처
        public override object CaptureCheckpointState()
        {
            // 생존 상어는 이후 사망해도 복원 전까지 오브젝트 유지
            preserveAfterDeath = shark != null && health != null && !health.IsDead;
            // 이미 사망한 상어는 현재 체크포인트에 존재하지 않는 상태로 정리
            if (shark != null && health != null && health.IsDead)
                shark.ScheduleDestroyAfterDeath(0f);

            return new EnemyState
            {
                Position = transform.position,
                Rotation = transform.rotation,
                Health = GameFlowHealthUtility.Capture(health),
                CreaturePhase = harvestable != null
                    ? harvestable.Phase
                    : HarvestableCreature.CreaturePhase.Hazard,
                AttachedSlot = harvestable != null ? harvestable.AttachedSlot : null
            };
        }

        // 복원 전 기존 부착 관계 해제
        public override void PrepareForCheckpointRestore()
        {
            if (harvestable != null && harvestable.IsAttached)
                harvestable.MakeCollectible();
        }

        // 위치 속도 체력 채집 단계 AI 상태 복원
        public override void RestoreCheckpointState(object state)
        {
            if (state is not EnemyState enemyState)
                return;

            transform.SetPositionAndRotation(enemyState.Position, enemyState.Rotation);
            // AI 속도는 저장하지 않고 동적 Rigidbody에서만 초기화
            // Kinematic 성게와 부착 생물에는 지원되지 않는 속도 값을 쓰지 않음
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            GameFlowHealthUtility.Restore(health, enemyState.Health);
            harvestable?.RestoreCheckpointPhase(enemyState.CreaturePhase, enemyState.AttachedSlot);

            // 생존 적 AI는 Idle 상태로 재시작
            if (shark != null && !enemyState.Health.IsDead)
                shark.RestoreCheckpointAI();
            octopus?.RestoreCheckpointAI();
        }

        // 결과 화면과 복원 중 적 AI 시뮬레이션 제어
        public override void SetGameplayEnabled(bool enabled)
        {
            if (shark != null)
                shark.enabled = enabled;
            if (octopus != null)
                octopus.enabled = enabled;
            if (urchin != null)
                urchin.enabled = enabled;
        }

        // 생존 스냅샷의 상어 지연 제거 취소
        private void HandleDeathForCheckpoint()
        {
            if (preserveAfterDeath)
                shark?.CancelScheduledDestroyForCheckpoint();
        }

        // 적 오브젝트 복원 데이터
        private sealed class EnemyState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public HealthCheckpointState Health;
            public HarvestableCreature.CreaturePhase CreaturePhase;
            public AttachmentSlot AttachedSlot;
        }
    }
}
