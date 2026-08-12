using UnityEngine;
using UnityEngine.UI;

// 수리할 때 손상 부위 위에 띄우는 UI
// 따로 연결된 UI가 없으면 CreateRuntime()에서 직접 만들어서 사용
public class RepairProgressWorldUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("수리 진행 UI를 그리는 월드 공간 Canvas")]
    [SerializeField] private Canvas worldCanvas;

    [Tooltip("수리 진행률을 0~1의 fillAmount로 표시하는 채움 이미지")]
    [SerializeField] private Image progressFill;

    [Tooltip("수리 가능 상태와 수리 중 상태를 안내하는 문구")]
    [SerializeField] private Text promptText;

    [Header("World Placement")]
    [Tooltip("수리 표면에서 카메라 방향으로 UI를 당겨오는 거리")]
    [SerializeField, Min(0f)] private float viewerOffset = 0.05f;

    // UI가 바라볼 카메라
    private Transform viewTransform;
    private bool isVisible;

    // 런타임에서 만든 게이지 이미지
    private Texture2D runtimeGaugeTexture;
    private Sprite runtimeGaugeSprite;

    private void Awake()
    {
        // 직접 연결 안 했으면 자식에서 찾기
        if (worldCanvas == null)
            worldCanvas = GetComponentInChildren<Canvas>(true);

        if (worldCanvas != null)
            worldCanvas.renderMode = RenderMode.WorldSpace;

        if (progressFill != null)
        {
            // 왼쪽에서 오른쪽으로 차오르게 설정
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = 0f;
        }

        Hide();
    }

    private void LateUpdate()
    {
        if (!isVisible || viewTransform == null)
            return;

        Vector3 toViewer = viewTransform.position - transform.position;
        if (toViewer.sqrMagnitude > 0.0001f)
        {
            // 반대 방향으로 돌리면 UI가 좌우반전돼서 -방향 사용
            transform.rotation = Quaternion.LookRotation(-toViewer.normalized, viewTransform.up);
        }
    }

    // 수리 위치로 UI 옮기고 진행도 갱신
    public void Show(
        Vector3 slotWorldPosition,
        Vector3 slotWorldNormal,
        float progress01,
        Transform viewer)
    {
        viewTransform = viewer;

        Vector3 toViewer = viewer != null
            ? viewer.position - slotWorldPosition
            : Vector3.zero;

        if (toViewer.sqrMagnitude > 0.0001f)
        {
            transform.position = slotWorldPosition
                + toViewer.normalized * viewerOffset;
        }
        else
        {
            Vector3 normal = slotWorldNormal.sqrMagnitude > 0.0001f
                ? slotWorldNormal.normalized
                : Vector3.up;
            transform.position = slotWorldPosition + normal * viewerOffset;
        }

        // fillAmount는 0~1만 사용
        float clampedProgress = Mathf.Clamp01(progress01);
        if (progressFill != null)
            progressFill.fillAmount = clampedProgress;

        if (promptText != null)
        {
            promptText.text = clampedProgress > 0f
                ? "\uC218\uB9AC \uC911..."
                : "\uC6B0\uD074\uB9AD \uAE38\uAC8C \uB20C\uB7EC \uC218\uB9AC";
        }

        isVisible = true;
        if (worldCanvas != null)
            worldCanvas.gameObject.SetActive(true);
        else
            gameObject.SetActive(true);
    }

    // 수리 끝나거나 타겟에서 벗어나면 숨김
    public void Hide()
    {
        isVisible = false;
        viewTransform = null;

        if (worldCanvas != null && worldCanvas.gameObject != gameObject)
            worldCanvas.gameObject.SetActive(false);
        else if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    // HammerItem에 progressUI가 없을 때 사용할 UI 생성
    public static RepairProgressWorldUI CreateRuntime()
    {
        // 다 만든 다음 Awake 실행하려고 일단 꺼둠
        GameObject root = new GameObject("Runtime Repair Progress UI", typeof(RectTransform));
        root.SetActive(false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(180f, 80f);
        rootRect.localScale = Vector3.one * 0.0025f;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        // 기본 스프라이트가 없을 수도 있어서 흰색 이미지 직접 생성
        Texture2D gaugeTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            name = "Runtime Repair Gauge Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        gaugeTexture.SetPixel(0, 0, Color.white);
        gaugeTexture.Apply(false, true);

        Sprite gaugeSprite = Sprite.Create(
            gaugeTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        gaugeSprite.hideFlags = HideFlags.DontSave;
        gaugeSprite.name = "Runtime Repair Gauge Sprite";

        // 게이지 배경
        Image background = CreateImage("Gauge Background", root.transform, gaugeSprite);
        RectTransform backgroundRect = background.rectTransform;
        backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = new Vector2(0f, 15f);
        backgroundRect.sizeDelta = new Vector2(148f, 24f);
        background.color = new Color(0.02f, 0.035f, 0.045f, 0.82f);

        // 실제로 차오르는 부분
        Image fill = CreateImage("Gauge Fill", root.transform, gaugeSprite);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0.5f, 0.5f);
        fillRect.anchorMax = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = new Vector2(0f, 15f);
        fillRect.sizeDelta = new Vector2(138f, 14f);
        fill.color = new Color(0.15f, 0.88f, 1f, 0.95f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;

        // 숫자는 안 넣고 상태만 표시
        GameObject textObject = new GameObject("Repair Prompt", typeof(RectTransform));
        textObject.transform.SetParent(root.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0f);
        textRect.anchorMax = new Vector2(0.5f, 0f);
        textRect.anchoredPosition = new Vector2(0f, 9f);
        textRect.sizeDelta = new Vector2(160f, 30f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        RepairProgressWorldUI runtimeUI = root.AddComponent<RepairProgressWorldUI>();
        runtimeUI.worldCanvas = canvas;
        runtimeUI.progressFill = fill;
        runtimeUI.promptText = text;
        runtimeUI.runtimeGaugeTexture = gaugeTexture;
        runtimeUI.runtimeGaugeSprite = gaugeSprite;

        // Awake 실행하면 Hide()에서 다시 꺼짐
        root.SetActive(true);
        return runtimeUI;
    }

    private void OnDestroy()
    {
        // 런타임에 만든 Texture와 Sprite 정리
        DestroyRuntimeObject(runtimeGaugeSprite);
        DestroyRuntimeObject(runtimeGaugeTexture);
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private static Image CreateImage(string objectName, Transform parent, Sprite sprite)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }
}
