using UnityEngine;

// 팀원을 바라보면서 F키를 누르면, 지금 손에 든 아이템을 자기 자신이 아니라
// 그 팀원에게 사용하는 스크립트. PlayerReviver와 감지 방식(범위+각도+최단거리)이 동일하다.
public class PlayerItemGiver : MonoBehaviour
{
    [Header("감지 설정")]
    public float giveRange = 2.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식")]
    public float giveAngle = 90f;
    public KeyCode giveKey = KeyCode.F;

    [Header("참조")]
    [Tooltip("조준 기준점. 자기 몸에 안 걸리도록 AttackPoint 같은 몸 앞쪽 지점을 권장")]
    public Transform lookReference;
    public PlayerHotbar hotbar;

    void Update()
    {
        if (!Input.GetKeyDown(giveKey)) return;
        if (hotbar == null || lookReference == null) return;

        CarryableItem active = hotbar.GetActiveItem();
        if (active == null) return; // 손에 든 게 없으면 줄 것도 없음

        GameObject teammate = FindTeammate();
        if (teammate == null) return;

        active.OnUse(gameObject, teammate); // 자신이 아니라 teammate를 대상으로 사용

        if (active.isConsumable)
            hotbar.RemoveActiveItem();
    }

    private GameObject FindTeammate()
    {
        Collider[] candidates = Physics.OverlapSphere(lookReference.position, giveRange);

        GameObject best = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in candidates)
        {
            // 상대방도 PlayerHotbar가 붙어있어야 "플레이어"로 인식 (자기 자신은 제외)
            PlayerHotbar otherHotbar = col.GetComponentInParent<PlayerHotbar>();
            if (otherHotbar == null || otherHotbar == hotbar) continue;

            Vector3 toTarget = col.transform.position - lookReference.position;
            float angle = Vector3.Angle(lookReference.forward, toTarget);
            if (angle > giveAngle * 0.5f) continue;

            float dist = toTarget.magnitude;
            if (dist < closestDist)
            {
                closestDist = dist;
                best = otherHotbar.gameObject;
            }
        }

        return best;
    }

    // 디버그용: 씬 뷰에서 감지 범위(부채꼴)를 눈으로 확인하기 위함
    void OnDrawGizmosSelected()
    {
        if (lookReference == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(lookReference.position, giveRange);

        Quaternion leftEdge = Quaternion.AngleAxis(-giveAngle * 0.5f, Vector3.up);
        Quaternion rightEdge = Quaternion.AngleAxis(giveAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(lookReference.position, lookReference.position + leftEdge * lookReference.forward * giveRange);
        Gizmos.DrawLine(lookReference.position, lookReference.position + rightEdge * lookReference.forward * giveRange);
    }
}