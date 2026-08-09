using UnityEngine;

namespace Varco.GameFlow
{
    // 체력 복원에 필요한 최소 데이터
    public readonly struct HealthCheckpointState
    {
        public HealthCheckpointState(float currentHealth, bool isDead)
        {
            CurrentHealth = currentHealth;
            IsDead = isDead;
        }

        public float CurrentHealth { get; } // 캡처 당시 체력
        public bool IsDead { get; } // 캡처 당시 사망 상태
    }

    // 공용 Health 수정 없이 공개 API만 사용하는 복원 도구
    public static class GameFlowHealthUtility
    {
        // 현재 체력과 사망 상태 캡처
        public static HealthCheckpointState Capture(Health health)
        {
            return health == null
                ? default
                : new HealthCheckpointState(health.CurrentHealth, health.IsDead);
        }

        // 피해 회복 부활 API만 사용해 목표 체력 적용
        public static void Restore(Health health, HealthCheckpointState state)
        {
            if (health == null)
                return;

            // 최대 체력 범위 안으로 목표값 제한
            float target = Mathf.Clamp(state.CurrentHealth, 0f, health.maxHealth);

            // 사망 스냅샷은 피해 API를 통한 사망 처리
            if (state.IsDead)
            {
                if (!health.IsDead)
                    health.TakeDamage(Mathf.Max(health.CurrentHealth, health.maxHealth), null, false);
                return;
            }

            // 생존 스냅샷은 먼저 부활 처리
            if (health.IsDead)
            {
                float ratio = health.maxHealth > 0f ? target / health.maxHealth : 0f;
                health.ReviveWithRatio(ratio);
            }

            // 현재값과 목표값 차이만큼 회복 또는 피해 적용
            float difference = target - health.CurrentHealth;
            if (difference > 0f)
                health.Heal(difference);
            else if (difference < 0f)
                health.TakeDamage(-difference, null, false);
        }

        // 산소와 허기 같은 감소형 스탯 복원
        public static void RestoreStat(DepletingStat stat, float currentValue, float maxValue)
        {
            if (stat == null)
                return;

            stat.SetMaxValue(maxValue);
            float ratio = stat.maxValue > 0f ? currentValue / stat.maxValue : 0f;
            stat.SetValueRatio(ratio);
        }
    }
}
