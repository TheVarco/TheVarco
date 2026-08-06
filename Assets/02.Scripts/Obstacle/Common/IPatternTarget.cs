using UnityEngine;

// 공용 패턴 시간표가 제어할 장애물의 명령 규칙
public interface IPatternTarget
{
    Object PatternTargetObject { get; } // 중복 검사와 경고 출력에 사용할 Unity 오브젝트

    bool ClaimPatternControl(ObstaclePatternBase owner); // 외부 패턴에 제어권을 전달
    void ReleasePatternControl(ObstaclePatternBase owner); // 외부 패턴 제어권을 반환
    void EnterPatternWarning(); // 피해 판정 없는 예고 상태 실행
    void EnterPatternActive(); // 장애물의 실제 작동 상태 실행
    void EnterPatternInactive(); // 장애물의 작동 상태 정지
    void ResetPatternTarget(); // 시각 효과와 판정 기록을 완전 초기화
}
