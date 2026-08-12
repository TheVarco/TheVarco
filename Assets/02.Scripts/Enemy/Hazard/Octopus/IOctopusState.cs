/// <summary>
/// 문어 상태의 진입, 실행, 종료 규칙
/// </summary>
public interface IOctopusState
{
    void Enter();  // 상태 진입 시 실행
    void Update(); // 상태 유지 중 실행
    void Exit();   // 상태 종료 시 실행
}
