// ============================================================================
// USDZ import pipeline.
//
// Receives .usdz downloads from VARCO3D and places them in the scene using
// Unity's USD Importer package (com.unity.importer.usd). USDZ is supported
// natively as an all-in-one format — the file just needs to be inside Assets/
// for Unity to auto-import it. No unzipping required, unlike the Blender /
// Maya / 3ds Max paths.
//
// UsdPreviewSurface materials are handled by the USD Importer, which picks
// the right shader for the active render pipeline (Standard / URP Lit /
// HDRP Lit). This is the whole point of moving to USDZ — the FBX path's
// per-pipeline material reconstruction (VARCO3DFbxImporter.cs) is gone.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// USDZ → scene pipeline. Runs on the main thread via VARCO3DImporter.
    /// </summary>
    public static class VARCO3DUsdzImporter
    {
        public static void Import(ImportTask task)
        {
            string assetPath = null;
            try
            {
                // 1. Build destination path inside Assets/ with collision-free naming
                //    (mirrors Blender/Maya/3ds Max patterns: {name}, {name}_1, {name}_2, ...).
                string assetDir = ResolveAssetDir(task.AssetName);
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "..",
                    assetDir).Replace('\\', '/'));

                string fileName = $"{SanitizeName(task.AssetName)}.usdz";
                assetPath = Path.Combine(assetDir, fileName).Replace('\\', '/');
                string absoluteDest = Path.Combine(Application.dataPath, "..",
                    assetPath).Replace('\\', '/');

                // 2. Copy the .usdz into Assets/ — Unity's USD Importer auto-imports
                //    USDZ files placed in the project; explicit ImportAsset() below
                //    forces synchronous import so we can place it in the scene right
                //    after.
                File.Copy(task.FilePath, absoluteDest, overwrite: true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                // 3. Place in scene on the next editor tick. delayCall ensures the
                //    import side effects (sub-asset creation, material remapping)
                //    have settled before we instantiate.
                string capturedPath = assetPath;
                string capturedName = task.AssetName;
                EditorApplication.delayCall += () => PlaceInScene(capturedPath, capturedName);
                // Note: SetupAnimator runs inside PlaceInScene — USDZ animations
                // are Generic transform-based (no Avatar). USD Importer emits the
                // AnimationClips as sub-assets but doesn't wire up an
                // AnimatorController, so the instance would be stuck in pose 0.

                Debug.Log($"{VARCO3DConstants.LogPrefix} USDZ import complete: {task.AssetName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} USDZ import failed for '{task.AssetName}': {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                // Remove the temp download regardless of outcome — Assets/ now owns its copy.
                try
                {
                    if (!string.IsNullOrEmpty(task.FilePath) && File.Exists(task.FilePath))
                        File.Delete(task.FilePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Temp cleanup warning: {e.Message}");
                }
            }
        }

        // ----------------------------------------------------------------
        // Scene placement
        // ----------------------------------------------------------------

        private static void PlaceInScene(string assetPath, string assetName)
        {
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} Failed to load USDZ as GameObject: {assetPath}");
                    return;
                }

                GameObject sceneObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (sceneObject == null)
                {
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} Failed to instantiate USDZ prefab");
                    return;
                }

                sceneObject.name = assetName;
                sceneObject.transform.position = Vector3.zero;
                sceneObject.transform.rotation = Quaternion.identity;

                // Stand on ground: shift Y so the bounds' bottom sits at Y=0,
                // matching the FBX importer's convention.
                Renderer[] renderers = sceneObject.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    if (bounds.size != Vector3.zero)
                        sceneObject.transform.position = new Vector3(0, -bounds.min.y, 0);
                }

                SetupAnimator(sceneObject, assetPath);

                // Editor-standard UX: register Undo so Ctrl+Z removes the imported
                // GameObject. Select and ping for visibility.
                Undo.RegisterCreatedObjectUndo(sceneObject, $"Import {assetName}");
                Selection.activeGameObject = sceneObject;
                EditorGUIUtility.PingObject(sceneObject);

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sceneObject.scene);
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} USDZ scene placement failed: {e.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Animator setup
        // ----------------------------------------------------------------

        /// <summary>
        /// Mirrors VARCO3DFbxImporter.SetupAnimator for USDZ. USD Importer exposes
        /// embedded animations as AnimationClip sub-assets but does not attach an
        /// Animator or AnimatorController, so the scene instance would freeze at
        /// the bind pose. We create a controller next to the .usdz, add each clip
        /// as a state (first = default), and assign it to a Generic-mode Animator
        /// on the instance (no Avatar — USDZ animations are transform-based, not
        /// Humanoid muscle-space).
        /// </summary>
        private static void SetupAnimator(GameObject sceneObject, string assetPath)
        {
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            AnimationClip[] clips = allAssets
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();

            if (clips.Length == 0) return;

            Animator animator = sceneObject.GetComponent<Animator>();
            if (animator == null)
                animator = sceneObject.AddComponent<Animator>();

            string controllerDir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string controllerFile = $"{SanitizeName(sceneObject.name)}_Controller.controller";
            string controllerPath = Path.Combine(controllerDir, controllerFile).Replace('\\', '/');

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            for (int i = 0; i < clips.Length; i++)
            {
                AnimatorState state = stateMachine.AddState(clips[i].name);
                state.motion = clips[i];

                if (i == 0)
                    stateMachine.defaultState = state;
            }

            animator.runtimeAnimatorController = controller;
            Debug.Log($"{VARCO3DConstants.LogPrefix} Created AnimatorController with {clips.Length} clip(s)");
        }

        // ----------------------------------------------------------------
        // Path helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Pick a free directory under Assets/VARCO3DImports/. Mirrors the
        /// collision-handling pattern used by Blender (operators.py:59-64) and
        /// Maya / 3ds Max: try the bare name first, then append _1, _2, ...
        /// </summary>
        private static string ResolveAssetDir(string assetName)
        {
            string baseName = SanitizeName(assetName);
            string projectRoot = Path.Combine(Application.dataPath, "..");

            string candidate = Path.Combine(VARCO3DConstants.ImportFolder, baseName).Replace('\\', '/');
            if (!Directory.Exists(Path.Combine(projectRoot, candidate)))
                return candidate;

            for (int i = 1; i < 10000; i++)
            {
                candidate = Path.Combine(VARCO3DConstants.ImportFolder, $"{baseName}_{i}").Replace('\\', '/');
                if (!Directory.Exists(Path.Combine(projectRoot, candidate)))
                    return candidate;
            }
            // Practically unreachable — fall back to a timestamp suffix.
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            return Path.Combine(VARCO3DConstants.ImportFolder, $"{baseName}_{ts}").Replace('\\', '/');
        }

        private static string SanitizeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name;
        }
    }
}
