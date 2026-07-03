// ============================================================================
// LEGACY FBX import pipeline.
//
// Handles the original ZIP+FBX+metadata.json bundle format that VARCO3D
// emitted before the USDZ migration. Preserved for backward compatibility
// while the service finishes its own switch to USDZ; this path will be
// removed once VARCO3D stops emitting ZIP+FBX URLs (see docs/roadmap.md
// "Deferred Work — legacy format removal").
//
// The USDZ path lives in VARCO3DUsdzImporter.cs; the dispatcher in
// VARCO3DImporter.cs routes between the two based on ImportTask.Fmt.
// ============================================================================

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// Legacy ZIP+FBX import pipeline with PBR material reconstruction per render
    /// pipeline. All methods run on the main thread (called from
    /// EditorApplication.update via VARCO3DImporter.ProcessImport).
    /// </summary>
    public static class VARCO3DFbxImporter
    {
        // ----------------------------------------------------------------
        // Main entry point
        // ----------------------------------------------------------------

        public static void Import(ImportTask task)
        {
            string extractDir = null;
            try
            {
                // 1. Extract ZIP
                extractDir = ExtractZip(task.FilePath, task.AssetName);

                // 2. Find FBX file
                string[] fbxFiles = Directory.GetFiles(extractDir, "*.fbx", SearchOption.AllDirectories);
                if (fbxFiles.Length == 0)
                {
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} No .fbx file found in ZIP");
                    return;
                }
                string fbxSourcePath = fbxFiles[0];

                // 3. Create model directory in Assets
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string modelDirName = $"{SanitizeName(task.AssetName)}_{timestamp}";
                string modelDir = Path.Combine(VARCO3DConstants.ImportFolder, modelDirName);
                string modelDirAbsolute = Path.Combine(Application.dataPath,
                    "..", modelDir).Replace('\\', '/');
                Directory.CreateDirectory(modelDirAbsolute);

                // 4. Copy FBX to model directory
                string fbxDestName = "model.fbx";
                string fbxDestAbsolute = Path.Combine(modelDirAbsolute, fbxDestName);
                File.Copy(fbxSourcePath, fbxDestAbsolute, true);

                // 5. Copy texture files to model directory
                CopyTextureFiles(extractDir, modelDirAbsolute);

                // 6. Parse metadata.json (before import so we can configure everything upfront)
                string[] metadataFiles = Directory.GetFiles(extractDir, "metadata.json", SearchOption.AllDirectories);
                string metadataPath = metadataFiles.Length > 0 ? metadataFiles[0] : null;
                AssetMetadata metadata = null;
                if (metadataPath != null)
                {
                    string metadataJson = File.ReadAllText(metadataPath);
                    metadata = JsonUtility.FromJson<AssetMetadata>(metadataJson);
                }
                else
                {
                    Debug.LogWarning($"{VARCO3DConstants.LogPrefix} No metadata.json found — using default materials");
                }

                // 7. Register files with AssetDatabase
                string fbxAssetPath = Path.Combine(modelDir, fbxDestName).Replace('\\', '/');
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // 8. Determine material strategy: metadata sidecar vs FBX embedded
                bool hasMetadata = metadata?.materials != null && metadata.materials.Length > 0;

                // 9. Configure model importer
                ModelImporter importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
                if (importer != null)
                {
                    if (hasMetadata)
                    {
                        // Sidecar data exists: ignore FBX materials, we create our own
                        importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    }
                    else
                    {
                        // No sidecar: use FBX embedded materials and textures
                        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                        importer.materialLocation = ModelImporterMaterialLocation.External;
                    }

                    // Configure rig type based on skeleton presence
                    bool hasSkeleton = false;
                    UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                    foreach (var asset in allAssets)
                    {
                        if (asset is Avatar || asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        {
                            hasSkeleton = true;
                            break;
                        }
                    }

                    if (hasSkeleton)
                    {
                        importer.importAnimation = true;
                        importer.animationType = ModelImporterAnimationType.Human;
                    }
                    else
                    {
                        importer.importAnimation = false;
                        importer.animationType = ModelImporterAnimationType.None;
                    }

                    importer.SaveAndReimport();
                }

                // 10. Create PBR materials from metadata (only when sidecar exists)
                Material[] materials = null;
                if (hasMetadata)
                {
                    materials = CreatePBRMaterials(metadata, modelDir);
                    // Clean up auto-generated directories (model.fbm, Materials)
                    CleanupAutoGeneratedDirs(modelDirAbsolute);
                }

                // 11. Place in scene and assign materials
                string capturedFbxPath = fbxAssetPath;
                string capturedName = task.AssetName;
                Material[] capturedMaterials = materials;
                EditorApplication.delayCall += () =>
                {
                    PlaceInScene(capturedFbxPath, capturedName, capturedMaterials);
                };

                Debug.Log($"{VARCO3DConstants.LogPrefix} FBX import complete: {task.AssetName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} FBX import failed for '{task.AssetName}': {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                Cleanup(extractDir, task.FilePath);
            }
        }

        // ----------------------------------------------------------------
        // ZIP extraction
        // ----------------------------------------------------------------

        private static string ExtractZip(string zipPath, string assetName)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string extractDir = Path.Combine(Application.temporaryCachePath,
                $"VARCO3D_Extract_{timestamp}");

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            ZipFile.ExtractToDirectory(zipPath, extractDir);
            return extractDir;
        }

        // ----------------------------------------------------------------
        // File helpers
        // ----------------------------------------------------------------

        private static void CopyTextureFiles(string sourceDir, string destDir)
        {
            string[] textureExtensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp", "*.tiff" };
            foreach (string ext in textureExtensions)
            {
                foreach (string file in Directory.GetFiles(sourceDir, ext, SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    string destPath = Path.Combine(destDir, fileName);
                    if (!File.Exists(destPath))
                        File.Copy(file, destPath);
                }
            }
        }

        private static string SanitizeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>
        /// Remove directories auto-generated by Unity's FBX importer (model.fbm, Materials).
        /// </summary>
        private static void CleanupAutoGeneratedDirs(string modelDirAbsolute)
        {
            string[] autoGenDirs = { "model.fbm", "Materials" };
            foreach (string dirName in autoGenDirs)
            {
                string dirPath = Path.Combine(modelDirAbsolute, dirName);
                if (Directory.Exists(dirPath))
                {
                    Directory.Delete(dirPath, true);
                    // Also remove the .meta file
                    string metaPath = dirPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }
            }
            AssetDatabase.Refresh();
        }

        // ----------------------------------------------------------------
        // PBR material pipeline
        // ----------------------------------------------------------------

        /// <summary>
        /// Creates PBR materials from metadata and saves them as .mat assets.
        /// Returns the array of created materials.
        /// </summary>
        private static Material[] CreatePBRMaterials(AssetMetadata metadata, string modelDir)
        {
            RenderPipelineType pipeline = DetectRenderPipeline();

            var materials = new System.Collections.Generic.List<Material>();

            foreach (MaterialDef matDef in metadata.materials)
            {
                if (string.IsNullOrEmpty(matDef.name)) continue;

                Material material = CreatePBRMaterial(matDef, modelDir, pipeline);
                if (material != null)
                {
                    string matAssetPath = Path.Combine(modelDir, $"{matDef.name}.mat").Replace('\\', '/');
                    AssetDatabase.CreateAsset(material, matAssetPath);
                    materials.Add(material);
                }
            }

            return materials.ToArray();
        }

        private static Material CreatePBRMaterial(MaterialDef matDef, string modelDir,
            RenderPipelineType pipeline)
        {
            string shaderName;
            switch (pipeline)
            {
                case RenderPipelineType.URP:
                    shaderName = "Universal Render Pipeline/Lit";
                    break;
                case RenderPipelineType.HDRP:
                    shaderName = "HDRP/Lit";
                    break;
                default:
                    shaderName = "Standard";
                    break;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Shader '{shaderName}' not found, falling back to Standard");
                shader = Shader.Find("Standard");
                if (shader == null) return null;
                pipeline = RenderPipelineType.BuiltIn;
            }

            Material mat = new Material(shader);
            mat.name = matDef.name;

            switch (pipeline)
            {
                case RenderPipelineType.BuiltIn:
                    ApplyBuiltInMaterial(mat, matDef, modelDir);
                    break;
                case RenderPipelineType.URP:
                    ApplyURPMaterial(mat, matDef, modelDir);
                    break;
                case RenderPipelineType.HDRP:
                    ApplyHDRPMaterial(mat, matDef, modelDir);
                    break;
            }

            return mat;
        }

        // ----------------------------------------------------------------
        // Built-In (Standard shader)
        // ----------------------------------------------------------------

        private static void ApplyBuiltInMaterial(Material mat, MaterialDef def, string modelDir)
        {
            // Base color
            if (!string.IsNullOrEmpty(def.baseColorTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.baseColorTexture);
                if (tex != null)
                    mat.SetTexture("_MainTex", tex);
            }
            if (def.baseColorFactor != null && def.baseColorFactor.Length >= 3)
            {
                float a = def.baseColorFactor.Length >= 4 ? def.baseColorFactor[3] : 1f;
                mat.SetColor("_Color", new Color(def.baseColorFactor[0], def.baseColorFactor[1],
                    def.baseColorFactor[2], a));
            }

            // Normal
            if (!string.IsNullOrEmpty(def.normalTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.normalTexture, isNormalMap: true);
                if (tex != null)
                {
                    mat.SetTexture("_BumpMap", tex);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            // Metallic / Roughness (float values — ORM channel mismatch with Standard shader)
            mat.SetFloat("_Metallic", def.metallicFactor);
            mat.SetFloat("_Glossiness", 1.0f - def.roughnessFactor);

            // Occlusion (ORM R channel = AO — correct for Standard shader)
            if (!string.IsNullOrEmpty(def.occlusionTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.occlusionTexture, isSRGB: false);
                if (tex != null)
                    mat.SetTexture("_OcclusionMap", tex);
            }

            // Emission
            if (!string.IsNullOrEmpty(def.emissiveTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.emissiveTexture);
                if (tex != null)
                {
                    mat.SetTexture("_EmissionMap", tex);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.white);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            if (def.emissiveFactor != null && def.emissiveFactor.Length >= 3 &&
                (def.emissiveFactor[0] > 0 || def.emissiveFactor[1] > 0 || def.emissiveFactor[2] > 0))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(
                    def.emissiveFactor[0], def.emissiveFactor[1], def.emissiveFactor[2]));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        // ----------------------------------------------------------------
        // URP (Universal Render Pipeline/Lit)
        // ----------------------------------------------------------------

        private static void ApplyURPMaterial(Material mat, MaterialDef def, string modelDir)
        {
            // Base color
            if (!string.IsNullOrEmpty(def.baseColorTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.baseColorTexture);
                if (tex != null)
                    mat.SetTexture("_BaseMap", tex);
            }
            if (def.baseColorFactor != null && def.baseColorFactor.Length >= 3)
            {
                float a = def.baseColorFactor.Length >= 4 ? def.baseColorFactor[3] : 1f;
                mat.SetColor("_BaseColor", new Color(def.baseColorFactor[0], def.baseColorFactor[1],
                    def.baseColorFactor[2], a));
            }

            // Normal
            if (!string.IsNullOrEmpty(def.normalTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.normalTexture, isNormalMap: true);
                if (tex != null)
                {
                    mat.SetTexture("_BumpMap", tex);
                    mat.SetFloat("_BumpScale", 1.0f);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            // Metallic / Roughness (float values)
            mat.SetFloat("_Metallic", def.metallicFactor);
            mat.SetFloat("_Smoothness", 1.0f - def.roughnessFactor);

            // Occlusion
            if (!string.IsNullOrEmpty(def.occlusionTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.occlusionTexture, isSRGB: false);
                if (tex != null)
                {
                    mat.SetTexture("_OcclusionMap", tex);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                }
            }

            // Emission
            if (!string.IsNullOrEmpty(def.emissiveTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.emissiveTexture);
                if (tex != null)
                {
                    mat.SetTexture("_EmissionMap", tex);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.white);
                }
            }
            if (def.emissiveFactor != null && def.emissiveFactor.Length >= 3 &&
                (def.emissiveFactor[0] > 0 || def.emissiveFactor[1] > 0 || def.emissiveFactor[2] > 0))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(
                    def.emissiveFactor[0], def.emissiveFactor[1], def.emissiveFactor[2]));
            }
        }

        // ----------------------------------------------------------------
        // HDRP (HDRP/Lit)
        // ----------------------------------------------------------------

        private static void ApplyHDRPMaterial(Material mat, MaterialDef def, string modelDir)
        {
            // Base color
            if (!string.IsNullOrEmpty(def.baseColorTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.baseColorTexture);
                if (tex != null)
                    mat.SetTexture("_BaseColorMap", tex);
            }
            if (def.baseColorFactor != null && def.baseColorFactor.Length >= 3)
            {
                float a = def.baseColorFactor.Length >= 4 ? def.baseColorFactor[3] : 1f;
                mat.SetColor("_BaseColor", new Color(def.baseColorFactor[0], def.baseColorFactor[1],
                    def.baseColorFactor[2], a));
            }

            // Normal
            if (!string.IsNullOrEmpty(def.normalTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.normalTexture, isNormalMap: true);
                if (tex != null)
                {
                    mat.SetTexture("_NormalMap", tex);
                    mat.SetFloat("_NormalScale", 1.0f);
                }
            }

            // Metallic / Roughness (float values — MaskMap channel mismatch)
            mat.SetFloat("_Metallic", def.metallicFactor);
            mat.SetFloat("_Smoothness", 1.0f - def.roughnessFactor);

            // Emission
            if (!string.IsNullOrEmpty(def.emissiveTexture))
            {
                Texture2D tex = LoadTextureAsset(modelDir, def.emissiveTexture);
                if (tex != null)
                {
                    mat.SetTexture("_EmissiveColorMap", tex);
                    mat.SetColor("_EmissiveColor", Color.white);
                }
            }
            if (def.emissiveFactor != null && def.emissiveFactor.Length >= 3 &&
                (def.emissiveFactor[0] > 0 || def.emissiveFactor[1] > 0 || def.emissiveFactor[2] > 0))
            {
                mat.SetColor("_EmissiveColor", new Color(
                    def.emissiveFactor[0], def.emissiveFactor[1], def.emissiveFactor[2]));
            }
        }

        // ----------------------------------------------------------------
        // Texture helpers
        // ----------------------------------------------------------------

        private static Texture2D LoadTextureAsset(string modelDir, string textureFileName,
            bool isNormalMap = false, bool isSRGB = true)
        {
            string assetPath = Path.Combine(modelDir, textureFileName).Replace('\\', '/');

            // Ensure correct import settings before loading
            SetTextureImportSettings(assetPath, isNormalMap, isNormalMap ? false : isSRGB);

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null)
                Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Texture not found: {assetPath}");

            return tex;
        }

        private static void SetTextureImportSettings(string assetPath, bool isNormalMap, bool isSRGB)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool needsReimport = false;

            if (isNormalMap && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                needsReimport = true;
            }
            else if (!isNormalMap && importer.sRGBTexture != isSRGB)
            {
                importer.sRGBTexture = isSRGB;
                needsReimport = true;
            }

            if (needsReimport)
                importer.SaveAndReimport();
        }

        // ----------------------------------------------------------------
        // Render pipeline detection
        // ----------------------------------------------------------------

        public static RenderPipelineType DetectRenderPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                return RenderPipelineType.BuiltIn;

            string pipelineName = GraphicsSettings.currentRenderPipeline.GetType().Name;

            if (pipelineName.Contains("UniversalRenderPipelineAsset"))
                return RenderPipelineType.URP;
            if (pipelineName.Contains("HDRenderPipelineAsset"))
                return RenderPipelineType.HDRP;

            return RenderPipelineType.Unsupported;
        }

        // ----------------------------------------------------------------
        // Scene placement
        // ----------------------------------------------------------------

        private static void PlaceInScene(string fbxAssetPath, string assetName, Material[] materials)
        {
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                if (prefab == null)
                {
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} Failed to load prefab: {fbxAssetPath}");
                    return;
                }

                GameObject sceneObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (sceneObject == null)
                {
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} Failed to instantiate prefab");
                    return;
                }

                sceneObject.name = assetName;
                sceneObject.transform.position = Vector3.zero;
                sceneObject.transform.rotation = Quaternion.identity;

                // Assign metadata-based materials to all renderers
                if (materials != null && materials.Length > 0)
                {
                    Renderer[] renderers = sceneObject.GetComponentsInChildren<Renderer>();
                    foreach (Renderer renderer in renderers)
                    {
                        Material[] slots = new Material[renderer.sharedMaterials.Length];
                        for (int i = 0; i < slots.Length; i++)
                            slots[i] = materials[i < materials.Length ? i : 0];
                        renderer.sharedMaterials = slots;
                    }
                }

                // Stand on ground: adjust Y so bottom of bounds sits at Y=0
                Renderer[] allRenderers = sceneObject.GetComponentsInChildren<Renderer>();
                if (allRenderers.Length > 0)
                {
                    Bounds bounds = allRenderers[0].bounds;
                    for (int i = 1; i < allRenderers.Length; i++)
                        bounds.Encapsulate(allRenderers[i].bounds);

                    if (bounds.size != Vector3.zero)
                        sceneObject.transform.position = new Vector3(0, -bounds.min.y, 0);
                }

                // Setup animator if animation clips exist
                SetupAnimator(sceneObject, fbxAssetPath);

                // Select and focus
                Selection.activeGameObject = sceneObject;
                EditorGUIUtility.PingObject(sceneObject);

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    sceneObject.scene);
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} Scene placement failed: {e.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Animation setup
        // ----------------------------------------------------------------

        private static void SetupAnimator(GameObject sceneObject, string fbxAssetPath)
        {
            // Load all animation clips from the FBX
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
            AnimationClip[] clips = allAssets
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();

            if (clips.Length == 0) return;

            // Ensure Animator component exists
            Animator animator = sceneObject.GetComponent<Animator>();
            if (animator == null)
                animator = sceneObject.AddComponent<Animator>();

            if (clips.Length >= 1)
            {
                // Create AnimatorController
                string controllerDir = Path.GetDirectoryName(fbxAssetPath).Replace('\\', '/');
                string controllerPath = Path.Combine(controllerDir,
                    $"{sceneObject.name}_Controller.controller").Replace('\\', '/');

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
        }

        // ----------------------------------------------------------------
        // Cleanup
        // ----------------------------------------------------------------

        private static void Cleanup(string extractDir, string zipPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(extractDir) && Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Cleanup warning: {e.Message}");
            }

            try
            {
                if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Cleanup warning: {e.Message}");
            }
        }
    }
}
