using UnityEngine;

// 상호작용 대상을 바라볼 때 화면에 함께 표시할 선택형 조작 안내 이미지.
// 안내가 필요한 Interactable과 같은 GameObject에 붙여 사용한다.
[DisallowMultipleComponent]
public sealed class InteractionGuideProvider : MonoBehaviour
{
    [SerializeField]
    [Tooltip("이 상호작용 대상의 조작 안내 이미지. 비워두면 추가 안내 UI를 표시하지 않습니다.")]
    private Sprite guideSprite;

    public Sprite GuideSprite => guideSprite;
}
