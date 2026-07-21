using UnityEngine;

// 플레이어에 붙이는 상호작용 감지기.
// 카메라가 보는 방향으로 구체 레이캐스트를 쏴서 가장 가까운 Interactable을 찾고,
// 상호작용 키를 누르면 실행한다. UI 갱신용 이벤트도 함께 제공.
public class PlayerInteractor : MonoBehaviour
{
    [Header("감지 설정")]
    [Tooltip("상호작용 가능한 최대 거리")]
    public float interactRange = 3f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식 (예: 90이면 좌우 45도씩)")]
    public float interactAngle = 90f;
    [Tooltip("이 레이어에 있는 오브젝트만 검사 (Interactable 전용 레이어 권장)")]
    public LayerMask interactableLayer = ~0;

    [Header("참조")]
    [Tooltip("조준 기준점. Player 자신(transform)을 쓰면 몸 위치/방향 기준, 카메라를 연결하면 시점 기준")]
    public Transform lookReference;
    public KeyCode interactKey = KeyCode.E;

    // UI가 구독해서 프롬프트를 띄우거나 지울 때 사용
    public System.Action<string> OnPromptChanged;

    private Interactable currentTarget;

    void Update()
    {
        DetectInteractable();

        if (currentTarget != null && Input.GetKeyDown(interactKey))
        {
            currentTarget.Interact(gameObject);
        }
    }

    private void DetectInteractable()
    {
        Interactable found = null;

        if (lookReference != null)
        {
            // 1단계: 범위 안에 있는 모든 Collider를 방향 상관없이 일단 다 찾음
            Collider[] candidates = Physics.OverlapSphere(lookReference.position, interactRange, interactableLayer);

            float closestDist = float.MaxValue;
            foreach (Collider col in candidates)
            {
                Vector3 toTarget = col.transform.position - lookReference.position;

                // 2단계: 정면 기준 각도(부채꼴) 안에 들어오는지 확인
                float angle = Vector3.Angle(lookReference.forward, toTarget);
                if (angle > interactAngle * 0.5f) continue;

                Interactable candidate = col.GetComponentInParent<Interactable>();
                if (candidate == null || !candidate.CanInteract(gameObject)) continue;

                // 3단계: 조건을 만족하는 것들 중 제일 가까운 걸 선택
                float dist = toTarget.magnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    found = candidate;
                }
            }
        }

        if (found != currentTarget)
        {
            currentTarget = found;
            OnPromptChanged?.Invoke(currentTarget != null ? currentTarget.GetInteractionPrompt() : null);
        }
    }

    // 디버그용: 씬 뷰에서 조준 범위(부채꼴)를 눈으로 확인하기 위함
    void OnDrawGizmosSelected()
    {
        if (lookReference == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lookReference.position, interactRange);

        Quaternion leftEdge = Quaternion.AngleAxis(-interactAngle * 0.5f, Vector3.up);
        Quaternion rightEdge = Quaternion.AngleAxis(interactAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(lookReference.position, lookReference.position + leftEdge * lookReference.forward * interactRange);
        Gizmos.DrawLine(lookReference.position, lookReference.position + rightEdge * lookReference.forward * interactRange);
    }
}