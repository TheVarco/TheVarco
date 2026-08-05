using UnityEngine;

/// <summary>
/// 플레이어 부착 앵커 및 슬롯 점유 관리
/// </summary>
public class AttachmentSlot : MonoBehaviour
{
    [Header("Attachment Anchors")]
    [SerializeField] private Transform faceAnchor; // 문어 부착 기준점
    [SerializeField] private Transform legAnchor;  // 성게 부착 기준점

    private HarvestableCreature faceOccupant; // 얼굴 슬롯 점유 생물
    private HarvestableCreature legOccupant;  // 다리 슬롯 점유 생물

    public Transform FaceAnchor => faceAnchor;
    public Transform LegAnchor => legAnchor;

    /// <summary>
    /// 지정 슬롯 사용 가능 여부 반환
    /// </summary>
    public bool IsAvailable(AttachmentSlotType slotType)
    {
        return GetAnchor(slotType) != null && GetOccupant(slotType) == null;
    }

    /// <summary>
    /// 지정 슬롯 점유 및 앵커 반환
    /// </summary>
    public bool TryOccupy(
        AttachmentSlotType slotType,
        HarvestableCreature creature,
        out Transform anchor)
    {
        anchor = GetAnchor(slotType);
        if (creature == null || anchor == null || GetOccupant(slotType) != null)
            return false;

        SetOccupant(slotType, creature);
        return true;
    }

    /// <summary>
    /// 지정 생물의 슬롯 점유 해제
    /// </summary>
    public bool Release(AttachmentSlotType slotType, HarvestableCreature creature)
    {
        if (creature == null || GetOccupant(slotType) != creature)
            return false;

        SetOccupant(slotType, null);
        return true;
    }

    /// <summary>
    /// 지정 슬롯 점유 생물 반환
    /// </summary>
    public HarvestableCreature GetOccupant(AttachmentSlotType slotType)
    {
        return slotType == AttachmentSlotType.Face ? faceOccupant : legOccupant;
    }

    /// <summary>
    /// 지정 슬롯 부착 앵커 반환
    /// </summary>
    public Transform GetAnchor(AttachmentSlotType slotType)
    {
        return slotType == AttachmentSlotType.Face ? faceAnchor : legAnchor;
    }

    private void SetOccupant(AttachmentSlotType slotType, HarvestableCreature creature)
    {
        if (slotType == AttachmentSlotType.Face)
            faceOccupant = creature;
        else
            legOccupant = creature;
    }
}
