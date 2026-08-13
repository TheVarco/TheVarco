using Fusion;
using UnityEngine;

// Player에 붙여서, 기절한 동료를 바라보며 E키를 일정 시간 누르고 있으면 부활시키는 스크립트.
// 즉시 부활이 아니라 "채널링"이라, 부활시키는 동안 나도 위험에 노출되는 협동 긴장감을 만든다.
public class PlayerReviver : NetworkBehaviour
{
    [Header("감지 설정")]
    public float reviveRange = 2.5f;
    [Tooltip("정면 기준 이 각도 안에 있는 대상만 인식")]
    public float reviveAngle = 90f;
    [Tooltip("이 거리 안이면 각도 판정을 건너뜀 (바짝 붙었을 때 대상 방향이 불안정해지는 문제 방지)")]
    public float closeRangeIgnoreAngle = 1f;

    [Header("채널링 설정")]
    [Tooltip("부활에 걸리는 시간(초)")]
    public float reviveDuration = 2.5f;
    public KeyCode reviveKey = KeyCode.E;

    [Header("부활 조건")]
    [Tooltip("부활하려면 손에 산소통을 들고 있어야 하는지")]
    public bool requireOxygenItem = true;

    [Header("참조")]
    public Transform lookReference;
    [Tooltip("부활 안내 문구를 띄울 상호작용기 (미지정 시 자동 감지)")]
    public PlayerInteractor interactor;
    [Tooltip("산소통 소지 확인/소모용 핫바 (미지정 시 자동 감지)")]
    public PlayerHotbar hotbar;

    public float ChannelProgress01 => currentTarget != null ? channelTimer / reviveDuration : 0f;

    private float channelTimer = 0f;
    private PlayerDownedState currentTarget;

    // 아이템 주기 등 다른 안내와 문구 칸이 겹치지 않게 하기 위한 확인용
    public bool HasReviveTarget => currentTarget != null;

    void Awake()
    {
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (hotbar == null) hotbar = GetComponent<PlayerHotbar>();
    }

    void Update()
    {
        // 내 캐릭터가 아니면(원격 플레이어) 로컬 입력을 읽지 않음. 비네트워크 씬에선 Object가 null이라 그대로 동작
        if (Object != null && !Object.HasInputAuthority) return;

        PlayerDownedState target = FindDownedTarget();

        // 보고 있는 대상이 바뀌면(다른 사람으로 바뀌거나, 대상을 놓치면) 채널링 진행도 리셋
        if (target != currentTarget)
        {
            channelTimer = 0f;
            currentTarget = target;
        }

        if (currentTarget == null)
        {
            SetPrompt(null);
            return;
        }

        // 부활에는 산소통이 필요하다. 들고 있지 않으면 안내만 띄우고 진행하지 않음
        if (requireOxygenItem && !(hotbar != null && hotbar.GetActiveItem() is OxygenItem))
        {
            channelTimer = 0f;
            SetPrompt("부활하려면 산소통이 필요합니다");
            return;
        }

        if (Input.GetKey(reviveKey))
        {
            channelTimer += Time.deltaTime;

            if (channelTimer >= reviveDuration)
            {
                // 보내기 직전에 한 번 더 확인해서 헛되이 산소통만 소모되는 창을 좁힌다
                // (호스트가 거절하는 경합은 아이템 네트워크화 때 왕복 확인으로 제대로 처리 예정)
                if (currentTarget.IsDowned)
                {
                    currentTarget.RPC_RequestRevive();
                    if (requireOxygenItem && hotbar != null)
                    {
                        GetComponentInParent<PlayerController>()
                            ?.RequestPlayerAudio(PlayerAudioCue.OxygenTankUse);
                        hotbar.RemoveActiveItem(); // 산소통 소모
                    }
                }

                channelTimer = 0f;
                currentTarget = null;
                SetPrompt(null);
                return;
            }
        }
        else
        {
            // 키를 떼면 진행도 리셋 (중간에 멈추면 처음부터 다시)
            channelTimer = 0f;
        }

        SetPrompt(channelTimer > 0f
            ? $"{reviveKey} : 부활시키는 중... {Mathf.RoundToInt(ChannelProgress01 * 100f)}%"
            : $"{reviveKey} : 부활시키기 (누르고 있기)");
    }

    private bool promptActive; // 지금 화면 문구가 내가 쓴 것인지

    // 상호작용을 잠그지 않고 문구만 덮어쓴다.
    // (잠가버리면 쓰러진 동료 옆에 떨어진 산소통조차 주울 수 없게 됨)
    private void SetPrompt(string text)
    {
        if (interactor == null) return;
        // 문구 칸은 PlayerItemGiver와 공유한다. 내가 쓴 게 아니면 남의 문구를 지우지 않는다
        if (text == null && !promptActive) return;

        interactor.SetOverridePrompt(text);
        promptActive = text != null;
    }

    void OnDisable()
    {
        SetPrompt(null);
    }

    private PlayerDownedState FindDownedTarget()
    {
        if (lookReference == null) return null;

        Collider[] candidates = Physics.OverlapSphere(lookReference.position, reviveRange);

        PlayerDownedState best = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in candidates)
        {
            PlayerDownedState downed = col.GetComponentInParent<PlayerDownedState>();
            if (downed == null || !downed.IsDowned) continue;
            if (downed.gameObject == gameObject) continue; // 자기 자신은 부활 대상에서 제외

            // 콜라이더 피벗(플레이어는 발밑)이 아니라 몸통 중심 기준.
            // 쓰러지면 위로 떠올라서 피벗 기준으로는 각도가 더 크게 어긋난다
            Vector3 toTarget = col.bounds.center - lookReference.position;
            float dist = toTarget.magnitude;

            // 바짝 붙으면 방향이 불안정해지므로 가까울 땐 각도 판정을 건너뛴다
            if (dist > closeRangeIgnoreAngle)
            {
                float angle = Vector3.Angle(lookReference.forward, toTarget);
                if (angle > reviveAngle * 0.5f) continue;
            }

            if (dist < closestDist)
            {
                closestDist = dist;
                best = downed;
            }
        }

        return best;
    }
}
