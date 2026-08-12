using Fusion;
using UnityEngine;

// 로컬 플레이어가 스폰되면, 씬에 있는 플레이어 관련 UI(체력바/산소바/상호작용 프롬프트/핫바)를
// 찾아서 내 컴포넌트들과 연결한다.
// 프리팹은 씬의 UI 오브젝트를 미리 참조할 수 없어서, 스폰 시 코드로 직접 연결한다.
// (핫바 UI는 자체적으로 아무 플레이어나 찾아 붙기 때문에, 여기서 내 것으로 덮어써야 멀티에서 안 섞임)
public class PlayerStatUIBinder : NetworkBehaviour
{
    [Tooltip("씬에서 찾을 체력바 오브젝트 이름")]
    public string healthBarObjectName = "HealthBar";
    [Tooltip("씬에서 찾을 산소바 오브젝트 이름")]
    public string oxygenBarObjectName = "OxygenBar";

    void Start()
    {
        // Fusion 러너가 없는 단독 씬(로컬 테스트)에서도 체력바/산소바가 바로 바인딩되도록 지원
        if (Object == null)
        {
            BindStats();
        }
    }

    public override void Spawned()
    {
        if (!Object.HasInputAuthority) return;

        BindStats();

        // 상호작용 프롬프트 UI: 내 PlayerInteractor의 신호를 받도록 등록
        PlayerInteractor myInteractor = GetComponent<PlayerInteractor>();
        InteractionPromptUI promptUI = FindFirstObjectByType<InteractionPromptUI>();
        if (myInteractor != null && promptUI != null)
            promptUI.SetInteractor(myInteractor);

        // 핫바 UI: 아무 플레이어나 잡지 않도록 내 핫바로 덮어씀
        PlayerHotbar myHotbar = GetComponent<PlayerHotbar>();
        HotbarUI hotbarUI = FindFirstObjectByType<HotbarUI>();
        if (myHotbar != null && hotbarUI != null)
            hotbarUI.hotbar = myHotbar;

        // 무기 조준 기준점: 프리팹은 씬의 CameraRig를 참조할 수 없어서 스폰 시 코드로 연결
        // (RangedWeaponItem이 이걸로 조준 방향과 줌을 계산하고, 비어있으면 발사 자체가 막힘)
        PlayerCameraRig aimRig = FindFirstObjectByType<PlayerCameraRig>();
        if (myHotbar != null && aimRig != null)
            myHotbar.aimReference = aimRig.transform;
    }

    private void BindStats()
    {
        // 1. 체력바 연결 (Health.OnHealthChanged -> StatBarUI.UpdateBar)
        Health health = GetComponent<Health>();
        if (health != null)
        {
            StatBarUI healthBar = FindStatBar(healthBarObjectName);
            if (healthBar != null)
            {
                health.OnHealthChanged.RemoveListener(healthBar.UpdateBar);
                health.OnHealthChanged.AddListener(healthBar.UpdateBar);
                healthBar.UpdateBar(health.CurrentHealth, health.maxHealth); // 초기값 즉시 반영
            }
        }

        // 2. 산소바 연결 (체력바와 동일한 방식: OxygenStat.OnValueChanged -> StatBarUI.UpdateBar)
        OxygenStat oxygen = GetComponentInChildren<OxygenStat>(); // Stats 자식 오브젝트에 있음
        HungerStat hunger = GetComponentInChildren<HungerStat>();

        if (oxygen != null)
        {
            StatBarUI oxygenBar = FindStatBar(oxygenBarObjectName);
            if (oxygenBar != null)
            {
                oxygenBar.oxygenStat = oxygen;
                oxygenBar.hungerStat = hunger;
                oxygen.OnValueChanged.RemoveListener(oxygenBar.UpdateBar);
                oxygen.OnValueChanged.AddListener(oxygenBar.UpdateBar);
                oxygenBar.UpdateBar(oxygen.CurrentValue, oxygen.maxValue); // 초기값 즉시 반영
            }

            // SegmentedStatBarUI도 함께 붙어있다면 호환성을 위해 stat 참조 전달
            GameObject oxygenBarObj = GameObject.Find(oxygenBarObjectName);
            SegmentedStatBarUI segmentedBar = oxygenBarObj != null ? oxygenBarObj.GetComponent<SegmentedStatBarUI>() : null;
            if (segmentedBar != null)
                segmentedBar.stat = oxygen;
        }
    }

    private StatBarUI FindStatBar(string objectName)
    {
        GameObject obj = GameObject.Find(objectName);
        return obj != null ? obj.GetComponent<StatBarUI>() : null;
    }
}
