using UnityEngine;

// CarryableItem을 상속받아 "사용하면 배고픔을 채운다"는 효과만 추가한 음식류 아이템.
// 물고기, 과일 등 배고픔을 채우는 아이템은 전부 이 클래스를 그대로 쓰고,
// Item Name/Hunger Restore Amount만 Inspector에서 다르게 설정하면 됨.
// OxygenItem이랑 완전히 같은 패턴 - 좌클릭(기본 OnPrimaryAction)하면 자동으로 OnUse가 불려서 소모됨.
public class FoodItem : CarryableItem
{
    [Tooltip("이 음식 하나로 채워지는 배고픔 회복량")]
    public float hungerRestoreAmount = 30f;

    public override bool OnPrimaryAction(GameObject user, Transform aimReference)
    {
        TriggerEatAnimation(user);
        OnUse(user, user);
        return isConsumable;
    }

    public override void OnUse(GameObject user, GameObject target)
    {
        TriggerEatAnimation(user);
        if (target != user) TriggerEatAnimation(target);

        HungerStat hunger = target != null ? target.GetComponentInChildren<HungerStat>() : null;
        if (hunger == null && target != null) hunger = target.GetComponentInParent<HungerStat>();

        if (hunger == null)
        {
            Debug.LogWarning("FoodItem: 대상에서 HungerStat을 찾을 수 없음");
            return;
        }

        hunger.Refill(hungerRestoreAmount);
        user?.GetComponentInParent<PlayerController>()
            ?.RequestPlayerAudio(PlayerAudioCue.Eat);
        Debug.Log($"[FoodItem] {target.name}의 배고픔 {hungerRestoreAmount} 회복함");
    }

    private void TriggerEatAnimation(GameObject character)
    {
        if (character == null) return;
        Animator anim = character.GetComponentInChildren<Animator>();
        if (anim == null) anim = character.GetComponentInParent<Animator>();
        if (anim == null) anim = character.GetComponent<Animator>();

        if (anim != null)
        {
            anim.SetTrigger("Eat");
            Debug.Log($"[FoodItem] SetTrigger('Eat') 실행됨! (대상: {anim.gameObject.name})");
        }
    }
}
