using UnityEngine;
using UnityEngine.Events;

// 체력이 필요한 모든 오브젝트(플레이어, 상어, 잠수함)에 그대로 붙여서 쓰는 공용 체력 컴포넌트.
// 데미지 수치는 공격 쪽 스크립트에서 넘어오고, 여기서는 "받은 만큼 깎고 0 이하면 죽는다"만 책임진다.
public class Health : MonoBehaviour, Damageable
{
    [Header("체력 수치 (밸런싱 담당자가 여기서 직접 조절)")]
    [Tooltip("최대 체력")]
    public float maxHealth = 100f;

    [Header("이벤트 (UI 체력바, 사망 연출 등에서 구독해서 사용)")]
    public UnityEvent<float, float> OnHealthChanged; // (현재 체력, 최대 체력)
    public UnityEvent OnDeath;
    public UnityEvent OnRevived;

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    void Awake()
    {
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float amount, GameObject source)
    {
        if (IsDead) return; // 이미 죽었으면 추가 데미지 무시

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Debug.Log($"[Health] {gameObject.name}이(가) {source.name}에게 {amount} 데미지를 받음. 남은 체력: {CurrentHealth}/{maxHealth}");
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0f)
        {
            IsDead = true;
            OnDeath?.Invoke();
        }
    }

    // 회복 아이템, 산소 보급 등에서 재사용할 수 있게 미리 만들어둠
    public void Heal(float amount)
    {
        if (IsDead) return;

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    // 기절(사망) 상태에서 동료가 부활시켰을 때 호출. 최대 체력의 특정 비율로 되살아남.
    public void ReviveWithRatio(float ratio)
    {
        if (!IsDead) return;

        IsDead = false;
        CurrentHealth = maxHealth * Mathf.Clamp01(ratio);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnRevived?.Invoke();
    }
}