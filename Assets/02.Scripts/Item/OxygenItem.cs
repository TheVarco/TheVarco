using UnityEngine;

// CarryableItem을 상속받아 "사용하면 산소를 채운다"는 효과만 추가한 산소통.
// 줍기/들기/내려놓기는 전부 CarryableItem 로직을 그대로 쓰고, OnUse()만 새로 정의함.
public class OxygenItem : CarryableItem
{
    [Tooltip("이 산소통 하나로 채워지는 산소량")]
    public float refillAmount = 50f;

    public override void OnUse(GameObject user, GameObject target)
    {
        // user가 아니라 target(자기 자신일 수도, 팀원일 수도)의 OxygenStat을 채움
        OxygenStat oxygen = target.GetComponentInChildren<OxygenStat>();
        if (oxygen == null)
        {
            Debug.LogWarning("OxygenItem: 대상에서 OxygenStat을 찾을 수 없음");
            return;
        }

        oxygen.Refill(refillAmount);
        Debug.Log($"[OxygenItem] {target.name}의 산소 {refillAmount} 회복함");
    }
}