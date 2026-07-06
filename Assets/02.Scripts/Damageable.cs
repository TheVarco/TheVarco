using UnityEngine;

// 플레이어, 상어, 잠수함 등 "데미지를 받을 수 있는 모든 것"이 공통으로 구현하는 규격.
// 공격 스크립트는 상대가 플레이어인지 상어인지 몰라도, 이 인터페이스만 보고 데미지를 넣을 수 있다.
public interface Damageable
{
    void TakeDamage(float amount, GameObject source);
    bool IsDead { get; }
}