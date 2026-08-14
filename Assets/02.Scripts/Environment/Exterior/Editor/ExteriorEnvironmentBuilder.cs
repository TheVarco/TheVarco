using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Builds the deterministic base of the sea environment outside the cave exit in MainScene_final:
    /// ocean surface, seabed, and the headland mountains flanking the exit. The purpose is a
    /// Cinemachine cutscene backdrop - the sub drives out of the 24x16 mouth, surfaces, and the beach
    /// is ahead - so placement is anchored to the measured exit pose.
    ///
    /// Everything this builder owns lives under a root named "Exterior" whose children are deleted and
    /// recreated by name on every run, the same contract CaveBlockoutBuilder uses: re-running the
    /// builder is always safe.
    ///
    /// 🔴 THE ISLAND IS NOT PART OF THIS. It is hand-placed scene content - the pack's own authored
    /// demo island, sitting in a root-level "Terrain" group with the demo "Prefabs" group beside it.
    /// It deliberately lives OUTSIDE the "Exterior" root so the recreate-by-name contract can never
    /// delete it. Do not add an island pass back into this file; see the note in Build().
    ///
    /// Why the headland exists at all: the cave shell is single-sided, facing inward, so from outside
    /// the mouth the shell reads as an inside-out smooth dome rather than rock. The mountains help
    /// frame that, but they cannot cover it - the tunnel profile is 85 m wide 20 m behind the mouth,
    /// so any peak clear of the corridor is far too far out. The actual fix for the dome is an
    /// outward-facing collar generated at the exit rim by the cave mesh generator, not more mountains.
    /// </summary>
    public static class ExteriorEnvironmentBuilder
    {
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const string RootName = "Exterior";

        private const string WaterPrefabPath =
            "Assets/ThirdParty/Uber Stylized Water/Prefabs/Water Template/Water Tempate Tropical.prefab";
        private const string SkyboxMaterialPath =
            "Assets/ThirdParty/Uber Stylized Water/Demo/Sky/SKy 22.mat";
        private const string SandTexturePath =
            "Assets/ThirdParty/Uber Stylized Water/Demo/Terrain/sand_01_color_2k.png";
        private const string SeaTemplateMaterialPath =
            "Assets/ThirdParty/Uber Stylized Water/Template Materials/UWa-Template-Tropical.mat";
        private const string GeneratedFolder = "Assets/Generated/Exterior";
        private const string SeabedMaterialPath = GeneratedFolder + "/ExteriorSeabed_Sand.mat";
        private const string SeaMaterialPath = GeneratedFolder + "/ExteriorSea_Tropical.mat";

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

            // The island is NOT built here any more - it is hand-placed scene content.
            //
            // It used to be generated: the pack's TerrainData was copied, its heightmap solved for a
            // waterline, its footprint scaled 4x and its terrain layer swapped. That was dropped in
            // favour of the pack's own authored island, which the author placed by hand as a root-level
            // "Terrain" group holding "Terrain_Lite" (referencing the pack's ORIGINAL TerrainData) plus
            // the demo "Prefabs" group.
            //
            // 🔴 Nothing in this file may create or destroy an island again. Every group under the
            // "Exterior" root is deleted and recreated by name on each run (see RecreateChild), so
            // re-adding a BuildIsland would either duplicate the hand-placed island or, if someone
            // parented the manual work under "Exterior", silently delete it. The beach props went with
            // it for the same reason: they seated themselves against whatever Terrain they found, and
            // the pack's demo island arrives already dressed.
            RemoveRetiredChild(root.transform, "Island");
            RemoveRetiredChild(root.transform, "BeachProps");

            BuildSea(root.transform);
            BuildSeabed(root.transform);
            BuildHeadland(root.transform);
            ApplySkybox();
            RaiseFarClip();
        }

        /// <summary>
        /// Deletes a group this builder used to own but no longer does, so a scene built by an older
        /// version does not keep a stale generated island alongside the hand-placed one.
        /// </summary>
        private static void RemoveRetiredChild(Transform root, string name)
        {
            Transform existing = root.Find(name);
            if (existing == null)
                return;
            Debug.Log($"EXTERIOR: removing retired tool-owned group '{name}' - " +
                      "the island and its props are hand-placed scene content now");
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
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

            Material seaMaterial = EnsureSeaMaterial();
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial = seaMaterial;

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

        /// <summary>
        /// A project-owned copy of the pack's Tropical water material, retuned for THIS scene's scale.
        ///
        /// Why a copy: the template lives under ThirdParty and is shared with the pack's own demo
        /// scenes. Why a retune at all - the numbers, because this was misdiagnosed once:
        ///
        /// The sea rendered as a single flat navy sheet. It was NOT a UV/tiling problem from scaling the
        /// plane to 2.4 km: the shader's textures default to world-space Y-projected UVs (the
        /// "WorldSpace Y-Project UV" subgraph feeds a Position node in AbsoluteWorld space into
        /// "PanningTexture"), so plane scale does not stretch foam, caustics or normals at all.
        ///
        /// The real cause is that every distance in the template is authored for the demo's scale -
        /// water about 5 m deep, camera about 5 m away:
        ///   - `_Water_Depth 0.3` with `_WorldSpaceDepth 1` finishes the shallow-to-deep gradient within
        ///     0.3 m of depth. Our sea is ~21 m deep (seabed 252, surface 273), so everything except a
        ///     30 cm strip at the shoreline is pinned to `_Color_Deep` - the flat navy, exactly.
        ///   - `_Caustics_Start 5` / `_Caustics_Fade 5` cull caustics by CAMERA distance, gone by ~10 m.
        ///     Every cutscene camera here is tens of metres out, so caustics never drew.
        ///   - `_DistanceMask_Start 5` / `_Fade 10` fade the surface detail on the same basis.
        ///
        /// Colours are left exactly as the pack authored them - that tropical palette is the look being
        /// asked for. Only the distances change. Overrides are re-applied every run so this code stays
        /// the single source of truth, the same contract UnderwaterZoneSet.ResetToGuideDefaults uses.
        /// </summary>
        private static Material EnsureSeaMaterial()
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(SeaTemplateMaterialPath);
            if (template == null)
                throw new InvalidOperationException($"no water template material at {SeaTemplateMaterialPath}");

            var material = AssetDatabase.LoadAssetAtPath<Material>(SeaMaterialPath);
            if (material == null)
            {
                material = new Material(template);
                AssetDatabase.CreateAsset(material, SeaMaterialPath);
            }
            else
            {
                // Re-seed from the template so a pack update propagates, then re-apply our overrides.
                material.shader = template.shader;
                material.CopyPropertiesFromMaterial(template);
            }

            // Depth gradient spans the real water column instead of 0.3 m.
            material.SetFloat("_Water_Depth", 12f);
            // Shoreline band wide enough to read at this scale.
            material.SetFloat("_SL_WaterDepth", 2.5f);
            // Caustics visible from cutscene distances, and reaching the actual seabed depth.
            material.SetFloat("_Caustics_Start", 70f);
            material.SetFloat("_Caustics_Fade", 160f);
            material.SetFloat("_Caustics_Depth", -12f);
            // Surface detail must survive past a few metres too.
            material.SetFloat("_DistanceMask_Start", 60f);
            material.SetFloat("_DistanceMask_Fade", 250f);

            EditorUtility.SetDirty(material);
            Debug.Log($"EXTERIOR sea material: '{material.name}' from template, " +
                      $"_Water_Depth {template.GetFloat("_Water_Depth"):0.##} -> {material.GetFloat("_Water_Depth"):0.##}, " +
                      $"_Caustics_Start {template.GetFloat("_Caustics_Start"):0.#} -> {material.GetFloat("_Caustics_Start"):0.#}, " +
                      $"keywords [{string.Join(", ", material.shaderKeywords)}]");
            return material;
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
