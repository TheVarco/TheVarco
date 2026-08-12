/// <summary>
/// 문어의 행동 상태 종류
/// </summary>
public enum OctopusStateType
{
    Idle,    // 대기 상태
    Patrol,  // 순찰 상태
    Chase,   // 추격 상태
    Attached, // 얼굴 부착 상태
    Dead     // 위험 기능 종료 상태
}
