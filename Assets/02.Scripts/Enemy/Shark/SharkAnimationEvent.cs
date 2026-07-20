using UnityEngine;

public class SharkAnimationEvent : MonoBehaviour
{
    private SharkController shark;

    private void Awake()
    {
        shark = GetComponentInParent<SharkController>();
    }

    public void AnimEvent_BeginBite()
    {
        shark.BeginAttackHitbox();
    }

    public void AnimEvent_EndBite()
    {
        shark.EndAttackHitbox();
    }
}