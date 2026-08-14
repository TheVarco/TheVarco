using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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

        /// <summary>
        /// Seabed plane Y. The island skirt lands slightly above this - see <see cref="SkirtOuterY"/>.
        /// </summary>
        private const float SeabedLevel = 252f;

        /// <summary>
        /// Alpha written into the water's _Color_Shallow. The pack template ships 0.41, which over the
        /// island's 4.7 m shelf left the terrain plate and its square edge fully readable through the
        /// water. _Color_Deep is forced to 1 alongside this; see EnsureSeaMaterial.
        /// </summary>
        private const float ShallowWaterAlpha = 0.75f;

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
            BuildIslandSkirt(root.transform);
            BuildExitCollar(root.transform);
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

            // ---- opacity -------------------------------------------------------------------------
            // The pack's own guide (docs/usage-guide/shader-properties/shader-prop-base.md) is explicit:
            // the ALPHA of _Color_Shallow / _Color_Deep is what controls water opacity, and _Water_Depth
            // decides which of the two a given pixel uses. The template ships _Color_Shallow.a = 0.41,
            // which over our shallow island shelf leaves the terrain plate - and its square edge - plainly
            // readable through the water.
            //
            // 🔴 _Water_Depth STAYS AT 12. Lowering it would pin the whole sea back to _Color_Deep, which
            // is the flat-navy bug the previous session fixed by raising it from 0.3. Opacity is the
            // alpha's job, not the depth's.
            Color shallow = material.GetColor("_Color_Shallow");
            Color deep = material.GetColor("_Color_Deep");
            float templateShallowAlpha = shallow.a;
            shallow.a = ShallowWaterAlpha;
            deep.a = 1f;
            material.SetColor("_Color_Shallow", shallow);
            material.SetColor("_Color_Deep", deep);

            EditorUtility.SetDirty(material);
            Debug.Log($"EXTERIOR sea material: '{material.name}' from template, " +
                      $"_Water_Depth {template.GetFloat("_Water_Depth"):0.##} -> {material.GetFloat("_Water_Depth"):0.##}, " +
                      $"_Caustics_Start {template.GetFloat("_Caustics_Start"):0.#} -> {material.GetFloat("_Caustics_Start"):0.#}, " +
                      $"_Color_Shallow.a {templateShallowAlpha:0.##} -> {shallow.a:0.##}, " +
                      $"_Color_Deep.a {deep.a:0.##}, " +
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
            plane.transform.position = ExitPosition + Bearing * 420f + Vector3.up * (SeabedLevel - ExitPosition.y);
            plane.transform.localScale = new Vector3(80f, 1f, 80f); // 10 m primitive -> 800 m

            plane.GetComponent<MeshRenderer>().sharedMaterial = EnsureSeabedMaterial();
        }

        /// <summary>
        /// The sand material shared by the seabed plane and the island skirt. They meet along the
        /// skirt's outer edge, so they have to be the same texture or the join reads as a colour seam.
        /// </summary>
        private static Material EnsureSeabedMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SeabedMaterialPath);
            if (material != null)
                return material;

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
            return material;
        }

        // ---- island skirt ------------------------------------------------------------------------

        /// <summary>Desired horizontal reach of the skirt. Clamped per perimeter sample by clearance.</summary>
        private const float SkirtWidthMeters = 70f;

        /// <summary>Never emit a skirt narrower than this - below it the join reads as a cliff anyway.</summary>
        private const float SkirtMinWidthMeters = 12f;

        /// <summary>Radial subdivisions between the terrain edge and the seabed.</summary>
        private const int SkirtRingCount = 10;

        /// <summary>Spacing of perimeter samples along the terrain border.</summary>
        private const float SkirtSegmentMeters = 4f;

        /// <summary>
        /// Outer edge Y. Deliberately 0.5 m ABOVE <see cref="SeabedLevel"/>: the seabed is an 800 m
        /// plane at exactly 252, and the terrain's outer band is dead flat, so landing the skirt on 252
        /// would put a large coplanar sheet against that plane and z-fight across the whole join. Half a
        /// metre under 20 m of water is invisible; z-fighting is not.
        /// </summary>
        private const float SkirtOuterY = SeabedLevel + 0.5f;

        /// <summary>Metres of slack demanded beyond the forbidden volumes when clamping skirt width.</summary>
        private const float SkirtClearanceMargin = 4f;

        private const string SkirtMeshPath = GeneratedFolder + "/ExteriorIslandSkirt.asset";

        /// <summary>
        /// Bridges the hand-placed island to the seabed. The pack's TerrainData is a 50 x 50 m tile whose
        /// outer band is dead flat at its base Y, so the island sits in the scene as a square plate
        /// floating ~16 m above the seabed plane with its edge in plain view (review cuts 5 and 6).
        ///
        /// The skirt is a rectangular apron welded to that border: every inner vertex takes its height
        /// from Terrain.SampleHeight, so the join is seamless by construction even if someone later
        /// paints the border, and it eases out to the seabed with a smoothstep profile.
        ///
        /// 🔴 WIDTH IS NOT UNIFORM, ON PURPOSE. The island sits only ~90 m from the mouth, so a full
        /// 70 m apron on the cave-facing side would reach z=418.7 - behind the exit plane at z=424.07,
        /// i.e. inside the cave. Each perimeter sample is clamped by <see cref="ExteriorClearance"/>
        /// instead, which narrows the apron on that side and leaves it full width everywhere else.
        /// Do not "fix" the asymmetry by forcing a constant width.
        ///
        /// No collider: like the headland, this is backdrop. The sub must never be able to hit it.
        /// </summary>
        private static void BuildIslandSkirt(Transform root)
        {
            Transform group = RecreateChild(root, "IslandSkirt");

            Terrain terrain = UnityEngine.Object
                .FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.terrainData != null);
            if (terrain == null)
            {
                Debug.LogWarning("EXTERIOR skirt: no Terrain in the scene, skirt skipped. The island is " +
                                 "hand-placed scene content - if it was removed, remove this call too.");
                return;
            }

            // Read the footprint from the scene rather than hard-coding it: the author has already moved
            // the island once (+53.27 m in z) and may move it again.
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            var innerMin = new Vector2(origin.x, origin.z);
            var innerMax = new Vector2(origin.x + size.x, origin.z + size.z);

            List<(Vector2 point, Vector2 outward)> perimeter = BuildSkirtPerimeter(innerMin, innerMax);

            ExteriorClearance clearance = ExteriorClearance.Create(
                ExitPosition, ExitDirection, EscapeCorridorRadius, EscapeCorridorLength, ShellMargin);

            float[] widths = SolveSkirtWidths(perimeter, terrain, origin.y, clearance);

            Mesh mesh = BuildSkirtMesh(perimeter, widths, terrain, origin.y);
            Mesh asset = SaveMeshAsset(mesh, SkirtMeshPath);

            var skirt = new GameObject("IslandSkirtMesh");
            skirt.transform.SetParent(group, false);
            skirt.AddComponent<MeshFilter>().sharedMesh = asset;
            skirt.AddComponent<MeshRenderer>().sharedMaterial = EnsureSeabedMaterial();
            skirt.isStatic = true;

            Debug.Log($"EXTERIOR skirt: terrain {size.x:0.#} x {size.z:0.#} m at " +
                      $"({origin.x:0.#}, {origin.y:0.##}, {origin.z:0.#}), " +
                      $"{perimeter.Count} perimeter samples x {SkirtRingCount} rings, " +
                      $"width {widths.Min():0.#}..{widths.Max():0.#} m (wanted {SkirtWidthMeters:0}), " +
                      $"outer y={SkirtOuterY:0.##} vs seabed {SeabedLevel:0.##}, " +
                      $"{asset.triangles.Length / 3} tris");
        }

        /// <summary>
        /// Walks the terrain's border once, emitting each corner exactly once with a mitred outward
        /// direction so the apron chamfers there instead of tearing.
        /// </summary>
        private static List<(Vector2 point, Vector2 outward)> BuildSkirtPerimeter(Vector2 min, Vector2 max)
        {
            var corners = new[]
            {
                new Vector2(min.x, min.y), new Vector2(max.x, min.y),
                new Vector2(max.x, max.y), new Vector2(min.x, max.y)
            };
            var edgeOutward = new[]
            {
                new Vector2(0f, -1f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(-1f, 0f)
            };

            var samples = new List<(Vector2, Vector2)>();
            for (int side = 0; side < 4; side++)
            {
                Vector2 a = corners[side];
                Vector2 b = corners[(side + 1) % 4];
                int steps = Mathf.Max(2, Mathf.RoundToInt(Vector2.Distance(a, b) / SkirtSegmentMeters));
                for (int i = 0; i < steps; i++)
                {
                    // i == 0 is the corner shared with the previous side: mitre the two face normals.
                    Vector2 outward = i == 0
                        ? (edgeOutward[(side + 3) % 4] + edgeOutward[side]).normalized
                        : edgeOutward[side];
                    samples.Add((Vector2.Lerp(a, b, i / (float)steps), outward));
                }
            }
            return samples;
        }

        /// <summary>
        /// Per perimeter sample, the largest apron width whose whole radial run stays clear of both
        /// forbidden volumes, then smoothed around the loop so the width changes gradually.
        /// </summary>
        private static float[] SolveSkirtWidths(List<(Vector2 point, Vector2 outward)> perimeter,
            Terrain terrain, float terrainBaseY, ExteriorClearance clearance)
        {
            const int SamplesPerRay = 8;
            const float StepMeters = 2f;

            var widths = new float[perimeter.Count];
            for (int i = 0; i < perimeter.Count; i++)
            {
                (Vector2 point, Vector2 outward) = perimeter[i];
                float innerY = SampleTerrainY(terrain, terrainBaseY, point);

                float width = SkirtWidthMeters;
                while (width > SkirtMinWidthMeters)
                {
                    if (IsSkirtRayClear(point, outward, width, innerY, clearance, SamplesPerRay))
                        break;
                    width -= StepMeters;
                }
                widths[i] = Mathf.Max(SkirtMinWidthMeters, width);
            }

            // Two 1-2-1 passes around the closed loop. Without this the clamp produces a visible step
            // where the escape cylinder starts biting.
            for (int pass = 0; pass < 2; pass++)
            {
                var smoothed = new float[widths.Length];
                for (int i = 0; i < widths.Length; i++)
                {
                    float previous = widths[(i - 1 + widths.Length) % widths.Length];
                    float next = widths[(i + 1) % widths.Length];
                    smoothed[i] = (previous + 2f * widths[i] + next) * 0.25f;
                }
                widths = smoothed;
            }
            return widths;
        }

        private static bool IsSkirtRayClear(Vector2 point, Vector2 outward, float width, float innerY,
            ExteriorClearance clearance, int samples)
        {
            for (int k = 1; k <= samples; k++)
            {
                float t = k / (float)samples;
                Vector2 flat = point + outward * (width * t);
                float y = Mathf.Lerp(innerY, SkirtOuterY, Mathf.SmoothStep(0f, 1f, t));
                // Intrusion is positive inside a forbidden volume; demand slack, not just "not inside".
                if (clearance.Intrusion(new Vector3(flat.x, y, flat.y), out _) > -SkirtClearanceMargin)
                    return false;
            }
            return true;
        }

        private static float SampleTerrainY(Terrain terrain, float terrainBaseY, Vector2 flat)
        {
            return terrainBaseY + terrain.SampleHeight(new Vector3(flat.x, 0f, flat.y));
        }

        private static Mesh BuildSkirtMesh(List<(Vector2 point, Vector2 outward)> perimeter,
            float[] widths, Terrain terrain, float terrainBaseY)
        {
            int loop = perimeter.Count;
            int rings = SkirtRingCount + 1;
            var vertices = new Vector3[loop * rings];
            var uvs = new Vector2[loop * rings];

            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)SkirtRingCount;
                float eased = Mathf.SmoothStep(0f, 1f, t);
                for (int i = 0; i < loop; i++)
                {
                    (Vector2 point, Vector2 outward) = perimeter[i];
                    Vector2 flat = point + outward * (widths[i] * t);
                    // Ring 0 sits exactly on the terrain surface, so the weld needs no tolerance.
                    float innerY = SampleTerrainY(terrain, terrainBaseY, point);
                    float y = Mathf.Lerp(innerY, SkirtOuterY, eased);
                    int index = ring * loop + i;
                    vertices[index] = new Vector3(flat.x, y, flat.y);
                    // World-space UVs so the sand tiles identically to the seabed plane beside it.
                    uvs[index] = new Vector2(flat.x, flat.y) / 120f;
                }
            }

            var triangles = new List<int>(loop * SkirtRingCount * 6);
            for (int ring = 0; ring < SkirtRingCount; ring++)
            {
                for (int i = 0; i < loop; i++)
                {
                    int next = (i + 1) % loop;
                    int a = ring * loop + i;
                    int b = ring * loop + next;
                    int c = (ring + 1) * loop + i;
                    int d = (ring + 1) * loop + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "ExteriorIslandSkirt", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            EnsureWindingTowards(mesh, Vector3.up);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Flips the whole index buffer if the surface came out facing away from <paramref name="facing"/>.
        /// Cheaper to verify than to reason about: the loop walk direction decides the winding, and
        /// getting it wrong makes the surface invisible from the side that matters rather than failing
        /// loudly.
        /// </summary>
        private static void EnsureWindingTowards(Mesh mesh, Vector3 facing)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            Vector3 accumulated = Vector3.zero;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                accumulated += Vector3.Cross(
                    vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]);
            }
            if (Vector3.Dot(accumulated, facing) >= 0f)
                return;

            for (int i = 0; i < triangles.Length; i += 3)
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            mesh.SetTriangles(triangles, 0);
        }

        private static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            // Reuse the asset so scene references survive a rebuild.
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetUVs(0, mesh.uv);
            existing.SetTriangles(mesh.triangles, 0);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---- exit collar -------------------------------------------------------------------------

        private const string CollarMeshPath = GeneratedFolder + "/ExteriorExitCollar.asset";
        private const string CollarMaterialPath = GeneratedFolder + "/ExteriorCollar_Rock.mat";

        /// <summary>
        /// Wraps land around the mouth. See <see cref="ExteriorExitCollar"/> for the geometry; this is
        /// the scene-facing half - finding the shell, clamping reach against the corridor, and refusing
        /// to leave anything in the scene that fails the gate.
        /// </summary>
        /// <summary>
        /// 🔴 OFF, and not because it is unfinished scaffolding - because the CONSTRUCTION below is
        /// wrong and the clearance gate is right to reject it. Turning this on without replacing the
        /// construction just puts the error back.
        ///
        /// What is proven and worth keeping: rim extraction (32 verts, centroid on the exit, verified),
        /// the skin clearance model, and the reach solver. What fails: spokes are extruded radially from
        /// the exit centroid in the rim plane, which is not normal to the tunnel surface, and the route
        /// curves so section centres drift behind the mouth. Some spokes therefore dive into the tunnel
        /// no matter how the reach is clamped, and the solver drives those to 0 while their neighbours
        /// keep the full 60 m. The measured distribution is literally 0..60 m around one loop, and the
        /// long skewed triangles bridging a 0-reach spoke to a 60-reach one sweep across the corridor -
        /// caught, correctly, by the line probes.
        ///
        /// The fix is to stop extruding from a point and follow the tunnel instead: sample the route the
        /// way ExteriorClearance.Create does (CaveRoutePolyline.Build/Sample plus
        /// CaveRoute.EvaluateWidth/EvaluateHeight) and place each collar ring at a FRACTION of the real
        /// cross-section - about 1.15 at the rim ramping outwards. Points at a fraction above 1 are
        /// outside the nominal tunnel analytically, so the reach search disappears along with the
        /// tolerances it needed. See HANDOFF-exterior.md.
        /// </summary>
        /// <remarks>
        /// static readonly rather than const so the disabled body still compiles as reachable code -
        /// a const would make every line below it a CS0162 warning and invite someone to delete it.
        /// </remarks>
        private static readonly bool CollarEnabled = false;

        private static void BuildExitCollar(Transform root)
        {
            Transform group = RecreateChild(root, "ExitCollar");
            if (!CollarEnabled)
            {
                Debug.Log("EXTERIOR collar: disabled - the current construction cannot pass the corridor " +
                          "gate. See the note on CollarEnabled for the diagnosis and the way in.");
                return;
            }

            MeshFilter shell = UnityEngine.Object
                .FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.gameObject.name == "CaveShell");
            if (shell == null)
            {
                Debug.LogWarning("EXTERIOR collar: no 'CaveShell' in the scene, collar skipped");
                return;
            }

            if (!ExteriorExitCollar.TryExtractRim(shell, ExitPosition, out List<Vector3> rim, out string failure))
            {
                Debug.LogError($"EXTERIOR collar: {failure}. No collar built - a collar welded to the " +
                               "wrong loop would be a sheet of rock inside the cave.");
                return;
            }

            // TWO clearance models, because the collar is a different kind of object to a headland.
            //
            // The headland volumes exist to keep FOREIGN geometry away from the mouth: the escape
            // cylinder is r=15 while the mouth's own half-height is 8, and ShellMargin adds another 8 m
            // around the tunnel. The rim therefore sits inside both by construction - the shell itself
            // would "fail" them. Testing a skin welded to that rim against them rejects everything, which
            // is exactly what the first run did: 0 m of reach at all 32 vertices.
            //
            // So the collar is shaped against a SKIN model - the nominal tunnel with no margin and no
            // escape cylinder, which answers the only question that matters for a surface hugging the
            // outside of the shell: is this point inside the tunnel? It is then verified against the FULL
            // model's line probes, which is the test that actually catches a sheet lying across the swim
            // path, and finally by the independent EXIT_CORRIDOR raycast gate in ExteriorReviewCapture.
            ExteriorClearance skin = ExteriorClearance.Create(
                ExitPosition, ExitDirection, 0.01f, 0.01f, 0f);
            ExteriorClearance clearance = ExteriorClearance.Create(
                ExitPosition, ExitDirection, EscapeCorridorRadius, EscapeCorridorLength, ShellMargin);

            Vector3 axis = ExitDirection.normalized;
            Mesh mesh = ExteriorExitCollar.Build(rim, ExitPosition, ExitDirection, SkirtOuterY,
                (rimPoint, outward, startReach) => SolveCollarReach(rimPoint, outward, axis, skin, startReach),
                out float minReach, out float maxReach);

            Debug.Log($"EXTERIOR collar: rim {rim.Count} verts, solved reach {minReach:0.#}..{maxReach:0.#} m " +
                      $"(wanted {ExteriorExitCollar.DesiredReach:0})");

            if (maxReach < ExteriorExitCollar.MinimumReach)
            {
                Debug.LogError($"EXTERIOR collar: clearance left only {maxReach:0.#} m of reach " +
                               "everywhere - nothing worth emitting. No collar built.");
                UnityEngine.Object.DestroyImmediate(mesh);
                return;
            }

            EnsureWindingTowards(mesh, ExitDirection);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!VerifyCollarClearance(mesh, skin, clearance, out string violation))
            {
                Debug.LogError($"EXTERIOR collar: finished mesh still violates the {violation}. " +
                               "No collar built - backdrop is never worth blocking the exit for.");
                UnityEngine.Object.DestroyImmediate(mesh);
                return;
            }

            Mesh asset = SaveMeshAsset(mesh, CollarMeshPath);

            var collar = new GameObject("ExitCollarMesh");
            collar.transform.SetParent(group, false);
            collar.AddComponent<MeshFilter>().sharedMesh = asset;
            collar.AddComponent<MeshRenderer>().sharedMaterial = EnsureCollarMaterial();
            collar.isStatic = true;

            Debug.Log($"EXTERIOR collar: rim {rim.Count} verts, reach {minReach:0.#}..{maxReach:0.#} m " +
                      $"(wanted {ExteriorExitCollar.DesiredReach:0}), {ExteriorExitCollar.Rings} rings, " +
                      $"{asset.triangles.Length / 3} tris, bounds y " +
                      $"{asset.bounds.min.y:0.#}..{asset.bounds.max.y:0.#}");
        }

        /// <summary>
        /// Largest reach whose whole run stays clear of both forbidden volumes.
        ///
        /// Sampled along the run rather than only at its end, and through
        /// <see cref="ExteriorExitCollar.Evaluate"/> rather than a straight line, because the collar
        /// sweeps BACK towards the cave as it goes out - testing the straight ray would clear a reach
        /// the built surface does not actually have.
        /// </summary>
        private static float SolveCollarReach(Vector3 rimPoint, Vector3 outward, Vector3 axis,
            ExteriorClearance skin, float startReach)
        {
            const float StepMeters = 2.5f;

            // Sample the RING parameters the loft will actually emit, not an independent grid. With a
            // grid of its own the solver cleared t=0.1 and t=0.2 while the mesh put a vertex at t=0.125,
            // which is how the first collar failed its own verification.
            int samples = ExteriorExitCollar.Rings;

            // The rim's own depth in the nominal tunnel is the baseline, NOT zero. The exit rim was
            // deliberately roughened (CaveMeshGenerator's exitRimNoiseWeight), so some rim vertices sit
            // several metres inside the nominal ellipse. Demanding the collar be outside it would reject
            // those vertices for a displacement that is the shell's own rock, not the collar's doing.
            // The invariant that actually matters: the collar never reaches further into the tunnel than
            // the rim it grows from.
            // Outside the tunnel, "outside" is all that is asked - hence the max with 0. Comparing raw
            // depths there rejects a vertex for sitting 12.35 m clear when its rim sits 12.41 m clear.
            float baseline = Mathf.Max(0f, skin.Intrusion(rimPoint, out _));

            float reach = startReach;
            while (reach > 0f)
            {
                bool clear = true;
                // k starts at 1: t=0 IS the weld ring, which is that baseline by definition.
                for (int k = 1; k <= samples; k++)
                {
                    Vector3 point = ExteriorExitCollar.Evaluate(
                        rimPoint, outward, axis, reach, k / (float)samples, SkirtOuterY);
                    if (skin.Intrusion(point, out _) > baseline)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear)
                    return reach;
                reach -= StepMeters;
            }
            return 0f;
        }

        /// <summary>
        /// The same two-test gate the headland goes through. The vertex test alone is not enough: a
        /// single collar triangle spans metres and can lie across the corridor with all three vertices
        /// outside it, which is exactly how LPN_Mountain_03 once passed with 0 m of push.
        /// </summary>
        private static bool VerifyCollarClearance(Mesh mesh, ExteriorClearance skin,
            ExteriorClearance clearance, out string violation)
        {
            Vector3[] vertices = mesh.vertices;
            int loop = mesh.vertexCount / (ExteriorExitCollar.Rings + 1);

            // Ring 0 IS the shell's rim, so each spoke is measured against its own weld vertex rather
            // than against zero - see the note in SolveCollarReach about the roughened rim.
            var baseline = new float[loop];
            for (int i = 0; i < loop; i++)
                baseline[i] = Mathf.Max(0f, skin.Intrusion(vertices[i], out _));

            // Tolerance is set by the clearance sampler's own resolution, not by taste. Sections are
            // taken every metre and Intrusion snaps to the nearest one, while across the exit taper the
            // half-extents move about 2 m per metre of route - so the depth it reports is quantised at
            // roughly that scale. Half a metre sits inside that error bar; anything tighter rejects the
            // sampler's noise rather than a real overhang.
            const float Tolerance = 0.5f;
            float worstExcess = float.NegativeInfinity;
            for (int i = loop; i < vertices.Length; i++)
            {
                float intrusion = skin.Intrusion(vertices[i], out string reason);
                float excess = intrusion - baseline[i % loop];
                worstExcess = Mathf.Max(worstExcess, excess);
                if (excess > Tolerance)
                {
                    violation = $"{reason} (collar vertex {i} reaches {intrusion:0.##} m into the tunnel, " +
                                $"{excess:0.##} m deeper than its rim vertex at {baseline[i % loop]:0.##} m)";
                    return false;
                }
            }
            Debug.Log($"EXTERIOR collar: worst vertex sits {worstExcess:0.##} m deeper than its own rim " +
                      $"vertex (tolerance {Tolerance:0.##} m)");
            // The probe test runs on everything EXCEPT the weld band. The cave-corridor probes thread the
            // tunnel at 0.9 of its cross-section, which passes straight through the rim's own
            // neighbourhood - so the triangles joining ring 0 to ring 1 are crossed by construction, no
            // matter how well behaved the collar is. Excluding them keeps the test meaningful for every
            // triangle that is genuinely the collar's own, which is where a sheet across the corridor
            // could actually appear.
            int[] triangles = mesh.triangles;
            var beyondWeld = new List<int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (triangles[i] < loop || triangles[i + 1] < loop || triangles[i + 2] < loop)
                    continue;
                beyondWeld.Add(triangles[i]);
                beyondWeld.Add(triangles[i + 1]);
                beyondWeld.Add(triangles[i + 2]);
            }

            if (clearance.IntersectsProbes(vertices, beyondWeld.ToArray(), out Vector3 crossing))
            {
                violation = $"corridor (triangle crossing near {crossing})";
                return false;
            }
            violation = null;
            return true;
        }

        /// <summary>
        /// Rock for the collar, taken from the headland mountains so the mouth surround and the peaks
        /// flanking it are the same stone. The collar rises about 19 m above the waterline, so it is
        /// seen against the sky - seabed sand would read as a beach floating in mid-air.
        /// </summary>
        private static Material EnsureCollarMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(CollarMaterialPath);
            if (existing != null)
                return existing;

            GameObject mountain = FindPrefab("LPN_Mountain_01");
            Material source = mountain != null
                ? mountain.GetComponentsInChildren<MeshRenderer>(true)
                    .Select(renderer => renderer.sharedMaterial)
                    .FirstOrDefault(material => material != null)
                : null;

            Material collar;
            if (source != null)
            {
                collar = new Material(source) { name = "ExteriorCollar_Rock" };
            }
            else
            {
                collar = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "ExteriorCollar_Rock"
                };
                collar.SetColor("_BaseColor", new Color(0.34f, 0.32f, 0.30f));
                collar.SetFloat("_Smoothness", 0.08f);
                Debug.LogWarning("EXTERIOR collar: no LPN_Mountain_01 material to match, using flat grey");
            }
            AssetDatabase.CreateAsset(collar, CollarMaterialPath);
            return collar;
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
                new Peak("LPN_Large_Rocks_Update_3.3", HeadlandRing.NearFrame, 30f, 14f, 250f, 26f, 34f, 140f)
                // LPN_Large_Rocks.003 (NearFrame, lateral -28) was DELETED FROM THE SCENE BY HAND and is
                // deliberately not re-authored here. Every child of the "Headland" group is destroyed and
                // recreated by name on each run, so leaving the entry in this table would silently undo
                // that deletion the next time anyone builds. If it should come back, add it here rather
                // than in the scene - the scene is not the source of truth for this group.
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
