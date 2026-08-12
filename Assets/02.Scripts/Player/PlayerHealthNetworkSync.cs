using Fusion;
using UnityEngine;

// Health(플레이어/상어/잠수함 공용 컴포넌트)는 건드리지 않고, Player한테만 붙어서
// 그 값을 네트워크로 동기화하는 다리 역할.
// State Authority(호스트) 쪽 Health 변화를 네트워크 프로퍼티에 실어 보내고,
// 다른 클라이언트들은 그 프로퍼티 변화를 받아 로컬 Health에 그대로 반영한다.
[RequireComponent(typeof(Health))]
public class PlayerHealthNetworkSync : NetworkBehaviour, IDamageRouter
{
    [Networked, OnChangedRender(nameof(OnNetworkedStateChanged))]
    private float NetworkedHealth { get; set; }

    [Networked, OnChangedRender(nameof(OnNetworkedStateChanged))]
    private NetworkBool NetworkedIsDead { get; set; }

    private Health health;

    private static readonly int HitHash = Animator.StringToHash("Hit");

    void Awake()
    {
        health = GetComponent<Health>();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            NetworkedHealth = health.CurrentHealth;
            NetworkedIsDead = health.IsDead;
        }

        health.OnHealthChanged.AddListener(HandleLocalHealthChanged);
        health.OnDeath.AddListener(HandleLocalDeath);
        health.OnRevived.AddListener(HandleLocalRevived);
        health.OnDamageApplied += HandleDamageApplied;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (health == null) return;

        health.OnHealthChanged.RemoveListener(HandleLocalHealthChanged);
        health.OnDeath.RemoveListener(HandleLocalDeath);
        health.OnRevived.RemoveListener(HandleLocalRevived);
        health.OnDamageApplied -= HandleDamageApplied;
    }

    // 호스트에서 실제로 데미지가 적용됐을 때만 다른 클라이언트에 피격 모션을 전파한다.
    // (산소 익사처럼 PlayHitAnimation이 false인 데미지는 제외)
    private float lastHitRpcTime = -999f;

    private void HandleDamageApplied(DamageAppliedInfo info)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        if (!info.Damage.PlayHitAnimation) return;

        // Health가 로컬에서 쿨다운으로 걸러내는 것과 같은 간격을 원격에도 적용한다.
        // (OnDamageApplied는 쿨다운과 무관하게 매번 발생해서, 안 그러면 원격에서만 모션이 더 자주 재생됨)
        if (Time.time - lastHitRpcTime < health.hitAnimationCooldown) return;
        lastHitRpcTime = Time.time;

        RPC_PlayHitAnimation();
    }

    // Proxies = 권한자를 뺀 나머지 전부. 호스트는 이미 로컬에서 재생했으므로 중복 재생 방지
    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    private void RPC_PlayHitAnimation()
    {
        if (health != null && health.animator != null)
            health.animator.SetTrigger(HitHash);
    }

    // 로컬 Health가 바뀌면(데미지/힐/부활) - State Authority만 네트워크 프로퍼티에 반영
    private void HandleLocalHealthChanged(float current, float max)
    {
        if (!Object.HasStateAuthority) return;
        NetworkedHealth = current;
    }

    private void HandleLocalDeath()
    {
        if (!Object.HasStateAuthority) return;
        NetworkedIsDead = true;
    }

    private void HandleLocalRevived()
    {
        if (!Object.HasStateAuthority) return;
        NetworkedIsDead = false;
    }

    // 네트워크 프로퍼티가 바뀌면(호스트에서 온 변화) - 로컬 Health에 그대로 반영
    private void OnNetworkedStateChanged()
    {
        if (Object.HasStateAuthority) return; // 내가 원본이면 이미 반영된 상태
        health.SyncFrom(NetworkedHealth, NetworkedIsDead);
    }

    // Health가 체력을 깎기 직전에 물어봄 (IDamageRouter).
    // 내가 권한자가 아니면 로컬에서 깎아봤자 아무에게도 안 퍼지므로, 호스트에 넘기고 true를 반환한다.
    public bool RouteDamage(DamageInfo damageInfo)
    {
        if (Object == null) return false;           // 비네트워크 씬이면 기존대로 로컬 처리
        if (Object.HasStateAuthority) return false; // 내가 권한자면 로컬 처리가 곧 정답 (RPC 낭비 없음)

        NetworkObject sourceObj = damageInfo.Source != null
            ? damageInfo.Source.GetComponentInParent<NetworkObject>()
            : null;

        RPC_RequestDamage(
            damageInfo.RequestedAmount,
            sourceObj != null ? sourceObj.Id : default,
            (int)damageInfo.Type,
            damageInfo.PlayHitAnimation,
            damageInfo.HasImpactPoint,
            damageInfo.Point,
            damageInfo.Normal);

        return true; // 호스트에 넘겼으니 로컬에서는 깎지 않음
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestDamage(float amount, NetworkId sourceId, int typeValue,
        NetworkBool playHitAnimation, NetworkBool hasImpactPoint, Vector3 point, Vector3 normal)
    {
        GameObject source = null;
        if (Runner.TryFindObject(sourceId, out NetworkObject sourceObj))
            source = sourceObj.gameObject;

        DamageType type = (DamageType)typeValue;
        DamageInfo info = hasImpactPoint
            ? new DamageInfo(amount, source, point, normal, type, playHitAnimation)
            : DamageInfo.WithoutImpact(amount, source, type, playHitAnimation);

        // 호스트에서는 HasStateAuthority라 RouteDamage가 false를 반환하므로 무한루프 없이 정상 처리됨
        health.ApplyDamage(info);
    }
}
