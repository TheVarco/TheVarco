using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Builds the deterministic base of the sea/island environment outside the cave exit in
    /// MainScene_final: ocean surface, island terrain, seabed, and the headland mountains that form
    /// the cliff the exit mouth pierces. The purpose is a Cinemachine cutscene backdrop - the sub
    /// drives out of the 24x16 mouth, surfaces, and the beach is ahead - so placement is anchored to
    /// the measured exit pose, and the artistic polish (palms, beach props, light direction) is an
    /// explicit manual pass on top.
    ///
    /// Everything lives under a tool-owned root named "Exterior" whose children are deleted and
    /// recreated by name on every run, the same contract CaveBlockoutBuilder uses: re-running the
    /// builder is always safe, and the manual pass adds objects under separate, untouched children.
    ///
    /// Why the headland exists at all: the cave shell is single-sided, facing inward. From outside
    /// the mouth the cave is invisible - the mountains ARE the landmass the exit appears to be
    /// carved into.
    /// </summary>
    public static class ExteriorEnvironmentBuilder
    {
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const string RootName = "Exterior";

        private const string WaterPrefabPath =
            "Assets/ThirdParty/Uber Stylized Water/Prefabs/Water Template/Water Tempate Tropical.prefab";
        private const string SkyboxMaterialPath =
            "Assets/ThirdParty/Uber Stylized Water/Demo/Sky/SKy 22.mat";
        private const string IslandTerrainDataPath =
            "Assets/LowPolyTropicalEnvironment_LITE/Terrain/TE_Lite_Terrain.asset";
        private const string SandTexturePath =
            "Assets/ThirdParty/Uber Stylized Water/Demo/Terrain/sand_01_color.png";
        private const string GeneratedFolder = "Assets/Generated/Exterior";
        private const string IslandTerrainCopyPath = GeneratedFolder + "/IslandTerrain.asset";
        private const string SeabedMaterialPath = GeneratedFolder + "/ExteriorSeabed_Sand.mat";

        // Measured exit pose (route end at 579.56 m). The exit points 26.9 degrees upward, bearing
        // ~11 degrees east of +Z; Bearing is that direction flattened to the horizon.
        private static readonly Vector3 ExitPosition = new Vector3(111.19f, 260.00f, 424.07f);
        private static readonly Vector3 ExitDirection = new Vector3(0.1702f, 0.4517f, 0.8758f);
        private static readonly Vector3 Bearing = new Vector3(0.1907f, 0f, 0.9816f);
        private static readonly Vector3 Right = new Vector3(0.9816f, 0f, -0.1907f);

        /// <summary>
        /// Sea surface Y. Mouth centre is 260 and the shrunken mouth's half-height is 8, so the top
        /// of the opening is ~268: 273 keeps the exit fully submerged with 5 m of margin, and the sub
        /// (pitched 27 degrees up as it exits) breaks the surface roughly 29 m past the mouth.
        /// Must match UnderwaterZoneDirector.seaSurfaceY.
        /// </summary>
        public const float SeaLevel = 273f;

        [MenuItem("Tools/Exterior/외부 환경 구성 (MainScene_final)")]
        public static void BuildInteractive()
        {
            if (SceneManager().path != PlayScenePath)
            {
                Debug.LogError($"open {PlayScenePath} first - the exterior belongs to the play scene only");
                return;
            }
            Build();
            EditorSceneManager.MarkSceneDirty(SceneManager());
        }

        public static void BuildMainSceneFinalBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
                Build();

                // Two saves: the scene carries Fusion NetworkObjects whose SortKeys settle on the
                // second serialisation.
                EditorSceneManager.MarkSceneDirty(SceneManager());
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("save refused");
                EditorSceneManager.MarkSceneDirty(SceneManager());
                EditorSceneManager.SaveOpenScenes();

                Debug.Log("EXTERIOR_BUILD PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"EXTERIOR_BUILD FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        private static UnityEngine.SceneManagement.Scene SceneManager()
            => UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        private static void Build()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/Generated", "Exterior");

            GameObject root = GameObject.Find(RootName) ?? new GameObject(RootName);

            BuildSea(root.transform);
            BuildSeabed(root.transform);
            BuildIsland(root.transform);
            BuildHeadland(root.transform);
            ApplySkybox();
            RaiseFarClip();
        }

        private static Transform RecreateChild(Transform root, string name)
        {
            Transform existing = root.Find(name);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var child = new GameObject(name).transform;
            child.SetParent(root, false);
            return child;
        }

        // ---- sea ---------------------------------------------------------------------------------

        private static void BuildSea(Transform root)
        {
            Transform sea = RecreateChild(root, "Sea");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"no water prefab at {WaterPrefabPath}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sea);
            // Forward of the exit so 550 m of water still lies behind the mouth for
            // looking-back shots. The plane may extend over the cave freely: the highest rock
            // (the mouth top) is ~268, below SeaLevel.
            instance.transform.position = ExitPosition + Bearing * 250f + Vector3.up * (SeaLevel - ExitPosition.y);

            Bounds bounds = ComputeBounds(instance);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 0.01f)
            {
                float scale = 1600f / span;
                instance.transform.localScale = instance.transform.localScale * scale;
            }
            Debug.Log($"EXTERIOR sea: prefab span {span:0.#} m -> scaled x{1600f / Mathf.Max(0.01f, span):0.##}, " +
                      $"surface y={instance.transform.position.y:0.#}");
        }

        // ---- seabed ------------------------------------------------------------------------------

        private static void BuildSeabed(Transform root)
        {
            Transform group = RecreateChild(root, "Seabed");

            // Centred 420 m out along the bearing so the 800 m plane starts ~20 m beyond the mouth.
            // It must not slice through the cave: at y=252 it would cross the visible Z6 interior if
            // it extended back over the route (the tunnel around 540-579 m spans y 240-260).
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "SeabedPlane";
            plane.transform.SetParent(group, false);
            plane.transform.position = ExitPosition + Bearing * 420f + Vector3.up * (252f - ExitPosition.y);
            plane.transform.localScale = new Vector3(80f, 1f, 80f); // 10 m primitive -> 800 m

            var material = AssetDatabase.LoadAssetAtPath<Material>(SeabedMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var sand = AssetDatabase.LoadAssetAtPath<Texture2D>(SandTexturePath);
                if (sand != null)
                {
                    material.SetTexture("_BaseMap", sand);
                    material.SetTextureScale("_BaseMap", new Vector2(120f, 120f));
                }
                else
                {
                    material.SetColor("_BaseColor", new Color(0.76f, 0.70f, 0.50f));
                    Debug.LogWarning($"EXTERIOR seabed: no sand texture at {SandTexturePath}, flat colour used");
                }
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, SeabedMaterialPath);
            }
            plane.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---- island ------------------------------------------------------------------------------

        private static void BuildIsland(Transform root)
        {
            Transform group = RecreateChild(root, "Island");

            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(IslandTerrainCopyPath);
            if (data == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<TerrainData>(IslandTerrainDataPath);
                if (source == null)
                    throw new InvalidOperationException($"no island TerrainData at {IslandTerrainDataPath}");

                // A copy, not the imported asset: the size is enlarged below (user-approved "scale the
                // island up if it is too small"), and mutating the imported pack asset would bleed the
                // change into its demo scenes and future reimports.
                data = UnityEngine.Object.Instantiate(source);
                data.size = new Vector3(source.size.x * 2.5f, source.size.y, source.size.z * 2.5f);
                AssetDatabase.CreateAsset(data, IslandTerrainCopyPath);
                Debug.Log($"EXTERIOR island: terrain copy {source.size} -> {data.size}");
            }

            var terrainObject = new GameObject("IslandTerrain");
            terrainObject.transform.SetParent(group, false);
            Terrain terrain = terrainObject.AddComponent<Terrain>();
            // Appearance comes from the Sand_Lite/Grass_Lite terrain layers the TerrainData already
            // references; the default URP TerrainLit material template is the correct pairing.
            terrain.terrainData = data;
            TerrainCollider collider = terrainObject.AddComponent<TerrainCollider>();
            collider.terrainData = data;

            // Terrain pivots at its corner and cannot be rotated, so the placement is: centre the
            // terrain 190 m out along the bearing, then sink it until roughly a third of its height
            // range is submerged - the waterline crosses mid-slope, which is where a beach lives.
            // Fine placement (which side the demo island's sand actually faces) is the manual pass.
            Vector3 centre = ExitPosition + Bearing * 190f;
            terrainObject.transform.position = new Vector3(
                centre.x - data.size.x * 0.5f,
                SeaLevel - data.size.y * 0.35f,
                centre.z - data.size.z * 0.5f);

            Debug.Log($"EXTERIOR island: size={data.size} corner={terrainObject.transform.position} " +
                      $"centre=({centre.x:0.#},{centre.z:0.#}) waterline crosses at 35% of height range");
        }

        // ---- headland ----------------------------------------------------------------------------

        /// <summary>One mountain placement: offset from the exit mouth, and a target height in metres.</summary>
        private readonly struct Peak
        {
            public readonly string prefabName;
            public readonly Vector3 offset;
            public readonly float targetHeight;
            public readonly float yawDegrees;

            public Peak(string prefabName, Vector3 offset, float targetHeight, float yawDegrees)
            {
                this.prefabName = prefabName;
                this.offset = offset;
                this.targetHeight = targetHeight;
                this.yawDegrees = yawDegrees;
            }
        }

        private static void BuildHeadland(Transform root)
        {
            Transform group = RecreateChild(root, "Headland");

            // A ring of peaks around the mouth, breaking the surface so the exit reads as carved into
            // an islet rather than floating in open water. Offsets are in exit-local axes (Right /
            // Bearing), base depth ~30 m below the mouth so the peaks root on the seabed plane depth.
            Peak[] peaks =
            {
                new Peak("LPN_Mountain_01", -Right * 45f - Bearing * 15f + Vector3.down * 30f, 110f, 20f),
                new Peak("LPN_Mountain_02", Right * 45f - Bearing * 15f + Vector3.down * 30f, 100f, 200f),
                new Peak("LPN_Mountain_03", -Bearing * 45f + Vector3.down * 30f, 130f, 90f),
                new Peak("LPN_Mountain_04", -Right * 25f - Bearing * 38f + Vector3.down * 30f, 95f, 310f),
                new Peak("LPN_Large_Rocks_Update_3.3", Right * 22f + Bearing * 5f + Vector3.down * 24f, 45f, 140f),
                new Peak("LPN_Large_Rocks.003", -Right * 20f + Bearing * 8f + Vector3.down * 22f, 38f, 75f)
            };

            foreach (Peak peak in peaks)
            {
                GameObject prefab = FindPrefab(peak.prefabName);
                if (prefab == null)
                {
                    Debug.LogWarning($"EXTERIOR headland: prefab '{peak.prefabName}' not found, skipped");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                instance.transform.position = ExitPosition + peak.offset;
                instance.transform.rotation = Quaternion.Euler(0f, peak.yawDegrees, 0f);

                Bounds bounds = ComputeBounds(instance);
                if (bounds.size.y > 0.01f)
                    instance.transform.localScale =
                        instance.transform.localScale * (peak.targetHeight / bounds.size.y);

                Debug.Log($"EXTERIOR headland: {peak.prefabName} raw height {bounds.size.y:0.#} m " +
                          $"-> {peak.targetHeight} m at {instance.transform.position}");
            }
        }

        private static GameObject FindPrefab(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:prefab",
                         new[] { "Assets/Low-Poly Style Nature" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        // ---- scene settings ----------------------------------------------------------------------

        private static void ApplySkybox()
        {
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (skybox == null)
            {
                Debug.LogWarning($"EXTERIOR: no skybox material at {SkyboxMaterialPath}");
                return;
            }
            RenderSettings.skybox = skybox;
        }

        /// <summary>
        /// 1000 -> 2000. The island centre sits ~190 m past the exit, but the far shore of a 750 m
        /// terrain seen from inside Z6 is over a kilometre away. This scene YAML value is the single
        /// authoring point: CaveBlockoutBuilder.CreatePlaytest only touches far clip on a from-scratch
        /// build, never on RegenerateCurrentScene.
        /// </summary>
        private static void RaiseFarClip()
        {
            Camera camera = UnityEngine.Object
                .FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.CompareTag("MainCamera")) ??
                UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (camera == null)
            {
                Debug.LogWarning("EXTERIOR: no camera found for the far-clip raise");
                return;
            }
            if (camera.farClipPlane < 2000f)
            {
                camera.farClipPlane = 2000f;
                EditorUtility.SetDirty(camera);
            }
        }

        private static Bounds ComputeBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
