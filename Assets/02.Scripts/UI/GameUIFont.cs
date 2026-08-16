using UnityEngine;
using UnityEngine.UI;

/// <summary>플레이어에게 표시되는 UI가 공유하는 CookieRun Regular 폰트.</summary>
public static class GameUIFont
{
    public const string RegularResourcePath = "UI/Fonts/CookieRun Regular";

    private static Font regular;
    private static bool missingFontReported;

    public static Font Regular
    {
        get
        {
            if (regular == null)
                regular = Resources.Load<Font>(RegularResourcePath);

            if (regular == null && !missingFontReported)
            {
                missingFontReported = true;
                Debug.LogError($"UI font was not found at Resources/{RegularResourcePath}.");
            }

            return regular;
        }
    }

    public static bool Apply(Text text)
    {
        if (text == null)
            return false;

        Font font = Regular;
        if (font == null)
            return false;

        text.font = font;
        return true;
    }
}
