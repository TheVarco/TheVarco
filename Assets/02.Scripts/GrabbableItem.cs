using UnityEngine;

// 좌클릭으로 잡아서 끌고 다닐 수 있는 무거운 물체에 붙이는 표식.
// 이 자체는 로직이 거의 없고, PlayerGrabber가 이 컴포넌트가 있는지만 확인해서 잡을지 판단한다.
[RequireComponent(typeof(Rigidbody))]
public class GrabbableItem : MonoBehaviour
{
    [Header("잡혔을 때 물리 반응 (밸런싱용)")]
    [Tooltip("스프링 세기. 클수록 목표 지점으로 세게/빠르게 끌려옴")]
    public float spring = 500f;
    [Tooltip("출렁임을 줄이는 감쇠값. 너무 낮으면 덜렁덜렁 흔들림")]
    public float damper = 20f;
    [Tooltip("스프링이 낼 수 있는 최대 힘. 낮출수록 '날아다니는' 느낌이 줄고 묵직해짐")]
    public float maxForce = 150f;
}