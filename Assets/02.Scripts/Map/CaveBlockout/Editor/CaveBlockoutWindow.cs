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

            DrawValidation();
            EditorGUILayout.EndScrollView();
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
