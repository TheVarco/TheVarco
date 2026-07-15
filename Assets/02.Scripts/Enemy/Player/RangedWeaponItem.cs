using UnityEngine;

// CarryableItem 중에서 "이건 원거리 무기다"라고 표시하는 마커 클래스.
// 로직 자체는 CarryableItem을 그대로 쓰고, 타입만 다르게 해서
// RangedAttack이 "지금 든 게 무기인지 산소통인지"를 구분할 수 있게 함.
// 나중에 무기별로 다른 투사체/연사속도를 주고 싶어지면 여기에 필드를 추가하면 됨.
public class RangedWeaponItem : CarryableItem
{

}