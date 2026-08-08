using Fusion;
using UnityEngine;

// 팀원을 바라보면서 F키를 누르면, 지금 손에 든 아이템을 자기 자신이 아니라
// 그 팀원에게 사용하는 스크립트. PlayerReviver와 감지 방식(범위+각도+최단거리)이 동일하다.
public class PlayerItemGiver : NetworkBehaviour
{
    [Header("감지 설정")]
    public float giveRange = 2.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식")]
    public float giveAngle = 90f;
    [Tooltip("이 거리 안이면 각도 판정을 건너뜀 (바짝 붙었을 때 대상 방향이 불안정해지는 문제 방지)")]
    public float closeRangeIgnoreAngle = 1f;
    public KeyCode giveKey = KeyCode.F;

    [Header("참조")]
    [Tooltip("조준 기준점. 자기 몸에 안 걸리도록 AttackPoint 같은 몸 앞쪽 지점을 권장")]
    public Transform lookReference;
    public PlayerHotbar hotbar;
    [Tooltip("안내 문구를 띄울 상호작용기 (미지정 시 자동 감지)")]
    public PlayerInteractor interactor;
    [Tooltip("부활 안내와 문구가 겹치지 않게 확인 (미지정 시 자동 감지)")]
    public PlayerReviver reviver;

    void Awake()
    {
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (reviver == null) reviver = GetComponent<PlayerReviver>();
        if (hotbar == null) hotbar = GetComponent<PlayerHotbar>();
    }

    void Update()
    {
        // 내 캐릭터가 아니면(원격 플레이어) 로컬 입력을 읽지 않음. 비네트워크 씬에선 Object가 null이라 그대로 동작
        if (Object != null && !Object.HasInputAuthority) return;
        if (hotbar == null || lookReference == null) return;

        // 문구를 띄우려면 키를 누르지 않아도 매 프레임 대상을 확인해야 함
        // 소모품(산소통, 식량 등)만 대상. 총 같은 비소모품은 "상대에게 써서 없애는" 개념이 없음
        CarryableItem active = hotbar.GetActiveItem();
        GameObject teammate = (active != null && active.isConsumable) ? FindTeammate() : null;

        // 부활 안내가 우선. 같은 문구 칸을 둘이 번갈아 덮어쓰면 깜빡임
        if (reviver == null || !reviver.HasReviveTarget)
            SetPrompt(teammate != null ? $"{giveKey} : {active.itemName} {active.giveActionName}" : null);

        if (teammate == null || !Input.GetKeyDown(giveKey)) return;

        active.OnUse(gameObject, teammate); // 자신이 아니라 teammate를 대상으로 사용

        if (active.isConsumable)
            hotbar.RemoveActiveItem();
    }

    private bool promptActive; // 지금 화면 문구가 내가 쓴 것인지

    // 문구 칸은 PlayerReviver와 공유한다. 내가 쓴 게 아니면 남의 문구를 지우지 않는다
    private void SetPrompt(string text)
    {
        if (interactor == null) return;
        if (text == null && !promptActive) return;

        interactor.SetOverridePrompt(text);
        promptActive = text != null;
    }

    void OnDisable()
    {
        SetPrompt(null);
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

            // 쓰러진 팀원은 아이템 전달 대상에서 제외 (그쪽은 PlayerReviver가 부활로 처리)
            PlayerDownedState downed = col.GetComponentInParent<PlayerDownedState>();
            if (downed != null && downed.IsDowned) continue;

            // 콜라이더 피벗(플레이어는 발밑)이 아니라 몸통 중심 기준
            Vector3 toTarget = col.bounds.center - lookReference.position;
            float dist = toTarget.magnitude;

            // 바짝 붙으면 방향이 불안정해지므로 가까울 땐 각도 판정을 건너뛴다
            if (dist > closeRangeIgnoreAngle)
            {
                float angle = Vector3.Angle(lookReference.forward, toTarget);
                if (angle > giveAngle * 0.5f) continue;
            }

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