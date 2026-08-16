using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기존 상태 값 갱신 로직은 그대로 두고, 상태 바의 렌더링 계층만
/// 잠수함 체력 바와 같은 둥근 트랙과 해칭 패턴으로 구성한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class HatchedStatBarStyle : MonoBehaviour
{
    private const string FrameName = "Hatched Frame";
    private const string ClipName = "Clip";
    private const string FillStripeName = "Fill Stripes";
    private const string LockedStripeName = "Locked Stripes";

    private static readonly Color TrackColor = new Color32(5, 29, 41, 235);
    private static readonly Color OutlineColor = new Color32(239, 253, 255, 245);

    private RectTransform primaryFill;
    private RectTransform lockedFill;
    private RectTransform clipRect;

    /// <summary>같은 상태 바에 여러 번 호출해도 시각 요소를 한 번만 만든다.</summary>
    public static HatchedStatBarStyle Ensure(
        GameObject owner,
        RectTransform primaryFill,
        RectTransform lockedFill = null)
    {
        if (owner == null || primaryFill == null)
            return null;

        HatchedStatBarStyle style = owner.GetComponent<HatchedStatBarStyle>();
        if (style == null)
            style = owner.AddComponent<HatchedStatBarStyle>();

        style.Configure(primaryFill, lockedFill);
        return style;
    }

    public void Configure(RectTransform newPrimaryFill, RectTransform newLockedFill = null)
    {
        primaryFill = newPrimaryFill;
        lockedFill = newLockedFill;
        BuildIfNeeded();
    }

    private void Start()
    {
        BuildIfNeeded();
    }

    private void BuildIfNeeded()
    {
        if (primaryFill == null)
            return;

        RectTransform root = transform as RectTransform;
        if (root == null)
            return;

        RectTransform backgroundRect = FindBackgroundRect();
        RectTransform frameRect = FindOrCreateFrame(root, backgroundRect);
        clipRect = frameRect.Find(ClipName) as RectTransform;
        if (clipRect == null)
            clipRect = CreateClip(frameRect);

        MovePrimaryFillIntoClip();
        MoveLockedFillIntoClip();
        DisableOriginalBackground(backgroundRect);
        EnsureStripe(primaryFill, FillStripeName, new Color(1f, 1f, 1f, 0.13f));

        if (lockedFill != null)
        {
            PrepareLockedGraphic();
            EnsureStripe(lockedFill, LockedStripeName, new Color(1f, 1f, 1f, 0.24f));
        }
    }

    private RectTransform FindBackgroundRect()
    {
        Transform background = transform.Find("Background");
        return background as RectTransform;
    }

    private static void DisableOriginalBackground(RectTransform backgroundRect)
    {
        if (backgroundRect == null)
            return;

        Graphic backgroundGraphic = backgroundRect.GetComponent<Graphic>();
        if (backgroundGraphic != null)
            backgroundGraphic.enabled = false;
    }

    private RectTransform FindOrCreateFrame(RectTransform root, RectTransform layoutSource)
    {
        RectTransform existing = root.Find(FrameName) as RectTransform;
        if (existing != null)
            return existing;

        GameObject frameObject = CreateUIObject(FrameName, root);
        RectTransform frame = frameObject.GetComponent<RectTransform>();
        if (layoutSource != null)
            CopyLayout(layoutSource, frame);
        else
            SetTrackLayout(frame);

        RoundedRectGraphic outline = frameObject.AddComponent<RoundedRectGraphic>();
        outline.Radius = 14f;
        outline.color = OutlineColor;
        outline.raycastTarget = false;
        frame.SetAsFirstSibling();
        return frame;
    }

    private RectTransform CreateClip(RectTransform frame)
    {
        GameObject clipObject = CreateUIObject(ClipName, frame);
        RectTransform clip = clipObject.GetComponent<RectTransform>();
        Stretch(clip, 3f);

        RoundedRectGraphic track = clipObject.AddComponent<RoundedRectGraphic>();
        track.Radius = 11f;
        track.color = TrackColor;
        track.raycastTarget = false;

        Mask mask = clipObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        return clip;
    }

    private void MovePrimaryFillIntoClip()
    {
        RectTransform container = primaryFill.parent as RectTransform;
        if (container == null)
            return;

        if (container == transform)
            container = primaryFill;

        if (container.parent != clipRect)
            container.SetParent(clipRect, false);

        Stretch(container, 0f);
        Graphic fillGraphic = primaryFill.GetComponent<Graphic>();
        if (fillGraphic != null)
            fillGraphic.raycastTarget = false;
    }

    private void MoveLockedFillIntoClip()
    {
        if (lockedFill == null)
            return;

        if (lockedFill.parent != clipRect)
            lockedFill.SetParent(clipRect, false);

        Stretch(lockedFill, 0f);
        lockedFill.SetAsLastSibling();
    }

    private void PrepareLockedGraphic()
    {
        Image lockedImage = lockedFill.GetComponent<Image>();
        if (lockedImage == null)
            return;

        // 기존 비트맵 해칭 대신 잠수함 HUD와 같은 절차식 해칭을 사용한다.
        // 색은 유지하고 값 갱신은 SegmentedStatBarUI의 RectTransform 경로를 사용한다.
        lockedImage.sprite = null;
        lockedImage.type = Image.Type.Simple;
        lockedImage.raycastTarget = false;
    }

    private void EnsureStripe(RectTransform parent, string stripeName, Color color)
    {
        Transform existing = parent.Find(stripeName);
        if (existing != null)
            return;

        GameObject stripeObject = CreateUIObject(stripeName, parent);
        RectTransform stripeRect = stripeObject.GetComponent<RectTransform>();
        Stretch(stripeRect, 0f);

        DiagonalStripeGraphic stripes = stripeObject.AddComponent<DiagonalStripeGraphic>();
        stripes.color = color;
        stripes.raycastTarget = false;
        stripeRect.SetAsLastSibling();
    }

    private GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        child.layer = gameObject.layer;
        child.GetComponent<RectTransform>().SetParent(parent, false);
        return child;
    }

    private static void CopyLayout(RectTransform source, RectTransform destination)
    {
        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.pivot = source.pivot;
        destination.anchoredPosition = source.anchoredPosition;
        destination.sizeDelta = source.sizeDelta;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    private static void SetTrackLayout(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 0.25f);
        rect.anchorMax = new Vector2(1f, 0.75f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(10f, 10f);
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }
}
