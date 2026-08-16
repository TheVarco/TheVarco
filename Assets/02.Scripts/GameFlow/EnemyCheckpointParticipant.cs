using UnityEngine;

namespace Varco.GameFlow
{
    // 상어의 기존 초기 자세 복원 동작과 적 체력을 관리하는 참가자.
    // 문어/성게의 자세, 채집 단계, 부착 관계는 items:session이 단일 소유한다.
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

        // 공통 체력 상태 캡처
        public override object CaptureCheckpointState()
        {
            // 생존 상어는 이후 사망해도 복원 전까지 오브젝트 유지
            preserveAfterDeath = shark != null && health != null && !health.IsDead;
            // 이미 사망한 상어는 현재 체크포인트에 존재하지 않는 상태로 정리
            if (shark != null && health != null && health.IsDead)
                shark.ScheduleDestroyAfterDeath(0f);

            return new EnemyState
            {
                Health = GameFlowHealthUtility.Capture(health)
            };
        }

        // 상어 자세/속도와 공통 체력, 최종 AI 활성 상태 복원
        public override void RestoreCheckpointState(object state)
        {
            if (state is not EnemyState enemyState)
                return;

            // 상어 최초 배치 위치 복원
            // 문어/성게 자세와 물리는 CarryableCheckpointParticipant가 먼저 복원한다.
            if (shark != null)
            {
                shark.RestoreInitialCheckpointPose();
                // AI 속도는 저장하지 않고 상어의 동적 Rigidbody만 초기화한다.
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            GameFlowHealthUtility.Restore(health, enemyState.Health);

            // items:session이 단계/부착을 복원한 다음 최종 AI 상태를 맞춘다.
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
            public HealthCheckpointState Health;
        }
    }
}
