using System;

/// <summary>
/// HUD와 체크포인트가 공유하는 잠수함의 여섯 방향 손상 구역.
/// 값 순서는 네트워크 배열 인덱스로 사용하므로 변경하지 않는다.
/// </summary>
public enum SubmarineDamageRegion : byte
{
    Front = 0,
    Rear = 1,
    Left = 2,
    Right = 3,
    Top = 4,
    Bottom = 5
}

public static class SubmarineDamageRegionUtility
{
    public const int RegionCount = 6;

    public static SubmarineDamageRegion FromDamageSlot(int slotIndex)
    {
        return slotIndex switch
        {
            0 => SubmarineDamageRegion.Front,
            1 => SubmarineDamageRegion.Rear,
            2 or 3 => SubmarineDamageRegion.Left,
            4 or 5 => SubmarineDamageRegion.Right,
            6 or 7 => SubmarineDamageRegion.Top,
            8 or 9 => SubmarineDamageRegion.Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Unknown submarine damage slot.")
        };
    }
}
