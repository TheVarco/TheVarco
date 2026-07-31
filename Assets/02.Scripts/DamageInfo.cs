using UnityEngine;

// 확장성 고려해서 임시 방편으로 타입을 만들어놨습니당
// ex) 데미지 원인별 다른 데칼 이미지 or 다른 사운드
public enum DamageType
{
    Unspecified, // 종류 없음
    Collision, // 동굴 벽
    Bite, // 상어 이빨
    Projectile, // 투사체 (총)
    Melee // 근접
}

// 한번의 공격이 가지고 있는 모든 입력 정보 저장
// readOnly로 값 변경되지 않도록
public readonly struct DamageInfo
{
    public float RequestedAmount { get; } // 피해량 (주의: AppliedAmount > 실제 들어간 양)
    public GameObject Source { get; } // 데미지를 발생시킨 오브젝트
    public Vector3 Point { get; } // 실제 피격 지점의 월드 좌표
    public Vector3 Normal { get; } // 충격 표면의 방향 > TODO : 충격에 맞게 흔들림 적용하기
    public DamageType Type { get; }
    public bool HasImpactPoint { get; } // 위치 정보가 있는 데미지인지 구분하는 플래그 (기존 구현 호환용)
    public bool PlayHitAnimation { get; }

    public DamageInfo(
        float requestedAmount,
        GameObject source,
        Vector3 point,
        Vector3 normal,
        DamageType type,
        bool playHitAnimation = true)
    {
        RequestedAmount = requestedAmount;
        Source = source;
        Point = point;
        Normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.zero;
        Type = type;
        HasImpactPoint = true;
        PlayHitAnimation = playHitAnimation;
    }

    private DamageInfo(
        float requestedAmount,
        GameObject source,
        DamageType type,
        bool playHitAnimation)
    {
        RequestedAmount = requestedAmount;
        Source = source;
        Point = Vector3.zero;
        Normal = Vector3.zero;
        Type = type;
        HasImpactPoint = false;
        PlayHitAnimation = playHitAnimation;
    }

    // 위치 정보 없이 숫자만으로 데미지 주는 경우
    public static DamageInfo WithoutImpact( 
        float requestedAmount,
        GameObject source,
        DamageType type = DamageType.Unspecified,
        bool playHitAnimation = true)
    {
        return new DamageInfo(requestedAmount, source, type, playHitAnimation);
    }
}

// 실제로 데미지가 얼마 들어갔는지 (데칼 누적 손상)
// GC 부담을 줄이기 위해 struct로 구현
public readonly struct DamageAppliedInfo
{
    public DamageInfo Damage { get; }
    public float AppliedAmount { get; }

    public DamageAppliedInfo(DamageInfo damage, float appliedAmount)
    {
        Damage = damage;
        AppliedAmount = appliedAmount;
    }
}
