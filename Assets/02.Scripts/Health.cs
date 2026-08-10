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
    private static readonly int HPHash = Animator.StringToHash("HP");
    private float lastHitAnimationTime = -999f;

    // 공용 Health가 다른 Animator 파라미터를 잘못 호출하지 않도록 지원 여부 저장 서영 추가
    private bool supportsHitParameter;
    private bool supportsHPParameter;

    // 네트워크 동기화 등으로 데미지 처리를 다른 곳에 넘겨야 하는 경우에만 존재 (없으면 예전과 동일하게 동작)
    private IDamageRouter damageRouter;

    void Awake()
    {
        CurrentHealth = maxHealth;
        damageRouter = GetComponent<IDamageRouter>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Animator[] anims = GetComponentsInChildren<Animator>(true);
            foreach (var a in anims)
            {
                if (a.runtimeAnimatorController != null)
                {
                    animator = a;
                    break;
                }
            }
            if (animator == null) animator = GetComponent<Animator>();
        }
        CacheAnimatorParameterSupport();
        UpdateAnimatorHP();
    }

    // 상어와 잠수함처럼 다른 Controller를 사용하는 대상의 해시 경고를 막기 위해 파라미터 검사 서영 추가
    private void CacheAnimatorParameterSupport()
    {
        supportsHitParameter = false;
        supportsHPParameter = false;

        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        // 이름과 타입이 모두 맞는 파라미터만 Health 애니메이션 호출 대상으로 인정 서영 추가
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == HitHash
                && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                supportsHitParameter = true;
            }
            else if (parameter.nameHash == HPHash
                && parameter.type == AnimatorControllerParameterType.Float)
            {
                supportsHPParameter = true;
            }
        }
    }

    private void UpdateAnimatorHP()
    {
        // HP Float가 있는 플레이어 Animator에만 현재 체력 전달 서영 추가
        if (animator != null && supportsHPParameter)
        {
            animator.SetFloat(HPHash, CurrentHealth);
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

        // 네트워크 동기화 대상이면 여기서 직접 깎지 않고 권한자(호스트)에게 넘김
        if (damageRouter != null && damageRouter.RouteDamage(damageInfo))
            return 0f;

        float appliedAmount = Mathf.Min(damageInfo.RequestedAmount, CurrentHealth); // 실제 남은 체력보다 더 많은 데미지가 들어가는 거 방지용
        if (appliedAmount <= 0f)
            return 0f;

        // if (playHitAnimation && animator != null)
        // Hit Trigger가 없는 상어와 잠수함 Animator에는 피격 트리거를 보내지 않음 서영 추가
        if (damageInfo.PlayHitAnimation && animator != null && supportsHitParameter) // 서영 변경
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

        UpdateAnimatorHP();

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
        UpdateAnimatorHP();
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        return appliedAmount;
    }

    // 기절(사망) 상태에서 동료가 부활시켰을 때 호출. 최대 체력의 특정 비율로 되살아남.
    public void ReviveWithRatio(float ratio)
    {
        if (!IsDead) return;

        IsDead = false;
        CurrentHealth = maxHealth * Mathf.Clamp01(ratio);
        UpdateAnimatorHP();
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnRevived?.Invoke();
    }

    // 외부(네트워크 동기화 등)에서 권위 있는 값으로 그대로 맞출 때 사용.
    // 데미지/회복 판정 없이 값만 반영하고, 사망/부활 전이 시 기존 이벤트를 그대로 발생시킴.
    public void SyncFrom(float syncedHealth, bool syncedIsDead)
    {
        bool wasDead = IsDead;
        CurrentHealth = Mathf.Clamp(syncedHealth, 0f, maxHealth);
        IsDead = syncedIsDead;
        UpdateAnimatorHP();
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (!wasDead && IsDead) OnDeath?.Invoke();
        else if (wasDead && !IsDead) OnRevived?.Invoke();
    }
}
