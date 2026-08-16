using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소나에 표시할 대상의 종류
/// </summary>
public enum SonarTargetCategory
{
    Creature,
    Item,
    PointOfInterest,
    Equipment
}

/// <summary>
/// 오브젝트를 소나 탐지 대상으로 등록
/// 활성화된 대상만 정적 레지스트리에 보관하므로 컨트롤러가 매 핑마다 씬 전체를 검색할 필요없음
/// </summary>
[DisallowMultipleComponent]
public sealed class SonarTarget : MonoBehaviour
{
    // 현재 활성화된 모든 소나 대상을 보관하는 공용 레지스트리
    private static readonly HashSet<SonarTarget> RegisteredTargets = new HashSet<SonarTarget>();

    [Tooltip("소나에서 이 대상을 구분할 종류입니다. 종류별 표시 색상은 잠수함의 소나 컨트롤러에서 설정합니다.")]
    [SerializeField] private SonarTargetCategory category = SonarTargetCategory.PointOfInterest;
    [SerializeField] private Transform detectionPoint; // 소나 위치 기준점

    // 적이나 파괴 가능한 대상이라면 사망 여부를 탐지 가능 상태에 반영
    private Health health;
    private CarryableItem carryableItem;

    public static IReadOnlyCollection<SonarTarget> ActiveTargets => RegisteredTargets;
    public SonarTargetCategory Category => category;
    public Vector3 Position => detectionPoint != null ? detectionPoint.position : transform.position;
    public bool IsDetectable => isActiveAndEnabled
        && (carryableItem == null || carryableItem.IsSonarDetectable)
        && (carryableItem != null || health == null || !health.IsDead);

    private void Awake()
    {
        health = GetComponentInParent<Health>();
        carryableItem = GetComponentInParent<CarryableItem>();
    }

    private void OnEnable()
    {
        RegisteredTargets.Add(this);
    }

    private void OnDisable()
    {
        RegisteredTargets.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        RegisteredTargets.Clear();
    }
}
