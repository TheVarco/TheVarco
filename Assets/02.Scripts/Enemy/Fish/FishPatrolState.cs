using UnityEngine;

public sealed class FishPatrolState : IFishState
{
    private const int PointSelectionAttempts = 6;
    private const float ProgressEpsilon = 0.05f;

    private readonly FishController fish;
    private Vector3 patrolPoint;
    private float closestDistance;
    private float stuckTimer;

    public FishPatrolState(FishController fish)
    {
        this.fish = fish;
    }

    public Vector3 PatrolPoint => patrolPoint;

    public void Enter()
    {
        SelectPatrolPoint();
    }

    public void Update()
    {
        Vector3 direction = patrolPoint - fish.transform.position;
        float distance = direction.magnitude;

        if (distance <= fish.PatrolArriveDistance)
        {
            fish.ChangeState(FishStateType.Idle);
            return;
        }

        if (distance < closestDistance - ProgressEpsilon)
        {
            closestDistance = distance;
            stuckTimer = 0f;
        }
        else
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= fish.PatrolStuckTime)
            {
                fish.ChangeState(FishStateType.Idle);
                return;
            }
        }

        fish.Navigator.RotateToDirection(direction);
        fish.Navigator.MoveToDirection(direction, fish.MoveSpeed);
    }

    public void Exit() { }

    private void SelectPatrolPoint()
    {
        Vector3 origin = fish.transform.position;
        Vector3 bestPoint = origin;
        float bestDistanceSqr = 0f;

        for (int i = 0; i < PointSelectionAttempts; i++)
        {
            Vector3 desiredPoint = fish.HomePosition
                + Random.insideUnitSphere * fish.PatrolRadius;
            Vector3 toDesiredPoint = desiredPoint - origin;
            float desiredDistance = toDesiredPoint.magnitude;

            if (desiredDistance <= fish.PatrolArriveDistance)
                continue;

            Vector3 candidate = fish.Navigator.GetOpenPointInDirection(
                origin,
                toDesiredPoint,
                desiredDistance);

            float distanceSqr = (candidate - origin).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestPoint = candidate;
            }
        }

        patrolPoint = bestPoint;
        closestDistance = Mathf.Sqrt(bestDistanceSqr);
        stuckTimer = 0f;

        if (closestDistance <= fish.PatrolArriveDistance)
            fish.ChangeState(FishStateType.Idle);
    }
}
