using UnityEngine;

/// <summary>MainScene_final의 기존 Canvas 아래에 잠수함 HUD를 한 번 생성한다.</summary>
[DisallowMultipleComponent]
public sealed class SubmarineHullHUDBootstrap : MonoBehaviour
{
    private void Awake()
    {
        if (transform is RectTransform canvasRect)
            SubmarineHullHUD.Create(canvasRect);
    }
}
