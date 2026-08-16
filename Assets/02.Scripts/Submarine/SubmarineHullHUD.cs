using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 상단 중앙에 잠수함 전체 체력과 최초 피격 순서 기반 방향별 손상을 표시한다.
/// 네트워크 값을 직접 소유하지 않고 Health와 RepairableStructure의 복제 결과만 읽는다.
/// </summary>
public sealed class SubmarineHullHUD : MonoBehaviour
{
    private const float HudWidth = 620f;
    private const float HudHeight = 130f;
    private const float BarHeight = 54f;
    private const float DamageEpsilon = 0.001f;
    private const float MinimumSegmentWidth = 40f;
    private const float AnimationResponse = 12f;
    private const float FlashDuration = 0.3f;

    private static readonly Color TrackColor = new Color32(5, 29, 41, 235);
    private static readonly Color OutlineColor = new Color32(239, 253, 255, 245);
    private static readonly Color LowHealthColor = new Color32(239, 68, 78, 255);
    private static readonly Color HealthColor = new Color32(24, 211, 196, 255);

    private sealed class SegmentView
    {
        public RectTransform Rect;
        public Image Background;
        public Image Flash;
        public CanvasGroup CanvasGroup;
        public float CurrentX;
        public float CurrentWidth;
        public float TargetX;
        public float TargetWidth;
        public Color CurrentColor;
        public Color TargetColor;
        public float FlashRemaining;
        public bool PendingDisable;
    }

    private readonly SegmentView[] segments = new SegmentView[SubmarineDamageRegionUtility.RegionCount];
    private readonly float[] previousDamage = new float[SubmarineDamageRegionUtility.RegionCount];
    private readonly float[] currentDamage = new float[SubmarineDamageRegionUtility.RegionCount];
    private readonly int[] currentOrder = new int[SubmarineDamageRegionUtility.RegionCount];
    private readonly List<SubmarineDamageRegion> activeRegions = new List<SubmarineDamageRegion>(
        SubmarineDamageRegionUtility.RegionCount);

    private Health health;
    private RepairableStructure repairable;
    private RectTransform healthFill;
    private RectTransform damageClip;
    private RoundedRectGraphic healthOutline;
    private GameObject emptyDamageMarker;
    private float targetHealthRatio = 1f;
    private float displayedHealthRatio = 1f;
    private float nextBindAttemptTime;
    private float lastDamageTrackWidth = -1f;
    private bool snapshotInitialized;
    private bool waitingForNetworkSnapshot;

    public static SubmarineHullHUD Create(RectTransform canvasRoot)
    {
        if (canvasRoot == null)
            return null;

        Transform existing = canvasRoot.Find("Submarine Hull HUD");
        if (existing != null && existing.TryGetComponent(out SubmarineHullHUD existingHud))
            return existingHud;

        GameObject root = new GameObject("Submarine Hull HUD", typeof(RectTransform));
        root.layer = canvasRoot.gameObject.layer;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(canvasRoot, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -32f);
        rect.sizeDelta = new Vector2(HudWidth, HudHeight);
        rect.SetAsLastSibling();
        return root.AddComponent<SubmarineHullHUD>();
    }

    public static Color EvaluateDamageColor(float damage)
    {
        float clamped = Mathf.Clamp(damage, 0f, 50f);
        if (clamped <= 20f)
        {
            return LerpRgb(
                new Color32(24, 211, 196, 255),
                new Color32(242, 210, 76, 255),
                clamped / 20f);
        }

        if (clamped <= 35f)
        {
            return LerpRgb(
                new Color32(242, 210, 76, 255),
                new Color32(243, 139, 61, 255),
                (clamped - 20f) / 15f);
        }

        return LerpRgb(
            new Color32(243, 139, 61, 255),
            new Color32(230, 71, 76, 255),
            (clamped - 35f) / 15f);
    }

    private static Color LerpRgb(Color a, Color b, float amount)
    {
        amount = Mathf.Clamp01(amount);
        return new Color(
            Mathf.Lerp(a.r, b.r, amount),
            Mathf.Lerp(a.g, b.g, amount),
            Mathf.Lerp(a.b, b.b, amount),
            1f);
    }

    private void Awake()
    {
        BuildInterface();
    }

    private void OnEnable()
    {
        TryBindSubmarine();
    }

    private void OnDisable()
    {
        UnbindSubmarine();
    }

    private void Update()
    {
        if (health == null || repairable == null)
        {
            if (Time.unscaledTime >= nextBindAttemptTime)
                TryBindSubmarine();
        }
        else if (waitingForNetworkSnapshot && repairable.UsesNetworkAuthority)
        {
            waitingForNetworkSnapshot = false;
            RefreshDamageSegments(false);
        }

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float blend = 1f - Mathf.Exp(-AnimationResponse * deltaTime);
        displayedHealthRatio = Mathf.Lerp(displayedHealthRatio, targetHealthRatio, blend);
        SetHorizontalFill(healthFill, displayedHealthRatio);
        UpdateLowHealthPulse();
        AnimateSegments(deltaTime, blend);

        if (damageClip != null)
        {
            float trackWidth = damageClip.rect.width;
            if (trackWidth > 0f && Mathf.Abs(lastDamageTrackWidth - trackWidth) > 0.5f)
            {
                lastDamageTrackWidth = trackWidth;
                if (repairable != null)
                    RefreshDamageSegments(false);
            }
        }
    }

    private void TryBindSubmarine()
    {
        nextBindAttemptTime = Time.unscaledTime + 0.5f;
        SubmarineController submarine = FindFirstObjectByType<SubmarineController>();
        if (submarine == null)
            return;

        Health nextHealth = submarine.GetComponent<Health>();
        RepairableStructure nextRepairable = submarine.GetComponent<RepairableStructure>();
        if (nextHealth == null || nextRepairable == null)
            return;

        if (health == nextHealth && repairable == nextRepairable)
            return;

        UnbindSubmarine();
        health = nextHealth;
        repairable = nextRepairable;
        health.OnHealthChanged.AddListener(HandleHealthChanged);
        repairable.DamageRegionsChanged += HandleDamageRegionsChanged;
        repairable.DamageRegionsReset += HandleDamageRegionsReset;

        targetHealthRatio = health.maxHealth > 0f
            ? Mathf.Clamp01(health.CurrentHealth / health.maxHealth)
            : 0f;
        displayedHealthRatio = targetHealthRatio;
        snapshotInitialized = false;
        waitingForNetworkSnapshot = !repairable.UsesNetworkAuthority;
        RefreshDamageSegments(false);
    }

    private void UnbindSubmarine()
    {
        if (health != null)
            health.OnHealthChanged.RemoveListener(HandleHealthChanged);
        if (repairable != null)
        {
            repairable.DamageRegionsChanged -= HandleDamageRegionsChanged;
            repairable.DamageRegionsReset -= HandleDamageRegionsReset;
        }

        health = null;
        repairable = null;
        snapshotInitialized = false;
        waitingForNetworkSnapshot = false;
    }

    private void HandleHealthChanged(float current, float maximum)
    {
        targetHealthRatio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
    }

    private void HandleDamageRegionsChanged()
    {
        bool isInitialNetworkSnapshot = waitingForNetworkSnapshot
            && repairable != null
            && repairable.UsesNetworkAuthority;
        if (isInitialNetworkSnapshot)
            waitingForNetworkSnapshot = false;
        RefreshDamageSegments(!isInitialNetworkSnapshot);
    }

    private void HandleDamageRegionsReset()
    {
        waitingForNetworkSnapshot = false;
        RefreshDamageSegments(false);
    }

    private void RefreshDamageSegments(bool animateChanges)
    {
        if (repairable == null || damageClip == null)
            return;

        activeRegions.Clear();
        for (int i = 0; i < SubmarineDamageRegionUtility.RegionCount; i++)
        {
            SubmarineDamageRegion region = (SubmarineDamageRegion)i;
            currentDamage[i] = repairable.GetRegionDamage(region);
            currentOrder[i] = repairable.GetRegionOrder(region);
            if (currentDamage[i] > DamageEpsilon)
                activeRegions.Add(region);
        }

        activeRegions.Sort(CompareRegionOrder);

        float availableWidth = Mathf.Max(1f, damageClip.rect.width);
        if (availableWidth <= 1f)
            availableWidth = HudWidth - 6f;

        float maxHealth = health != null ? Mathf.Max(1f, health.maxHealth) : 100f;
        float displayedWidthSum = 0f;
        for (int i = 0; i < activeRegions.Count; i++)
        {
            int regionIndex = (int)activeRegions[i];
            float proportionalWidth = availableWidth * currentDamage[regionIndex] / maxHealth;
            segments[regionIndex].TargetWidth = Mathf.Max(MinimumSegmentWidth, proportionalWidth);
            displayedWidthSum += segments[regionIndex].TargetWidth;
        }

        if (displayedWidthSum > availableWidth && activeRegions.Count > 0)
        {
            float minimumTotal = MinimumSegmentWidth * activeRegions.Count;
            float extraTotal = Mathf.Max(0.0001f, displayedWidthSum - minimumTotal);
            float availableExtra = Mathf.Max(0f, availableWidth - minimumTotal);
            float scale = Mathf.Clamp01(availableExtra / extraTotal);
            displayedWidthSum = 0f;
            for (int i = 0; i < activeRegions.Count; i++)
            {
                int regionIndex = (int)activeRegions[i];
                float extra = Mathf.Max(0f, segments[regionIndex].TargetWidth - MinimumSegmentWidth);
                segments[regionIndex].TargetWidth = MinimumSegmentWidth + extra * scale;
                displayedWidthSum += segments[regionIndex].TargetWidth;
            }
        }

        float x = 0f;
        for (int i = 0; i < activeRegions.Count; i++)
        {
            int regionIndex = (int)activeRegions[i];
            SegmentView view = segments[regionIndex];
            bool newlyDamaged = previousDamage[regionIndex] <= DamageEpsilon;
            bool damageIncreased = currentDamage[regionIndex] > previousDamage[regionIndex] + DamageEpsilon;

            view.TargetX = x;
            view.TargetColor = EvaluateDamageColor(currentDamage[regionIndex]);
            view.PendingDisable = false;
            if (!view.Rect.gameObject.activeSelf)
                view.Rect.gameObject.SetActive(true);

            if (!snapshotInitialized || !animateChanges)
            {
                view.CurrentX = view.TargetX;
                view.CurrentWidth = view.TargetWidth;
                view.Rect.localScale = Vector3.one;
                view.CanvasGroup.alpha = 1f;
                view.CurrentColor = view.TargetColor;
                view.Background.color = view.CurrentColor;
                view.FlashRemaining = 0f;
            }
            else if (newlyDamaged)
            {
                view.CurrentX = view.TargetX;
                view.CurrentWidth = Mathf.Min(MinimumSegmentWidth, view.TargetWidth);
                view.Rect.localScale = new Vector3(0.78f, 0.78f, 1f);
                view.CanvasGroup.alpha = 0f;
                view.FlashRemaining = FlashDuration;
            }
            else if (damageIncreased)
            {
                view.FlashRemaining = FlashDuration;
            }

            x += view.TargetWidth;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            if (currentDamage[i] > DamageEpsilon)
                continue;

            SegmentView view = segments[i];
            if (!view.Rect.gameObject.activeSelf)
                continue;

            if (snapshotInitialized && animateChanges && previousDamage[i] > DamageEpsilon)
            {
                view.TargetWidth = 0f;
                view.PendingDisable = true;
            }
            else
            {
                view.TargetWidth = 0f;
                view.CurrentWidth = 0f;
                view.Rect.gameObject.SetActive(false);
            }
        }

        emptyDamageMarker.SetActive(activeRegions.Count == 0);
        Array.Copy(currentDamage, previousDamage, currentDamage.Length);
        snapshotInitialized = true;
    }

    private int CompareRegionOrder(SubmarineDamageRegion a, SubmarineDamageRegion b)
    {
        int aOrder = currentOrder[(int)a] > 0 ? currentOrder[(int)a] : 100000 + (int)a;
        int bOrder = currentOrder[(int)b] > 0 ? currentOrder[(int)b] : 100000 + (int)b;
        return aOrder.CompareTo(bOrder);
    }

    private void AnimateSegments(float deltaTime, float blend)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            SegmentView view = segments[i];
            if (view == null || !view.Rect.gameObject.activeSelf)
                continue;

            view.CurrentX = Mathf.Lerp(view.CurrentX, view.TargetX, blend);
            view.CurrentWidth = Mathf.Lerp(view.CurrentWidth, view.TargetWidth, blend);
            view.CurrentColor = Color.Lerp(view.CurrentColor, view.TargetColor, blend);
            view.Rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, view.CurrentX, view.CurrentWidth);
            view.Background.color = view.CurrentColor;
            view.CanvasGroup.alpha = Mathf.Lerp(view.CanvasGroup.alpha, 1f, blend);
            view.Rect.localScale = Vector3.Lerp(view.Rect.localScale, Vector3.one, blend);

            if (view.FlashRemaining > 0f)
            {
                view.FlashRemaining = Mathf.Max(0f, view.FlashRemaining - deltaTime);
                float alpha = 0.72f * (view.FlashRemaining / FlashDuration);
                view.Flash.color = new Color(1f, 1f, 1f, alpha);
            }
            else if (view.Flash.color.a > 0f)
            {
                view.Flash.color = new Color(1f, 1f, 1f, 0f);
            }

            if (view.PendingDisable && view.CurrentWidth <= 0.5f)
            {
                view.PendingDisable = false;
                view.Rect.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateLowHealthPulse()
    {
        if (healthOutline == null)
            return;

        if (targetHealthRatio > 0.3f)
        {
            healthOutline.color = OutlineColor;
            return;
        }

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
        healthOutline.color = Color.Lerp(OutlineColor, LowHealthColor, 0.38f + pulse * 0.5f);
    }

    private void BuildInterface()
    {
        RectTransform root = transform as RectTransform;
        if (root == null)
            return;

        healthOutline = CreateTrack("Total Hull Health", root, 0f, out RectTransform healthClip);
        GameObject healthFillObject = CreateUIObject("Health Fill", healthClip);
        healthFill = healthFillObject.GetComponent<RectTransform>();
        healthFill.anchorMin = Vector2.zero;
        healthFill.anchorMax = Vector2.one;
        healthFill.offsetMin = Vector2.zero;
        healthFill.offsetMax = Vector2.zero;
        healthFill.pivot = new Vector2(0f, 0.5f);
        Image healthImage = healthFillObject.AddComponent<Image>();
        healthImage.color = HealthColor;
        healthImage.raycastTarget = false;

        GameObject healthStripeObject = CreateUIObject("Health Stripes", healthFill);
        RectTransform healthStripeRect = healthStripeObject.GetComponent<RectTransform>();
        Stretch(healthStripeRect, 0f);
        DiagonalStripeGraphic healthStripes = healthStripeObject.AddComponent<DiagonalStripeGraphic>();
        healthStripes.color = new Color(1f, 1f, 1f, 0.13f);
        healthStripes.raycastTarget = false;

        CreateTrack("Directional Hull Damage", root, -(BarHeight + 12f), out damageClip);
        emptyDamageMarker = CreateUIObject("No Damage Marker", damageClip);
        RectTransform emptyRect = emptyDamageMarker.GetComponent<RectTransform>();
        emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
        emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
        emptyRect.pivot = new Vector2(0.5f, 0.5f);
        emptyRect.anchoredPosition = Vector2.zero;
        emptyRect.sizeDelta = new Vector2(38f, 46f);
        AddRegionIconGraphic(
            emptyDamageMarker,
            SubmarineDamageRegion.Front,
            new Color(0.82f, 0.96f, 0.98f, 0.18f));

        for (int i = 0; i < segments.Length; i++)
            segments[i] = CreateSegment((SubmarineDamageRegion)i, damageClip);

        emptyRect.SetAsLastSibling();
        SetHorizontalFill(healthFill, displayedHealthRatio);
    }

    private RoundedRectGraphic CreateTrack(
        string name,
        RectTransform parent,
        float y,
        out RectTransform clipRect)
    {
        GameObject outerObject = CreateUIObject(name, parent);
        RectTransform outer = outerObject.GetComponent<RectTransform>();
        outer.anchorMin = new Vector2(0f, 1f);
        outer.anchorMax = new Vector2(1f, 1f);
        outer.pivot = new Vector2(0.5f, 1f);
        outer.anchoredPosition = new Vector2(0f, y);
        outer.sizeDelta = new Vector2(0f, BarHeight);
        RoundedRectGraphic outline = outerObject.AddComponent<RoundedRectGraphic>();
        outline.Radius = 14f;
        outline.color = OutlineColor;
        outline.raycastTarget = false;

        GameObject clipObject = CreateUIObject("Clip", outer);
        clipRect = clipObject.GetComponent<RectTransform>();
        Stretch(clipRect, 3f);
        RoundedRectGraphic clipGraphic = clipObject.AddComponent<RoundedRectGraphic>();
        clipGraphic.Radius = 11f;
        clipGraphic.color = TrackColor;
        clipGraphic.raycastTarget = false;
        Mask mask = clipObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        return outline;
    }

    private SegmentView CreateSegment(SubmarineDamageRegion region, RectTransform parent)
    {
        GameObject segmentObject = CreateUIObject($"{region} Damage", parent);
        RectTransform rect = segmentObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Image background = segmentObject.AddComponent<Image>();
        background.color = EvaluateDamageColor(0f);
        background.raycastTarget = false;
        CanvasGroup canvasGroup = segmentObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        GameObject stripeObject = CreateUIObject("Damage Stripes", rect);
        RectTransform stripeRect = stripeObject.GetComponent<RectTransform>();
        Stretch(stripeRect, 0f);
        DiagonalStripeGraphic stripes = stripeObject.AddComponent<DiagonalStripeGraphic>();
        stripes.color = new Color(1f, 1f, 1f, 0.16f);
        stripes.raycastTarget = false;

        GameObject separatorObject = CreateUIObject("Separator", rect);
        RectTransform separatorRect = separatorObject.GetComponent<RectTransform>();
        separatorRect.anchorMin = new Vector2(1f, 0f);
        separatorRect.anchorMax = new Vector2(1f, 1f);
        separatorRect.pivot = new Vector2(1f, 0.5f);
        separatorRect.anchoredPosition = Vector2.zero;
        separatorRect.sizeDelta = new Vector2(2f, 0f);
        Image separator = separatorObject.AddComponent<Image>();
        separator.color = new Color(1f, 1f, 1f, 0.86f);
        separator.raycastTarget = false;

        GameObject iconObject = CreateUIObject("Direction Icon", rect);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = region is SubmarineDamageRegion.Top or SubmarineDamageRegion.Bottom
            ? new Vector2(46f, 36f)
            : new Vector2(34f, 46f);
        AddRegionIconGraphic(iconObject, region, Color.white);

        GameObject flashObject = CreateUIObject("Hit Flash", rect);
        RectTransform flashRect = flashObject.GetComponent<RectTransform>();
        Stretch(flashRect, 0f);
        Image flash = flashObject.AddComponent<Image>();
        flash.color = new Color(1f, 1f, 1f, 0f);
        flash.raycastTarget = false;

        segmentObject.SetActive(false);
        return new SegmentView
        {
            Rect = rect,
            Background = background,
            Flash = flash,
            CanvasGroup = canvasGroup,
            CurrentColor = EvaluateDamageColor(0f),
            TargetColor = EvaluateDamageColor(0f)
        };
    }

    private GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        child.layer = gameObject.layer;
        child.GetComponent<RectTransform>().SetParent(parent, false);
        return child;
    }

    private static Graphic AddRegionIconGraphic(
        GameObject iconObject,
        SubmarineDamageRegion region,
        Color tint)
    {
        string resourceName = $"UI/SubmarineHullHUD/submarine_{region.ToString().ToLowerInvariant()}";
        Sprite sprite = Resources.Load<Sprite>(resourceName);
        if (sprite != null)
        {
            Image image = iconObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = tint;
            image.raycastTarget = false;
            return image;
        }

        // 에디터에서 새 스프라이트 임포트가 끝나기 전에도 아이콘이 사라지지 않게 한다.
        SubmarineRegionIconGraphic fallback = iconObject.AddComponent<SubmarineRegionIconGraphic>();
        fallback.Region = region;
        fallback.color = tint;
        fallback.raycastTarget = false;
        return fallback;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetHorizontalFill(RectTransform rect, float ratio)
    {
        if (rect == null)
            return;

        ratio = Mathf.Clamp01(ratio);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(ratio, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

/// <summary>전후좌우는 Top view, 상하는 Side view로 해당 선체 면을 강조한다.</summary>
public sealed class SubmarineRegionIconGraphic : MaskableGraphic
{
    [SerializeField] private SubmarineDamageRegion region;

    public SubmarineDamageRegion Region
    {
        get => region;
        set
        {
            region = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (region is SubmarineDamageRegion.Top or SubmarineDamageRegion.Bottom)
            DrawSideView(vertexHelper);
        else
            DrawTopView(vertexHelper);
    }

    private void DrawTopView(VertexHelper vertexHelper)
    {
        Vector2[] hull =
        {
            new Vector2(0.5f, 0.98f), new Vector2(0.36f, 0.84f), new Vector2(0.29f, 0.64f),
            new Vector2(0.27f, 0.22f), new Vector2(0.38f, 0.08f), new Vector2(0.62f, 0.08f),
            new Vector2(0.73f, 0.22f), new Vector2(0.71f, 0.64f), new Vector2(0.64f, 0.84f)
        };
        AddPolygon(vertexHelper, hull, new Color32(5, 38, 50, 210));

        Vector2[] highlight = region switch
        {
            SubmarineDamageRegion.Front => new[]
            {
                new Vector2(0.5f, 0.96f), new Vector2(0.37f, 0.82f),
                new Vector2(0.31f, 0.66f), new Vector2(0.69f, 0.66f), new Vector2(0.63f, 0.82f)
            },
            SubmarineDamageRegion.Rear => new[]
            {
                new Vector2(0.28f, 0.34f), new Vector2(0.72f, 0.34f),
                new Vector2(0.72f, 0.21f), new Vector2(0.62f, 0.09f),
                new Vector2(0.38f, 0.09f), new Vector2(0.28f, 0.21f)
            },
            SubmarineDamageRegion.Left => new[]
            {
                new Vector2(0.5f, 0.9f), new Vector2(0.36f, 0.82f), new Vector2(0.29f, 0.62f),
                new Vector2(0.28f, 0.24f), new Vector2(0.39f, 0.1f), new Vector2(0.5f, 0.1f)
            },
            _ => new[]
            {
                new Vector2(0.5f, 0.9f), new Vector2(0.64f, 0.82f), new Vector2(0.71f, 0.62f),
                new Vector2(0.72f, 0.24f), new Vector2(0.61f, 0.1f), new Vector2(0.5f, 0.1f)
            }
        };
        AddPolygon(vertexHelper, highlight, color);
        AddOutline(vertexHelper, hull, color, 1.8f);
        AddLine(vertexHelper, new Vector2(0.3f, 0.65f), new Vector2(0.7f, 0.65f), Fade(color, 0.55f), 1.2f);
        AddLine(vertexHelper, new Vector2(0.28f, 0.34f), new Vector2(0.72f, 0.34f), Fade(color, 0.55f), 1.2f);
        AddLine(vertexHelper, new Vector2(0.39f, 0.06f), new Vector2(0.39f, 0.01f), color, 1.5f);
        AddLine(vertexHelper, new Vector2(0.61f, 0.06f), new Vector2(0.61f, 0.01f), color, 1.5f);
        AddLine(vertexHelper, new Vector2(0.32f, 0.02f), new Vector2(0.68f, 0.02f), color, 1.5f);
    }

    private void DrawSideView(VertexHelper vertexHelper)
    {
        Vector2[] hull =
        {
            new Vector2(0.04f, 0.5f), new Vector2(0.16f, 0.68f), new Vector2(0.34f, 0.79f),
            new Vector2(0.76f, 0.76f), new Vector2(0.91f, 0.61f), new Vector2(0.91f, 0.39f),
            new Vector2(0.76f, 0.24f), new Vector2(0.31f, 0.22f), new Vector2(0.13f, 0.35f)
        };
        AddPolygon(vertexHelper, hull, new Color32(5, 38, 50, 210));

        Vector2[] highlight = region == SubmarineDamageRegion.Top
            ? new[]
            {
                new Vector2(0.06f, 0.52f), new Vector2(0.17f, 0.67f), new Vector2(0.34f, 0.77f),
                new Vector2(0.75f, 0.74f), new Vector2(0.89f, 0.6f), new Vector2(0.89f, 0.52f)
            }
            : new[]
            {
                new Vector2(0.06f, 0.48f), new Vector2(0.89f, 0.48f), new Vector2(0.89f, 0.4f),
                new Vector2(0.75f, 0.26f), new Vector2(0.32f, 0.24f), new Vector2(0.14f, 0.36f)
            };
        AddPolygon(vertexHelper, highlight, color);
        AddOutline(vertexHelper, hull, color, 1.8f);
        AddLine(vertexHelper, new Vector2(0.06f, 0.5f), new Vector2(0.9f, 0.5f), Fade(color, 0.55f), 1.2f);
        AddLine(vertexHelper, new Vector2(0.92f, 0.42f), new Vector2(0.98f, 0.32f), color, 1.4f);
        AddLine(vertexHelper, new Vector2(0.92f, 0.58f), new Vector2(0.98f, 0.68f), color, 1.4f);
        AddLine(vertexHelper, new Vector2(0.98f, 0.28f), new Vector2(0.98f, 0.72f), color, 1.4f);
    }

    private void AddPolygon(VertexHelper vertexHelper, Vector2[] normalizedPoints, Color fillColor)
    {
        if (normalizedPoints == null || normalizedPoints.Length < 3)
            return;

        int start = vertexHelper.currentVertCount;
        for (int i = 0; i < normalizedPoints.Length; i++)
            vertexHelper.AddVert(ToLocal(normalizedPoints[i]), fillColor, normalizedPoints[i]);

        for (int i = 1; i < normalizedPoints.Length - 1; i++)
            vertexHelper.AddTriangle(start, start + i, start + i + 1);
    }

    private void AddOutline(VertexHelper vertexHelper, Vector2[] points, Color lineColor, float thickness)
    {
        for (int i = 0; i < points.Length; i++)
            AddLine(vertexHelper, points[i], points[(i + 1) % points.Length], lineColor, thickness);
    }

    private void AddLine(
        VertexHelper vertexHelper,
        Vector2 normalizedStart,
        Vector2 normalizedEnd,
        Color lineColor,
        float thickness)
    {
        Vector2 start = ToLocal(normalizedStart);
        Vector2 end = ToLocal(normalizedEnd);
        Vector2 direction = end - start;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int index = vertexHelper.currentVertCount;
        vertexHelper.AddVert(start - normal, lineColor, Vector2.zero);
        vertexHelper.AddVert(start + normal, lineColor, Vector2.up);
        vertexHelper.AddVert(end + normal, lineColor, Vector2.one);
        vertexHelper.AddVert(end - normal, lineColor, Vector2.right);
        vertexHelper.AddTriangle(index, index + 1, index + 2);
        vertexHelper.AddTriangle(index, index + 2, index + 3);
    }

    private Vector2 ToLocal(Vector2 normalized)
    {
        Rect rect = GetPixelAdjustedRect();
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, normalized.x),
            Mathf.Lerp(rect.yMin, rect.yMax, normalized.y));
    }

    private static Color Fade(Color source, float alphaMultiplier)
    {
        source.a *= alphaMultiplier;
        return source;
    }
}
