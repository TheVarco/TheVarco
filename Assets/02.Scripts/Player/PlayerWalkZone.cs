using System.Collections.Generic;
using UnityEngine;

// 잠수함 내부처럼 "걸어야 하는 공간"에 트리거로 배치.
// 트리거 안에 있어도 바로 밑에 바닥이 없으면(입구 통과 중 등) 계속 수영 모드를 유지하고,
// 바닥이 가까울 때만 실제로 걷기 모드로 전환함.
[RequireComponent(typeof(Collider))]
public class PlayerWalkZone : MonoBehaviour
{
    [Tooltip("바닥까지 이 거리 안이면 걷기 모드로 전환 (아니면 트리거 안이어도 계속 수영 모드 유지)")]
    public float groundCheckDistance = 1f;
    [Tooltip("걷기에서 수영으로 되돌아갈 때 쓰는 거리 배수.\n" +
             "1이면 경계에서 두 모드가 매 프레임 뒤집혀 몸이 떨고 애니메이션이 겹친다")]
    [Range(1f, 3f)] public float releaseDistanceMultiplier = 1.6f;
    [Tooltip("모드가 한 번 바뀌면 이 시간 동안은 다시 안 바꾼다.\n" +
             "경계에서 레이캐스트가 흔들려도 프레임마다 뒤집히지 않는다")]
    public float modeSwitchCooldown = 0.5f;
    public LayerMask groundLayer = ~0;

    // 여러 명이 동시에 들어올 수 있다. 한 명만 들고 있으면 나중에 들어온 사람이 앞사람을 덮어써서,
    // 모든 플레이어의 이동을 시뮬레이션하는 호스트에서 누군가는 잠수함 안인데도 수영 모드로 남는다.
    // 값은 "다음에 모드를 바꿔도 되는 시각"
    private readonly Dictionary<PlayerController, float> playersInZone = new Dictionary<PlayerController, float>();
    private readonly List<PlayerController> iterationBuffer = new List<PlayerController>();

    public bool ContainsPlayer(PlayerController player)
    {
        return player != null && playersInZone.ContainsKey(player);
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null)
            playersInZone[controller] = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null && playersInZone.Remove(controller))
            controller.SetSwimMode(true);
    }

    private void Update()
    {
        if (playersInZone.Count == 0) return;

        // 순회 중에 값을 고쳐야 해서 키를 먼저 뜬다 (버퍼는 재사용해 매 프레임 할당을 안 만듦)
        iterationBuffer.Clear();
        foreach (PlayerController p in playersInZone.Keys) iterationBuffer.Add(p);

        foreach (PlayerController player in iterationBuffer)
        {
            if (player == null)
            {
                playersInZone.Remove(player); // 나가서 despawn된 플레이어 정리
                continue;
            }

            // 수영 중이면 짧은 거리로 "바닥에 닿았나"를 보고, 이미 걷는 중이면 더 멀어질
            // 때까지 걷기를 유지한다. 문턱이 하나뿐이면 경계에서 매 프레임 모드가 뒤집히고,
            // 뒤집힐 때마다 중력이 켜졌다 꺼져서 몸이 떨고 애니메이션도 걷기/헤엄을 오간다
            float checkDistance = player.IsSwimMode
                ? groundCheckDistance
                : groundCheckDistance * releaseDistanceMultiplier;

            Vector3 origin = player.transform.position + Vector3.up * 0.1f;
            bool wantSwim = !Physics.Raycast(origin, Vector3.down, checkDistance, groundLayer, QueryTriggerInteraction.Ignore);

            if (wantSwim == player.IsSwimMode) continue;

            // 거리 문턱을 벌려도 레이캐스트 자체가 흔들리면(물리 보간, 아래를 지나가는 물체 등)
            // 경계를 계속 넘나든다. 한 번 바꾸면 잠깐 잠가서 원인이 뭐든 초당 60번 뒤집히는 건 막는다
            if (Time.time < playersInZone[player]) continue;

            playersInZone[player] = Time.time + modeSwitchCooldown;
            player.SetSwimMode(wantSwim);
        }
    }
}
