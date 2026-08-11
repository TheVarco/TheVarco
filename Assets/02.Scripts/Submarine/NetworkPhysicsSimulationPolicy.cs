using Fusion;
using UnityEngine;

/// <summary>
/// Physics Addon Spawn 검증 전 원격 Client Proxy의 로컬 Fusion Simulation 등록
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPhysicsSimulationPolicy : NetworkBehaviour
{
    public override void Spawned()
    {
        // State Authority와 Input Authority Object는 Fusion Simulation에 기본 등록
        // Dedicated Server도 State Authority 보유
        // 현재 Topology와 무관한 Client 원격 Proxy 등록
        if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            Runner.SetIsSimulated(Object, true);
    }
}
