using System;
using System.Collections.Generic;
using System.Linq;
using CaveBlockout;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Varco.Underwater;

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
        private const string ZoneSetPath = "Assets/Settings/Underwater/MainMapUnderwaterZones.asset";
        private const string ExteriorZoneId = "Exterior";
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

            // Resolved once and shared: the seabed, the skirt and the island all have to be the same
            // sand at the same scale, and the terrain's own layer is the reference for that.
            Terrain terrain = FindIslandTerrain();

            BuildSea(root.transform);
            BuildSeabed(root.transform, terrain);
            BuildIslandSkirt(root.transform, terrain);
            BuildExitCollar(root.transform);
            BuildHeadland(root.transform);
            ApplySkybox();
            ApplyEditModeAmbient();
            RaiseFarClip();
        }

        /// <summary>
        /// Writes the Exterior profile's lighting into RenderSettings so the SCENE VIEW shows the beach
        /// the way the game does.
        ///
        /// 🔴 The scene was carrying a fossil. Its saved ambient was sky (0.202, 1.132, 1.245) - red a
        /// sixth of green and blue, a ratio of 1 : 5.6 : 6.2, which is exactly the pre-PR#11 cyan noted
        /// in HANDOFF.md 2-E, divided by UnderwaterEnvironmentBuilder's 1/8 editor preview scale. Nobody
        /// saw it because UnderwaterZoneDirector overwrites ambient every frame at runtime; edit mode has
        /// no director, so the scene view kept lighting everything with a colour that has almost no red
        /// in it. Yellow sand cannot render yellow under that - it came out cyan.
        ///
        /// The exterior is what gets authored in this scene view, so the Exterior profile is what belongs
        /// in the saved state. Values are written 1:1 rather than through the 1/8 preview scale, because
        /// the point is for the scene view to match what the director applies at runtime.
        /// </summary>
        private static void ApplyEditModeAmbient()
        {
            var zoneSet = AssetDatabase.LoadAssetAtPath<UnderwaterZoneSet>(ZoneSetPath);
            if (zoneSet == null)
            {
                Debug.LogWarning($"EXTERIOR: no zone set at {ZoneSetPath}, scene-view ambient left as is");
                return;
            }

            UnderwaterZoneProfile exterior = zoneSet.Resolve(ExteriorZoneId);
            if (exterior == null || exterior.zoneId != ExteriorZoneId)
            {
                Debug.LogWarning($"EXTERIOR: zone set has no '{ExteriorZoneId}' profile, " +
                                 "scene-view ambient left as is");
                return;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = exterior.ambientSky * exterior.ambientIntensity;
            RenderSettings.ambientEquatorColor = exterior.ambientEquator * exterior.ambientIntensity;
            RenderSettings.ambientGroundColor = exterior.ambientGround * exterior.ambientIntensity;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            Light sun = UnityEngine.Object
                .FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.type == LightType.Directional);
            if (sun != null)
            {
                sun.color = exterior.directionalColor;
                sun.intensity = exterior.directionalIntensity;
                EditorUtility.SetDirty(sun);
            }

            Debug.Log($"EXTERIOR scene-view lighting: ambient sky {RenderSettings.ambientSkyColor}, " +
                      $"sun {(sun != null ? sun.color.ToString() : "none")} - " +
                      "replaces the pre-PR#11 cyan the scene had kept since before the navy re-grade");
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

        private const string SeabedMeshPath = GeneratedFolder + "/ExteriorSeabed.asset";

        /// <summary>Fallback sand tile size if the terrain cannot be read. The pack's Sand layer uses 4 m.</summary>
        private const float FallbackSandTileMeters = 4f;

        private const float SeabedSpanMeters = 800f;

        /// <summary>Metres along the bearing to the sheet's centre. Sets the far edge and both sides.</summary>
        private const float SeabedCentreOffsetMeters = 420f;

        /// <summary>
        /// Z of the sheet's near edge - the one facing the cave.
        ///
        /// 🔴 This used to fall out of the centre and the span, which put it at z = 436.34: eight metres
        /// in FRONT of the mouth. Nothing rendered between the two (the shell ends at z=430.51 and the
        /// exit rim's lowest point is 252.29 at z=428.13), so the sand stopped in a hard straight line
        /// with a void behind it, and either side of the collar's x[40, 176] footprint that void ran
        /// back indefinitely. That is the hole this constant and <see cref="BuildSeabedSheet"/> close.
        ///
        /// 300 puts the near edge behind the whole massif. The cave breaks the sand plane over one
        /// bounded window and no more, so exactly one hole has to be carved and everything behind it can
        /// stay solid: the build reports the carve strip spanning z 323.6..430.6, which leaves this edge
        /// about 24 m clear of it. (Binning shell vertices puts the nominal roof's crossing nearer
        /// z=333 - the strip reaches further back because it is taken at fraction 1.05. Size this
        /// constant against the strip, which is what the sheet actually has to clear.)
        /// </summary>
        private const float SeabedNearZ = 300f;

        /// <summary>
        /// Cross-section fraction the carved edge sits at. Above 1 for the same reason
        /// <see cref="ExteriorExitCollar"/>'s rings are: a point at fraction &gt; 1 is outside the
        /// nominal tunnel by definition, so "sand never appears inside the cave" is a property of the
        /// construction rather than something a tolerance has to catch.
        ///
        /// 1.05 clears the shell's authored rim noise (about 1.14 m inside the nominal ellipse; the
        /// half-height around the carve is ~37 m, so 1.05 buys 1.9 m) and stays BELOW the collar's 1.12,
        /// which is what lets the sand run UNDER the collar's rock instead of stopping short of it and
        /// opening a second seam.
        /// </summary>
        private const float SeabedCarveFraction = 1.05f;

        /// <summary>Route spacing of the carve contour's stations.</summary>
        private const float SeabedCarveStationMeters = 1f;

        /// <summary>Station spacing of the check that the carve covers the whole waterline.</summary>
        private const float SeabedVerifyStationMeters = 0.5f;

        /// <summary>
        /// How far outside the carve a nominal wall point may sit and still count as covered.
        ///
        /// This exists for one exact case, not as slack. At a fixed station the sand plane's height
        /// pins the ellipse's up-component, so EVERY same-station point at y=252 lies on one straight
        /// line along the section's right axis, whatever the fraction. At the route's last station the
        /// hole's front boundary IS the chord between that station's two carve points, so the fraction-1
        /// wall points land exactly ON it and a strict inside test is a coin flip. A genuine miss is
        /// metres wide - the discarded nearest-centre gate reported 6.11 m - so 10 cm cannot hide one.
        /// </summary>
        private const float SeabedVerifyTouchToleranceMeters = 0.1f;

        /// <summary>Radial subdivisions between the carved hole and the sheet's outer rectangle.</summary>
        private const int SeabedRings = 8;

        /// <summary>
        /// Uniform ray count of the polar sweep. The rectangle's four corners are added to it.
        ///
        /// Sized by the hole, not by the sheet. The ring that follows the carve is a chord polygon, so
        /// where the carve turns hardest it bites inside the true curve; at 192 rays the gate measured
        /// that bite as 0.18 m at the lens's leftmost turn (route 532 m, around x=47 / z=392). The error
        /// falls with the square of the spacing, so 512 puts it near 0.03 m - below anything the rock
        /// sitting on top of that edge could show. The outer ring is unaffected either way: it is an
        /// exact rectangle intersection, not a sampled curve.
        /// </summary>
        private const int SeabedRayCount = 512;

        /// <summary>
        /// The seabed sheet: a flat rectangle at <see cref="SeabedLevel"/> with the cave's own waterline
        /// carved out of it.
        ///
        /// Generated rather than a CreatePrimitive plane so its UVs can be authored in world metres: a
        /// primitive's UVs run 0..1 across the whole 800 m, which forced a tiling factor on the material
        /// and made it impossible for the seabed, the skirt and the terrain to share one scale.
        /// Generating it also drops the MeshCollider CreatePrimitive adds, which this backdrop has no
        /// use for.
        ///
        /// If the carve cannot be built the sheet falls back to the original four-vertex quad stopping
        /// short of the mouth. That leaves the gap visible, which is a cosmetic bug; guessing at a sheet
        /// that might cross the tunnel interior is not.
        /// </summary>
        private static void BuildSeabed(Transform root, Terrain terrain)
        {
            Transform group = RecreateChild(root, "Seabed");

            Vector3 centre = ExitPosition + Bearing * SeabedCentreOffsetMeters
                             + Vector3.up * (SeabedLevel - ExitPosition.y);
            float half = SeabedSpanMeters * 0.5f;
            float tile = SandTileMeters(terrain);

            Mesh mesh = BuildSeabedSheet(centre.x - half, centre.x + half, SeabedNearZ, centre.z + half, tile)
                        ?? BuildSeabedQuad(centre.x - half, centre.x + half,
                            centre.z - half, centre.z + half, tile);

            var seabed = new GameObject("SeabedPlane");
            seabed.transform.SetParent(group, false);
            seabed.AddComponent<MeshFilter>().sharedMesh = SaveMeshAsset(mesh, SeabedMeshPath);
            seabed.AddComponent<MeshRenderer>().sharedMaterial = EnsureSeabedMaterial(terrain);
            seabed.isStatic = true;
        }

        /// <summary>The pre-carve sheet: one quad that stops before the cave. Fallback only.</summary>
        private static Mesh BuildSeabedQuad(float xMin, float xMax, float zMin, float zMax, float tile)
        {
            var corners = new[]
            {
                new Vector3(xMin, SeabedLevel, zMin),
                new Vector3(xMax, SeabedLevel, zMin),
                new Vector3(xMax, SeabedLevel, zMax),
                new Vector3(xMin, SeabedLevel, zMax)
            };

            var mesh = new Mesh { name = "ExteriorSeabed" };
            mesh.SetVertices(corners);
            mesh.SetUVs(0, corners.Select(c => new Vector2(c.x, c.z) / tile).ToArray());
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            EnsureWindingTowards(mesh, Vector3.up);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Rectangle minus the cave's waterline, as one welded annulus. Null if the carve or its
        /// clearance gate fails, so the caller can fall back rather than ship a sheet through the cave.
        ///
        /// 🔴 THE OUTER BOUNDARY IS A RECTANGLE, NOT A CIRCLE, AND THAT IS LOAD-BEARING. A polar sweep
        /// whose outer ring were a circle would be a 192-gon inscribed in the sheet and would cut tens
        /// of metres inside the intended edge along the long sides - a brand new void, the exact bug
        /// being fixed here. Because the outer radius is the EXACT ray/rectangle intersection and the
        /// four corner directions are in the ray set, every chord of the outer ring joins two samples on
        /// the same straight edge and therefore lies on it: the boundary is reproduced without error.
        /// </summary>
        private static Mesh BuildSeabedSheet(float xMin, float xMax, float zMin, float zMax, float tile)
        {
            SeabedCarve carve = BuildSeabedCarve(out string carveFailure);
            if (carve == null)
            {
                Debug.LogError($"EXTERIOR seabed: {carveFailure}. Falling back to the uncarved quad - " +
                               "the sand will stop short of the mouth again.");
                return null;
            }

            Vector2 centroid = carve.Centroid;
            if (centroid.x <= xMin || centroid.x >= xMax || centroid.y <= zMin || centroid.y >= zMax)
            {
                Debug.LogError($"EXTERIOR seabed: the carve's centroid {centroid} is outside the sheet " +
                               $"x[{xMin:0.#}, {xMax:0.#}] z[{zMin:0.#}, {zMax:0.#}], so the polar sweep " +
                               "has no annulus to build.");
                return null;
            }

            float[] angles = BuildSweepAngles(centroid, xMin, xMax, zMin, zMax);
            int rays = angles.Length;
            int rings = SeabedRings + 1;

            var vertices = new Vector3[rays * rings];
            var uvs = new Vector2[rays * rings];
            float tightestHole = float.PositiveInfinity;
            float widestHole = 0f;

            for (int ray = 0; ray < rays; ray++)
            {
                var direction = new Vector2(Mathf.Cos(angles[ray]), Mathf.Sin(angles[ray]));

                if (!TryRaySegmentsFarthest(centroid, direction, carve.Segments,
                        out float inner, out int hits))
                {
                    Debug.LogError($"EXTERIOR seabed: ray {ray} at {angles[ray] * Mathf.Rad2Deg:0.#} deg " +
                                   "misses the carve strip entirely, so the centroid is not inside it.");
                    return null;
                }
                tightestHole = Mathf.Min(tightestHole, inner);
                widestHole = Mathf.Max(widestHole, inner);

                float outer = RayRectangleDistance(centroid, direction, xMin, xMax, zMin, zMax);
                if (outer <= inner * 1.001f)
                {
                    Debug.LogError($"EXTERIOR seabed: the carve reaches the sheet edge on ray {ray} " +
                                   $"(hole {inner:0.#} m, edge {outer:0.#} m). Push SeabedNearZ back.");
                    return null;
                }

                for (int ring = 0; ring < rings; ring++)
                {
                    // Geometric so the triangles are fine against the rock and coarsen outward. The
                    // sheet is dead flat and its UVs are linear in world space, so the far triangles
                    // being large costs nothing.
                    float radius = inner * Mathf.Pow(outer / inner, ring / (float)SeabedRings);
                    if (ring == SeabedRings)
                        radius = outer;

                    Vector2 point = centroid + direction * radius;
                    int index = ring * rays + ray;
                    vertices[index] = new Vector3(point.x, SeabedLevel, point.y);
                    uvs[index] = point / tile;
                }
            }

            var triangles = new List<int>(rays * SeabedRings * 6);
            for (int ring = 0; ring < SeabedRings; ring++)
            {
                for (int ray = 0; ray < rays; ray++)
                {
                    int next = (ray + 1) % rays;
                    int a = ring * rays + ray;
                    int b = ring * rays + next;
                    int c = (ring + 1) * rays + ray;
                    int d = (ring + 1) * rays + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "ExteriorSeabed" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            EnsureWindingTowards(mesh, Vector3.up);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var innerRing = new Vector2[rays];
            for (int ray = 0; ray < rays; ray++)
                innerRing[ray] = new Vector2(vertices[ray].x, vertices[ray].z);

            if (!VerifySeabedSheet(mesh, innerRing, out string violation))
            {
                Debug.LogError($"EXTERIOR seabed: {violation}. Falling back to the uncarved quad.");
                UnityEngine.Object.DestroyImmediate(mesh);
                return null;
            }

            Debug.Log($"EXTERIOR seabed: x[{xMin:0.#}, {xMax:0.#}] z[{zMin:0.#}, {zMax:0.#}] at y " +
                      $"{SeabedLevel:0.#}, carve {carve.Right.Count} stations over z " +
                      $"{carve.Points.Min(p => p.y):0.#}..{carve.Points.Max(p => p.y):0.#} " +
                      $"(widest {carve.Points.Max(p => p.x) - carve.Points.Min(p => p.x):0.#} m), " +
                      $"{rays} rays x {rings} rings = {mesh.vertexCount} verts / " +
                      $"{mesh.triangles.Length / 3} tris, hole radius {tightestHole:0.#}..{widestHole:0.#} m " +
                      $"from {centroid}");
            return mesh;
        }

        /// <summary>
        /// Where the tunnel breaks the sand plane: one chord per station, plus the chains joining their
        /// ends, as a swept strip of segments in XZ.
        ///
        /// The chord is closed form, no search. <see cref="ExteriorExitCollar.Section"/>'s right vector
        /// is cross(tangent, up), so right.y is ALWAYS zero and a point's height on the section ellipse
        /// depends on the angle's sine alone:
        ///
        ///     y(angle) = centre.y + halfHeight * fraction * sin(angle) * up.y
        ///
        /// Solve that for y = 252 and the station yields two points, one either side. Everything on the
        /// plane that is inside THAT section lies on the segment between them, and it also follows that
        /// a fraction-1 chord is contained in the same station's fraction-1.05 chord: the up-component
        /// is fixed by the height, so only the right-component varies, as hw*sqrt(f^2 - k^2), which
        /// grows with f. That containment, station by station, is the whole safety argument.
        ///
        /// 🔴 THE CHAINS ARE NOT THE OUTLINE, AND TREATING THEM AS ONE IS A BUG I SHIPPED AND THE GATE
        /// CAUGHT. Walking one chain out and the other back looks like a lens and usually is, but where
        /// the route turns hard - around route 525 m here - the chain cusps: its z reaches a minimum of
        /// 391.6 and comes back. Raising the fraction slides a point along the section's right axis,
        /// which near that cusp runs nearly ALONG the chain rather than out of it, so the fraction-1
        /// curve dips to z=390.2, a metre and a half OUTSIDE the fraction-1.05 chain. A hole bounded by
        /// the chains leaves sand standing inside the cave there.
        ///
        /// So the region is described as what it actually is - the union of the chords - and the sweep
        /// takes the farthest hit over every segment of the strip. That over-covers where the strip
        /// folds, which is the safe direction, and it restores the station-by-station containment above
        /// as a property of the construction.
        ///
        /// The two ends are not alike. Going back the tunnel sinks until the plane clears its roof and
        /// the chords shrink to nothing. Going forward there is nothing to pinch: the nominal profile at
        /// the route end is 36 x 24 on a centre at y=260, so its floor is at 249.3 and the window is
        /// still open when the route runs out. The front is therefore closed by that last station's own
        /// chord, which lands within a few tenths of a metre of the real rim's lowest vertex (252.29 at
        /// z=428.13) - the sand runs right in under the lip.
        /// </summary>
        private sealed class SeabedCarve
        {
            public readonly List<Vector2> Left = new List<Vector2>();
            public readonly List<Vector2> Right = new List<Vector2>();
            public readonly List<(Vector2 from, Vector2 to)> Segments =
                new List<(Vector2 from, Vector2 to)>();
            public Vector2 Centroid;

            /// <summary>Chords first, then both chains - together the swept strip's whole skeleton.</summary>
            public void Seal()
            {
                for (int i = 0; i < Right.Count; i++)
                {
                    Segments.Add((Right[i], Left[i]));
                    if (i == 0)
                        continue;
                    Segments.Add((Right[i - 1], Right[i]));
                    Segments.Add((Left[i - 1], Left[i]));
                }

                var sum = Vector2.zero;
                for (int i = 0; i < Right.Count; i++)
                    sum += (Right[i] + Left[i]) * 0.5f;
                Centroid = sum / Right.Count;
            }

            public IEnumerable<Vector2> Points => Right.Concat(Left);
        }

        private static SeabedCarve BuildSeabedCarve(out string failure)
        {
            if (!TryBuildRouteSampler(out Func<float, ExteriorExitCollar.Section> sample,
                    out float endDistance, out string routeFailure))
            {
                failure = routeFailure;
                return null;
            }

            var carve = new SeabedCarve();
            bool opened = false;
            bool closed = false;
            int extraWindows = 0;

            for (float distance = endDistance; distance >= 0f; distance -= SeabedCarveStationMeters)
            {
                ExteriorExitCollar.Section section = sample(distance);
                float denominator = section.halfHeight * SeabedCarveFraction * section.up.y;

                // A vertical tunnel would put the plane parallel to the section's own height axis. The
                // route never does that here, and pretending otherwise would divide by ~0.
                float sine = Mathf.Abs(denominator) < 1e-4f
                    ? float.NaN
                    : (SeabedLevel - section.centre.y) / denominator;

                if (float.IsNaN(sine) || sine < -1f || sine > 1f)
                {
                    // Entirely below the plane. Going back from the mouth that is what ends the window;
                    // it cannot happen before the window opens, because the window is already open at
                    // the route end (see the summary).
                    if (opened)
                        closed = true;
                    continue;
                }

                if (closed)
                {
                    extraWindows++;
                    continue;
                }

                opened = true;
                float angle = Mathf.Asin(sine);
                Vector3 positive = section.At(SeabedCarveFraction, angle);
                Vector3 negative = section.At(SeabedCarveFraction, Mathf.PI - angle);
                carve.Right.Add(new Vector2(positive.x, positive.z));
                carve.Left.Add(new Vector2(negative.x, negative.z));
            }

            if (!opened)
            {
                failure = "the route never crosses y=" + SeabedLevel.ToString("0.#") +
                          ", so there is nothing to carve and the sheet would be wrong either way";
                return null;
            }
            if (extraWindows > 0)
            {
                failure = $"the route crosses y={SeabedLevel:0.#} in more than one window " +
                          $"({extraWindows} further stations behind the first). One swept strip cannot " +
                          "describe that - the carve needs to become one strip per window.";
                return null;
            }
            if (carve.Right.Count < 2)
            {
                failure = $"the carve collapsed to {carve.Right.Count} station(s)";
                return null;
            }

            carve.Seal();
            failure = null;
            return carve;
        }

        /// <summary>Uniform directions plus the four corners, so the outer ring lands exactly on them.</summary>
        private static float[] BuildSweepAngles(Vector2 centroid,
            float xMin, float xMax, float zMin, float zMax)
        {
            var angles = new List<float>(SeabedRayCount + 4);
            for (int i = 0; i < SeabedRayCount; i++)
                angles.Add(i * Mathf.PI * 2f / SeabedRayCount);

            foreach (Vector2 corner in new[]
                     {
                         new Vector2(xMin, zMin), new Vector2(xMax, zMin),
                         new Vector2(xMax, zMax), new Vector2(xMin, zMax)
                     })
            {
                float angle = Mathf.Atan2(corner.y - centroid.y, corner.x - centroid.x);
                angles.Add(angle < 0f ? angle + Mathf.PI * 2f : angle);
            }

            angles.Sort();

            var distinct = new List<float>(angles.Count);
            foreach (float angle in angles)
            {
                if (distinct.Count > 0 && angle - distinct[distinct.Count - 1] < 1e-4f)
                    continue;
                distinct.Add(angle);
            }
            // The wrap-around pair has to stay distinct too, or the last quad column is degenerate.
            if (distinct.Count > 1 && distinct[0] + Mathf.PI * 2f - distinct[distinct.Count - 1] < 1e-4f)
                distinct.RemoveAt(distinct.Count - 1);

            return distinct.ToArray();
        }

        /// <summary>
        /// Farthest intersection of a ray with a set of segments, and how many it found.
        ///
        /// Farthest, not nearest, and over the strip's whole skeleton rather than an outline: a ray from
        /// inside crosses most of the chords on its way out, and stopping at the nearest one would leave
        /// sand standing inside the cave. Overshooting only eats sand that the collar's rock is sitting
        /// on anyway. That also makes the containment argument trivial - any point of the strip lies on
        /// some chord, so the farthest hit along the ray through it is at least as far out as it is.
        /// </summary>
        private static bool TryRaySegmentsFarthest(Vector2 origin, Vector2 direction,
            List<(Vector2 from, Vector2 to)> segments, out float distance, out int hits)
        {
            distance = 0f;
            hits = 0;

            foreach ((Vector2 from, Vector2 to) in segments)
            {
                Vector2 edge = to - from;

                float denominator = direction.x * edge.y - direction.y * edge.x;
                if (Mathf.Abs(denominator) < 1e-9f)
                    continue;

                Vector2 delta = from - origin;
                float along = (delta.x * edge.y - delta.y * edge.x) / denominator;
                float across = (delta.x * direction.y - delta.y * direction.x) / denominator;
                if (along < 0f || across < 0f || across > 1f)
                    continue;

                hits++;
                if (along > distance)
                    distance = along;
            }

            return hits > 0 && distance > 0f;
        }

        /// <summary>Distance from a point inside the rectangle to its border along a direction.</summary>
        private static float RayRectangleDistance(Vector2 origin, Vector2 direction,
            float xMin, float xMax, float zMin, float zMax)
        {
            float best = float.PositiveInfinity;
            if (direction.x > 1e-9f)
                best = Mathf.Min(best, (xMax - origin.x) / direction.x);
            else if (direction.x < -1e-9f)
                best = Mathf.Min(best, (xMin - origin.x) / direction.x);
            if (direction.y > 1e-9f)
                best = Mathf.Min(best, (zMax - origin.y) / direction.y);
            else if (direction.y < -1e-9f)
                best = Mathf.Min(best, (zMin - origin.y) / direction.y);
            return best;
        }

        /// <summary>
        /// Two tests: the hole covers the tunnel's whole waterline, and no sand triangle lies across
        /// the corridor.
        ///
        /// 🔴 WHY NOT <see cref="ExteriorClearance.Intrusion"/> PER VERTEX, THE WAY THE COLLAR AND THE
        /// HEADLAND ARE GATED. It was tried and it is the wrong instrument for this surface, measured:
        /// it called the contour point (58.95, 252, 387.26) 6.11 m INSIDE the tunnel, and sliding it 12 m
        /// sideways only bought it back to 3.05 m. That is not a near miss to be tuned away. Intrusion
        /// resolves which cross-section a point belongs to by NEAREST CENTRE, which is sound for a
        /// headland peak sitting off the tunnel envelope and meaningless for a point that is 48 m
        /// off-axis on the waterline of a 127 m-wide ellipse: half a dozen stations are all about
        /// equally near, and the one it picks is not the one the point was generated from.
        ///
        /// So this asserts the property that actually matters instead. Walk the route; at every station
        /// where the NOMINAL wall (fraction 1.0) breaks y=252, both waterline points must lie inside the
        /// hole the mesh actually emitted. If they do, no part of the plane inside the tunnel got left
        /// as sand - which is the whole claim - and it is checked against the emitted ring rather than
        /// the analytic contour, so a sweep, ordering or star-shape bug cannot slip through.
        ///
        /// The probe half is kept as is: the outer rings span hundreds of metres, and a single triangle
        /// can lie across the tunnel with all three vertices well clear of it. EXIT_CORRIDOR in
        /// ExteriorReviewCapture raycasts the real renderers afterwards and is the independent check.
        /// </summary>
        private static bool VerifySeabedSheet(Mesh mesh, Vector2[] innerRing, out string violation)
        {
            if (!TryBuildRouteSampler(out Func<float, ExteriorExitCollar.Section> sample,
                    out float endDistance, out string routeFailure))
            {
                violation = $"the route sampler went away between building and checking ({routeFailure})";
                return false;
            }

            int stations = 0;
            float worstMargin = float.PositiveInfinity;
            for (float distance = endDistance; distance >= 0f; distance -= SeabedVerifyStationMeters)
            {
                ExteriorExitCollar.Section section = sample(distance);
                float denominator = section.halfHeight * section.up.y;
                if (Mathf.Abs(denominator) < 1e-4f)
                    continue;

                float sine = (SeabedLevel - section.centre.y) / denominator;
                if (sine < -1f || sine > 1f)
                    continue;

                stations++;
                float angle = Mathf.Asin(sine);
                foreach (Vector3 wall in new[]
                         {
                             section.At(1f, angle), section.At(1f, Mathf.PI - angle)
                         })
                {
                    var flat = new Vector2(wall.x, wall.z);
                    float depth = DistanceToPolygon(flat, innerRing);
                    if (!IsInsidePolygon(flat, innerRing))
                        depth = -depth;

                    if (depth < -SeabedVerifyTouchToleranceMeters)
                    {
                        violation = $"the carve misses the tunnel wall at {wall} (route {distance:0.#} m, " +
                                    $"{-depth:0.##} m outside the emitted hole) - sand is left where the " +
                                    "plane is inside the cave";
                        return false;
                    }
                    worstMargin = Mathf.Min(worstMargin, depth);
                }
            }

            if (stations == 0)
            {
                violation = "no station puts the nominal wall on the sand plane, so the carve is " +
                            "describing something other than this cave";
                return false;
            }

            ExteriorClearance skin = ExteriorClearance.Create(
                ExitPosition, ExitDirection, 0.01f, 0.01f, 0f);
            if (skin.IntersectsProbes(mesh.vertices, mesh.triangles, out Vector3 crossing))
            {
                violation = $"a sand triangle crosses a corridor probe near {crossing}";
                return false;
            }

            Debug.Log($"EXTERIOR seabed: the carve contains the nominal waterline at all {stations} " +
                      $"crossing stations with {worstMargin:0.##} m to spare at the tightest, and no " +
                      "triangle crosses the corridor probes");
            violation = null;
            return true;
        }

        private static bool IsInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if (polygon[i].y > point.y == polygon[j].y > point.y)
                    continue;
                float x = (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                    (polygon[j].y - polygon[i].y) + polygon[i].x;
                if (point.x < x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Shortest distance from a point to a polygon's boundary, unsigned.</summary>
        private static float DistanceToPolygon(Vector2 point, Vector2[] polygon)
        {
            float best = float.PositiveInfinity;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 edge = polygon[i] - polygon[j];
                float length = edge.sqrMagnitude;
                float t = length < 1e-9f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - polygon[j], edge) / length);
                best = Mathf.Min(best, Vector2.Distance(point, polygon[j] + edge * t));
            }
            return best;
        }

        /// <summary>
        /// Metres of world space per sand tile, read from the terrain's own sand layer.
        ///
        /// This is the number that made the island look like it had a step around it: the terrain tiles
        /// its sand every 4 m, the seabed plane was tiling every 6.7 m and the skirt every 1 m, so three
        /// surfaces meant to be one beach were drawn at three different scales.
        /// </summary>
        private static float SandTileMeters(Terrain terrain)
        {
            TerrainLayer layer = FindSandLayer(terrain);
            if (layer == null || layer.tileSize.x <= 0.01f)
                return FallbackSandTileMeters;
            return layer.tileSize.x;
        }

        private static TerrainLayer FindSandLayer(Terrain terrain)
        {
            TerrainLayer[] layers = terrain != null && terrain.terrainData != null
                ? terrain.terrainData.terrainLayers
                : null;
            if (layers == null || layers.Length == 0)
                return null;

            return layers.FirstOrDefault(layer => layer != null && layer.name.IndexOf(
                       "sand", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? layers.FirstOrDefault(layer => layer != null);
        }

        /// <summary>
        /// The sand shared by the seabed sheet and the island skirt, matched to the terrain's own sand
        /// layer - same texture, same normal map, same smoothness, and above all the same tile size.
        /// Both meshes carry UVs already expressed in tiles, so the material's own scale stays at 1 and
        /// there is exactly one place the scale is decided.
        ///
        /// Rebuilt from the layer every run so the code stays the single source of truth, the same
        /// contract EnsureSeaMaterial uses.
        /// </summary>
        private static Material EnsureSeabedMaterial(Terrain terrain)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SeabedMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, SeabedMaterialPath);
            }

            TerrainLayer sandLayer = FindSandLayer(terrain);
            Texture2D diffuse = sandLayer != null ? sandLayer.diffuseTexture : null;
            if (diffuse == null)
                diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(SandTexturePath);

            if (diffuse != null)
            {
                material.SetTexture("_BaseMap", diffuse);

                // 🔴 The tint has to be carried over as _BaseColor. This pack's sand texture is a nearly
                // white greyscale and ALL of its colour lives in the layer's m_DiffuseRemapMax - for
                // Sand_Lite that is (1, 0.861, 0.355). The terrain shader applies it, so the island
                // renders warm tan while a plain Lit material drawing the same texture renders almost
                // white. That mismatch, not the tiling, is what still read as a step around the island
                // after the tile sizes were unified.
                //
                // (HANDOFF-exterior.md 4-D says URP ignores m_DiffuseRemapMax. That held for the
                // generated island's TerrainLit material, but this hand-placed terrain plainly does
                // apply it - the island is tan on screen. Matching the value is correct either way.)
                Vector4 remap = sandLayer != null ? sandLayer.diffuseRemapMax : Vector4.one;
                material.SetColor("_BaseColor", new Color(remap.x, remap.y, remap.z));
            }
            else
            {
                material.SetColor("_BaseColor", new Color(0.76f, 0.70f, 0.50f));
                Debug.LogWarning($"EXTERIOR seabed: no sand texture on the terrain or at " +
                                 $"{SandTexturePath}, flat colour used");
            }

            // UVs are authored in tiles, so the material must not scale them again.
            material.SetTextureScale("_BaseMap", Vector2.one);

            if (sandLayer != null && sandLayer.normalMapTexture != null)
            {
                material.SetTexture("_BumpMap", sandLayer.normalMapTexture);
                material.SetTextureScale("_BumpMap", Vector2.one);
                material.SetFloat("_BumpScale", sandLayer.normalScale);
                material.EnableKeyword("_NORMALMAP");
            }

            material.SetFloat("_Smoothness", sandLayer != null ? sandLayer.smoothness : 0.1f);
            EditorUtility.SetDirty(material);

            Debug.Log($"EXTERIOR sand: layer '{(sandLayer != null ? sandLayer.name : "none")}', " +
                      $"texture '{(diffuse != null ? diffuse.name : "none")}', " +
                      $"tile {SandTileMeters(terrain):0.##} m, " +
                      $"tint {material.GetColor("_BaseColor")} - shared by the seabed sheet and the skirt");
            return material;
        }

        private static Terrain FindIslandTerrain()
        {
            return UnityEngine.Object
                .FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.terrainData != null);
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
        private static void BuildIslandSkirt(Transform root, Terrain terrain)
        {
            Transform group = RecreateChild(root, "IslandSkirt");

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
            skirt.AddComponent<MeshRenderer>().sharedMaterial = EnsureSeabedMaterial(terrain);
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
            float tile = SandTileMeters(terrain);
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
                    // UVs in TILES of world space, matching the terrain's own sand layer, so the skirt,
                    // the seabed and the island are one continuous beach rather than three scales.
                    uvs[index] = new Vector2(flat.x, flat.y) / tile;
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
        private static void BuildExitCollar(Transform root)
        {
            Transform group = RecreateChild(root, "ExitCollar");

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

            if (!TryBuildRouteSampler(out Func<float, ExteriorExitCollar.Section> sample,
                    out float endDistance, out string routeFailure))
            {
                Debug.LogWarning($"EXTERIOR collar: {routeFailure}, collar skipped");
                return;
            }

            // The SKIN model - the nominal tunnel, no margin, no escape cylinder. The headland volumes
            // exist to keep FOREIGN geometry away from the mouth: the escape cylinder is r=15 while the
            // mouth's own half-height is 8, and ShellMargin adds another 8 m around the tunnel. The rim
            // sits inside both by construction, so the shell itself would "fail" them. For a skin welded
            // to that rim the only meaningful question is the one this model answers: is the point
            // inside the tunnel?
            ExteriorClearance skin = ExteriorClearance.Create(
                ExitPosition, ExitDirection, 0.01f, 0.01f, 0f);
            ExteriorClearance clearance = ExteriorClearance.Create(
                ExitPosition, ExitDirection, EscapeCorridorRadius, EscapeCorridorLength, ShellMargin);

            Mesh mesh = ExteriorExitCollar.Build(rim, sample, endDistance, out float roofClearance);

            Debug.Log($"EXTERIOR collar: rim {rim.Count} verts, {ExteriorExitCollar.Rings} rings over " +
                      $"{ExteriorExitCollar.SpanMeters:0} m of route, roof clamp clearance " +
                      $"{roofClearance:0.#} m");

            if (roofClearance < 0f)
            {
                Debug.LogError($"EXTERIOR collar: the crest clamp sits {-roofClearance:0.#} m BELOW the " +
                               "tunnel roof, so clamping would push vertices back inside. Raise CrestY. " +
                               "No collar built.");
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

            Debug.Log($"EXTERIOR collar: {asset.triangles.Length / 3} tris, bounds y " +
                      $"{asset.bounds.min.y:0.#}..{asset.bounds.max.y:0.#} " +
                      $"(sea level {SeaLevel:0.#}), x {asset.bounds.min.x:0.#}..{asset.bounds.max.x:0.#}");
        }

        /// <summary>
        /// Exposes the main route's cross-sections to the collar loft, sampled exactly the way
        /// <see cref="ExteriorClearance"/> samples them so a fraction means the same thing to both.
        /// </summary>
        private static bool TryBuildRouteSampler(out Func<float, ExteriorExitCollar.Section> sample,
            out float endDistance, out string failure)
        {
            const string MainRouteId = "MainRoute";
            sample = null;
            endDistance = 0f;

            CaveRoute route = UnityEngine.Object
                .FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.Definitions != null &&
                                             candidate.Definitions.Any(d => d != null && d.routeId == MainRouteId));
            if (route == null)
            {
                failure = $"no CaveRoute carrying '{MainRouteId}'";
                return false;
            }

            CaveRouteSplineDefinition definition =
                route.Definitions.First(d => d != null && d.routeId == MainRouteId);
            CaveRoutePolyline polyline = CaveRoutePolyline.Build(route, definition.splineIndex);
            if (polyline == null || !polyline.IsValid)
            {
                failure = $"'{MainRouteId}' spline would not build";
                return false;
            }

            int splineIndex = definition.splineIndex;
            endDistance = polyline.Length;
            sample = distance =>
            {
                polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out float parameter);
                return new ExteriorExitCollar.Section(centre, tangent,
                    route.EvaluateWidth(splineIndex, parameter) * 0.5f,
                    route.EvaluateHeight(splineIndex, parameter) * 0.5f);
            };
            failure = null;
            return true;
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

            // Rings 1..N sit at a cross-section fraction above 1, so being outside the nominal tunnel is
            // a property of the construction, not something to be searched for or absorbed by a
            // tolerance. This loop is therefore an ASSERTION: a hit means the loft is wrong, and the
            // right response is to fix the loft rather than to loosen this number.
            //
            // Ring 0 is skipped because it IS the shell's rim - the exit rim is deliberately roughened
            // (CaveMeshGenerator's exitRimNoiseWeight), so some of those vertices sit over a metre inside
            // the nominal ellipse. That displacement is the shell's own rock, not the collar's.
            float worstIntrusion = float.NegativeInfinity;
            for (int i = loop; i < vertices.Length; i++)
            {
                float intrusion = skin.Intrusion(vertices[i], out string reason);
                worstIntrusion = Mathf.Max(worstIntrusion, intrusion);
                if (intrusion > 0f)
                {
                    violation = $"{reason} (collar vertex {i} is {intrusion:0.##} m inside the nominal " +
                                "tunnel, which a fraction above 1 should make impossible)";
                    return false;
                }
            }
            Debug.Log($"EXTERIOR collar: closest vertex clears the nominal tunnel by " +
                      $"{-worstIntrusion:0.##} m");
            // The probe test runs against the SKIN model and on everything EXCEPT the weld band.
            //
            // Weld band: the corridor probes thread the tunnel at 0.9 of its cross-section, which passes
            // through the rim's own neighbourhood, so ring 0 to ring 1 is crossed by construction.
            //
            // Skin model: probes are straight chords between stations, and across the exit taper the
            // profile flares about 2 m of half-width per metre of route - so a chord bulges OUTSIDE the
            // tapered wall between its endpoints. Inflating the sections by ShellMargin's 8 m first, as
            // the full model does, pushes those chords 8 m further out again and they strike a collar
            // that the exact per-vertex test says clears the tunnel everywhere by 1.35 m. The skin
            // model's probes follow the real tunnel, so they answer the question actually being asked:
            // does a collar triangle lie across the swim path?
            //
            // The independent EXIT_CORRIDOR gate in ExteriorReviewCapture raycasts real renderers through
            // the mouth afterwards, and is the check that has to pass either way.
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

            if (skin.IntersectsProbes(vertices, beyondWeld.ToArray(), out Vector3 crossing))
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
