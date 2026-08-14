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
            "Assets/ThirdParty/Uber Stylized Water/Demo/Terrain/sand_01_color_2k.png";
        private const string BeachTerrainLayerPath =
            "Assets/ThirdParty/Uber Stylized Water/Demo/Terrain/Sand.terrainlayer";
        private const string GeneratedFolder = "Assets/Generated/Exterior";
        private const string IslandTerrainCopyPath = GeneratedFolder + "/IslandTerrain.asset";
        private const string SeabedMaterialPath = GeneratedFolder + "/ExteriorSeabed_Sand.mat";
        private const string TerrainMaterialPath = GeneratedFolder + "/ExteriorIsland_TerrainLit.mat";

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

        /// <summary>Footprint multiplier on the pack's 50 x 50 m island - user-approved upscaling.</summary>
        private const float FootprintScale = 4f;

        /// <summary>Fraction of the island footprint that should end up above water.</summary>
        private const float IslandDryFraction = 0.35f;

        /// <summary>Metres from waterline to summit once the height range is stretched.</summary>
        private const float IslandReliefMeters = 28f;

        /// <summary>
        /// Distance from the exit to the island centre, along the horizontal bearing. Internal
        /// because the review capture frames its island shots from the same number - two copies
        /// drifted apart once already.
        /// </summary>
        internal const float IslandDistanceMeters = 190f;

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
            // After the island: props are seated by sampling the terrain that BuildIsland creates.
            BuildBeachProps(root.transform);
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
            instance.transform.position = new Vector3(ExitPosition.x, SeaLevel, ExitPosition.z);

            // Must out-reach the seabed plane (800 m centred 420 m out) from every angle, or its edge
            // shows as a hard line with bare seabed beyond it.
            const float TargetSpan = 2400f;
            Bounds bounds = ComputeBounds(instance);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 0.01f)
                instance.transform.localScale = instance.transform.localScale * (TargetSpan / span);

            // The water prefab pivots at a CORNER of its mesh, not the centre: positioning it directly
            // put the whole 2.4 km sheet behind and to the left of the exit, leaving the island's far
            // half in open air. Re-seat from the measured bounds instead of trusting the pivot.
            Vector3 wantedCentre = ExitPosition + Bearing * 250f;
            Bounds placed = ComputeBounds(instance);
            instance.transform.position += new Vector3(
                wantedCentre.x - placed.center.x, 0f, wantedCentre.z - placed.center.z);

            Bounds scaledBounds = ComputeBounds(instance);
            Debug.Log($"EXTERIOR sea: prefab span {span:0.#} m -> scaled x{TargetSpan / Mathf.Max(0.01f, span):0.##}, " +
                      $"actual span {scaledBounds.size.x:0.#} x {scaledBounds.size.z:0.#} m, " +
                      $"x[{scaledBounds.min.x:0.#}..{scaledBounds.max.x:0.#}] " +
                      $"z[{scaledBounds.min.z:0.#}..{scaledBounds.max.z:0.#}], " +
                      $"surface y={instance.transform.position.y:0.#}");

            // Without the planar reflection volume the water shader samples nothing and reads as a
            // flat dark sheet from above; with it, sky and headland reflect and the surface reads
            // as water.
            var reflectionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThirdParty/Uber Stylized Water/Prefabs/Planner Reflection/PlannerReflectionVolume.prefab");
            if (reflectionPrefab != null)
            {
                var reflection = (GameObject)PrefabUtility.InstantiatePrefab(reflectionPrefab, sea);
                reflection.transform.position = instance.transform.position;
            }
            else
            {
                Debug.LogWarning("EXTERIOR sea: PlannerReflectionVolume prefab missing, water will read flat");
            }
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

            var source = AssetDatabase.LoadAssetAtPath<TerrainData>(IslandTerrainDataPath);
            if (source == null)
                throw new InvalidOperationException($"no island TerrainData at {IslandTerrainDataPath}");

            // A copy, not the imported asset: the size is enlarged below (user-approved "scale the
            // island up if it is too small"), and mutating the imported pack asset would bleed the
            // change into its demo scenes and future reimports. Recreated every run so multiplier
            // changes take effect - a stale cached copy cost one full iteration.
            AssetDatabase.DeleteAsset(IslandTerrainCopyPath);
            TerrainData data = UnityEngine.Object.Instantiate(source);

            // Height and waterline are SOLVED from the heightmap, not chosen. The pack's island has
            // roughly 5.6 m of real relief inside a 600 m nominal range, so any hand-picked "sink it
            // by N metres" is an order of magnitude off - the first two attempts drowned it by 190 m
            // and then left 0.6 m of sand showing. Instead: pick the height at which the wanted
            // fraction of the footprint is dry, then scale the range so the dry part stands
            // IslandReliefMeters tall.
            SolveIslandHeights(data, out float waterlineNormalised, out float verticalScale);
            data.size = new Vector3(source.size.x * FootprintScale, source.size.y * verticalScale,
                source.size.z * FootprintScale);

            // The baked detail (grass) prototypes use the legacy terrain-detail shaders URP does not
            // ship - they rendered as a magenta ribbon along the shoreline. Trees go with them:
            // palms are hand-placed in the polish pass, where they can face the camera line anyway.
            data.treeInstances = Array.Empty<TreeInstance>();
            data.treePrototypes = Array.Empty<TreePrototype>();
            data.detailPrototypes = Array.Empty<DetailPrototype>();

            // The pack's own Sand_Lite / Grass_Lite layers render WHITE under URP. Their diffuse
            // textures are near-white greyscale and the actual colour lives entirely in
            // m_DiffuseRemapMax (sand is 1, 0.86, 0.36; grass 0.37, 0.70, 0.10) - a TerrainLayer field
            // only HDRP's terrain shader consumes. URP ignores it and draws the raw greyscale, which is
            // why the island read as snow. This was NOT an exposure problem: the review capture
            // measures 0.0% clipped pixels, so darkening the exterior would only have dimmed the sky
            // and left the island just as colourless.
            //
            // The water pack's Sand layer has a neutral remap and a genuinely sand-coloured albedo, and
            // it is the same texture the seabed material uses - so the beach and the seabed match where
            // they meet at the waterline.
            var sandLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(BeachTerrainLayerPath);
            if (sandLayer != null)
                data.terrainLayers = new[] { sandLayer };
            else
                Debug.LogWarning($"EXTERIOR island: no TerrainLayer at {BeachTerrainLayerPath}; the " +
                                 "island keeps the pack's HDRP-remapped layers and will render white");

            AssetDatabase.CreateAsset(data, IslandTerrainCopyPath);
            Debug.Log($"EXTERIOR island: terrain copy {source.size} -> {data.size} " +
                      $"(vertical x{verticalScale:0.#}), details/trees stripped");

            var terrainObject = new GameObject("IslandTerrain");
            terrainObject.transform.SetParent(group, false);
            Terrain terrain = terrainObject.AddComponent<Terrain>();
            terrain.terrainData = data;

            // A Terrain added from script has no materialTemplate and falls back to the built-in
            // terrain shader, which under URP renders magenta. The pack's own material is a mesh
            // material and cannot drive a Terrain either - it has to be a TerrainLit.
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null)
            {
                var terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
                if (terrainMaterial == null)
                {
                    terrainMaterial = new Material(terrainShader);
                    AssetDatabase.CreateAsset(terrainMaterial, TerrainMaterialPath);
                }
                terrain.materialTemplate = terrainMaterial;
            }
            else
            {
                Debug.LogWarning("EXTERIOR island: URP TerrainLit shader not found, terrain will be magenta");
            }
            TerrainCollider collider = terrainObject.AddComponent<TerrainCollider>();
            collider.terrainData = data;

            // Terrain pivots at its corner and cannot be rotated, so the horizontal placement centres
            // the footprint on the bearing; the vertical drop comes from the solved waterline.
            Vector3 centre = ExitPosition + Bearing * IslandDistanceMeters;
            terrainObject.transform.position = new Vector3(
                centre.x - data.size.x * 0.5f,
                SeaLevel - waterlineNormalised * data.size.y,
                centre.z - data.size.z * 0.5f);

            Debug.Log($"EXTERIOR island: size={data.size} corner={terrainObject.transform.position} " +
                      $"centre=({centre.x:0.#},{centre.z:0.#})");
            ReportIslandProfile(terrain, data);
        }

        /// <summary>
        /// Solves the two island unknowns from the heightmap itself.
        ///
        /// <paramref name="waterlineNormalised"/> is the normalised height (0-1, the units
        /// TerrainData stores) that should sit exactly at sea level, chosen so
        /// <see cref="IslandDryFraction"/> of the footprint ends up dry.
        /// <paramref name="verticalScale"/> then stretches size.y so the dry part stands
        /// <see cref="IslandReliefMeters"/> tall.
        /// </summary>
        private static void SolveIslandHeights(TerrainData data, out float waterlineNormalised,
            out float verticalScale)
        {
            int resolution = data.heightmapResolution;
            float[,] map = data.GetHeights(0, 0, resolution, resolution);

            var heights = new System.Collections.Generic.List<float>(resolution * resolution);
            foreach (float height in map)
                heights.Add(height);
            heights.Sort();

            // The value with IslandDryFraction of the map above it.
            int index = Mathf.Clamp(
                Mathf.RoundToInt((1f - IslandDryFraction) * (heights.Count - 1)), 0, heights.Count - 1);
            waterlineNormalised = heights[index];
            float peak = heights[heights.Count - 1];

            float dryRange = peak - waterlineNormalised;
            verticalScale = dryRange > 1e-6f
                ? IslandReliefMeters / (dryRange * data.size.y)
                : 1f;

            Debug.Log($"EXTERIOR island solve: peak={peak:0.#####} waterline={waterlineNormalised:0.#####} " +
                      $"(dry range {dryRange * data.size.y:0.##} m at source scale) -> vertical x{verticalScale:0.#}");
        }

        /// <summary>
        /// How much of the island actually clears the water, measured rather than assumed - the check
        /// on <see cref="SolveIslandHeights"/> rather than a restatement of it, because it samples the
        /// built Terrain component through SampleHeight instead of the raw map.
        /// </summary>
        private static void ReportIslandProfile(Terrain terrain, TerrainData data)
        {
            const int Steps = 33;
            float highest = float.NegativeInfinity;
            Vector3 highestPoint = Vector3.zero;
            int aboveWater = 0, total = 0;

            for (int ix = 0; ix < Steps; ix++)
            {
                for (int iz = 0; iz < Steps; iz++)
                {
                    Vector3 world = terrain.transform.position + new Vector3(
                        data.size.x * ix / (Steps - 1f), 0f, data.size.z * iz / (Steps - 1f));
                    float y = terrain.SampleHeight(world) + terrain.transform.position.y;
                    total++;
                    if (y > SeaLevel)
                        aboveWater++;
                    if (y > highest)
                    {
                        highest = y;
                        highestPoint = new Vector3(world.x, y, world.z);
                    }
                }
            }

            Debug.Log($"EXTERIOR island profile: highest y={highest:0.#} " +
                      $"({highest - SeaLevel:+0.#;-0.#} m vs sea) at ({highestPoint.x:0.#},{highestPoint.z:0.#}), " +
                      $"{aboveWater * 100f / total:0.#}% of the footprint is above water");
        }

        // ---- headland ----------------------------------------------------------------------------

        /// <summary>
        /// Which job a peak does. This picks its starting offsets and which way the clearance solver
        /// pushes it; both rings are then held to the SAME test, because
        /// <see cref="ExteriorClearance"/> measures the real elliptical cross-section and needs no
        /// per-ring relaxation to avoid over-rejecting.
        /// </summary>
        private enum HeadlandRing
        {
            /// <summary>Coastline mass flanking the tunnel: reads as the land the cave bores through.</summary>
            FarMass,

            /// <summary>Rocks just outside the mouth, breaking the surface to give the opening a rim.</summary>
            NearFrame
        }

        /// <summary>
        /// One peak, authored in exit-local terms: <paramref name="lateral"/> metres along Right (its
        /// sign is also the direction the solver pushes), <paramref name="along"/> metres along the
        /// horizontal Bearing, and a base seated at an explicit world Y.
        /// </summary>
        private readonly struct Peak
        {
            public readonly string prefabName;
            public readonly HeadlandRing ring;
            public readonly float lateral;
            public readonly float along;
            public readonly float baseY;
            public readonly float targetHeight;
            public readonly float footprintMeters;
            public readonly float yawDegrees;

            public Peak(string prefabName, HeadlandRing ring, float lateral, float along, float baseY,
                float targetHeight, float footprintMeters, float yawDegrees)
            {
                this.prefabName = prefabName;
                this.ring = ring;
                this.lateral = lateral;
                this.along = along;
                this.baseY = baseY;
                this.targetHeight = targetHeight;
                this.footprintMeters = footprintMeters;
                this.yawDegrees = yawDegrees;
            }
        }

        /// <summary>Escape cylinder radius: the 24 x 16 m mouth's half-width plus 3 m.</summary>
        private const float EscapeCorridorRadius = 15f;

        /// <summary>How far past the mouth the sub's climb must stay clear of exterior geometry.</summary>
        private const float EscapeCorridorLength = 120f;

        /// <summary>Metres demanded beyond the nominal shell profile, covering the preset's noise.</summary>
        private const float ShellMargin = 8f;

        private const float PushStepMeters = 3f;

        /// <summary>
        /// Generous on purpose. Twenty metres back from the mouth the profile is still 85 m wide, so a
        /// 110 m mountain - well over 100 m across once scaled - has to stand a long way out to clear
        /// it. A cap that silently truncated the push would leave a peak in the tunnel, which is the
        /// original bug.
        /// </summary>
        private const float MaxPushMeters = 200f;

        private static void BuildHeadland(Transform root)
        {
            Transform group = RecreateChild(root, "Headland");

            ExteriorClearance clearance = ExteriorClearance.Create(
                ExitPosition, ExitDirection, EscapeCorridorRadius, EscapeCorridorLength, ShellMargin);

            // Two mountains per side, staggered in depth, plus two rocks framing the mouth. Every
            // number here is a STARTING point: the solver moves each peak outward until its mesh is
            // out of the tunnel and the escape path, and logs how far it had to go. Do not re-tune
            // these back inward to "look closer" without reading the solver's output - the previous
            // layout had LPN_Mountain_03 dead on the axis, 45 m inside the cave.
            Peak[] peaks =
            {
                new Peak("LPN_Mountain_01", HeadlandRing.FarMass, -103f, -20f, 230f, 100f, 100f, 20f),
                new Peak("LPN_Mountain_02", HeadlandRing.FarMass, 103f, -20f, 230f, 90f, 100f, 200f),
                new Peak("LPN_Mountain_03", HeadlandRing.FarMass, -128f, -70f, 230f, 120f, 110f, 90f),
                new Peak("LPN_Mountain_04", HeadlandRing.FarMass, 124f, -75f, 230f, 85f, 100f, 310f),
                // Seated on the seabed (252) and only just breaking the 273 m surface: kept small so
                // they can stay close to the mouth. Footprint buys distance, so these stay modest.
                new Peak("LPN_Large_Rocks_Update_3.3", HeadlandRing.NearFrame, 30f, 14f, 250f, 26f, 34f, 140f),
                new Peak("LPN_Large_Rocks.003", HeadlandRing.NearFrame, -28f, 16f, 250f, 24f, 30f, 75f)
            };

            int placed = 0;
            int dropped = 0;
            foreach (Peak peak in peaks)
            {
                GameObject prefab = FindPrefab(peak.prefabName);
                if (prefab == null)
                {
                    Debug.LogWarning($"EXTERIOR headland: prefab '{peak.prefabName}' not found, skipped");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                instance.transform.position = ExitPosition + Right * peak.lateral + Bearing * peak.along;
                instance.transform.rotation = Quaternion.Euler(0f, peak.yawDegrees, 0f);

                // Height and footprint are scaled INDEPENDENTLY. These prefabs are about 1.6 m tall and
                // 4 m across natively, so the previous uniform scale-to-height turned a 110 m
                // "mountain" into a 270 x 238 m mound - a gentle plateau, not a peak, and so wide that
                // clearing the 85 m tunnel profile pushed it 123 m sideways where it framed nothing.
                // X and Z take the same factor, so this stays commutative with the yaw applied above.
                // Safe against shear for these six prefabs specifically: all of them, including the
                // 7-transform LPN_Large_Rocks_Update_3.3, carry identity local rotations throughout, so
                // no child sits at an angle that a non-uniform parent scale could skew. Check that
                // again before adding a prefab whose parts are rotated.
                Bounds raw = ComputeBounds(instance);
                float rawFootprint = Mathf.Max(raw.size.x, raw.size.z);
                if (raw.size.y > 0.01f && rawFootprint > 0.01f)
                {
                    float horizontal = peak.footprintMeters / rawFootprint;
                    instance.transform.localScale = Vector3.Scale(instance.transform.localScale,
                        new Vector3(horizontal, peak.targetHeight / raw.size.y, horizontal));
                }

                // The LPN prefabs pivot at their centre, not their base: scaled x60+, "position" put
                // the MIDDLE of each mountain at the intended base and drowned all but the summits.
                // Re-seat so the scaled bounds' bottom lands on the intended base height. This must
                // happen BEFORE the clearance solve - seating moves the mesh in world space.
                Bounds scaled = ComputeBounds(instance);
                instance.transform.position += Vector3.up * (peak.baseY - scaled.min.y);

                Vector3 pushDirection = peak.lateral >= 0f ? Right : -Right;
                bool clear = TryPushClearOfCorridor(instance, clearance, pushDirection,
                    out float push, out float intrusion, out string reason);

                Bounds finalBounds = ComputeBounds(instance);
                string footprint = $"{finalBounds.size.x:0.#} x {finalBounds.size.z:0.#} m footprint";

                if (!clear)
                {
                    // Never leave a blocker in the scene. A missing mountain is a composition note;
                    // a mountain in the tunnel is the sub driving into a wall.
                    Debug.LogError($"EXTERIOR headland: {peak.prefabName} ({footprint}) could not be " +
                                   $"cleared of the {reason} within {MaxPushMeters:0} m of push - DROPPED. " +
                                   "Lower its targetHeight or start it further out.");
                    UnityEngine.Object.DestroyImmediate(instance);
                    dropped++;
                    continue;
                }

                placed++;
                Debug.Log($"EXTERIOR headland: {peak.prefabName} [{peak.ring}] raw height " +
                          $"{raw.size.y:0.#} -> {peak.targetHeight} m, {footprint}, base y={peak.baseY:0.#}, " +
                          $"top y={peak.baseY + peak.targetHeight:0.#}, lateral {peak.lateral:0.#} " +
                          $"-> {peak.lateral + Mathf.Sign(peak.lateral == 0f ? 1f : peak.lateral) * push:0.#} m " +
                          $"(pushed {push:0.#} m" +
                          (push > 0f ? $", was {intrusion:0.##} m into the {reason})" : ")"));
            }

            if (clearance.ClampedToWindowStart)
                Debug.LogWarning("EXTERIOR headland: a clearance query hit the oldest sampled section - " +
                                 "a peak sits near the far edge of the sampled window and may be " +
                                 "under-tested. Widen ExteriorClearance.WindowMeters.");

            Debug.Log($"EXTERIOR headland: {placed} peaks placed, {dropped} dropped");
        }

        /// <summary>
        /// Slides <paramref name="instance"/> along <paramref name="pushDirection"/> in
        /// <see cref="PushStepMeters"/> increments until it is out of the tunnel and the escape path,
        /// and reports how far that took.
        ///
        /// BOTH of ExteriorClearance's tests run, because each alone gives false passes here:
        ///
        /// - The per-vertex test catches detail poking into the corridor. Renderer bounds would not do:
        ///   at these yaws and scales an axis-aligned box inflates by tens of metres and would push
        ///   peaks much further out than their geometry requires.
        /// - The line-probe test catches whole triangles lying across the corridor with every vertex
        ///   outside it. This is not hypothetical - it is how LPN_Mountain_03 passed the vertex test
        ///   with 0 m of push while sitting 5.4 m in front of the mouth.
        ///
        /// Local vertices are read once and re-transformed per step: Mesh.vertices allocates a fresh
        /// array on every access.
        /// </summary>
        private static bool TryPushClearOfCorridor(GameObject instance, ExteriorClearance clearance,
            Vector3 pushDirection, out float pushMeters, out float initialIntrusion, out string reason)
        {
            pushMeters = 0f;
            initialIntrusion = float.NegativeInfinity;
            reason = null;

            var meshes = instance.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null)
                .Select(filter => (node: filter.transform, mesh: filter.sharedMesh))
                .ToArray();

            foreach ((Transform node, Mesh mesh) in meshes)
            {
                if (!mesh.isReadable)
                    Debug.LogWarning($"EXTERIOR headland: '{node.name}' mesh is not readable, so it is " +
                                     "EXCLUDED from the clearance test. Enable Read/Write on its import.");
            }

            var parts = meshes
                .Where(entry => entry.mesh.isReadable)
                .Select(entry => (entry.node, local: entry.mesh.vertices, triangles: entry.mesh.triangles,
                    world: new Vector3[entry.mesh.vertexCount]))
                .ToArray();

            Vector3 origin = instance.transform.position;

            for (int step = 0; step * PushStepMeters <= MaxPushMeters; step++)
            {
                float push = step * PushStepMeters;
                instance.transform.position = origin + pushDirection * push;

                // Step 0 measures the true worst intrusion for the log; later steps only need to know
                // whether anything still violates, so they stop at the first hit.
                bool stopAtFirst = step > 0;
                float worst = float.NegativeInfinity;
                string worstReason = null;

                foreach ((Transform node, Vector3[] local, int[] triangles, Vector3[] world) in parts)
                {
                    Matrix4x4 matrix = node.localToWorldMatrix;
                    for (int i = 0; i < local.Length; i++)
                        world[i] = matrix.MultiplyPoint3x4(local[i]);

                    for (int i = 0; i < world.Length; i++)
                    {
                        float intrusion = clearance.Intrusion(world[i], out string hitReason);
                        if (intrusion > worst)
                        {
                            worst = intrusion;
                            worstReason = hitReason;
                        }
                        if (stopAtFirst && worst > 0f)
                            break;
                    }

                    if (worst <= 0f && clearance.IntersectsProbes(world, triangles, out Vector3 crossing))
                    {
                        // No depth to report - a triangle crossing is binary. Use a nominal positive
                        // value so the caller sees a violation, and name it distinctly in the log.
                        worst = Mathf.Max(worst, 0.01f);
                        worstReason = $"corridor (triangle crossing near {crossing})";
                    }

                    if (stopAtFirst && worst > 0f)
                        break;
                }

                if (step == 0)
                {
                    initialIntrusion = worst;
                    reason = worstReason;
                }

                if (worst <= 0f)
                {
                    pushMeters = push;
                    return true;
                }
            }

            instance.transform.position = origin;
            return false;
        }

        // ---- beach props -------------------------------------------------------------------------

        private const string TropicalPrefabFolder = "Assets/LowPolyTropicalEnvironment_LITE";

        /// <summary>Span of the jetty. Scaling is uniform, so this also caps how tall it gets.</summary>
        private const float PierLengthMeters = 12f;

        /// <summary>
        /// Dresses the island: palms, undergrowth, shore rocks and a pier on the side the submarine
        /// approaches from.
        ///
        /// The plan called this a manual hand-placement pass. It is scripted instead, for the same
        /// reason the rest of the exterior is: the whole group is deleted and rebuilt by name on every
        /// run, so a hand-placed prop would be destroyed the next time anyone re-ran the builder. Every
        /// position is derived from a fixed integer hash and from the terrain's own height, so the
        /// result is byte-identical run to run and survives an island reshape.
        ///
        /// Placement is biased toward the exit-facing side: that is where every cutscene camera looks
        /// from, and the far side of a 200 m island is never on screen.
        /// </summary>
        private static void BuildBeachProps(Transform root)
        {
            Transform group = RecreateChild(root, "BeachProps");

            Terrain terrain = root.GetComponentInChildren<Terrain>();
            if (terrain == null)
            {
                Debug.LogWarning("EXTERIOR props: no island Terrain, beach props skipped");
                return;
            }

            Vector3 islandCentre = ExitPosition + Bearing * IslandDistanceMeters;
            // Seaward = back toward the exit, the direction every approach shot comes from.
            Vector3 seaward = -Bearing;

            int palms = ScatterProps(group, terrain, "PalmTree_05", islandCentre, seaward,
                count: 14, targetHeight: 13f, minElevation: 1.5f, maxElevation: 16f, seed: 9101);
            int plants = ScatterProps(group, terrain, "Plant_01", islandCentre, seaward,
                count: 12, targetHeight: 3.5f, minElevation: 0.6f, maxElevation: 9f, seed: 2273);
            int rocks = ScatterProps(group, terrain, "Rock_01", islandCentre, seaward,
                count: 9, targetHeight: 5f, minElevation: 0.2f, maxElevation: 5f, seed: 5519);

            bool pier = TryPlacePier(group, terrain, islandCentre, seaward);

            Debug.Log($"EXTERIOR props: {palms} palms, {plants} plants, {rocks} rocks, " +
                      $"pier={(pier ? "placed" : "SKIPPED")}");
        }

        /// <summary>
        /// Scatters <paramref name="count"/> copies on ground standing between
        /// <paramref name="minElevation"/> and <paramref name="maxElevation"/> metres above the
        /// waterline. Returns how many were actually seated - the elevation band can be narrow, and a
        /// silent shortfall would read as "the beach is dressed" when it is not.
        /// </summary>
        private static int ScatterProps(Transform group, Terrain terrain, string prefabName,
            Vector3 islandCentre, Vector3 seaward, int count, float targetHeight, float minElevation,
            float maxElevation, int seed)
        {
            GameObject prefab = FindTropicalPrefab(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"EXTERIOR props: prefab '{prefabName}' not found, skipped");
                return 0;
            }

            var parent = new GameObject(prefabName).transform;
            parent.SetParent(group, false);

            float seawardAngle = Mathf.Atan2(seaward.z, seaward.x);
            int placed = 0;

            // Bounded candidate budget: the accept band is a ring on a solved heightmap, so a fixed
            // attempt cap is what keeps a reshaped island from looping forever.
            const int MaxAttempts = 600;
            for (int attempt = 0; attempt < MaxAttempts && placed < count; attempt++)
            {
                // +/-110 degrees about the seaward bearing, so props hug the visible arc.
                float angle = seawardAngle + (Hash01(seed, attempt * 3) - 0.5f) * (220f * Mathf.Deg2Rad);
                float radius = Mathf.Lerp(18f, 78f, Mathf.Sqrt(Hash01(seed, attempt * 3 + 1)));
                var candidate = new Vector3(
                    islandCentre.x + Mathf.Cos(angle) * radius, 0f,
                    islandCentre.z + Mathf.Sin(angle) * radius);

                float ground = terrain.transform.position.y + terrain.SampleHeight(candidate);
                float elevation = ground - SeaLevel;
                if (elevation < minElevation || elevation > maxElevation)
                    continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.transform.position = new Vector3(candidate.x, ground, candidate.z);
                instance.transform.rotation = Quaternion.Euler(0f, Hash01(seed, attempt * 3 + 2) * 360f, 0f);

                Bounds bounds = ComputeBounds(instance);
                if (bounds.size.y > 0.01f)
                {
                    // These props are authored for the pack's 50 m demo island; at native size on a
                    // 200 m one a palm is a shrub. Uniform, so foliage keeps its proportions.
                    float scale = targetHeight / bounds.size.y;
                    instance.transform.localScale *= scale;

                    // Re-seat: scaling about the pivot lifts or sinks the base depending on where the
                    // pivot sits inside the prefab.
                    Bounds scaled = ComputeBounds(instance);
                    instance.transform.position += Vector3.up * (ground - scaled.min.y);
                }

                placed++;
            }

            if (placed < count)
                Debug.LogWarning($"EXTERIOR props: only {placed}/{count} '{prefabName}' fitted the " +
                                 $"{minElevation:0.#}-{maxElevation:0.#} m elevation band in {MaxAttempts} tries");
            return placed;
        }

        /// <summary>
        /// Walks in from open water along the seaward bearing until the terrain rises out of the sea,
        /// then lays the pier across that waterline pointing out to sea.
        /// </summary>
        private static bool TryPlacePier(Transform group, Terrain terrain, Vector3 islandCentre,
            Vector3 seaward)
        {
            GameObject prefab = FindTropicalPrefab("Pier_02");
            if (prefab == null)
            {
                Debug.LogWarning("EXTERIOR props: 'Pier_02' not found, skipped");
                return false;
            }

            // Offset off the centre line: dead centre put the pier on the island's summit silhouette in
            // every approach shot, which is where the eye goes first and where this prop reads worst.
            Vector3 lateral = Right * -30f;

            Vector3 shoreline = Vector3.zero;
            bool found = false;
            // From 120 m out (open water) inward to the centre, first crossing wins.
            for (float distance = 120f; distance >= 0f; distance -= 1f)
            {
                Vector3 probe = islandCentre + lateral + seaward * distance;
                float ground = terrain.transform.position.y + terrain.SampleHeight(probe);
                if (ground <= SeaLevel)
                    continue;
                shoreline = new Vector3(probe.x, SeaLevel, probe.z);
                found = true;
                break;
            }

            if (!found)
            {
                Debug.LogWarning("EXTERIOR props: no waterline crossing along the seaward bearing, " +
                                 "pier skipped");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            instance.transform.position = shoreline;

            // Aim the pier's LONG horizontal axis out to sea rather than assuming the prefab's forward
            // is its length - a pier laid across the shore instead of out from it is instantly wrong.
            instance.transform.rotation = Quaternion.identity;
            Bounds flat = ComputeBounds(instance);
            float yaw = Mathf.Atan2(seaward.x, seaward.z) * Mathf.Rad2Deg;
            if (flat.size.x > flat.size.z)
                yaw += 90f;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // 12 m, not 30. The prefab is a ~3 m dock piece and the scale is uniform, so asking for a
            // 30 m span also made it 30 m TALL: it rendered as a wooden gantry towering over the
            // island's summit. At this size it reads as a small jetty, which is all a backdrop needs.
            Bounds oriented = ComputeBounds(instance);
            float length = Mathf.Max(oriented.size.x, oriented.size.z);
            if (length > 0.01f)
                instance.transform.localScale *= PierLengthMeters / length;

            // Feet just under the surface, and drawn back so the landward end sits on the beach.
            Bounds scaled = ComputeBounds(instance);
            instance.transform.position += Vector3.up * (SeaLevel - 0.5f - scaled.min.y) - seaward * 4f;

            Bounds placed = ComputeBounds(instance);
            Debug.Log($"EXTERIOR props: pier at {instance.transform.position} yaw {yaw:0.#} deg, " +
                      $"{length:0.#} m -> {PierLengthMeters:0.#} m span, " +
                      $"{placed.size.y:0.#} m tall, top y={placed.max.y:0.#}");
            return true;
        }

        /// <summary>
        /// Deterministic [0,1) from two integers. A fixed hash rather than System.Random so the layout
        /// is reproducible from the source alone, with no dependence on call order or runtime version.
        /// </summary>
        private static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint hash = (uint)(seed * 73856093) ^ (uint)(salt * 19349663 + 83492791);
                hash ^= hash << 13;
                hash ^= hash >> 17;
                hash ^= hash << 5;
                return (hash & 0xFFFFFFu) / 16777216f;
            }
        }

        private static GameObject FindTropicalPrefab(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:prefab",
                         new[] { TropicalPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
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
