using UnityEngine;

/// <summary>
/// 문어의 타깃 추격 및 얼굴 부착 상태
/// </summary>
public class OctopusChaseState : IOctopusState
{
    private readonly OctopusController octopus; // 문어 상태 컨텍스트

    public OctopusChaseState(OctopusController octopus)
    {
        this.octopus = octopus;
    }

    public void Enter() { }

    /// <summary>
    /// 타깃 갱신 및 부착 거리까지 추격
    /// </summary>
    public void Update()
    {
        if (!octopus.Targeting.TryUpdateChaseTarget())
        {
            octopus.ChangeState(OctopusStateType.Patrol);
            return;
        }

        Vector3 targetPoint = octopus.Targeting.GetTargetPoint(octopus.transform.position);
        Vector3 direction = targetPoint - octopus.transform.position;

        if (direction.magnitude <= octopus.AttachDistance)
        {
            if (octopus.TryAttachToCurrentTarget())
                return;

            octopus.Targeting.ClearTarget();
            octopus.ChangeState(OctopusStateType.Patrol);
            return;
        }

        octopus.Navigator.RotateToDirection(direction);
        octopus.Navigator.MoveToDirection(direction, octopus.MoveSpeed + octopus.ChaseSpeedBonus);
    }

    public void Exit() { }
}
