using UnityEngine;

// 좌클릭을 누르고 있는 동안, 바라보는 GrabbableItem을 물리적으로 끌어당겨 들고 다니는 스크립트.
// SpringJoint 대신 매 프레임 직접 스프링-감쇠 힘을 계산해서 AddForce로 적용한다.
// 이렇게 하면 최대 힘(maxForce)을 확실하게 제한할 수 있어서 "날아다니는" 현상을 막을 수 있고,
// 손에 딱 붙이는 방식(부모-자식)이 아니라서 여러 명이 동시에 같은 물체를 잡아도 각자 힘이 자연스럽게 더해진다.
public class PlayerGrabber : MonoBehaviour
{
    [Header("감지 설정")]
    public float grabRange = 2.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식 (예: 90이면 좌우 45도씩)")]
    public float grabAngle = 90f;
    public LayerMask grabbableLayer = ~0;

    [Header("들고 있을 때 위치")]
    [Tooltip("잡은 물체를 플레이어 앞 몇 미터 지점에 유지할지")]
    public float holdDistance = 1f;
    [Tooltip("잡은 물체를 카메라 높이 기준 위/아래로 얼마나 띄울지")]
    public float holdHeightOffset = 0f;

    [Header("참조")]
    [Tooltip("조준 기준이 되는 카메라(또는 카메라 리그) Transform")]
    public Transform lookReference;
    [Tooltip("맨손(슬롯 1)일 때만 잡기 가능하게 하려면 연결. 안 하면 항상 가능")]
    public PlayerHotbar hotbar;
    public KeyCode grabKey = KeyCode.Mouse0;
    [Tooltip("플레이어 애니메이터 (미설정 시 자동 감지)")]
    public Animator animator;

    private Rigidbody grabbedBody;
    private GrabbableItem grabbedItem;
    private static readonly int IsPushPullHash = Animator.StringToHash("IsPushPull");

    void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }
    }

    void Update()
    {
        // 무기 슬롯을 들고 있으면 손이 찼으니 무거운 물체를 못 잡게 함
        bool bareHanded = hotbar == null || hotbar.ActiveSlot == 1;
        if (!bareHanded)
        {
            if (grabbedBody != null) Release();
            if (animator != null) animator.SetBool(IsPushPullHash, false);
            return;
        }

        // 맨손 상태에서 좌클릭(Mouse0)을 누르고 있으면 PushPull 모션 실행
        bool isHoldingGrab = Input.GetKey(grabKey);
        if (animator != null)
        {
            animator.SetBool(IsPushPullHash, isHoldingGrab || grabbedBody != null);
        }

        if (Input.GetKeyDown(grabKey))
            TryGrab();
        else if (Input.GetKeyUp(grabKey))
            Release();
    }

    void FixedUpdate()
    {
        if (grabbedBody == null || grabbedItem == null) return;

        Vector3 targetPoint = lookReference.position
            + lookReference.forward * holdDistance
            + Vector3.up * holdHeightOffset;

        Vector3 toTarget = targetPoint - grabbedBody.position;

        // 직접 스프링-감쇠 힘 계산: (목표까지 거리 * 스프링 세기) - (현재 속도 * 감쇠)
        Vector3 force = toTarget * grabbedItem.spring - grabbedBody.linearVelocity * grabbedItem.damper;

        // 여기서 상한선을 걸어서, 너무 멀어졌을 때 순간적으로 확 튕겨나가는 걸 방지
        force = Vector3.ClampMagnitude(force, grabbedItem.maxForce);

        grabbedBody.AddForce(force, ForceMode.Force);
    }

    private void TryGrab()
    {
        // 1단계: 범위 안에 있는 모든 Collider를 방향 상관없이 일단 다 찾음
        Collider[] candidates = Physics.OverlapSphere(lookReference.position, grabRange, grabbableLayer);

        GrabbableItem bestTarget = null;
        Rigidbody bestBody = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in candidates)
        {
            GrabbableItem grabbable = col.GetComponentInParent<GrabbableItem>();
            if (grabbable == null) continue;

            Vector3 toTarget = col.transform.position - lookReference.position;

            // 2단계: 정면 기준 각도(부채꼴) 안에 들어오는지 확인
            float angle = Vector3.Angle(lookReference.forward, toTarget);
            if (angle > grabAngle * 0.5f) continue;

            // 3단계: 조건을 만족하는 것들 중 제일 가까운 걸 선택
            float dist = toTarget.magnitude;
            if (dist < closestDist)
            {
                closestDist = dist;
                bestTarget = grabbable;
                bestBody = col.attachedRigidbody;
            }
        }

        if (bestTarget == null || bestBody == null) return;

        grabbedBody = bestBody;
        grabbedItem = bestTarget;

        if (animator != null)
        {
            animator.SetBool(IsPushPullHash, true);
        }
    }

    private void Release()
    {
        grabbedBody = null;
        grabbedItem = null;

        if (animator != null)
        {
            animator.SetBool(IsPushPullHash, false);
        }
    }

    // 디버그용: 씬 뷰에서 감지 범위(부채꼴)를 눈으로 확인하기 위함
    void OnDrawGizmosSelected()
    {
        if (lookReference == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(lookReference.position, grabRange);

        Quaternion leftEdge = Quaternion.AngleAxis(-grabAngle * 0.5f, Vector3.up);
        Quaternion rightEdge = Quaternion.AngleAxis(grabAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(lookReference.position, lookReference.position + leftEdge * lookReference.forward * grabRange);
        Gizmos.DrawLine(lookReference.position, lookReference.position + rightEdge * lookReference.forward * grabRange);
    }
}