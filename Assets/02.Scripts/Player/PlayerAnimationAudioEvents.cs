using UnityEngine;

// Animator가 붙은 비주얼 오브젝트에서 발생한 AnimationEvent를
// 네트워크 플레이어 루트의 오디오 처리로 전달한다.
[DisallowMultipleComponent]
public sealed class PlayerAnimationAudioEvents : MonoBehaviour
{
    private PlayerController playerController;

    private void Awake()
    {
        playerController = GetComponentInParent<PlayerController>();
    }

    public void OnRepairHammerImpact()
    {
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();

        playerController?.OnRepairHammerImpact();
    }
}
