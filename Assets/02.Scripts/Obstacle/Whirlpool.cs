using System.Collections.Generic;
using UnityEngine;

// 씬에 배치하는 회오리. 스스로 플레이어를 찾아 힘을 주지 않고,
// "어디에 얼마나 센 회오리가 있는지"만 들고 있는다.
// 실제로 당기는 건 각 플레이어의 PlayerWhirlpoolState가 자기 네트워크 틱에서 처리한다.
// (Unity FixedUpdate에서 힘을 주면 Fusion이 굴리는 틱과 어긋나서, 클라이언트 예측이
//  되감길 때 회오리 힘만 다시 계산되지 않아 호스트와 위치가 벌어진다)
public class Whirlpool : MonoBehaviour
{
    // 플레이어가 매 틱 OverlapSphere를 돌리지 않도록 씬의 회오리를 여기 모아둠 (몇 개 안 됨)
    public static readonly List<Whirlpool> Active = new List<Whirlpool>();

    [Header("범위 설정")]
    [Tooltip("이 반경 밖에 있으면 회오리 영향을 아예 안 받음. 안으로 들어오면 거리에 비례해 서서히 당겨짐")]
    public float outerRadius = 10f;
    [Tooltip("이 반경보다 안쪽이면 '갇힘' 상태. 밧줄에 끌려 이 반경을 다시 벗어나면 그 순간 풀려남")]
    public float innerRadius = 2f;

    [Header("힘 설정 (밸런싱용)")]
    [Tooltip("안쪽 반경 안에서는 항상 이 힘으로 고정 당김. 밧줄 힘과 이 값이 그대로 힘겨루기가 됨")]
    public float maxPullForce = 20f;

    [Tooltip("바깥쪽 감쇠 곡선. 1이면 직선, 클수록 가장자리가 헐렁하고 중심 근처에서만 세짐")]
    [Range(1f, 4f)] public float falloffExponent = 2f;

    [Tooltip("회오리 안에서 받는 물의 저항. 이게 없으면 중심을 관통해 왔다 갔다 진동한다")]
    public float pullDamping = 5f;

    private AudioSource tornadoAudioSource;

    void OnEnable()
    {
        Active.Add(this);
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library != null)
            tornadoAudioSource = VarcoAudio.EnsureLoop(
                transform, "Tornado Audio", library.underwaterTornado, true, 0.48f, 2f, outerRadius * 2.5f);
    }

    void OnDisable()
    {
        Active.Remove(this);
        if (tornadoAudioSource != null)
            tornadoAudioSource.Stop();
    }

    // 거리에 따른 당기는 힘. 안쪽 반경 안에서는 항상 최대 (밧줄 힘과 숫자 그대로 겨루게)
    public float GetPullForce(float distance)
    {
        if (distance <= innerRadius) return maxPullForce;

        // 두 반경이 같거나 뒤집히면 0으로 나눠 NaN이 나온다. Mathf.Clamp01은 NaN 비교가 전부
        // false라 그대로 통과시키고, 그 값이 AddForce로 들어가면 Rigidbody 위치가 NaN이 되어
        // 플레이어가 월드에서 사라진다 (세션 중 복구 불가). 인스펙터 실수 한 번으로 나는 사고
        float range = outerRadius - innerRadius;
        if (range <= 0.001f) return maxPullForce;

        float t = Mathf.Clamp01((outerRadius - distance) / range);
        return maxPullForce * Mathf.Pow(t, falloffExponent);
    }

    // 디버그용: 씬 뷰에서 바깥/안쪽 범위를 눈으로 확인
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, outerRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, innerRadius);
    }
}
