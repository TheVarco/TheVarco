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
    public LayerMask groundLayer = ~0;

    // 여러 명이 동시에 들어올 수 있다. 한 명만 들고 있으면 나중에 들어온 사람이 앞사람을 덮어써서,
    // 모든 플레이어의 이동을 시뮬레이션하는 호스트에서 누군가는 잠수함 안인데도 수영 모드로 남는다
    private readonly HashSet<PlayerController> playersInZone = new HashSet<PlayerController>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null)
            playersInZone.Add(controller);
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

        playersInZone.RemoveWhere(p => p == null); // 나가서 despawn된 플레이어 정리

        foreach (PlayerController player in playersInZone)
        {
            Vector3 origin = player.transform.position + Vector3.up * 0.1f;
            bool nearGround = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
            player.SetSwimMode(!nearGround);
        }
    }
}
