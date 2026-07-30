using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public static class CaveBlockoutBuilder
    {
        public const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        public const string RootName = "CaveBlockout";
        private const string CaveMaterialPath = "Assets/04.Materials/MapMaterial/dark_rock_02_8k/DarkRock_Cave_Triplanar.mat";
        private const string ProxyMaterialPath = "Assets/Generated/CaveBlockout/SubmarineProxy.mat";

        [MenuItem("Tools/Underwater Cave/Create or Reset MainMap Blockout")]
        public static void BuildMainMapInteractive()
        {
            if (!EditorUtility.DisplayDialog("Reset cave blockout?", "This replaces only tool-owned children below CaveBlockout. Other scene objects are preserved.", "Build", "Cancel"))
                return;

            if (SceneManager.GetActiveScene().path != MainMapPath)
                EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            BuildDefaultInCurrentScene(true);
        }

        public static void BuildMainMapBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveValidationResult result = BuildDefaultInCurrentScene(true);
            Debug.Log($"CAVE_BLOCKOUT_BUILD {(result.Passed ? "PASS" : "WARN")} length={result.routeLength:F1}m rise={result.totalRise:F1}m slope={result.maximumSlope:F1} radius={result.minimumTurnRadius:F1}");
            foreach (string issue in result.issues)
                Debug.LogWarning("CAVE_BLOCKOUT: " + issue);
        }

        public static void RegenerateMainMapBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveValidationResult result = RegenerateCurrentScene(true);
            Debug.Log($"CAVE_BLOCKOUT_REGENERATE {(result.Passed ? "PASS" : "WARN")} length={result.routeLength:F1}m rise={result.totalRise:F1}m slope={result.maximumSlope:F1} radius={result.minimumTurnRadius:F1}");
            foreach (string issue in result.issues)
                Debug.LogWarning("CAVE_BLOCKOUT: " + issue);
        }

        public static void ApplyStrongNoiseMainMapBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveRoute mainRoute = UnityEngine.Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(route => route.Definitions.Any(definition => definition.isMainRoute));
            if (mainRoute == null)
                throw new InvalidOperationException("Main cave route was not found.");

            mainRoute.NoiseSettings.ApplyStrongPreset();
            EditorUtility.SetDirty(mainRoute);
            EditorSceneManager.MarkSceneDirty(mainRoute.gameObject.scene);
            CaveValidationResult result = RegenerateCurrentScene(true);
            Debug.Log($"CAVE_STRONG_NOISE {(result.Passed ? "PASS" : "WARN")} amplitude={mainRoute.NoiseSettings.amplitudeMeters:F2} " +
                      $"gain={mainRoute.NoiseSettings.strengthGain:F2} ratio={mainRoute.NoiseSettings.maximumDisplacementRatio:F2}");
        }

        public static void ValidateMainMapBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveRoute[] routes = UnityEngine.Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CaveRoute mainRoute = routes.FirstOrDefault(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute branches = routes.FirstOrDefault(route => route != mainRoute);
            CaveValidationResult routeResult = CaveBlockoutValidator.Validate(mainRoute, branches);
            bool clearancePassed = CaveClearanceValidator.ValidateAll(mainRoute, branches, out string clearanceDetails);
            CaveValidationSummary summary = UnityEngine.Object.FindFirstObjectByType<CaveValidationSummary>(FindObjectsInactive.Include);
            bool geometryPassed = summary != null && summary.triangleCount < 50000 && summary.boundaryLoopCount == 1 &&
                                  summary.nonManifoldEdgeCount == 0 && summary.windingMismatchCount == 0 && summary.degenerateTriangleCount == 0;
            Debug.Log($"CAVE_VALIDATION route={(routeResult.Passed ? "PASS" : "FAIL")} clearance={(clearancePassed ? "PASS" : "FAIL")} triangles={(summary != null ? summary.triangleCount : -1)} boundaryLoops={(summary != null ? summary.boundaryLoopCount : -1)}");
            Debug.Log(clearanceDetails);
            if (!routeResult.Passed || !clearancePassed || !geometryPassed)
                throw new InvalidOperationException(string.Join("\n", routeResult.issues.Concat(new[] { clearanceDetails })));
        }

        [MenuItem("Tools/Underwater Cave/Regenerate and Bake Current Blockout")]
        public static void RegenerateCurrentSceneMenu()
        {
            RegenerateCurrentScene(true);
        }

        public static CaveValidationResult BuildDefaultInCurrentScene(bool saveScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera mainCamera = FindSceneCamera(scene);
            if (mainCamera != null)
                mainCamera.transform.SetParent(null, true);

            GameObject root = GameObject.Find(RootName) ?? new GameObject(RootName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            ClearToolOwnedChildren(root.transform);

            Transform routesRoot = CreateGroup(root.transform, "Routes");
            Transform generatedRoot = CreateGroup(root.transform, "Generated");
            Transform markersRoot = CreateGroup(root.transform, "Markers");
            Transform validationRoot = CreateGroup(root.transform, "Validation");
            Transform playtestRoot = CreateGroup(root.transform, "Playtest");

            CaveBlockoutPreset.CreateRoutes(routesRoot, out CaveRoute mainRoute, out CaveRoute branches);
            Material caveMaterial = AssetDatabase.LoadAssetAtPath<Material>(CaveMaterialPath);
            if (caveMaterial == null)
                throw new InvalidOperationException($"Cave triplanar material was not found at {CaveMaterialPath}.");
            CaveMeshGenerationResult meshResult = CaveMeshGenerator.GenerateAll(mainRoute, branches, generatedRoot, caveMaterial);
            CreateZoneMarkers(mainRoute, markersRoot);
            CaveValidationResult validation = CreateValidation(mainRoute, branches, validationRoot, meshResult);
            CreatePlaytest(mainRoute, playtestRoot, mainCamera);

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
            {
                EditorSceneManager.SaveScene(scene, MainMapPath);
                EditorSceneManager.SaveOpenScenes();
            }
            AssetDatabase.SaveAssets();
            return validation;
        }

        public static CaveValidationResult RegenerateCurrentScene(bool saveScene)
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
                return BuildDefaultInCurrentScene(saveScene);

            CaveRoute[] routes = root.GetComponentsInChildren<CaveRoute>(true);
            CaveRoute mainRoute = routes.FirstOrDefault(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute branches = routes.FirstOrDefault(route => route != mainRoute);
            if (mainRoute == null || branches == null)
                return BuildDefaultInCurrentScene(saveScene);

            Transform generatedRoot = GetOrCreateGroup(root.transform, "Generated");
            Transform markersRoot = GetOrCreateGroup(root.transform, "Markers");
            Transform validationRoot = GetOrCreateGroup(root.transform, "Validation");
            ClearChildren(markersRoot);
            ClearChildren(validationRoot);

            Material caveMaterial = AssetDatabase.LoadAssetAtPath<Material>(CaveMaterialPath);
            if (caveMaterial == null)
                throw new InvalidOperationException($"Cave triplanar material was not found at {CaveMaterialPath}.");
            CaveMeshGenerationResult meshResult = CaveMeshGenerator.GenerateAll(mainRoute, branches, generatedRoot, caveMaterial);
            CreateZoneMarkers(mainRoute, markersRoot);
            CaveValidationResult validation = CreateValidation(mainRoute, branches, validationRoot, meshResult);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (saveScene)
                EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            return validation;
        }

        private static CaveValidationResult CreateValidation(CaveRoute mainRoute, CaveRoute branches, Transform validationRoot, CaveMeshGenerationResult meshResult)
        {
            CaveValidationResult result = CaveBlockoutValidator.Validate(mainRoute, branches);
            GameObject reportObject = new GameObject("BlockoutValidation");
            reportObject.transform.SetParent(validationRoot, false);
            CaveValidationSummary summary = reportObject.AddComponent<CaveValidationSummary>();
            summary.passed = result.Passed && meshResult.triangleCount < 50000 && meshResult.topology.IsValidOpenCave;
            summary.routeLength = result.routeLength;
            summary.totalRise = result.totalRise;
            summary.minimumWidth = result.minimumWidth;
            summary.minimumHeight = result.minimumHeight;
            summary.maximumSlope = result.maximumSlope;
            summary.minimumTurnRadius = result.minimumTurnRadius;
            summary.maximumReverseDrop = result.maximumReverseDrop;
            summary.branchCount = result.branchCount;
            summary.triangleCount = meshResult.triangleCount;
            summary.boundaryLoopCount = meshResult.topology.boundaryLoopCount;
            summary.nonManifoldEdgeCount = meshResult.topology.nonManifoldEdgeCount;
            summary.windingMismatchCount = meshResult.topology.windingMismatchCount;
            summary.degenerateTriangleCount = meshResult.topology.degenerateTriangleCount;
            List<string> details = new List<string>(result.issues);
            if (meshResult.triangleCount >= 50000)
                details.Add($"Triangle budget exceeded: {meshResult.triangleCount}.");
            if (!meshResult.topology.IsValidOpenCave)
                details.Add($"Mesh topology failed: boundaryLoops={meshResult.topology.boundaryLoopCount}, nonManifold={meshResult.topology.nonManifoldEdgeCount}, winding={meshResult.topology.windingMismatchCount}, degenerate={meshResult.topology.degenerateTriangleCount}.");
            summary.details = details.Count == 0 ? "PASS - Guide dimensions and route constraints are satisfied." : string.Join("\n", details);
            return result;
        }

        private static void CreateZoneMarkers(CaveRoute mainRoute, Transform markersRoot)
        {
            Spline spline = mainRoute.Container[0];
            CaveRouteSplineDefinition definition = mainRoute.Definitions[0];
            Color[] colors =
            {
                new Color(0.1f, 0.55f, 1f, 0.3f), new Color(0.15f, 0.8f, 1f, 0.3f),
                new Color(0.2f, 0.9f, 0.75f, 0.3f), new Color(0.45f, 0.8f, 0.4f, 0.3f),
                new Color(0.85f, 0.75f, 0.25f, 0.3f), new Color(1f, 0.45f, 0.2f, 0.3f),
                new Color(0.9f, 0.25f, 0.5f, 0.3f)
            };

            for (int i = 0; i < definition.sections.Count; i++)
            {
                CaveRouteSection section = definition.sections[i];
                float startT = mainRoute.ResolveSectionStartT(definition, section);
                float endT = mainRoute.ResolveSectionEndT(definition, section);
                float t = Mathf.Lerp(startT, endT, 0.5f);
                Vector3 position = mainRoute.Container.EvaluatePosition(0, t);
                Vector3 tangent = ((Vector3)mainRoute.Container.EvaluateTangent(0, t)).normalized;
                GameObject markerObject = new GameObject(section.zoneId + "_GuideVolume");
                markerObject.transform.SetParent(markersRoot, false);
                markerObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(tangent, Vector3.up));
                CaveZoneMarker marker = markerObject.AddComponent<CaveZoneMarker>();
                marker.zoneId = section.zoneId;
                marker.guideSize = new Vector3(section.guideSize.x, section.guideSize.y, section.nominalLength);
                marker.color = colors[i % colors.Length];
            }
        }

        private static void CreatePlaytest(CaveRoute mainRoute, Transform playtestRoot, Camera existingCamera)
        {
            SplineContainer container = mainRoute.Container;
            Vector3 submarinePosition = container.EvaluatePosition(0, 0.035f);
            Vector3 otterPosition = container.EvaluatePosition(0, 0.055f);
            Vector3 tangent = ((Vector3)container.EvaluateTangent(0, 0.04f)).normalized;
            Quaternion facing = Quaternion.LookRotation(tangent, Vector3.up);

            GameObject cameraRigObject = new GameObject("CameraRig");
            cameraRigObject.transform.SetParent(playtestRoot, false);
            CavePlaytestCameraRig cameraRig = cameraRigObject.AddComponent<CavePlaytestCameraRig>();

            Camera camera = existingCamera;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            camera.transform.SetParent(cameraRigObject.transform, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1200f);
            cameraRig.controlledCamera = camera;

            GameObject submarine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            submarine.name = "SubmarineProxy";
            submarine.transform.SetParent(playtestRoot, false);
            submarine.transform.SetPositionAndRotation(submarinePosition, facing);
            submarine.transform.localScale = new Vector3(3f, 3f, 6f);
            submarine.layer = 2;
            Rigidbody submarineBody = submarine.AddComponent<Rigidbody>();
            submarineBody.mass = 8f;
            CaveSwimController submarineController = submarine.AddComponent<CaveSwimController>();
            submarineController.lookReference = cameraRigObject.transform;
            submarineController.moveSpeed = 12f;
            submarine.GetComponent<MeshRenderer>().sharedMaterial = EnsureMaterial(ProxyMaterialPath, new Color(0.95f, 0.68f, 0.12f, 1f), false);

            GameObject otter = new GameObject("OtterPlayer");
            otter.transform.SetParent(playtestRoot, false);
            otter.transform.SetPositionAndRotation(otterPosition, facing);
            otter.layer = 2;
            CapsuleCollider capsule = otter.AddComponent<CapsuleCollider>();
            capsule.height = 1.2f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.1f, 0f);
            MonoBehaviour otterController = AddExistingOtterController(otter, cameraRigObject.transform);
            CreateOtterVisual(otter.transform);
            SetLayerRecursively(otter, 2);

            CavePlaytestSwitcher switcher = playtestRoot.gameObject.AddComponent<CavePlaytestSwitcher>();
            switcher.submarineRoot = submarine;
            switcher.otterRoot = otter;
            switcher.submarineController = submarineController;
            switcher.otterController = otterController;
            switcher.cameraRig = cameraRig;
            switcher.startWithSubmarine = true;
            cameraRig.target = submarine.transform;
            cameraRigObject.transform.position = submarinePosition - tangent * 10f + Vector3.up * 3f;
        }

        private static MonoBehaviour AddExistingOtterController(GameObject otter, Transform lookReference)
        {
            Type controllerType = Type.GetType("PlayerController, Assembly-CSharp");
            if (controllerType == null || !typeof(MonoBehaviour).IsAssignableFrom(controllerType))
            {
                CaveSwimController fallback = otter.AddComponent<CaveSwimController>();
                fallback.lookReference = lookReference;
                fallback.moveSpeed = 5f;
                Debug.LogWarning("PlayerController was not available; OtterPlayer uses CaveSwimController as a fallback.");
                return fallback;
            }

            MonoBehaviour controller = (MonoBehaviour)otter.AddComponent(controllerType);
            SerializedObject serializedController = new SerializedObject(controller);
            SerializedProperty lookProperty = serializedController.FindProperty("lookReference");
            if (lookProperty != null) lookProperty.objectReferenceValue = lookReference;
            SerializedProperty speedProperty = serializedController.FindProperty("moveSpeed");
            if (speedProperty != null) speedProperty.floatValue = 5f;
            SerializedProperty accelerationProperty = serializedController.FindProperty("acceleration");
            if (accelerationProperty != null) accelerationProperty.floatValue = 10f;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        private static void CreateOtterVisual(Transform parent)
        {
            const string modelPath = "Assets/99.Resources/Modeling/rawOtter/3D Otter.fbx";
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "OtterVisual_Fallback";
                fallback.transform.SetParent(parent, false);
                UnityEngine.Object.DestroyImmediate(fallback.GetComponent<Collider>());
                return;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, parent);
            visual.name = "OtterVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float maximumDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maximumDimension > 0.001f)
                visual.transform.localScale *= 1.2f / maximumDimension;
        }

        private static Material EnsureMaterial(string path, Color color, bool twoSided)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", twoSided ? 0f : 2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Camera FindSceneCamera(Scene scene)
        {
            return Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera.gameObject.scene == scene && camera.CompareTag("MainCamera"))
                ?? Resources.FindObjectsOfTypeAll<Camera>().FirstOrDefault(camera => camera.gameObject.scene == scene);
        }

        private static Transform CreateGroup(Transform parent, string name)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Transform GetOrCreateGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            return existing != null ? existing : CreateGroup(parent, name);
        }

        private static void ClearToolOwnedChildren(Transform root)
        {
            string[] names = { "Routes", "Generated", "Markers", "Validation", "Playtest" };
            foreach (string name in names)
            {
                Transform child = root.Find(name);
                if (child != null)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
