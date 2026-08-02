using UnityEngine;

// 씬에 배치하는 회오리. 주변 플레이어를 감지해서 중심으로 끌어당기고,
// 안쪽 반경(innerRadius)에 들어오면 "갇힘" 상태로 표시한다.
// 조작을 강제로 끄지는 않고, 당기는 힘 자체가 세게 설계되어 있어서 물리적으로 혼자 못 빠져나가는 방식.
public class Whirlpool : MonoBehaviour
{
    [Header("범위 설정")]
    [Tooltip("이 반경 밖에 있으면 회오리 영향을 아예 안 받음. 이 반경 안으로 들어오면 거리에 비례해서 서서히 당겨지기 시작함")]
    public float outerRadius = 10f;
    [Tooltip("이 반경보다 안쪽으로 들어오면 '갇힘' 상태가 됨. 밧줄에 끌려서 이 반경을 다시 벗어나면 그 순간 풀려남")]
    public float innerRadius = 2f;

    [Header("힘 설정 (밸런싱용)")]
    [Tooltip("안쪽 반경(Inner Radius) 안에서는 항상 이 힘으로 고정 당김. 이 값이 Rope Pull Force보다 세면 혼자 못 버티고, 밧줄 구조 시 이 값 대 Rope Pull Force가 그대로 힘겨루기가 됨")]
    public float maxPullForce = 20f;

    [Tooltip("어떤 대상을 회오리 영향 대상으로 볼지 (Player 전용 레이어로 좁혀두면 다른 오브젝트까지 끌려오는 걸 방지)")]
    public LayerMask playerLayer = ~0;

    void FixedUpdate()
    {
        Collider[] candidates = Physics.OverlapSphere(transform.position, outerRadius, playerLayer);

        foreach (Collider col in candidates)
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null) continue;

            Vector3 toCenter = transform.position - rb.position;
            float dist = toCenter.magnitude;
            if (dist < 0.01f) continue;

            // 안쪽 반경(innerRadius) 안에서는 항상 최대 힘으로 고정 - "갇힌 동안은 일정하게 강한 저항"을 만들어서,
            // Rope Pull Force랑 이 Max Pull Force가 숫자 그대로 정직하게 겨루게 함.
            // innerRadius ~ outerRadius 사이에서만 거리에 따라 서서히 약해짐
            float pullForce;
            if (dist <= innerRadius)
            {
                pullForce = maxPullForce;
            }
            else
            {
                float t = Mathf.Clamp01((outerRadius - dist) / (outerRadius - innerRadius));
                pullForce = maxPullForce * t;
            }

            rb.AddForce(toCenter.normalized * pullForce, ForceMode.Acceleration);

            PlayerWhirlpoolState trapState = col.GetComponentInParent<PlayerWhirlpoolState>();

            // 테스트용: 갇혀있는 동안 1초에 한 번씩 실제 힘/거리 확인
            if (trapState != null && trapState.IsTrapped)
            {
                logTimer += Time.fixedDeltaTime;
                if (logTimer >= 1f)
                {
                    logTimer = 0f;
                    Debug.Log($"[Whirlpool] {col.gameObject.name} 갇힌 거리={dist:F2}, 회오리 힘={pullForce:F1}");
                }
            }

            // 안쪽 반경에 아직 없으면 가두고, 이미 갇혀있었는데 벗어났으면 풀어줌
            // (밧줄에 끌려 나오다가 반경을 벗어나는 순간이 바로 "진짜로 탈출한" 순간)
            if (trapState != null)
            {
                if (dist <= innerRadius && !trapState.IsTrapped)
                {
                    trapState.Trap(transform);
                }
                else if (dist > innerRadius && trapState.IsTrapped)
                {
                    trapState.Release();
                }
            }
        }
    }

    private float logTimer = 0f;

    // 디버그용: 씬 뷰에서 바깥/안쪽 범위를 눈으로 확인하기 위함
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, outerRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, innerRadius);
    }
}