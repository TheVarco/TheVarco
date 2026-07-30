using UnityEngine;
using UnityEngine.UI;

public class RepairProgressWorldUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas worldCanvas;
    [SerializeField] private Image progressFill;
    [SerializeField] private Text promptText;

    [Header("World Placement")]
    [SerializeField, Min(0f)] private float surfaceOffset = 0.15f;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.12f, 0f);

    private Transform viewTransform;
    private bool isVisible;

    private void Awake()
    {
        if (worldCanvas == null)
            worldCanvas = GetComponentInChildren<Canvas>(true);

        if (worldCanvas != null)
            worldCanvas.renderMode = RenderMode.WorldSpace;

        if (progressFill != null)
        {
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Radial360;
            progressFill.fillOrigin = (int)Image.Origin360.Top;
            progressFill.fillClockwise = true;
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
            transform.rotation = Quaternion.LookRotation(toViewer.normalized, viewTransform.up);
    }

    public void Show(
        Vector3 slotWorldPosition,
        Vector3 slotWorldNormal,
        float progress01,
        Transform viewer)
    {
        viewTransform = viewer;

        Vector3 normal = slotWorldNormal.sqrMagnitude > 0.0001f
            ? slotWorldNormal.normalized
            : Vector3.up;
        transform.position = slotWorldPosition + normal * surfaceOffset + worldOffset;

        float clampedProgress = Mathf.Clamp01(progress01);
        if (progressFill != null)
            progressFill.fillAmount = clampedProgress;

        if (promptText != null)
        {
            promptText.text = clampedProgress > 0f
                ? $"\uC218\uB9AC \uC911... {Mathf.RoundToInt(clampedProgress * 100f)}%"
                : "\uC88C\uD074\uB9AD \uAE38\uAC8C \uB20C\uB7EC \uC218\uB9AC";
        }

        isVisible = true;
        if (worldCanvas != null)
            worldCanvas.gameObject.SetActive(true);
        else
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        isVisible = false;
        viewTransform = null;

        if (worldCanvas != null && worldCanvas.gameObject != gameObject)
            worldCanvas.gameObject.SetActive(false);
        else if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public static RepairProgressWorldUI CreateRuntime()
    {
        GameObject root = new GameObject("Runtime Repair Progress UI", typeof(RectTransform));
        root.SetActive(false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(160f, 110f);
        rootRect.localScale = Vector3.one * 0.0025f;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        Sprite circleSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");

        Image background = CreateImage("Gauge Background", root.transform, circleSprite);
        RectTransform backgroundRect = background.rectTransform;
        backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = new Vector2(0f, 18f);
        backgroundRect.sizeDelta = new Vector2(68f, 68f);
        background.color = new Color(0.02f, 0.035f, 0.045f, 0.82f);

        Image fill = CreateImage("Gauge Fill", root.transform, circleSprite);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0.5f, 0.5f);
        fillRect.anchorMax = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = new Vector2(0f, 18f);
        fillRect.sizeDelta = new Vector2(58f, 58f);
        fill.color = new Color(0.15f, 0.88f, 1f, 0.95f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;

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

        // Trigger Awake once so the object returns in its normal hidden state.
        root.SetActive(true);
        return runtimeUI;
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
