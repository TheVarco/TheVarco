using UnityEngine;

// 키네마틱 대상에 외부 이동 전달
public interface IExternalMotionReceiver
{
    // 대상 식별값
    int ExternalMotionReceiverId { get; }

    // 순간 속도 전달
    void ApplyExternalImpulse(Vector3 velocityChange);

    // 지속 가속 전달
    void ApplyExternalAcceleration(Vector3 acceleration, float deltaTime);
}
