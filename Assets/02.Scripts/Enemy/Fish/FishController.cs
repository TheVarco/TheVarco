using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EnemyNavigator))]
public sealed class FishController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private EnemyData fishData;

    private readonly Dictionary<FishStateType, IFishState> states = new();
    private Rigidbody body;
    private IFishState currentState;
    private FishStateType currentStateType;

    public EnemyNavigator Navigator { get; private set; }
    public FishStateType CurrentState => currentStateType;
    public Vector3 HomePosition { get; private set; }

    public float MoveSpeed => fishData != null ? fishData.moveSpeed : 0f;
    public float PatrolRadius => fishData != null ? Mathf.Max(0f, fishData.patrolRadius) : 0f;
    public float PatrolArriveDistance => fishData != null
        ? Mathf.Max(0.01f, fishData.patrolArriveDistance)
        : 0.5f;
    public float PatrolStuckTime => fishData != null
        ? Mathf.Max(0.01f, fishData.patrolStuckTime)
        : 2f;
    public float IdleWaitMin => fishData != null ? Mathf.Max(0f, fishData.idleWaitMin) : 1f;
    public float IdleWaitMax => fishData != null ? Mathf.Max(0f, fishData.idleWaitMax) : 3f;

    private bool IsNetworkActive => Object != null && Object.IsValid;
    private bool HasSimulationAuthority => !IsNetworkActive || Object.HasStateAuthority;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        Navigator = GetComponent<EnemyNavigator>();
        HomePosition = transform.position;

        states.Add(FishStateType.Idle, new FishIdleState(this));
        states.Add(FishStateType.Patrol, new FishPatrolState(this));
        states.Add(FishStateType.Held, new FishHeldState(this));
        states.Add(FishStateType.Passive, new FishHeldState(this));
    }

    private void Start()
    {
        TryStartSimulation();
    }

    public override void Spawned()
    {
        ConfigureSwimmingPhysics();
        TryStartSimulation();
    }

    private void OnDisable()
    {
        Navigator?.StopMovement();
    }

    private void FixedUpdate()
    {
        if (!HasSimulationAuthority
            || currentStateType == FishStateType.Held
            || currentStateType == FishStateType.Passive)
            return;

        currentState?.Update();
    }

    internal void TryStartSimulation()
    {
        if (!HasSimulationAuthority || currentState != null)
            return;

        ConfigureSwimmingPhysics();
        ChangeState(FishStateType.Idle);
    }

    public void ChangeState(FishStateType newStateType)
    {
        if (!states.TryGetValue(newStateType, out IFishState newState))
            return;

        if (currentState != null && currentStateType == newStateType)
            return;

        Navigator.StopMovement();
        currentState?.Exit();

        currentStateType = newStateType;
        currentState = newState;
        currentState.Enter();
    }

    public void SuspendForPickup()
    {
        ChangeState(FishStateType.Held);

        if (body == null)
            return;

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
    }

    /// <summary>
    /// 한 번 수집된 물고기를 AI 없이 수중 물리 아이템으로 유지한다.
    /// 실제 Dynamic/Proxy 물리 설정은 CarryableItem의 드롭 처리가 담당한다.
    /// </summary>
    public void EnterCollectedPassiveState()
    {
        ChangeState(FishStateType.Passive);
        Navigator?.StopMovement();
    }

    /// <summary>
    /// 체크포인트가 채집 전 상태였다면 해당 위치를 새 순찰 중심으로 삼아 AI를 복원한다.
    /// 일반 드롭 경로에서는 호출하지 않는다.
    /// </summary>
    public void RestoreWildCheckpointState(Vector3 newHomePosition)
    {
        HomePosition = newHomePosition;
        ConfigureSwimmingPhysics();
        ChangeState(FishStateType.Idle);
    }

    private void ConfigureSwimmingPhysics()
    {
        if (body == null)
            return;

        bool isProxy = IsNetworkActive && !Object.HasStateAuthority;

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (isProxy)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.isKinematic = isProxy;
        body.useGravity = false;
        body.detectCollisions = true;
        body.interpolation = isProxy
            ? RigidbodyInterpolation.None
            : RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = isProxy
            ? CollisionDetectionMode.ContinuousSpeculative
            : CollisionDetectionMode.ContinuousDynamic;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? HomePosition : transform.position;
        float radius = fishData != null ? Mathf.Max(0f, fishData.patrolRadius) : 0f;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(center, radius);

        if (!Application.isPlaying || currentStateType != FishStateType.Patrol)
            return;

        if (currentState is not FishPatrolState patrol)
            return;

        Gizmos.DrawLine(transform.position, patrol.PatrolPoint);
        Gizmos.DrawWireSphere(patrol.PatrolPoint, 0.2f);
    }
}
