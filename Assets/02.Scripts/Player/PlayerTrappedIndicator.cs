using Fusion;
using UnityEngine;
using UnityEngine.UI;

// 회오리에 갇혔을 때 "갇힘!" 표시.
// 본인은 화면 문구(1인칭에서도 보여야 하니까), 남은 머리 위 라벨(누굴 도우러 갈지 찾아야 하니까).
//
// 둘 다 화면 캔버스에 그린다. 남의 라벨은 머리 위치를 화면 좌표로 변환해서 찍는 방식이라
// 빌보드 회전 / 월드 스케일 / 캔버스 렌더 순서 문제가 없고, 벽에 가리는 건 레이캐스트로 직접 판정한다.
//
// PlayerWhirlpoolState.IsTrapped를 매 프레임 읽지 않고 이벤트로 받는다.
// [Networked] 값을 LateUpdate에서 읽으면 보간 버퍼 때문에 값이 안 변해도 프레임마다 튄다.
[RequireComponent(typeof(PlayerWhirlpoolState))]
public class PlayerTrappedIndicator : MonoBehaviour
{
    [Tooltip("남의 화면에서 라벨이 뜰 머리 높이")]
    public Vector3 headOffset = new Vector3(0f, 1.4f, 0f);
    [Tooltip("남의 갇힘 라벨을 가리는 레이어. 플레이어와 같은 레이어는 자동으로 제외됨")]
    public LayerMask occluderMask = ~0;
    public string selfMessage = "갇힘! 도와줘!";
    public string otherMessage = "갇힘!";

    private PlayerWhirlpoolState state;
    private NetworkObject netObject;
    private GameObject uiRoot;
    private Text label;
    private Camera viewCamera;
    private bool isRemoteLabel;
    private bool visible;
    private bool built;

    // Camera.main은 MainCamera 태그가 없으면 null이라 폴백을 둔다
    private Camera ViewCamera
    {
        get
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
                viewCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            return viewCamera;
        }
    }

    void Awake()
    {
        state = GetComponent<PlayerWhirlpoolState>();
        netObject = GetComponent<NetworkObject>();
        state.TrappedChanged += HandleTrappedChanged;
    }

    void OnDestroy()
    {
        if (state != null) state.TrappedChanged -= HandleTrappedChanged;
        if (uiRoot != null) Destroy(uiRoot);
    }

    private void HandleTrappedChanged(bool trapped)
    {
        visible = trapped;
        if (!trapped && !built) return; // 한 번도 안 갇혔으면 UI를 아예 안 만듦

        if (!built) Build();
        label.enabled = trapped;
    }

    void LateUpdate()
    {
        if (!visible) return;

        // 살짝 통통 튀는 느낌
        label.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 6f) * 0.08f);

        if (!isRemoteLabel) return;

        Camera view = ViewCamera;
        if (view == null) { label.enabled = false; return; }

        Vector3 head = transform.position + headOffset;
        Vector3 screenPoint = view.WorldToScreenPoint(head);

        // z가 음수면 카메라 뒤쪽. 그대로 찍으면 화면 반대편에 뒤집혀 나타난다
        bool shown = screenPoint.z > 0f && HasLineOfSight(view, head);
        label.enabled = shown;
        if (shown) label.rectTransform.position = screenPoint;
    }

    // 벽 뒤에 있는 사람은 안 보이게. 세계 안에 있는 것처럼 느껴지려면 이게 있어야 함
    private bool HasLineOfSight(Camera view, Vector3 head)
    {
        Vector3 toHead = head - view.transform.position;
        float distance = toHead.magnitude;
        if (distance < 0.01f) return true;

        // 플레이어와 같은 레이어는 뺀다. 1인칭이면 내 몸이 카메라 바로 앞이라 전부 가려지고,
        // 팀원이 앞을 지나갈 때마다 표시가 끊긴다
        int mask = occluderMask & ~(1 << gameObject.layer);

        return !Physics.Raycast(view.transform.position, toHead / distance, distance,
                                mask, QueryTriggerInteraction.Ignore);
    }

    private void Build()
    {
        built = true;
        // 러너 없는 씬이면 씬에 놓인 그 플레이어가 곧 본인
        isRemoteLabel = netObject != null && !netObject.HasInputAuthority;
        label = isRemoteLabel ? CreateRemoteLabel() : CreateSelfLabel();
    }

    // 갇힌 본인 화면 한가운데 위쪽에 고정으로 뜨는 문구
    private Text CreateSelfLabel()
    {
        uiRoot = CreateOverlayCanvas("Trapped Self Label");

        Text text = CreateText(uiRoot.transform, selfMessage, 34);
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = new Vector2(0f, 140f);
        text.rectTransform.sizeDelta = new Vector2(420f, 60f);
        return text;
    }

    // 남의 머리 위를 따라다니는 라벨. 위치는 LateUpdate에서 화면 좌표로 매 프레임 갱신
    private Text CreateRemoteLabel()
    {
        uiRoot = CreateOverlayCanvas("Trapped Remote Label");

        Text text = CreateText(uiRoot.transform, otherMessage, 28);
        // Overlay 캔버스에서는 RectTransform.position이 곧 화면 픽셀 좌표라서 앵커를 좌하단에 둔다
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
        text.rectTransform.sizeDelta = new Vector2(240f, 40f);
        return text;
    }

    private static GameObject CreateOverlayCanvas(string objectName)
    {
        GameObject root = new GameObject(objectName);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        return root;
    }

    private static Text CreateText(Transform parent, string message, int fontSize)
    {
        GameObject obj = new GameObject("Label", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        Text text = obj.AddComponent<Text>();
        GameUIFont.Apply(text);
        text.text = message;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.82f, 0.2f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
