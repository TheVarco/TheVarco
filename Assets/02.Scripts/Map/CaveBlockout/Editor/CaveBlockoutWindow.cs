using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public sealed class CaveBlockoutWindow : EditorWindow
    {
        private Vector2 scroll;
        private CaveValidationResult lastValidation;
        private string clearanceResult;
        private bool showNoise = true;
        private bool showSplineHelp = true;
        private int editingSplineIndex;
        private int editingSegmentIndex;
        private string editingStatus;

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
                "경로 편집과 메시 생성을 분리한 블록아웃 도구입니다. Spline 점을 바꾼 뒤에는 반드시 재생성 버튼을 눌러야 메시와 Collider가 갱신됩니다.",
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

            DrawSplineEditingTools();
            DrawNoiseSettings();

            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSplineEditingTools()
        {
            EditorGUILayout.Space();
            showSplineHelp = EditorGUILayout.Foldout(showSplineHelp, "Spline 경로 편집 도움말", true);
            if (!showSplineHelp)
                return;

            CaveRoute route = FindSelectedRoute();
            if (route == null || route.Container == null || route.Container.Splines.Count == 0)
            {
                EditorGUILayout.HelpBox("먼저 Main Route 또는 Branch Routes를 선택하세요.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("현재 경로", route.name);
            SyncActiveSplineElement(route);
            editingSplineIndex = EditorGUILayout.IntSlider(
                "Spline 번호", editingSplineIndex, 0, route.Container.Splines.Count - 1);
            Spline spline = route.Container[editingSplineIndex];
            if (spline.Count < 2)
            {
                EditorGUILayout.HelpBox("점이 2개 이상인 Spline에서만 구간을 나눌 수 있습니다.", MessageType.Warning);
                return;
            }

            editingSegmentIndex = Mathf.Clamp(editingSegmentIndex, 0, spline.Count - 2);
            editingSegmentIndex = EditorGUILayout.IntSlider(
                $"나눌 구간 (K{editingSegmentIndex} → K{editingSegmentIndex + 1})",
                editingSegmentIndex, 0, spline.Count - 2);

            if (GUILayout.Button("점 이동 도구 (W)"))
            {
                Selection.activeGameObject = route.gameObject;
                ToolManager.SetActiveContext<SplineToolContext>();
                ToolManager.SetActiveTool<SplineMoveTool>();
                editingStatus = "Scene 창에서 점 또는 탄젠트를 선택해 이동할 수 있습니다.";
            }

            if (GUILayout.Button("선택 구간의 실제 길이 중간에 점 추가"))
            {
                CaveRouteKnotInsertionResult result = CaveRouteEditingUtility.InsertKnotAtSegmentMidpoint(
                    route, editingSplineIndex, editingSegmentIndex);
                editingSegmentIndex = Mathf.Clamp(result.knotIndex, 0, spline.Count - 2);
                editingStatus = $"K{result.knotIndex} 점을 추가했습니다. 경로 모양과 기존 폭·높이·구역·분기 정보는 보존됩니다.";
            }

            if (GUILayout.Button("현재 경로를 Scene 화면에 맞추기"))
            {
                Selection.activeGameObject = route.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            EditorGUILayout.HelpBox(
                "1. Main/Branch 경로 선택 → 2. 점 이동 도구에서 점을 클릭하고 W로 이동 → " +
                "3. 구간 중간점이 필요하면 Spline 번호와 K구간을 정해 위 버튼 사용 → " +
                "4. 청록색 단면 손잡이로 폭·높이 조절 → 5. Regenerate로 메시·Collider 갱신 → " +
                "6. Validate와 Capture Review Sweep으로 확인\n\n" +
                "Shift: 여러 점 선택 · C: 탄젠트 모드 순환 · X: 핸들 기준 전환 · Ctrl+Z: 실행 취소\n" +
                "선을 통째로 옮기려면 Shift로 여러 점을 고른 뒤 이동하세요. 분기 입구 주변은 편집 후 반드시 Clearance 검사를 실행하세요.\n" +
                "중간점 추가 시 원래 곡선을 정확히 유지하기 위해 양옆 점의 탄젠트 모드는 Broken으로 고정됩니다. 이후 C로 모드를 바꾸면 곡선 모양이 달라질 수 있습니다.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(editingStatus))
                EditorGUILayout.HelpBox(editingStatus, MessageType.None);
        }

        private void SyncActiveSplineElement(CaveRoute route)
        {
            List<SplineInfo> infos = new List<SplineInfo>();
            for (int i = 0; i < route.Container.Splines.Count; i++)
                infos.Add(new SplineInfo(route.Container, i));
            ISelectableElement active = SplineSelection.GetActiveElement(infos);
            if (active == null)
                return;

            editingSplineIndex = active.SplineInfo.Index;
            editingSegmentIndex = Mathf.Clamp(active.KnotIndex, 0,
                route.Container[editingSplineIndex].Count - 2);
        }

        private static CaveRoute FindSelectedRoute()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                CaveRoute selectedRoute = selected.GetComponentInParent<CaveRoute>();
                if (selectedRoute != null)
                    return selectedRoute;
            }

            FindRoutes(out CaveRoute mainRoute, out _);
            return mainRoute;
        }

        private void DrawNoiseSettings()
        {
            EditorGUILayout.Space();
            showNoise = EditorGUILayout.Foldout(showNoise, "동굴 표면 노이즈", true);
            if (!showNoise) return;
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            if (mainRoute == null)
            {
                EditorGUILayout.HelpBox("MainRoute를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            CaveNoiseSettings settings = mainRoute.NoiseSettings;
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("활성화", settings.enabled);
            int seed = EditorGUILayout.IntField("Seed", settings.seed);
            float amplitude = EditorGUILayout.Slider("구조 진폭 (m)", settings.amplitudeMeters, 0f, 5f);
            float strengthGain = EditorGUILayout.Slider("노이즈 증폭", settings.strengthGain, 0.5f, 2.5f);
            float displacementRatio = EditorGUILayout.Slider("반경 대비 최대 변위", settings.maximumDisplacementRatio, 0.05f, 0.35f);
            float wavelength = EditorGUILayout.Slider("구조 파장 (m)", settings.wavelengthMeters, 2f, 30f);
            int octaves = EditorGUILayout.IntSlider("Octave", settings.octaves, 1, 4);
            float lacunarity = EditorGUILayout.Slider("Lacunarity", settings.lacunarity, 1f, 3f);
            float persistence = EditorGUILayout.Slider("Persistence", settings.persistence, 0.1f, 0.9f);
            float floor = EditorGUILayout.Slider("바닥 강도", settings.floorMultiplier, 0f, 3f);
            float wall = EditorGUILayout.Slider("벽 강도", settings.wallMultiplier, 0f, 3f);
            float ceiling = EditorGUILayout.Slider("천장 강도", settings.ceilingMultiplier, 0f, 3f);
            float fade = EditorGUILayout.Slider("접합부 Fade (m)", settings.portalFadeDistance, 1f, 10f);
            bool detail = EditorGUILayout.Toggle("시각 디테일", settings.visualDetailEnabled);
            float detailAmplitude = EditorGUILayout.Slider("디테일 진폭 (m)", settings.visualDetailAmplitude, 0f, 0.75f);
            float detailWavelength = EditorGUILayout.Slider("디테일 파장 (m)", settings.visualDetailWavelength, 4f, 16f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(mainRoute, "Change cave noise settings");
                settings.enabled = enabled;
                settings.seed = seed;
                settings.amplitudeMeters = amplitude;
                settings.strengthGain = strengthGain;
                settings.maximumDisplacementRatio = displacementRatio;
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

            if (GUILayout.Button("Disable All Noise + Regenerate"))
            {
                DisableAllNoiseAndRegenerate(mainRoute, branches);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Smooth")) ApplyNoisePreset(mainRoute, settings.ApplySmoothPreset, "Apply smooth cave preset");
            if (GUILayout.Button("Rocky")) ApplyNoisePreset(mainRoute, settings.ApplyRockyPreset, "Apply rocky cave preset");
            if (GUILayout.Button("Rough")) ApplyNoisePreset(mainRoute, settings.ApplyRoughPreset, "Apply rough cave preset");
            if (GUILayout.Button("Strong")) ApplyNoisePreset(mainRoute, settings.ApplyStrongPreset, "Apply strong cave preset");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("설정을 바꾼 뒤 'Regenerate Visual + Collider Meshes'를 눌러 적용합니다. 구조 노이즈는 Collider에도 적용되며 최소 안전 반경 3.5m를 보존합니다. 시각 디테일은 Visual Mesh 안쪽에만 적용됩니다. Clearance는 Collider 통과를, Capture Review Sweep은 시각 디테일을 확인합니다.", MessageType.Info);
        }

        private static void ApplyNoisePreset(CaveRoute route, System.Action apply, string undoName)
        {
            Undo.RecordObject(route, undoName);
            apply();
            EditorUtility.SetDirty(route);
            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
        }

        private void DisableAllNoiseAndRegenerate(CaveRoute mainRoute, CaveRoute branches)
        {
            List<CaveRoute> routes = new List<CaveRoute> { mainRoute };
            if (branches != null) routes.Add(branches);
            Undo.RecordObjects(routes.Cast<UnityEngine.Object>().ToArray(), "Disable all cave noise");
            foreach (CaveRoute route in routes)
            {
                route.NoiseSettings.ApplySmoothPreset();
                EditorUtility.SetDirty(route);
                EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
            }

            lastValidation = CaveBlockoutBuilder.RegenerateCurrentScene(true);
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
