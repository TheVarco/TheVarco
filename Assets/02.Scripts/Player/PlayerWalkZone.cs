using UnityEngine;

// 잠수함 내부처럼 "걸어야 하는 공간"에 트리거로 배치.
// 플레이어가 들어오면 걷기 모드로, 나가면 다시 수영 모드로 전환시킴.
[RequireComponent(typeof(Collider))]
public class PlayerWalkZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null)
            controller.SetSwimMode(false);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController controller = other.GetComponentInParent<PlayerController>();
        if (controller != null)
            controller.SetSwimMode(true);
    }
}
