using UnityEngine;

/// <summary>
/// 적 종류별 타깃 허용 조건 제공.
/// </summary>
public interface IEnemyTargetFilter
{
    /// <summary>
    /// 후보의 타깃 사용 가능 여부 반환.
    /// </summary>
    bool CanTarget(Transform candidate);
}
