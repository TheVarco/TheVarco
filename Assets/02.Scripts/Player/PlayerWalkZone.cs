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

    private PlayerController playerInZone;

    private void OnTriggerEnter(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null)
            playerInZone = controller;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller == playerInZone)
        {
            playerInZone = null;
            controller.SetSwimMode(true);
        }
    }

    private void Update()
    {
        if (playerInZone == null) return;

        bool nearGround = Physics.Raycast(playerInZone.transform.position, Vector3.down, groundCheckDistance, groundLayer);
        playerInZone.SetSwimMode(!nearGround);
    }
}
