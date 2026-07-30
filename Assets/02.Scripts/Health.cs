using UnityEngine;
using UnityEngine.Events;

// 체력이 필요한 모든 오브젝트(플레이어, 상어, 잠수함)에 그대로 붙여서 쓰는 공용 체력 컴포넌트.
// 데미지 수치는 공격 쪽 스크립트에서 넘어오고, 여기서는 "받은 만큼 깎고 0 이하면 죽는다"만 책임진다.
public class Health : MonoBehaviour, Damageable
{
    [Header("체력 수치 (밸런싱 담당자가 여기서 직접 조절)")]
    [Tooltip("최대 체력")]
    public float maxHealth = 100f;

    [Header("피격 애니메이션 설정")]
    [Tooltip("피격 애니메이션 최소 발동 간격(초). 이 시간 동안은 애니메이션이 연속으로 리셋되지 않음")]
    public float hitAnimationCooldown = 0.5f;

    [Header("이벤트 (UI 체력바, 사망 연출 등에서 구독해서 사용)")]
    public UnityEvent<float, float> OnHealthChanged; // (현재 체력, 최대 체력)
    public UnityEvent OnDeath;
    public UnityEvent OnRevived;
    public event System.Action<float, GameObject> OnDamaged; // 서영 추가
    public event System.Action<DamageAppliedInfo> OnDamageApplied; // 서영 추가

    [Header("참조")]
    [Tooltip("피격 애니메이션용 애니메이터 (미설정 시 자동 감지)")]
    public Animator animator;

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    private static readonly int HitHash = Animator.StringToHash("Hit");
    private float lastHitAnimationTime = -999f;

    void Awake()
    {
        CurrentHealth = maxHealth;
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }
    }

    public void TakeDamage(float amount, GameObject source)
    {
        // TakeDamage(amount, source, true);
        ApplyDamage(DamageInfo.WithoutImpact(amount, source)); // 서영 변경
    }

    public void TakeDamage(float amount, GameObject source, bool playHitAnimation)
    {
        // if (IsDead) return; // 이미 죽었으면 추가 데미지 무시
        ApplyDamage(DamageInfo.WithoutImpact(amount, source, DamageType.Unspecified, playHitAnimation)); // 서영 변경
    }

    public float ApplyDamage(DamageInfo damageInfo)
    {
        if (IsDead || damageInfo.RequestedAmount <= 0f)
            return 0f;

        float appliedAmount = Mathf.Min(damageInfo.RequestedAmount, CurrentHealth); // 실제 남은 체력보다 더 많은 데미지가 들어가는 거 방지용
        if (appliedAmount <= 0f)
            return 0f;

        // if (playHitAnimation && animator != null)
        if (damageInfo.PlayHitAnimation && animator != null) // 서영 변경
        {
            if (Time.time - lastHitAnimationTime >= hitAnimationCooldown)
            {
                animator.SetTrigger(HitHash);
                lastHitAnimationTime = Time.time;
            }
        }

        // CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        // Debug.Log($"[Health] {gameObject.name}이(가) {source.name}에게 {amount} 데미지를 받음. 남은 체력: {CurrentHealth}/{maxHealth}");

        // 서영 추가
        CurrentHealth -= appliedAmount;

        string sourceName = damageInfo.Source != null
            ? damageInfo.Source.name
            : "알 수 없는 대상";
        Debug.Log($"[Health] {gameObject.name}이(가) {sourceName}에게 {appliedAmount} 데미지를 받음. 남은 체력: {CurrentHealth}/{maxHealth}");

        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnDamaged?.Invoke(appliedAmount, damageInfo.Source); // 서영 추가
        OnDamageApplied?.Invoke(new DamageAppliedInfo(damageInfo, appliedAmount)); // 서영 추가, 어느 위치에 데미지가 들어갔는지 전달용

        if (CurrentHealth <= 0f)
        {
            IsDead = true;
            OnDeath?.Invoke();
        }

        return appliedAmount;
    }

    // 회복 아이템, 산소 보급 등에서 재사용할 수 있게 미리 만들어둠
    public float Heal(float amount)
    {
        // if (IsDead) return;
        if (IsDead || amount <= 0f)
            return 0f;

        float appliedAmount = Mathf.Min(amount, maxHealth - CurrentHealth);
        if (appliedAmount <= 0f)
            return 0f;

        // CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        CurrentHealth += appliedAmount;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        return appliedAmount;
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
