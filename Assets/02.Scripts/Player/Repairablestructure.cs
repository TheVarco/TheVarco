using UnityEngine;

// 잠수함처럼 "망치로 수리 가능한 구조물"에 붙이는 스크립트.
// 체력 자체는 새로 안 만들고 기존 Health를 그대로 재사용하며, 여기서는
// "이건 수리 대상이다"라는 표식 역할 + 체력에 따라 손상 데칼을 켜고 끄는 역할만 함.
[RequireComponent(typeof(Health))]
public class RepairableStructure : MonoBehaviour
{
    [Tooltip("체력이 낮을수록 순서대로 더 많이 표시될 손상 데칼들 (에디터에서 미리 배치)")]
    public GameObject[] damageDecals;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        health.OnHealthChanged.AddListener(UpdateDecals);

        UpdateDecals(health.CurrentHealth, health.maxHealth); // 시작할 때도 한 번 맞춰둠
    }

    private void UpdateDecals(float current, float max)
    {
        if (damageDecals == null || damageDecals.Length == 0 || max <= 0f) return;

        float damageRatio = 1f - (current / max); // 0 = 멀쩡함, 1 = 완전 박살
        int visibleCount = Mathf.RoundToInt(damageRatio * damageDecals.Length);

        for (int i = 0; i < damageDecals.Length; i++)
        {
            damageDecals[i].SetActive(i < visibleCount);
        }
    }

    // 망치(HammerItem)가 이 함수를 불러서 실제로 체력을 회복시킴
    public void Repair(float amount)
    {
        health.Heal(amount);
    }
}