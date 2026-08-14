using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SubmarineHullHUDValidation
{
    private const string MainScenePath = "Assets/01.Scenes/MainScene_final.unity";
    private const int CaptureWidth = 1920;
    private const int CaptureHeight = 1080;

    public static void RunBatchValidation()
    {
        try
        {
            ValidateMappingsAndColors();
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            Canvas canvas = FindHudCanvas();
            SubmarineController submarine = UnityEngine.Object.FindFirstObjectByType<SubmarineController>();
            Require(canvas != null, "MainScene_final HUD Canvas를 찾지 못했습니다.");
            Require(submarine != null, "MainScene_final 잠수함을 찾지 못했습니다.");

            Health health = submarine.GetComponent<Health>();
            RepairableStructure repairable = submarine.GetComponent<RepairableStructure>();
            Require(health != null && repairable != null, "잠수함 Health/RepairableStructure 참조가 없습니다.");
            Require(repairable.SlotCount >= 10, "잠수함 손상 슬롯이 10개보다 적습니다.");

            ValidateRegionAggregation(repairable);
            ValidateOrderRestoreAndReentry(repairable);
            ValidateSprites();

            float[] tinyDamage = new float[repairable.SlotCount];
            tinyDamage[0] = 1f;
            repairable.RestoreCheckpointDamage(tinyDamage, null, new[] { 1, 0, 0, 0, 0, 0 }, 1);
            health.SyncFrom(99f, false);

            SubmarineHullHUD hud = SubmarineHullHUD.Create(canvas.transform as RectTransform);
            Require(hud != null, "HUD 생성에 실패했습니다.");
            EnsureHudInitialized(hud);
            InvokePrivate(hud, "AnimateSegments", 0f, 1f);
            Canvas.ForceUpdateCanvases();

            RectTransform tinyFront = hud.transform.Find("Directional Hull Damage/Clip/Front Damage") as RectTransform;
            Require(tinyFront != null && tinyFront.rect.width >= 39.5f,
                $"최소 칸 폭 검증 실패: {(tinyFront != null ? tinyFront.rect.width : -1f):F2}px");

            float[] exampleDamage = new float[repairable.SlotCount];
            exampleDamage[0] = 20f;
            exampleDamage[1] = 10f;
            repairable.RestoreCheckpointDamage(exampleDamage, null, new[] { 1, 2, 0, 0, 0, 0 }, 2);
            health.SyncFrom(70f, false);
            InvokePrivate(hud, "RefreshDamageSegments", false);
            InvokePrivate(hud, "HandleHealthChanged", 70f, 100f);
            SetPrivateField(hud, "displayedHealthRatio", 0.7f);
            InvokePrivate(hud, "Update");
            InvokePrivate(hud, "AnimateSegments", 0f, 1f);
            Canvas.ForceUpdateCanvases();

            Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Front), 20f),
                "예시 전방 피해가 20이 아닙니다.");
            Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Rear), 10f),
                "예시 후방 피해가 10이 아닙니다.");
            Require(repairable.GetRegionOrder(SubmarineDamageRegion.Front) == 1
                    && repairable.GetRegionOrder(SubmarineDamageRegion.Rear) == 2,
                "예시 피격 순서가 전방→후방이 아닙니다.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string artifactDirectory = Path.Combine(projectRoot, "Artifacts", "SubmarineHullHUD");
            Directory.CreateDirectory(artifactDirectory);
            string fullPath = Path.Combine(artifactDirectory, "SubmarineHullHUD_1920x1080.png");
            string closeupPath = Path.Combine(artifactDirectory, "SubmarineHullHUD_Closeup.png");
            CaptureHud(canvas, hud, fullPath, closeupPath);

            Debug.Log($"[SubmarineHullHUDValidation] PASS\nFull={fullPath}\nCloseup={closeupPath}");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateMappingsAndColors()
    {
        Require(SubmarineDamageRegionUtility.FromDamageSlot(0) == SubmarineDamageRegion.Front, "슬롯 0 매핑 실패");
        Require(SubmarineDamageRegionUtility.FromDamageSlot(1) == SubmarineDamageRegion.Rear, "슬롯 1 매핑 실패");
        Require(SubmarineDamageRegionUtility.FromDamageSlot(2) == SubmarineDamageRegion.Left
                && SubmarineDamageRegionUtility.FromDamageSlot(3) == SubmarineDamageRegion.Left,
            "좌측 슬롯 매핑 실패");
        Require(SubmarineDamageRegionUtility.FromDamageSlot(4) == SubmarineDamageRegion.Right
                && SubmarineDamageRegionUtility.FromDamageSlot(5) == SubmarineDamageRegion.Right,
            "우측 슬롯 매핑 실패");
        Require(SubmarineDamageRegionUtility.FromDamageSlot(6) == SubmarineDamageRegion.Top
                && SubmarineDamageRegionUtility.FromDamageSlot(7) == SubmarineDamageRegion.Top,
            "상단 슬롯 매핑 실패");
        Require(SubmarineDamageRegionUtility.FromDamageSlot(8) == SubmarineDamageRegion.Bottom
                && SubmarineDamageRegionUtility.FromDamageSlot(9) == SubmarineDamageRegion.Bottom,
            "하단 슬롯 매핑 실패");

        RequireColor(SubmarineHullHUD.EvaluateDamageColor(0f), new Color32(24, 211, 196, 255), "damage 0");
        RequireColor(SubmarineHullHUD.EvaluateDamageColor(20f), new Color32(242, 210, 76, 255), "damage 20");
        RequireColor(SubmarineHullHUD.EvaluateDamageColor(35f), new Color32(243, 139, 61, 255), "damage 35");
        RequireColor(SubmarineHullHUD.EvaluateDamageColor(50f), new Color32(230, 71, 76, 255), "damage 50");
        RequireColor(SubmarineHullHUD.EvaluateDamageColor(80f), new Color32(230, 71, 76, 255), "damage 80 cap");
    }

    private static void ValidateRegionAggregation(RepairableStructure repairable)
    {
        float[] damage = new float[repairable.SlotCount];
        damage[0] = 10f;
        damage[1] = 11f;
        damage[2] = 3f;
        damage[3] = 7f;
        damage[4] = 4f;
        damage[5] = 6f;
        damage[6] = 2f;
        damage[7] = 8f;
        damage[8] = 1f;
        damage[9] = 9f;
        repairable.RestoreCheckpointDamage(damage, null, new[] { 1, 2, 3, 4, 5, 6 }, 6);

        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Front), 10f), "전방 집계 실패");
        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Rear), 11f), "후방 집계 실패");
        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Left), 10f), "좌측 2슬롯 집계 실패");
        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Right), 10f), "우측 2슬롯 집계 실패");
        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Top), 10f), "상단 2슬롯 집계 실패");
        Require(Mathf.Approximately(repairable.GetRegionDamage(SubmarineDamageRegion.Bottom), 10f), "하단 2슬롯 집계 실패");
    }

    private static void ValidateOrderRestoreAndReentry(RepairableStructure repairable)
    {
        float[] rearOnly = new float[repairable.SlotCount];
        rearOnly[1] = 10f;
        repairable.RestoreCheckpointDamage(rearOnly, null, new[] { 0, 2, 0, 0, 0, 0 }, 2);
        Require(repairable.GetRegionOrder(SubmarineDamageRegion.Front) == 0, "완전 수리된 전방 순서 제거 실패");

        float[] frontAndRear = new float[repairable.SlotCount];
        frontAndRear[0] = 10f;
        frontAndRear[1] = 10f;
        repairable.RestoreCheckpointDamage(frontAndRear, null, new[] { 0, 2, 0, 0, 0, 0 }, 2);
        Require(repairable.GetRegionOrder(SubmarineDamageRegion.Rear) == 2
                && repairable.GetRegionOrder(SubmarineDamageRegion.Front) == 3,
            "수리 후 재피격 부위가 오른쪽 끝 순서를 받지 못했습니다.");
        Require(repairable.CaptureCheckpointDamageSequence() == 3, "다음 피격 시퀀스 복원 실패");
    }

    private static void ValidateSprites()
    {
        foreach (SubmarineDamageRegion region in Enum.GetValues(typeof(SubmarineDamageRegion)))
        {
            string path = $"UI/SubmarineHullHUD/submarine_{region.ToString().ToLowerInvariant()}";
            Require(Resources.Load<Sprite>(path) != null, $"스프라이트 로드 실패: {path}");
        }
    }

    private static Canvas FindHudCanvas()
    {
        foreach (Canvas candidate in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.GetComponent<SubmarineHullHUDBootstrap>() != null)
                return candidate;
        }

        return null;
    }

    private static void EnsureHudInitialized(SubmarineHullHUD hud)
    {
        if (hud.transform.Find("Total Hull Health") == null)
            InvokePrivate(hud, "BuildInterface");
        InvokePrivate(hud, "TryBindSubmarine");
    }

    private static void CaptureHud(Canvas canvas, SubmarineHullHUD hud, string fullPath, string closeupPath)
    {
        foreach (Transform child in canvas.transform)
        {
            if (child != hud.transform)
                child.gameObject.SetActive(false);
        }

        GameObject cameraObject = new GameObject("HUD Validation Camera", typeof(Camera));
        Camera captureCamera = cameraObject.GetComponent<Camera>();
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = new Color32(5, 24, 34, 255);
        captureCamera.cullingMask = 1 << canvas.gameObject.layer;
        captureCamera.nearClipPlane = 0.01f;
        captureCamera.farClipPlane = 10f;

        RenderMode previousRenderMode = canvas.renderMode;
        Camera previousCamera = canvas.worldCamera;
        float previousPlaneDistance = canvas.planeDistance;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = captureCamera;
        canvas.planeDistance = 1f;

        RenderTexture renderTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
        captureCamera.targetTexture = renderTexture;
        Canvas.ForceUpdateCanvases();
        captureCamera.Render();

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = renderTexture;
        Texture2D full = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
        full.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0);
        full.Apply();
        File.WriteAllBytes(fullPath, full.EncodeToPNG());

        const int closeupWidth = 760;
        const int closeupHeight = 200;
        int closeupX = (CaptureWidth - closeupWidth) / 2;
        int closeupY = CaptureHeight - closeupHeight;
        Texture2D closeup = new Texture2D(closeupWidth, closeupHeight, TextureFormat.RGBA32, false);
        closeup.SetPixels(full.GetPixels(closeupX, closeupY, closeupWidth, closeupHeight));
        closeup.Apply();
        File.WriteAllBytes(closeupPath, closeup.EncodeToPNG());

        RenderTexture.active = previousActive;
        canvas.renderMode = previousRenderMode;
        canvas.worldCamera = previousCamera;
        canvas.planeDistance = previousPlaneDistance;
        captureCamera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(full);
        UnityEngine.Object.DestroyImmediate(closeup);
        UnityEngine.Object.DestroyImmediate(renderTexture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Require(method != null, $"검증용 메서드를 찾지 못했습니다: {methodName}");
        method.Invoke(target, arguments);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, $"검증용 필드를 찾지 못했습니다: {fieldName}");
        field.SetValue(target, value);
    }

    private static void RequireColor(Color actual, Color expected, string label)
    {
        const float tolerance = 1.1f / 255f;
        Require(Mathf.Abs(actual.r - expected.r) <= tolerance
                && Mathf.Abs(actual.g - expected.g) <= tolerance
                && Mathf.Abs(actual.b - expected.b) <= tolerance,
            $"{label} 색상 검증 실패: actual={actual}, expected={expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
