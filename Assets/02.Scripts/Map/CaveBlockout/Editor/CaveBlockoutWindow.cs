using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveBlockout.Editor
{
    public sealed class CaveBlockoutWindow : EditorWindow
    {
        private Vector2 scroll;
        private CaveValidationResult lastValidation;
        private string clearanceResult;
        private bool showNoise = true;

        [MenuItem("Tools/Underwater Cave/Blockout Window")]
        public static void ShowWindow()
        {
            GetWindow<CaveBlockoutWindow>("Cave Blockout");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Underwater Cave Blockout", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit MainRoute and Branches with Unity's Spline tools. Select a CaveRoute to expose width, height and roll handles at every knot. Meshes update only when Regenerate is pressed.",
                MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Open MainMap"))
                EditorSceneManager.OpenScene(CaveBlockoutBuilder.MainMapPath);

            if (GUILayout.Button("Create / Reset Default Z1-Z7 Routes"))
                CaveBlockoutBuilder.BuildMainMapInteractive();

            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerate Visual + Collider Meshes"))
                lastValidation = CaveBlockoutBuilder.RegenerateCurrentScene(true);

            if (GUILayout.Button("Validate Route Constraints"))
            {
                FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
                lastValidation = CaveBlockoutValidator.Validate(mainRoute, branches);
            }

            if (GUILayout.Button("Validate 6 x 3 x 3m Clearance"))
            {
                FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
                bool passed = CaveClearanceValidator.ValidateAll(mainRoute, branches, out clearanceResult);
                if (!passed) Debug.LogWarning(clearanceResult);
                else Debug.Log(clearanceResult);
            }

            if (GUILayout.Button("Capture Review Sweep"))
            {
                CaveReviewCaptureResult result = CaveReviewCapture.CaptureCurrentScene();
                EditorUtility.RevealInFinder(result.contactSheetPath);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Route Selection", EditorStyles.boldLabel);
            if (GUILayout.Button("Select Main Route"))
            {
                FindRoutes(out CaveRoute mainRoute, out _);
                if (mainRoute != null) Selection.activeGameObject = mainRoute.gameObject;
            }
            if (GUILayout.Button("Select Branch Routes"))
            {
                FindRoutes(out _, out CaveRoute branches);
                if (branches != null) Selection.activeGameObject = branches.gameObject;
            }

            DrawNoiseSettings();

            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawNoiseSettings()
        {
            EditorGUILayout.Space();
            showNoise = EditorGUILayout.Foldout(showNoise, "동굴 표면 노이즈", true);
            if (!showNoise) return;
            FindRoutes(out CaveRoute mainRoute, out _);
            if (mainRoute == null)
            {
                EditorGUILayout.HelpBox("MainRoute를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            CaveNoiseSettings settings = mainRoute.NoiseSettings;
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("활성화", settings.enabled);
            int seed = EditorGUILayout.IntField("Seed", settings.seed);
            float amplitude = EditorGUILayout.Slider("구조 진폭 (m)", settings.amplitudeMeters, 0f, 2f);
            float wavelength = EditorGUILayout.Slider("구조 파장 (m)", settings.wavelengthMeters, 2f, 30f);
            int octaves = EditorGUILayout.IntSlider("Octave", settings.octaves, 1, 4);
            float lacunarity = EditorGUILayout.Slider("Lacunarity", settings.lacunarity, 1f, 3f);
            float persistence = EditorGUILayout.Slider("Persistence", settings.persistence, 0.1f, 0.9f);
            float floor = EditorGUILayout.Slider("바닥 강도", settings.floorMultiplier, 0f, 2f);
            float wall = EditorGUILayout.Slider("벽 강도", settings.wallMultiplier, 0f, 2f);
            float ceiling = EditorGUILayout.Slider("천장 강도", settings.ceilingMultiplier, 0f, 2f);
            float fade = EditorGUILayout.Slider("접합부 Fade (m)", settings.portalFadeDistance, 1f, 10f);
            bool detail = EditorGUILayout.Toggle("시각 디테일", settings.visualDetailEnabled);
            float detailAmplitude = EditorGUILayout.Slider("디테일 진폭 (m)", settings.visualDetailAmplitude, 0f, 0.15f);
            float detailWavelength = EditorGUILayout.Slider("디테일 파장 (m)", settings.visualDetailWavelength, 4f, 10f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(mainRoute, "Change cave noise settings");
                settings.enabled = enabled;
                settings.seed = seed;
                settings.amplitudeMeters = amplitude;
                settings.wavelengthMeters = wavelength;
                settings.octaves = octaves;
                settings.lacunarity = lacunarity;
                settings.persistence = persistence;
                settings.floorMultiplier = floor;
                settings.wallMultiplier = wall;
                settings.ceilingMultiplier = ceiling;
                settings.portalFadeDistance = fade;
                settings.visualDetailEnabled = detail;
                settings.visualDetailAmplitude = detailAmplitude;
                settings.visualDetailWavelength = detailWavelength;
                EditorUtility.SetDirty(mainRoute);
                EditorSceneManager.MarkSceneDirty(mainRoute.gameObject.scene);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Smooth")) ApplyNoisePreset(mainRoute, settings.ApplySmoothPreset, "Apply smooth cave preset");
            if (GUILayout.Button("Rocky")) ApplyNoisePreset(mainRoute, settings.ApplyRockyPreset, "Apply rocky cave preset");
            if (GUILayout.Button("Rough")) ApplyNoisePreset(mainRoute, settings.ApplyRoughPreset, "Apply rough cave preset");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("설정을 바꾼 뒤 'Regenerate Visual + Collider Meshes'를 눌러 적용합니다. 구조 노이즈는 Collider에도 동일하게 적용됩니다.", MessageType.Info);
        }

        private static void ApplyNoisePreset(CaveRoute route, System.Action apply, string undoName)
        {
            Undo.RecordObject(route, undoName);
            apply();
            EditorUtility.SetDirty(route);
            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
        }

        private void DrawValidation()
        {
            if (lastValidation != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(lastValidation.Passed ? "Route validation: PASS" : "Route validation: REVIEW", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Length", $"{lastValidation.routeLength:F1} m");
                EditorGUILayout.LabelField("Rise", $"{lastValidation.totalRise:F1} m");
                EditorGUILayout.LabelField("Minimum opening", $"{lastValidation.minimumWidth:F1} x {lastValidation.minimumHeight:F1} m");
                EditorGUILayout.LabelField("Maximum slope", $"{lastValidation.maximumSlope:F1}°");
                EditorGUILayout.LabelField("Minimum turn radius", $"{lastValidation.minimumTurnRadius:F1} m");
                foreach (string issue in lastValidation.issues)
                    EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(clearanceResult))
                EditorGUILayout.HelpBox(clearanceResult, clearanceResult.StartsWith("All") ? MessageType.Info : MessageType.Warning);
        }

        private static void FindRoutes(out CaveRoute mainRoute, out CaveRoute branches)
        {
            CaveRoute[] routes = Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            mainRoute = routes.FirstOrDefault(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute foundMainRoute = mainRoute;
            branches = routes.FirstOrDefault(route => route != foundMainRoute);
        }
    }
}
