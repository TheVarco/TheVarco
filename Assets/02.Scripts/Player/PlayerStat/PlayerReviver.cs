using UnityEngine;

// Player에 붙여서, 기절한 동료를 바라보며 E키를 일정 시간 누르고 있으면 부활시키는 스크립트.
// 즉시 부활이 아니라 "채널링"이라, 부활시키는 동안 나도 위험에 노출되는 협동 긴장감을 만든다.
public class PlayerReviver : MonoBehaviour
{
    [Header("감지 설정")]
    public float reviveRange = 2.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식")]
    public float reviveAngle = 90f;

    [Header("채널링 설정")]
    [Tooltip("부활에 걸리는 시간(초)")]
    public float reviveDuration = 2.5f;
    public KeyCode reviveKey = KeyCode.E;

    [Header("참조")]
    public Transform lookReference;

    public float ChannelProgress01 => currentTarget != null ? channelTimer / reviveDuration : 0f;

    private float channelTimer = 0f;
    private PlayerDownedState currentTarget;

    void Update()
    {
        PlayerDownedState target = FindDownedTarget();

        // 보고 있는 대상이 바뀌면(다른 사람으로 바뀌거나, 대상을 놓치면) 채널링 진행도 리셋
        if (target != currentTarget)
        {
            channelTimer = 0f;
            currentTarget = target;
        }

        if (currentTarget == null) return;

        if (Input.GetKey(reviveKey))
        {
            channelTimer += Time.deltaTime;

            if (channelTimer >= reviveDuration)
            {
                currentTarget.Revive();
                channelTimer = 0f;
                currentTarget = null;
            }
        }
        else
        {
            // 키를 떼면 진행도 리셋 (중간에 멈추면 처음부터 다시)
            channelTimer = 0f;
        }
    }

    private PlayerDownedState FindDownedTarget()
    {
        if (lookReference == null) return null;

        Collider[] candidates = Physics.OverlapSphere(lookReference.position, reviveRange);

        PlayerDownedState best = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in candidates)
        {
            PlayerDownedState downed = col.GetComponentInParent<PlayerDownedState>();
            if (downed == null || !downed.IsDowned) continue;
            if (downed.gameObject == gameObject) continue; // 자기 자신은 부활 대상에서 제외

            Vector3 toTarget = col.transform.position - lookReference.position;
            float angle = Vector3.Angle(lookReference.forward, toTarget);
            if (angle > reviveAngle * 0.5f) continue;

            float dist = toTarget.magnitude;
            if (dist < closestDist)
            {
                closestDist = dist;
                best = downed;
            }
        }

        return best;
    }
}