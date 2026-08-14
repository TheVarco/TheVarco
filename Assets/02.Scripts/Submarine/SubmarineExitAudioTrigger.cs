using UnityEngine;

public enum SubmarineExitZoneKind
{
    Inside,
    Outside
}

// 프리팹에 배치된 Inside/Outside Trigger의 접촉을 SubmarineDoor에 전달한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class SubmarineExitAudioTrigger : MonoBehaviour
{
    [SerializeField] private SubmarineDoor door;
    [SerializeField] private SubmarineExitZoneKind zoneKind;

    public void Initialize(SubmarineDoor owner, SubmarineExitZoneKind kind)
    {
        door = owner;
        zoneKind = kind;

        Collider zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (door == null || other == null)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
            door.NotifyExitZoneEntered(player, zoneKind);
    }
}
