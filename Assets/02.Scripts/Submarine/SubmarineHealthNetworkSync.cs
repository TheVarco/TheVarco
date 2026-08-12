using Fusion;
using UnityEngine;

/// <summary>
/// 잠수함 체력은 StateAuthority에서만 변경하고 프록시는 권위 값을 Health에 반영
/// 프록시에서 발생한 로컬 적과 장애물 판정은 중복 피해를 막기 위해 소비
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class SubmarineHealthNetworkSync : NetworkBehaviour, IDamageRouter
{
    // 호스트가 기록하는 체력과 사망 상태
    [Networked] private float NetworkedHealth { get; set; }
    [Networked] private NetworkBool NetworkedIsDead { get; set; }

    // 로컬 Health 참조와 이벤트 연결 상태
    private Health health;
    private bool subscribed;

    // 필수 Health 참조 확보
    private void Awake()
    {
        // 같은 오브젝트의 Health 컴포넌트 저장
        health = GetComponent<Health>();
    }

    // 권한에 따라 초기 기록과 초기 적용 분리
    public override void Spawned()
    {
        // 네트워크 수명 동안 Health 변화 이벤트를 받을 수 있게 연결
        Subscribe();
        if (Object.HasStateAuthority)
        {
            // 호스트는 현재 Health 값을 최초 복제 상태로 기록
            NetworkedHealth = health.CurrentHealth;
            NetworkedIsDead = health.IsDead;
        }
        else
        {
            // 클라이언트는 처음 수신한 권위 값을 로컬 Health에 반영
            ApplyReplicatedHealth();
        }
    }

    // 클라이언트 화면에 최신 복제 상태 적용
    public override void Render()
    {
        // StateAuthority가 아닌 피어만 복제 체력 적용
        if (!Object.HasStateAuthority)
            ApplyReplicatedHealth();
    }

    // 네트워크 수명 종료 시 이벤트 연결 해제
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 네트워크 오브젝트가 사라질 때 Health 이벤트 연결 해제
        Unsubscribe();
    }

    // 비권위 피어의 로컬 피해 적용 차단
    public bool RouteDamage(DamageInfo damageInfo)
    {
        // 유효한 NetworkObject인지 확인하고 클라이언트 피해 소비 여부 반환
        if (Object == null || !Object.IsValid)
            return false;

        // 충돌과 적과 장애물은 호스트도 같은 물리 세계에서 판정
        // 프록시의 판정을 RPC로 다시 보내면 동일 공격이 피어 수만큼 중복
        return !Object.HasStateAuthority;
    }

    // 체력 변화 이벤트 한 번만 연결
    private void Subscribe()
    {
        // 중복 구독을 막고 체력 변화와 사망과 부활 이벤트 연결
        if (subscribed || health == null)
            return;

        health.OnHealthChanged.AddListener(HandleHealthChanged);
        health.OnDeath.AddListener(HandleDeath);
        health.OnRevived.AddListener(HandleRevived);
        subscribed = true;
    }

    // 연결된 체력 이벤트 안전 해제
    private void Unsubscribe()
    {
        // 구독 상태와 Health 참조를 확인한 뒤 모든 리스너 제거
        if (!subscribed || health == null)
            return;

        health.OnHealthChanged.RemoveListener(HandleHealthChanged);
        health.OnDeath.RemoveListener(HandleDeath);
        health.OnRevived.RemoveListener(HandleRevived);
        subscribed = false;
    }

    // 호스트 체력 변화 복제
    private void HandleHealthChanged(float current, float maximum)
    {
        // 호스트에서만 현재 체력을 네트워크 속성에 기록
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedHealth = current;
    }

    // 호스트 사망 상태 복제
    private void HandleDeath()
    {
        // 호스트에서만 사망 불 값을 참으로 기록
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedIsDead = true;
    }

    // 호스트 부활 상태 복제
    private void HandleRevived()
    {
        // 호스트에서만 사망 불 값을 거짓으로 기록
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetworkedIsDead = false;
    }

    // 복제 값이 달라진 경우에만 Health 갱신
    private void ApplyReplicatedHealth()
    {
        // 로컬 값과 복제 값이 다를 때만 Health 동기화 API 호출
        if (health == null)
            return;

        if (!Mathf.Approximately(health.CurrentHealth, NetworkedHealth)
            || health.IsDead != NetworkedIsDead)
        {
            health.SyncFrom(NetworkedHealth, NetworkedIsDead);
        }
    }

    // 오브젝트 파괴 시 이벤트 누수 방지
    private void OnDestroy()
    {
        // 일반 Unity 파괴 경로에서도 이벤트 연결 해제
        Unsubscribe();
    }
}
