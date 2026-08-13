using System.Collections.Generic;
using UnityEngine;

// 문 중앙의 얇은 Trigger를 통과한 방향을 플레이어별로 추적한다.
// 내부에서 진입해 외부로 완전히 빠져나간 경우에만 SubmarineDoor에 퇴장 이벤트를 전달한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class SubmarineExitAudioTrigger : MonoBehaviour
{
    private sealed class PassageState
    {
        public float EntrySide;
        public readonly HashSet<Collider> Colliders = new HashSet<Collider>();
    }

    [SerializeField] private SubmarineDoor door;

    private readonly Dictionary<PlayerController, PassageState> passages =
        new Dictionary<PlayerController, PassageState>();

    public void Initialize(SubmarineDoor owner)
    {
        door = owner;
    }

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;

        if (door == null)
            door = GetComponentInParent<SubmarineDoor>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (door == null || !door.CanProcessExitDetection || other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        if (!passages.TryGetValue(player, out PassageState state))
        {
            state = new PassageState
            {
                EntrySide = door.GetPassageSide(GetPlayerCenter(player))
            };
            passages.Add(player, state);
        }

        state.Colliders.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (door == null || !door.CanProcessExitDetection || other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !passages.TryGetValue(player, out PassageState state))
            return;

        state.Colliders.Remove(other);
        if (state.Colliders.Count > 0)
            return;

        passages.Remove(player);

        float exitSide = door.GetPassageSide(GetPlayerCenter(player));
        float threshold = door.PassageSideThreshold;
        if (state.EntrySide <= -threshold && exitSide >= threshold)
            door.NotifyPlayerExited();
    }

    private static Vector3 GetPlayerCenter(PlayerController player)
    {
        Rigidbody body = player.GetComponent<Rigidbody>();
        return body != null ? body.worldCenterOfMass : player.transform.position;
    }

    private void OnDisable()
    {
        passages.Clear();
    }
}
