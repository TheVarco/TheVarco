using System.IO;
using CaveBlockout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Builds the underwater atmosphere for the active scene: generated assets, a scene root holding
    /// the global volume / zone director / particle emitter, camera and light configuration, and the
    /// project renderer's full-screen pass.
    ///
    /// Idempotent - every object and asset is looked up by name or path and reused, so running it
    /// twice does not duplicate anything.
    /// </summary>
    public static class UnderwaterEnvironmentBuilder
    {
        public const string RootName = "UnderwaterEnvironment";

        private const string MaterialsFolder = "Assets/04.Materials/Underwater";
        private const string SettingsFolder = "Assets/Settings/Underwater";
        private const string ZoneSetPath = SettingsFolder + "/MainMapUnderwaterZones.asset";
        private const string VolumeProfilePath = SettingsFolder + "/MainMapUnderwaterProfile.asset";
        private const string FullScreenMaterialPath = MaterialsFolder + "/UnderwaterFullScreen.mat";
        private const string CausticsMaterialPath = MaterialsFolder + "/UnderwaterCaustics.mat";
        private const string MotesMaterialPath = MaterialsFolder + "/UnderwaterMotes.mat";
        private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";

        private const string FullScreenShaderName = "Varco/Underwater/FullScreen";
        private const string CausticsShaderName = "Varco/Underwater/Caustics";
        private const string MotesShaderName = "Varco/Underwater/Motes";

        private const string VolumeObjectName = "Underwater Volume";
        private const string DirectorObjectName = "Underwater Zone Director";
        private const string ParticlesObjectName = "Suspended Particles";

        [MenuItem("Tools/Underwater Cave/Apply Underwater Environment")]
        public static void ApplyInteractive()
        {
            Apply(true);
            Debug.Log("UNDERWATER_ENVIRONMENT_APPLIED: zone director, volume, particles, camera, light and screen pass are configured.");
        }

        [MenuItem("Tools/Underwater Cave/Remove Underwater Environment")]
        public static void RemoveInteractive()
        {
            Remove(true);
            Debug.Log("UNDERWATER_ENVIRONMENT_REMOVED: scene root and renderer feature removed, fog and ambient restored.");
        }

        public static void Apply(bool saveScene)
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(SettingsFolder);

            UnderwaterZoneSet zoneSet = EnsureZoneSet();
            Material fullScreenMaterial = EnsureMaterial(FullScreenMaterialPath, FullScreenShaderName);
            EnsureCausticsMaterial();
            Material motesMaterial = EnsureMotesMaterial();
            VolumeProfile volumeProfile = EnsureVolumeProfile(zoneSet);

            EnsureRendererFeature(fullScreenMaterial);

            Scene scene = SceneManager.GetActiveScene();
            Transform root = EnsureRoot(scene);

            // Snapshot before anything is overwritten, and only on the first Apply, so repeated builds
            // keep pointing at the genuine pre-underwater state.
            var snapshot = root.GetComponent<UnderwaterEnvironmentSnapshot>()
                           ?? root.gameObject.AddComponent<UnderwaterEnvironmentSnapshot>();
            snapshot.CaptureIfNeeded(FindMainCamera(), FindDirectionalLight(), FindCaveShell());

            Volume volume = EnsureVolume(root, volumeProfile);
            UnderwaterZoneDirector director = EnsureDirector(root, zoneSet, volume);
            EnsureParticles(root, director, motesMaterial);

            ConfigureCamera();
            ConfigureDirectionalLight(zoneSet);
            ConfigureCaveShellShadows();
            BakeBaseRenderSettings(zoneSet);

            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        public static void Remove(bool saveScene)
        {
            Scene scene = SceneManager.GetActiveScene();

            GameObject root = FindSceneRoot(scene, RootName);
            if (root != null)
            {
                UnderwaterZoneDirector director = root.GetComponentInChildren<UnderwaterZoneDirector>(true);
                if (director != null)
                {
                    director.ClearUnderwaterEffect();
                    director.enabled = false;
                }

                // Put fog, ambient, the camera and the directional light back to the values recorded on
                // the first Apply, rather than assuming Unity's defaults were what the scene had.
                var snapshot = root.GetComponent<UnderwaterEnvironmentSnapshot>();
                if (snapshot != null && snapshot.Captured)
                    snapshot.Restore();
                else
                    Debug.LogWarning("No underwater snapshot was found, so fog, ambient, camera and light " +
                                     "settings were left as-is. Adjust them by hand if needed.");

                Object.DestroyImmediate(root);
            }

            Shader.SetGlobalFloat(Shader.PropertyToID("_Underwater_Strength"), 0f);
            RemoveRendererFeature();

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        // ----------------------------------------------------------------------------------------
        // Generated assets
        // ----------------------------------------------------------------------------------------

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        private static UnderwaterZoneSet EnsureZoneSet()
        {
            UnderwaterZoneSet zoneSet = AssetDatabase.LoadAssetAtPath<UnderwaterZoneSet>(ZoneSetPath);
            if (zoneSet != null)
            {
                // An asset written by an older data version has fields that still exist but no longer
                // mean the same thing, so it is regenerated rather than left half-migrated.
                if (zoneSet.NeedsRegeneration)
                {
                    zoneSet.ResetToGuideDefaults();
                    EditorUtility.SetDirty(zoneSet);
                    AssetDatabase.SaveAssetIfDirty(zoneSet);
                    Debug.Log("Underwater zone set was regenerated to the current data version and saved.");
                }
                return zoneSet;
            }

            zoneSet = ScriptableObject.CreateInstance<UnderwaterZoneSet>();
            zoneSet.ResetToGuideDefaults();
            AssetDatabase.CreateAsset(zoneSet, ZoneSetPath);
            return zoneSet;
        }

        private static Material EnsureMaterial(string path, string shaderName)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException(
                    $"Shader \"{shaderName}\" was not found. Let Unity finish importing " +
                    $"{MaterialsFolder} before running this tool.");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static void EnsureCausticsMaterial()
        {
            Material material = EnsureMaterial(CausticsMaterialPath, CausticsShaderName);
            material.SetColor("_Color", new Color(0.35f, 1f, 0.78f, 0.14f));
            material.SetFloat("_Scale", 1.8f);
            material.SetFloat("_Speed", 0.32f);
            material.SetFloat("_Strength", 0.18f);
            material.SetFloat("_FollowZone", 1f);
            EditorUtility.SetDirty(material);
        }

        private static Material EnsureMotesMaterial()
        {
            Material material = EnsureMaterial(MotesMaterialPath, MotesShaderName);
            material.SetColor("_BaseColor", new Color(0.68f, 0.90f, 1f, 0.40f));
            material.SetFloat("_SoftEdge", 2.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static VolumeProfile EnsureVolumeProfile(UnderwaterZoneSet zoneSet)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            // The director writes .value every frame, so every parameter it touches must have its
            // override state on. Add() + Override() below is what turns those on.
            UnderwaterZoneProfile baseline = zoneSet.Zones.Count > 0 ? zoneSet.Zones[0] : zoneSet.Fallback;

            ColorAdjustments colorAdjustments = GetOrAdd<ColorAdjustments>(profile);
            colorAdjustments.postExposure.Override(baseline.postExposure);
            colorAdjustments.contrast.Override(baseline.contrast);
            colorAdjustments.saturation.Override(baseline.saturation);
            colorAdjustments.colorFilter.Override(baseline.colorFilter);

            LiftGammaGain liftGammaGain = GetOrAdd<LiftGammaGain>(profile);
            liftGammaGain.lift.Override(new Vector4(-0.018f, 0.012f, 0.030f, 0f));
            liftGammaGain.gamma.Override(new Vector4(-0.012f, 0.020f, 0.042f, 0f));
            liftGammaGain.gain.Override(new Vector4(0.020f, 0.045f, 0.060f, 0f));

            WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
            whiteBalance.temperature.Override(baseline.whiteBalanceTemperature);
            whiteBalance.tint.Override(baseline.whiteBalanceTint);

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.Override(baseline.bloomIntensity);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.62f);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(baseline.vignetteIntensity);
            vignette.smoothness.Override(0.78f);
            vignette.color.Override(new Color(0.004f, 0.030f, 0.055f));

            DepthOfField depthOfField = GetOrAdd<DepthOfField>(profile);
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);

            // Baked out past the far end of the map, not at the baseline zone's visibility.
            //
            // The director rewrites both distances from the resolved profile every LateUpdate, so these
            // numbers only ever describe the editor. Baking Z1's values instead - start 6 m, end 18 m -
            // puts maximum gaussian blur on everything past eighteen metres of a five-hundred-metre
            // cave, which is the entire scene view: rock silhouettes go mushy and prop edges soften,
            // and judging placement against that is not possible.
            //
            // Overriding the distances rather than clearing `active` is deliberate. ApplyPostProcessing
            // assigns .value and never touches .active, so an inactive component here would disable
            // depth of field in the shipping look too.
            depthOfField.gaussianStart.Override(EditorDepthOfFieldStartMeters);
            depthOfField.gaussianEnd.Override(EditorDepthOfFieldEndMeters);
            depthOfField.gaussianMaxRadius.Override(0.7f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// Adds a volume component and parents it to the profile asset so it actually survives a save.
        ///
        /// VolumeProfile.Add creates a loose ScriptableObject and registers it in the profile's list;
        /// nothing owns it on disk. Unity's own VolumeProfile inspector is what normally calls
        /// AddObjectToAsset, so a profile built from script serialises with the right number of entries
        /// and every one of them null. MainMapUnderwaterProfile.asset was in exactly that state - 610
        /// bytes, "components:" holding seven {fileID: 0} - which meant every TryGet in
        /// UnderwaterZoneDirector.ApplyPostProcessing failed and the whole post-processing block was a
        /// no-op from the day it was written.
        /// </summary>
        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            // A profile that was saved in the broken state comes back with null entries. TryGet walks
            // that list, so they have to go before anything is looked up or added.
            profile.components.RemoveAll(component => component == null);

            if (profile.TryGet(out T existing))
                return existing;

            T added = profile.Add<T>();
            added.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        // ----------------------------------------------------------------------------------------
        // Renderer feature
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Adds the underwater full-screen pass to PC_Renderer. Mirrors URP's own AddComponent flow:
        /// the feature is stored as a sub-asset and its local file id is recorded in
        /// m_RendererFeatureMap, without which the reference is dropped on the next reimport.
        ///
        /// Mobile_Renderer is intentionally left alone: Mobile_RPAsset has depth and opaque textures
        /// disabled, and fog / ambient / post-processing already carry the look there.
        /// </summary>
        private static void EnsureRendererFeature(Material passMaterial)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (rendererData == null)
            {
                Debug.LogWarning($"{PcRendererPath} was not found, so the underwater screen pass was not added. " +
                                 "Fog, ambient and post-processing still apply.");
                return;
            }

            foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            {
                if (existing is not FullScreenPassRendererFeature fullScreenExisting)
                    continue;
                if (fullScreenExisting.passMaterial != passMaterial && fullScreenExisting.name != nameof(FullScreenPassRendererFeature) + "_Underwater")
                    continue;

                ConfigureFullScreenFeature(fullScreenExisting, passMaterial);
                SyncFeatureMap(rendererData);
                EditorUtility.SetDirty(rendererData);
                rendererData.SetDirty();
                return;
            }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = nameof(FullScreenPassRendererFeature) + "_Underwater";
            ConfigureFullScreenFeature(feature, passMaterial);
            AssetDatabase.AddObjectToAsset(feature, rendererData);

            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            serialized.ApplyModifiedProperties();

            SyncFeatureMap(rendererData);

            EditorUtility.SetDirty(rendererData);
            rendererData.SetDirty();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Rebuilds m_RendererFeatureMap from scratch so it always has exactly one local file id per
        /// entry in m_RendererFeatures.
        ///
        /// Appending a single entry to each list is not safe: the incoming asset may already be out of
        /// sync - PC_Renderer arrived from main with two features but a one-entry map - and appending
        /// carries that mismatch forward. URP treats a count mismatch as an invalid map and falls back
        /// to re-linking features by scanning sub-assets, which can silently drop a feature reference on
        /// reimport.
        /// </summary>
        private static void SyncFeatureMap(UniversalRendererData rendererData)
        {
            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");

            featureMap.arraySize = features.arraySize;
            for (int i = 0; i < features.arraySize; i++)
            {
                Object candidate = features.GetArrayElementAtIndex(i).objectReferenceValue;
                long localId = 0L;
                if (candidate != null)
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string _, out localId);
                featureMap.GetArrayElementAtIndex(i).longValue = localId;
            }

            serialized.ApplyModifiedProperties();
        }

        private static void ConfigureFullScreenFeature(FullScreenPassRendererFeature feature, Material passMaterial)
        {
            feature.passMaterial = passMaterial;
            // Before post-processing so tonemapping and bloom operate on already-fogged colour.
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            // Depth only. fetchColorBuffer is what binds the active colour target as _BlitTexture,
            // so requesting ScriptableRenderPassInput.Color as well would copy a second time for nothing.
            feature.requirements = ScriptableRenderPassInput.Depth;
            feature.fetchColorBuffer = true;
            feature.bindDepthStencilAttachment = false;
            feature.SetActive(true);
        }

        private static void RemoveRendererFeature()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (rendererData == null)
                return;

            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serialized.FindProperty("m_RendererFeatureMap");

            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                Object candidate = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (candidate is not FullScreenPassRendererFeature fullScreen)
                    continue;
                if (fullScreen.passMaterial == null ||
                    AssetDatabase.GetAssetPath(fullScreen.passMaterial) != FullScreenMaterialPath)
                    continue;

                features.DeleteArrayElementAtIndex(i);
                if (i < featureMap.arraySize)
                    featureMap.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedProperties();
                AssetDatabase.RemoveObjectFromAsset(fullScreen);
                Object.DestroyImmediate(fullScreen, true);
                serialized = new SerializedObject(rendererData);
                features = serialized.FindProperty("m_RendererFeatures");
                featureMap = serialized.FindProperty("m_RendererFeatureMap");
            }

            EditorUtility.SetDirty(rendererData);
            rendererData.SetDirty();
        }

        // ----------------------------------------------------------------------------------------
        // Scene objects
        // ----------------------------------------------------------------------------------------

        private static GameObject FindSceneRoot(Scene scene, string name)
        {
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == name)
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// The root is created as a sibling of CaveBlockout, not a child. CaveBlockoutBuilder deletes
        /// the Routes / Generated / Markers / Validation / Playtest groups under that root when the
        /// blockout is regenerated, and a child here would be at risk of the same treatment.
        /// </summary>
        private static Transform EnsureRoot(Scene scene)
        {
            GameObject root = FindSceneRoot(scene, RootName);
            if (root == null)
                root = new GameObject(RootName);

            root.transform.SetParent(null, true);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root.transform;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        private static Volume EnsureVolume(Transform root, VolumeProfile profile)
        {
            Transform volumeTransform = EnsureChild(root, VolumeObjectName);
            Volume volume = volumeTransform.GetComponent<Volume>() ?? volumeTransform.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 1f;

            // sharedProfile, not profile. Volume.profile is the *instance* accessor: its setter writes
            // m_InternalProfile, a runtime-only clone that is never serialised, so assigning through it
            // from an editor tool leaves the saved scene with "sharedProfile: {fileID: 0}". The getter
            // then hands UnderwaterZoneDirector a freshly created empty profile, every TryGet in
            // ApplyPostProcessing misses, and no post-processing has ever been applied to this scene.
            volume.sharedProfile = profile;

            // MainMap's camera has m_VolumeLayerMask set to layer 0 only, so the volume must sit there.
            volumeTransform.gameObject.layer = 0;
            return volume;
        }

        private static UnderwaterZoneDirector EnsureDirector(Transform root, UnderwaterZoneSet zoneSet, Volume volume)
        {
            Transform directorTransform = EnsureChild(root, DirectorObjectName);
            UnderwaterZoneDirector director = directorTransform.GetComponent<UnderwaterZoneDirector>()
                                             ?? directorTransform.gameObject.AddComponent<UnderwaterZoneDirector>();

            CaveRoute mainRoute = FindMainRoute();
            Camera camera = FindMainCamera();

            var serialized = new SerializedObject(director);
            serialized.FindProperty("zoneSet").objectReferenceValue = zoneSet;
            serialized.FindProperty("globalVolume").objectReferenceValue = volume;
            serialized.FindProperty("mainRoute").objectReferenceValue = mainRoute;
            serialized.FindProperty("trackedCamera").objectReferenceValue = camera;
            serialized.FindProperty("trackedTransform").objectReferenceValue = camera != null ? camera.transform : null;
            serialized.FindProperty("directionalLight").objectReferenceValue = FindDirectionalLight();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (mainRoute == null)
                Debug.LogWarning("No main CaveRoute found in the scene. The zone director will fall back to " +
                                 "CaveZoneMarker bounds, which is less precise at zone boundaries.");

            return director;
        }

        private static void EnsureParticles(Transform root, UnderwaterZoneDirector director, Material motesMaterial)
        {
            Transform particleTransform = EnsureChild(root, ParticlesObjectName);
            var system = particleTransform.GetComponent<ParticleSystem>();
            if (system == null)
                system = particleTransform.gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.maxParticles = 900;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 9.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.07f);
            // Kept small deliberately: the emitter box is centred just ahead of the lens, so anything
            // larger reads as out-of-focus blobs across the frame rather than suspended sediment.
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.030f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.85f, 1f, 0.10f),
                new Color(0.90f, 1f, 1f, 0.42f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 60f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.scale = new Vector3(32f, 21f, 32f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.045f, 0.060f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.004f, 0.030f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.030f, 0.030f);

            var renderer = particleTransform.GetComponent<ParticleSystemRenderer>()
                           ?? particleTransform.gameObject.AddComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = motesMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 0f;

            var follower = particleTransform.GetComponent<UnderwaterParticleFollower>()
                           ?? particleTransform.gameObject.AddComponent<UnderwaterParticleFollower>();

            Camera camera = FindMainCamera();
            var serialized = new SerializedObject(follower);
            serialized.FindProperty("director").objectReferenceValue = director;
            serialized.FindProperty("followTarget").objectReferenceValue = camera != null ? camera.transform : null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ----------------------------------------------------------------------------------------
        // Existing scene objects
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Internal rather than private so <see cref="UnderwaterBatch.FixPlaySceneCamera"/> can apply
        /// just this one step to MainScene_final without running the whole builder.
        ///
        /// The full Apply() has only ever run on MainMap, so what it would rewrite in the play scene is
        /// unknown; the camera settings below are the one part that scene is demonstrably missing.
        /// </summary>
        internal static void ConfigureCamera()
        {
            Camera camera = FindMainCamera();
            if (camera == null)
            {
                Debug.LogWarning("No main camera found, so post-processing was not enabled. " +
                                 "The underwater volume will have no effect until a camera renders it.");
                return;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>()
                             ?? camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            // MainMap shipped with renderPostProcessing off, which silently disabled the whole volume.
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.requiresDepthOption = CameraOverrideOption.On;
            cameraData.dithering = true;

            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraData);
        }

        internal static Light FindDirectionalLight()
        {
            foreach (Light candidate in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.type == LightType.Directional)
                    return candidate;
            }
            return null;
        }

        private static void ConfigureDirectionalLight(UnderwaterZoneSet zoneSet)
        {
            Light directional = FindDirectionalLight();
            if (directional == null)
                return;

            UnderwaterZoneProfile baseline = zoneSet.Zones.Count > 0 ? zoneSet.Zones[0] : zoneSet.Fallback;

            // Filtered blue-white surface light instead of the warm terrestrial default. Intensity is
            // the Z1 baseline; the director takes over per zone at runtime.
            directional.color = new Color(0.62f, 0.82f, 1f);
            directional.intensity = baseline.directionalIntensity;
            directional.shadows = LightShadows.Soft;
            directional.shadowStrength = 0.72f;
            directional.bounceIntensity = 0.35f;
            RenderSettings.sun = directional;

            EditorUtility.SetDirty(directional);
        }

        /// <summary>
        /// The cave shell is a closed tube. Left casting shadows it seals the directional light out of
        /// the entire interior, and with god rays and local lights out of scope there is nothing left to
        /// light the rock - measured as a fully black interior. Decor keeps casting shadows.
        /// </summary>
        private static void ConfigureCaveShellShadows()
        {
            MeshRenderer shell = FindCaveShell();
            if (shell == null)
                return;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            EditorUtility.SetDirty(shell);
        }

        internal static MeshRenderer FindCaveShell()
        {
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.name == "CaveShell")
                    return renderer;
            }
            return null;
        }

        /// <summary>
        /// Writes the first zone's values into the scene's RenderSettings so the Scene view reads as
        /// underwater without entering play mode. The director takes over at runtime.
        /// </summary>
        private static void BakeBaseRenderSettings(UnderwaterZoneSet zoneSet)
        {
            UnderwaterZoneProfile baseline = zoneSet.Zones.Count > 0 ? zoneSet.Zones[0] : zoneSet.Fallback;

            // Fog off, and the values behind it left correct anyway.
            //
            // RenderSettings fog is the *fallback* path: UnderwaterZoneDirector.Apply only drives it
            // when the screen pass is inactive, and every zone in the set - all six plus the fallback -
            // is authored with screenStrength 1, so the running game always takes the other branch and
            // sets fog = false itself. Baking it on therefore never matches the shipping look; all it
            // does is put a 12 m exponential-squared fog over the scene view, on top of an ambient that
            // is already being cut for the editor's benefit, and drag the whole viewport to black.
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = baseline.fogColor;
            RenderSettings.fogDensity = baseline.FogDensity;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ScaleRgb(baseline.ambientSky, EditorAmbientScale);
            RenderSettings.ambientEquatorColor = ScaleRgb(baseline.ambientEquator, EditorAmbientScale);
            RenderSettings.ambientGroundColor = ScaleRgb(baseline.ambientGround, EditorAmbientScale);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.subtractiveShadowColor = new Color(0.008f, 0.035f, 0.060f);
        }

        /// <summary>
        /// Divisor applied to the baseline zone's ambient before it is baked into the scene.
        ///
        /// Zone ambient is authored pre-compensated for the screen-space extinction pass: Z1 asks for a
        /// sky colour of (1.61, 9.06, 9.96), roughly forty times a conventional scene, because
        /// UnderwaterFullScreen eats most of it back out at 12 m visibility. That pass is gated on
        /// _Underwater_Strength, which only a *playing* director raises - UnderwaterZoneDirector.LateUpdate
        /// returns immediately in edit mode by design, and Remove() zeroes the global. So the scene view
        /// gets the forty-times ambient with none of the extinction, every surface clips well past 1.0,
        /// and the whole viewport sits on the clipping threshold: a pixel of camera movement flips large
        /// areas at once, which is what reads as flicker while editing.
        ///
        /// Scaling only the *baked* copy costs nothing at runtime. The director overwrites all three
        /// ambient colours from the resolved profile every LateUpdate, so this value is visible in the
        /// editor and for the single frame between scene load and the director's first update.
        /// </summary>
        /// Sized against what actually clips, which is ambient x albedo rather than the ambient value.
        /// Z1's sky blue is 9.96; at 1/8 that is 1.25, so the palest rock in the library (albedo ~0.7)
        /// lands at 0.87 and nothing hard-clips, while mid-grey rock sits around 0.3 and stays readable.
        /// Two earlier passes bracket it: 1/20 with the fog still baked on was unreadably dark, and 1/12
        /// was still dimmer than it needed to be.
        private const float EditorAmbientScale = 1f / 8f;

        /// Past the far corner of a 580 m route, so nothing in the scene view falls inside the blur.
        private const float EditorDepthOfFieldStartMeters = 600f;
        private const float EditorDepthOfFieldEndMeters = 900f;

        private static Color ScaleRgb(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        internal static Camera FindMainCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            foreach (Camera candidate in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.cameraType == CameraType.Game)
                    return candidate;
            }
            return null;
        }

        internal static CaveRoute FindMainRoute()
        {
            foreach (CaveRoute route in Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (CaveRouteSplineDefinition definition in route.Definitions)
                {
                    if (definition.isMainRoute)
                        return route;
                }
            }
            return null;
        }
    }
}
