using UnityEngine;
using UnityEngine.UI;

// 대기방의 플레이어 카드 한 칸. 채워졌는지 여부에 따라 겉모습만 바꾼다.
//
// LobbyUI가 슬롯 내부 구조(초상화가 어디 붙어 있는지, 배경이 무슨 색인지)를
// 몰라도 되게 여기서 감싼다. 이름으로 자식을 찾으면 오브젝트 이름을 바꾸는 순간
// 에러 없이 조용히 안 붙으므로, 참조는 전부 인스펙터로 받는다.
public class LobbyPlayerSlot : MonoBehaviour
{
    [SerializeField] private Image background;
    [Tooltip("사람이 들어왔을 때만 보이는 초상화. 없으면 비워둬도 된다")]
    [SerializeField] private Image portrait;
    [SerializeField] private Text nameText;
    [Tooltip("여러 장 넣으면 플레이어마다 다른 포즈가 나온다. 비워두면 Portrait에 넣은 그림 그대로")]
    [SerializeField] private Sprite[] portraitPoses;

    [Header("빈 칸 / 찬 칸 구분")]
    [SerializeField] private Color filledColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private string emptyLabel = "비어있음";

    public void SetEmpty() => Apply(emptyLabel, false);
    // 포즈를 Random으로 뽑으면 누가 들어오고 나갈 때마다 4칸을 다시 그리면서 전원 포즈가
    // 다시 굴러가고, 기계마다 난수가 달라 내 화면의 나와 친구 화면의 내가 달라진다.
    // PlayerId로 정하면 모두 같은 계산을 해서 통신 없이 같은 그림을 보고, 안 바뀐다
    public void SetPlayer(string label, int poseSeed)
    {
        if (portrait != null && portraitPoses != null && portraitPoses.Length > 0)
            portrait.sprite = portraitPoses[Mathf.Abs(poseSeed) % portraitPoses.Length];

        Apply(label, true);
    }

    private void Apply(string label, bool filled)
    {
        if (nameText != null) nameText.text = label;
        if (background != null) background.color = filled ? filledColor : emptyColor;
        if (portrait != null) portrait.gameObject.SetActive(filled);
    }
}
