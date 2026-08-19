using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;

// 클라이언트에서 잠수함 프록시를 호스트와 같은 방식(스윕 이동)으로 움직이게 하는 드라이버.
//
// 호스트는 SubmarineController가 body.MovePosition으로 선체를 옮긴다. PhysX가 그 이동에서
// 속도를 계산해서, 바닥에 선 플레이어를 마찰로 끌고 가고 벽은 접촉으로 민다.
// 클라이언트에서는 NetworkRigidbody3D가 복제 자세를 rb.position에 직접 대입(순간이동)해서
// 속도가 항상 0이다 — 마찰도 없고, 벽은 몸에 겹친 뒤 밀쳐내(depenetration) 튄다.
// 그래서 움직이는 잠수함 안에서 클라이언트의 예측 플레이어만 매 틱 어긋나 되감기 스냅이 생겼다.
//
// 이 컴포넌트는 Physics.Simulate 직전(RunnerSimulatePhysics3D.OnBeforeSimulate)에 끼어들어,
// 이미 스냅된 자세를 직전 자세로 되감고 MovePosition/MoveRotation으로 다시 보낸다.
// 스텝이 끝나면 최종 위치는 기존과 동일하고, 그 과정에서만 호스트와 같은 마찰과 접촉이 생긴다.
// (SimulateForward에서 이 이벤트는 전진 틱에서만 발화한다. 재시뮬 틱은 SyncTransforms만 한다)
//
// 롤백: 프리팹에서 이 컴포넌트를 떼면 기존 동작(직접 대입)으로 돌아간다.
[RequireComponent(typeof(Rigidbody))]
public sealed class SubmarineClientMotionDriver : NetworkBehaviour
{
    [SerializeField, Min(0f)]
    [Tooltip("한 스텝에 이 거리보다 크게 이동했으면 텔레포트(체크포인트 복원 등)로 보고 스윕하지 않습니다. " +
             "텔레포트를 스윕하면 잠수함이 맵을 가로지르며 경로의 플레이어를 쓸어버립니다. " +
             "정상 이동은 틱당 약 0.13m라 여유가 큽니다")]
    private float teleportThreshold = 2f;

    private Rigidbody body;
    private RunnerSimulatePhysics3D physicsRunner;
    private Vector3 lastPosition;
    private Quaternion lastRotation;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public override void Spawned()
    {
        // 호스트(또는 혼자 하기)는 SubmarineController가 이미 MovePosition으로 움직인다. 이중 이동 금지
        if (Object.HasStateAuthority)
            return;

        physicsRunner = Runner.GetComponent<RunnerSimulatePhysics3D>();
        if (physicsRunner == null)
        {
            // NetworkTestStarter가 러너에 항상 붙여주지만, 다른 경로로 세션이 만들어질 수도 있다
            Debug.LogWarning("[SubmarineClientMotionDriver] RunnerSimulatePhysics3D가 러너에 없습니다. " +
                             "잠수함 프록시가 순간이동 방식으로 남아 내부에서 덜컹거립니다.", this);
            return;
        }

        lastPosition = body.position;
        lastRotation = body.rotation;
        physicsRunner.OnBeforeSimulate += HandleBeforeSimulate;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (physicsRunner != null)
        {
            physicsRunner.OnBeforeSimulate -= HandleBeforeSimulate;
            physicsRunner = null;
        }
    }

    // 전진 틱의 Physics.Simulate 직전에 호출된다.
    // 이 시점의 rb 자세는 루프 시작에 NetworkRigidbody3D의 CopyToEngine이 스냅해 둔 최신 서버 자세다
    private void HandleBeforeSimulate()
    {
        // 권한이 넘어오면 개입을 멈춘다 (SubmarineController와 이중 이동 방지)
        if (Object == null || Object.HasStateAuthority)
            return;

        Vector3 targetPosition = body.position;
        Quaternion targetRotation = body.rotation;

        float delta = Vector3.Distance(targetPosition, lastPosition);

        // 텔레포트는 스윕하지 않고 그대로 인정한다
        if (delta > teleportThreshold)
        {
            lastPosition = targetPosition;
            lastRotation = targetRotation;
            return;
        }

        if (delta > 1e-6f || Quaternion.Angle(targetRotation, lastRotation) > 1e-4f)
        {
            // 직전 자세로 되감고 이번 스텝에 스윕으로 다시 이동한다.
            // kinematic MovePosition은 스텝 동안 경로를 쓸며 속도를 갖고, 끝나면 정확히 목표에 도달한다
            body.position = lastPosition;
            body.rotation = lastRotation;
            body.MovePosition(targetPosition);
            body.MoveRotation(targetRotation);
        }

        lastPosition = targetPosition;
        lastRotation = targetRotation;
    }
}
