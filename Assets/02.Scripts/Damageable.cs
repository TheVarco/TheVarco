using UnityEngine;

// 플레이어, 상어, 잠수함 등 "데미지를 받을 수 있는 모든 것"이 공통으로 구현하는 규격.
// 공격 스크립트는 상대가 플레이어인지 상어인지 몰라도, 이 인터페이스만 보고 데미지를 넣을 수 있다.
public interface Damageable
{
    void TakeDamage(float amount, GameObject source);
    bool IsDead { get; }
}

// 데미지를 여기서 바로 처리하지 않고 다른 곳(네트워크 권한자 등)으로 넘겨야 할 때 쓰는 규격.
// 같은 오브젝트에 이걸 구현한 컴포넌트가 붙어있으면 Health가 체력을 깎기 직전에 먼저 물어본다.
// 이 판단을 공격하는 쪽이 아니라 맞는 쪽에 두기 때문에, 공격 스크립트는 아무것도 신경쓸 필요가 없다.
public interface IDamageRouter
{
    // true를 반환하면 "내가 다른 곳으로 넘겼으니 Health는 여기서 멈춰라"는 뜻
    bool RouteDamage(DamageInfo damageInfo);
}