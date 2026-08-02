using UnityEngine;

// Player에 붙어서 "지금 회오리에 갇혀있는지"만 관리하는 스크립트.
// 조작 자체를 꺼버리지 않음 - 회오리가 당기는 힘이 원래 세게 설계되어 있어서,
// 그 힘 자체 때문에 혼자서는 못 빠져나가는 "물리적으로 자연스러운" 방식만 씀.
// 밧줄 관련 로직(당기는 힘, 연결 등)은 PlayerRopeTarget으로 완전히 분리되어 있음.
// 갇힌 사람이 PlayerRopeTarget도 같이 갖고 있으면, 두 컴포넌트가 각자 계산한 힘이
// 물리적으로 부딪히면서 자연스럽게 줄다리기가 됨 (이 스크립트는 그 결과만 감지함)
public class PlayerWhirlpoolState : MonoBehaviour
{
    [Header("구조된 직후 설정")]
    [Tooltip("구조되자마자 다시 즉시 붙잡히지 않도록 잠깐 면역이 되는 시간(초). 이 시간 동안은 안쪽 반경에 들어와도 안 갇힘")]
    public float rescueImmunityDuration = 2f;

    public bool IsTrapped { get; private set; }
    public bool IsImmune => immunityTimer > 0f;

    private float immunityTimer = 0f;

    void Update()
    {
        if (immunityTimer > 0f)
            immunityTimer -= Time.deltaTime;
    }

    // Whirlpool이 안쪽 반경에 들어온 플레이어를 발견했을 때 호출
    public void Trap(Transform whirlpool)
    {
        if (IsTrapped || IsImmune) return;
        IsTrapped = true;
    }

    // Whirlpool이 "이 플레이어가 안쪽 반경을 벗어났다"고 판단했을 때 호출 - 실제로 풀려나는 순간
    public void Release()
    {
        if (!IsTrapped) return;

        IsTrapped = false;
        immunityTimer = rescueImmunityDuration;
    }
}