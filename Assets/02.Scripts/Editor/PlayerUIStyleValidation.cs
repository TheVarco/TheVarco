#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PlayerUIStyleValidation
{
    private const string RegularFontGuid = "f973132a629b80f44b9ed90a78c678a6";
    private const string BoldFontGuid = "8054da2f76c68bc4094c909ebf658c1e";
    private const string BlackFontGuid = "23aff83660a80cb4f992c81d063985c2";

    [MenuItem("Tools/Underwater Cave/Validate Player UI Style")]
    public static void ValidatePlayerUIStyle()
    {
        Font font = Resources.Load<Font>(GameUIFont.RegularResourcePath);
        Require(font != null, "CookieRun Regular could not be loaded through Resources.");

        int introTextCount = ValidateSceneFonts(
            "01.Scenes/IntroScene_final.unity",
            RegularFontGuid,
            BoldFontGuid,
            BlackFontGuid);
        int mainTextCount = ValidateSceneFonts(
            "01.Scenes/MainScene_final.unity",
            RegularFontGuid);
        ValidateHatchedBarHierarchy();

        Debug.Log(
            $"[PlayerUIStyleValidation] PASS - " +
            $"CookieRun family assigned to {introTextCount + mainTextCount} build-scene Text components; " +
            "hatched bar hierarchy is clipped and idempotent.");
    }

    private static int ValidateSceneFonts(string relativeAssetPath, params string[] allowedFontGuids)
    {
        string fullPath = Path.Combine(Application.dataPath, relativeAssetPath);
        Require(File.Exists(fullPath), $"Build scene was not found: {relativeAssetPath}");

        int textCount = 0;
        foreach (string line in File.ReadLines(fullPath))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("m_Font:", StringComparison.Ordinal))
                continue;

            textCount++;
            bool usesAllowedFont = false;
            foreach (string allowedGuid in allowedFontGuids)
            {
                if (!trimmed.Contains(allowedGuid, StringComparison.Ordinal))
                    continue;

                usesAllowedFont = true;
                break;
            }

            Require(usesAllowedFont, $"A Text component in {relativeAssetPath} does not use an allowed CookieRun font: {trimmed}");
        }

        Require(textCount > 0, $"No serialized Text components were found in {relativeAssetPath}.");
        return textCount;
    }

    private static void ValidateHatchedBarHierarchy()
    {
        GameObject root = new GameObject("Player UI Style Validation", typeof(RectTransform));
        try
        {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(500f, 100f);

            RectTransform background = CreateImage("Background", root.transform).rectTransform;
            background.anchorMin = new Vector2(0f, 0.25f);
            background.anchorMax = new Vector2(1f, 0.75f);
            background.sizeDelta = new Vector2(10f, 10f);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
            fillArea.SetParent(root.transform, false);
            RectTransform fill = CreateImage("Fill", fillArea).rectTransform;

            RectTransform locked = CreateImage("LockedOverlay", root.transform).rectTransform;
            HatchedStatBarStyle first = HatchedStatBarStyle.Ensure(root, fill, locked);
            HatchedStatBarStyle second = HatchedStatBarStyle.Ensure(root, fill, locked);

            Require(first != null && first == second, "HatchedStatBarStyle was duplicated.");
            Require(root.GetComponents<HatchedStatBarStyle>().Length == 1, "More than one style component exists.");

            Transform frame = root.transform.Find("Hatched Frame");
            Require(frame != null, "Rounded frame was not created.");
            Transform clip = frame.Find("Clip");
            Require(clip != null && clip.GetComponent<Mask>() != null, "Rounded clip mask was not created.");
            Require(fillArea.parent == clip, "Primary fill container was not moved under the clip.");
            Require(locked.parent == clip, "Locked fill was not moved under the clip.");
            Require(fill.Find("Fill Stripes") != null, "Primary fill stripes were not created.");
            Require(locked.Find("Locked Stripes") != null, "Locked fill stripes were not created.");
            Require(fill.GetComponentsInChildren<DiagonalStripeGraphic>(true).Length == 1, "Primary stripes were duplicated.");
            Require(locked.GetComponentsInChildren<DiagonalStripeGraphic>(true).Length == 1, "Locked stripes were duplicated.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Image CreateImage(string objectName, Transform parent)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        return imageObject.GetComponent<Image>();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
