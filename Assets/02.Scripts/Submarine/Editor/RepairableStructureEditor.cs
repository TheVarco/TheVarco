using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[CustomEditor(typeof(RepairableStructure))]
public sealed class RepairableStructureEditor : Editor
{
    private const int ExpectedSlotCount = 10;
    private const string PreviewObjectName = "__RepairableStructure Damage Preview";

    private readonly List<GameObject> previewObjects = new();

    private SerializedProperty damagePerStageProperty;
    private SerializedProperty damageStageMaterialsProperty;
    private SerializedProperty glassDamageStageAlbedosProperty;
    private SerializedProperty glassDamageStageNormalsProperty;
    private SerializedProperty damageSlotsProperty;

    private int previewStage;

    private void OnEnable()
    {
        damagePerStageProperty = serializedObject.FindProperty("damagePerStage");
        damageStageMaterialsProperty = serializedObject.FindProperty("damageStageMaterials");
        glassDamageStageAlbedosProperty = serializedObject.FindProperty("glassDamageStageAlbedos");
        glassDamageStageNormalsProperty = serializedObject.FindProperty("glassDamageStageNormals");
        damageSlotsProperty = serializedObject.FindProperty("damageSlots");

        AssemblyReloadEvents.beforeAssemblyReload += CleanupPreview;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.quitting += CleanupPreview;
    }

    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= CleanupPreview;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.quitting -= CleanupPreview;
        CleanupPreview();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        DrawPreviewInspector();

        EditorGUILayout.Space();
        bool settingsChanged = DrawDefaultInspector();

        if (!settingsChanged)
            return;

        serializedObject.UpdateIfRequiredOrScript();
        int maxStage = GetMaximumStageCount();
        int clampedStage = Mathf.Clamp(previewStage, 0, maxStage);
        bool stageChanged = clampedStage != previewStage;
        previewStage = clampedStage;

        if (previewStage > 0)
            RebuildPreview();
        else if (stageChanged)
            CleanupPreview();
    }

    private void DrawPreviewInspector()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("씬 뷰 손상 미리보기", EditorStyles.boldLabel);

            bool previewUnavailable = EditorApplication.isPlayingOrWillChangePlaymode;
            int maxStage = GetMaximumStageCount();
            float damagePerStage = GetDamagePerStage();
            float maximumDamage = damagePerStage * maxStage;

            using (new EditorGUI.DisabledScope(previewUnavailable || maxStage <= 0))
            {
                float currentDamage = previewStage * damagePerStage;
                EditorGUI.BeginChangeCheck();
                float requestedDamage = EditorGUILayout.Slider(
                    "손상도",
                    currentDamage,
                    0f,
                    maximumDamage);

                if (EditorGUI.EndChangeCheck())
                {
                    int newStage = damagePerStage > 0f
                        ? Mathf.RoundToInt(requestedDamage / damagePerStage)
                        : 0;
                    SetPreviewStage(Mathf.Clamp(newStage, 0, maxStage));
                }

                Rect progressRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
                float progress = maxStage > 0 ? (float)previewStage / maxStage : 0f;
                EditorGUI.ProgressBar(
                    progressRect,
                    progress,
                    $"{FormatDamage(previewStage * damagePerStage)} / {FormatDamage(maximumDamage)}");

                EditorGUILayout.LabelField(BuildStageLabel(maxStage, damagePerStage), EditorStyles.miniLabel);
            }

            if (previewUnavailable)
            {
                EditorGUILayout.HelpBox(
                    "플레이 모드에서는 실제 손상 및 네트워크 상태와 충돌하지 않도록 미리보기를 사용할 수 없습니다.",
                    MessageType.Info);
            }
            else if (maxStage <= 0)
            {
                EditorGUILayout.HelpBox(
                    "사용할 수 있는 손상 단계 머티리얼 또는 유리 텍스처가 없습니다.",
                    MessageType.Warning);
            }

            int missingReferenceCount = CountMissingConfigurationReferences();
            if (missingReferenceCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"손상 슬롯 또는 단계 리소스에 누락된 참조가 {missingReferenceCount}개 있습니다. 누락된 항목은 미리보기에서 건너뜁니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                "미리보기는 임시 오브젝트만 사용하며 씬·프리팡에 저장되지 않습니다.",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void SetPreviewStage(int newStage)
    {
        if (previewStage == newStage)
            return;

        previewStage = newStage;
        if (previewStage <= 0)
            CleanupPreview();
        else
            RebuildPreview();
    }

    private void RebuildPreview()
    {
        CleanupPreview(false);

        if (previewStage <= 0 || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        serializedObject.UpdateIfRequiredOrScript();
        float previewDamage = previewStage * GetDamagePerStage();

        for (int i = 0; i < damageSlotsProperty.arraySize; i++)
        {
            SerializedProperty slotProperty = damageSlotsProperty.GetArrayElementAtIndex(i);
            GlassDamageOverlay sourceOverlay = slotProperty.FindPropertyRelative("glassOverlay").objectReferenceValue
                as GlassDamageOverlay;

            if (sourceOverlay != null)
            {
                CreateGlassPreview(sourceOverlay, previewDamage);
                continue;
            }

            DecalProjector sourceProjector = slotProperty.FindPropertyRelative("projector").objectReferenceValue
                as DecalProjector;
            if (sourceProjector != null)
                CreateProjectorPreview(sourceProjector, previewDamage);
        }

        SceneView.RepaintAll();
    }

    private void CreateProjectorPreview(DecalProjector sourceProjector, float previewDamage)
    {
        int stageCount = damageStageMaterialsProperty.arraySize;
        int stage = RepairableStructure.CalculateDamageStage(
            previewDamage,
            GetDamagePerStage(),
            stageCount);
        if (stage <= 0)
            return;

        Material stageMaterial = damageStageMaterialsProperty.GetArrayElementAtIndex(stage - 1).objectReferenceValue
            as Material;
        if (stageMaterial == null)
            return;

        GameObject previewObject = CreatePreviewObject(sourceProjector.transform);
        DecalProjector previewProjector = previewObject.AddComponent<DecalProjector>();
        EditorUtility.CopySerialized(sourceProjector, previewProjector);
        previewProjector.material = stageMaterial;
        previewProjector.enabled = true;
        ApplyPreviewHideFlags(previewObject);
        previewObjects.Add(previewObject);
    }

    private void CreateGlassPreview(GlassDamageOverlay sourceOverlay, float previewDamage)
    {
        int stageCount = glassDamageStageAlbedosProperty.arraySize;
        int stage = RepairableStructure.CalculateDamageStage(
            previewDamage,
            GetDamagePerStage(),
            stageCount);
        if (stage <= 0)
            return;

        Texture2D albedo = glassDamageStageAlbedosProperty.GetArrayElementAtIndex(stage - 1).objectReferenceValue
            as Texture2D;
        if (albedo == null)
            return;

        Texture2D normal = stage <= glassDamageStageNormalsProperty.arraySize
            ? glassDamageStageNormalsProperty.GetArrayElementAtIndex(stage - 1).objectReferenceValue as Texture2D
            : null;

        GameObject previewObject = CreatePreviewObject(sourceOverlay.transform);
        GlassDamageOverlay previewOverlay = previewObject.AddComponent<GlassDamageOverlay>();
        EditorUtility.CopySerialized(sourceOverlay, previewOverlay);
        previewOverlay.enabled = true;
        previewOverlay.Show(albedo, normal);
        ApplyPreviewHideFlags(previewObject);
        previewObjects.Add(previewObject);
    }

    private static GameObject CreatePreviewObject(Transform sourceTransform)
    {
        GameObject previewObject = new(PreviewObjectName)
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = sourceTransform.gameObject.layer
        };

        previewObject.transform.SetParent(sourceTransform, false);
        previewObject.transform.localPosition = Vector3.zero;
        previewObject.transform.localRotation = Quaternion.identity;
        previewObject.transform.localScale = Vector3.one;
        return previewObject;
    }

    private static void ApplyPreviewHideFlags(GameObject previewObject)
    {
        previewObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (Component component in previewObject.GetComponents<Component>())
            component.hideFlags = HideFlags.HideAndDontSave;
    }

    private void CleanupPreview()
    {
        CleanupPreview(true);
    }

    private void CleanupPreview(bool repaintSceneView)
    {
        for (int i = previewObjects.Count - 1; i >= 0; i--)
        {
            if (previewObjects[i] != null)
                DestroyImmediate(previewObjects[i]);
        }

        previewObjects.Clear();
        if (repaintSceneView)
            SceneView.RepaintAll();
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state is not (PlayModeStateChange.ExitingEditMode or PlayModeStateChange.EnteredPlayMode))
            return;

        previewStage = 0;
        CleanupPreview();
        Repaint();
    }

    private int GetMaximumStageCount()
    {
        bool hasProjectorSlot = false;
        bool hasGlassSlot = false;

        for (int i = 0; i < damageSlotsProperty.arraySize; i++)
        {
            SerializedProperty slotProperty = damageSlotsProperty.GetArrayElementAtIndex(i);
            if (slotProperty.FindPropertyRelative("glassOverlay").objectReferenceValue != null)
                hasGlassSlot = true;
            else if (slotProperty.FindPropertyRelative("projector").objectReferenceValue != null)
                hasProjectorSlot = true;
        }

        int projectorStages = hasProjectorSlot ? damageStageMaterialsProperty.arraySize : 0;
        int glassStages = hasGlassSlot ? glassDamageStageAlbedosProperty.arraySize : 0;
        return Mathf.Max(projectorStages, glassStages);
    }

    private int CountMissingConfigurationReferences()
    {
        int missingCount = Mathf.Max(0, ExpectedSlotCount - damageSlotsProperty.arraySize);
        bool hasProjectorSlot = false;
        bool hasGlassSlot = false;

        for (int i = 0; i < damageSlotsProperty.arraySize; i++)
        {
            SerializedProperty slotProperty = damageSlotsProperty.GetArrayElementAtIndex(i);
            bool hasGlassOverlay = slotProperty.FindPropertyRelative("glassOverlay").objectReferenceValue != null;
            bool hasProjector = slotProperty.FindPropertyRelative("projector").objectReferenceValue != null;

            hasGlassSlot |= hasGlassOverlay;
            hasProjectorSlot |= !hasGlassOverlay && hasProjector;
            if (!hasGlassOverlay && !hasProjector)
                missingCount++;
        }

        if (hasProjectorSlot)
            missingCount += CountNullArrayEntries(damageStageMaterialsProperty);
        if (hasGlassSlot)
            missingCount += CountNullArrayEntries(glassDamageStageAlbedosProperty);

        return missingCount;
    }

    private static int CountNullArrayEntries(SerializedProperty arrayProperty)
    {
        if (arrayProperty.arraySize == 0)
            return 1;

        int missingCount = 0;
        for (int i = 0; i < arrayProperty.arraySize; i++)
        {
            if (arrayProperty.GetArrayElementAtIndex(i).objectReferenceValue == null)
                missingCount++;
        }

        return missingCount;
    }

    private float GetDamagePerStage()
    {
        return Mathf.Max(0.01f, damagePerStageProperty.floatValue);
    }

    private static string BuildStageLabel(int maxStage, float damagePerStage)
    {
        if (maxStage <= 0)
            return "단계: 없음";

        List<string> labels = new(maxStage + 1);
        for (int stage = 0; stage <= maxStage; stage++)
            labels.Add(FormatDamage(stage * damagePerStage));

        return $"단계: {string.Join("  ·  ", labels)}";
    }

    private static string FormatDamage(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString("0.##");
    }
}
