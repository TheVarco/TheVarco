using UnityEngine;

// 산소처럼 매끄럽게 계속 줄어드는 게 아니라, "일정 시간마다 한 번에 뚝 떨어지는" 방식의 배고픔.
// DepletingStat의 기본 Update()(매 프레임 조금씩 감소)를 재정의해서 계단식으로 바꿈.
public class HungerStat : DepletingStat
{
    [Header("계단식 감소 설정")]
    [Tooltip("몇 초마다 한 번씩 배고픔이 줄어들지")]
    public float stepInterval = 10f;
    [Tooltip("한 번 줄어들 때 얼마나 한꺼번에 줄어들지")]
    public float stepAmount = 5f;

    private float stepTimer = 0f;

    protected override void Update()
    {
        stepTimer += Time.deltaTime;

        if (stepTimer >= stepInterval)
        {
            stepTimer = 0f;
            Deplete(stepAmount); // 한 번에 stepAmount만큼 뚝 떨어뜨림
        }
    }
}