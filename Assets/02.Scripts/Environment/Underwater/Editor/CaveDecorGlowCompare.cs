using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Renders the Z2 coral basin once per glow treatment so the "flat cut-out" complaint can be judged
    /// against numbers instead of against a scene-view impression.
    ///
    /// Why this exists: the six Z2_*_Emissive materials carry a uniform HDR _EmissionColor with an empty
    /// _EmissionMap. URP/Lit multiplies emission by that map, so an empty map means every texel receives
    /// the same constant. The gradient ambient does vary across the surface - roughly 0.39 to 1.93 in
    /// blue for LowPolyBlueCoral - but adding a flat 2.85 on top pushes the whole surface into the ACES
    /// shoulder, where that variation compresses to a few percent. The prop reads as a solid colour.
    /// Z2 also runs postExposure 0.55, which pushes it further in.
    ///
    /// The same defect was diagnosed and fixed for Z5's volcano on 2026-08-13 (see CaveDecorCatalog's
    /// Lowpoly Volcano entry): emissionFollowsAlbedo plus a large intensity cut. Z2 never got it.
    ///
    /// Second, separate defect this harness also measures: emission lights nothing. The scene has never
    /// been baked, the decor is not marked ContributeGI, RealtimeEmissive is a no-op since Enlighten was
    /// removed in Unity 6, and no CaveDecor prefab carries a Light. So the coral glows without
    /// illuminating the rock around it. LIGHTS/HALO variants below test the fix for that.
    ///
    /// Nothing is saved. Materials, renderers and spawned lights are restored in the finally block.
    ///
    /// ⚠ AFTER THE FIX LANDED, ONLY `S_shipped` AND VerifyPlaySceneBatch ARE TRUSTWORTHY.
    /// The catalog now sets emissionFollowsAlbedo and the six CaveDecor prefabs carry their own point
    /// light, so: `A0_control` forces the emission map off but still gets the prefab lights and the new
    /// Z2 ambient, and therefore no longer reproduces the original bug; and every pointLights variant
    /// stacks a spawned light on top of the prefab's. Those rows are kept because they document the
    /// investigation, not because they can be re-read as current results.
    /// </summary>
    public static class CaveDecorGlowCompare
    {
        private const string ScenePath = "Assets/01.Scenes/MainScene_Intro_Cinemachine.unity";
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";

        /// <summary>
        /// Straddles the Z1 to Z2 boundary rather than sampling zone centres, because the risk from
        /// halving Z2's ambient is a step at the transition, which a centre-of-zone shot cannot show.
        /// Distances follow UnderwaterGradingCompare's route table.
        /// </summary>
        private static readonly (string label, float distance)[] PlayViewpoints =
        {
            ("01_Z1_start_25m", 25f),
            ("02_Z1Z2_edge_55m", 55f),
            ("03_Z1Z2_edge_70m", 70f),
            ("04_Z2_basin_90m", 90f),
            ("05_Z2_basin_110m", 110f),
            ("06_Z5_chimney_400m", 400f)
        };
        private const string OutputFolder = "Artifacts/GlowVariants";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Route metres into the Z2 coral basin. Matches UnderwaterGradingCompare's Z2_basin.</summary>
        private const float Z2Distance = 90f;

        private const string MaterialRoot = "Assets/04.Materials/CaveArtPass";

        private static readonly string[] TargetMaterials =
        {
            MaterialRoot + "/Z2_LowPolyBlueCoral_Emissive.mat",
            MaterialRoot + "/Z2_VioletSeaFan_Emissive.mat",
            MaterialRoot + "/Z2_BlueCrystal_Emissive.mat",
            MaterialRoot + "/Z2_StylizedTealCoral_Emissive.mat",
            MaterialRoot + "/Z2_BlueCrystalSeaweed_Emissive.mat",
            MaterialRoot + "/Z2_ColorfulStylizedCoral_Emissive.mat"
        };

        private sealed class Variant
        {
            public string id;
            public string note;
            public float emissionScale = 1f;
            public bool followsAlbedo;
            public bool pointLights;
            public bool halos;
            public float lightIntensity = 1.2f;
            public float lightRange = 3.5f;
            public float ambientScale = 1f;

            /// <summary>
            /// Render the materials exactly as they are on disk, cloning nothing. The other variants
            /// prove what a set of values looks like; only this one proves that the generator actually
            /// wrote those values, which is a different claim.
            /// </summary>
            public bool useShippedMaterials;
        }

        /// <summary>
        /// H1 and H3 deliberately use different scale ranges. emissionFollowsAlbedo multiplies emission by
        /// an albedo texel well under 1, so H3 takes two reductions where H1 takes one. Z5 is the
        /// calibration: it cut 1.2 to 0.55 (x0.46) *while* enabling albedo modulation. Giving H3 the same
        /// scales as H1 would render it near-black and it would read as a failure when it is the right
        /// mechanism mis-tuned.
        /// </summary>
        /// <summary>
        /// Round 2. Round 1 settled two things and they are folded in here rather than re-tested:
        ///   - The problem is real through the game camera (H0 reproduced the flat cut-out with ACES and
        ///     Z2 grading applied, clipped 0.00%, so it is shoulder compression and not clipping).
        ///   - emissionFollowsAlbedo at FULL intensity (old H2) recovered the form while staying bright.
        ///     The Z5-style intensity cut overshot: x0.5 read as an ordinary dim prop, not a glowing one.
        ///     So the cut is dropped and x1.0 / x0.8 are the range now.
        /// Round 1's point lights were invisible at 1.2 intensity / 3.5 m range from 30-60 m away, so this
        /// round sweeps them properly and adds a close-up pose to judge them at all.
        /// </summary>
        private static readonly Variant[] Variants =
        {
            // Reads the .mat files as the generator wrote them. After the catalog fix this must match
            // A1; before it, it matched A0. It is the only variant that tests the production path.
            new Variant { id = "S_shipped", note = "materials as written on disk", useShippedMaterials = true },

            new Variant { id = "A0_control", note = "pre-fix look (map forced off)", emissionScale = 1f },
            new Variant { id = "A1_albedo_x100", note = "albedo, full (form fix)", emissionScale = 1f, followsAlbedo = true },

            new Variant { id = "S_shipped_amb050", note = "disk materials + ambient x0.50",
                          useShippedMaterials = true, ambientScale = 0.5f },
            new Variant { id = "S_shipped_amb050_light", note = "disk materials + ambient x0.50 + light 100/12m",
                          useShippedMaterials = true, ambientScale = 0.5f,
                          pointLights = true, lightIntensity = 100f, lightRange = 12f },

            // Round 2 showed intensity 15 / range 14 m was still invisible. The cause is not the light
            // budget, it is that Z2 ambient is authored at 1.575/2.17/3.5 linear and then lifted again by
            // postExposure 0.55, so the rock already sits high on the response curve and a blue light on
            // blue ambient moves it barely at all. Sweep an order of magnitude up.
            new Variant { id = "D1_light_i40", note = "A1 + light 40/12m", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 40f, lightRange = 12f },
            new Variant { id = "D2_light_i100", note = "A1 + light 100/12m", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 100f, lightRange = 12f },
            new Variant { id = "D3_light_i250", note = "A1 + light 250/12m", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 250f, lightRange = 12f },

            // The other lever: if ambient is what is drowning the glow, dimming it is what lets any of
            // this read. Tested separately so the two causes do not get conflated.
            new Variant { id = "E1_amb050", note = "D2 + ambient x0.50", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 100f, lightRange = 12f, ambientScale = 0.5f },
            new Variant { id = "E2_amb025", note = "D2 + ambient x0.25", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 100f, lightRange = 12f, ambientScale = 0.25f },
            new Variant { id = "E3_amb025_nolight", note = "ambient x0.25, no light", emissionScale = 1f,
                          followsAlbedo = true, ambientScale = 0.25f },

            new Variant { id = "C1_halos", note = "E2 + additive halos", emissionScale = 1f, followsAlbedo = true,
                          pointLights = true, lightIntensity = 100f, lightRange = 12f, ambientScale = 0.25f, halos = true }
        };

        [MenuItem("Tools/Underwater Cave/Lighting/Z2 발광 변형 비교")]
        public static void CompareInteractive()
        {
            Debug.Log(Compare());
        }

        public static void CompareBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log(Compare());
        }

        /// <summary>
        /// Verifies the shipped state in the PLAY scene, which is MainScene_final rather than the
        /// Cinemachine intro everything else here was tuned against.
        ///
        /// Two things this exists to catch that the intro scene cannot:
        ///   - Whether the zone-set edit and the prefab lights actually reach the scene players load.
        ///   - Whether dropping Z2 ambient from 1.15 to 0.58 put a brightness cliff at the Z1 boundary.
        ///     Screen brightness goes roughly as ambient * 2^postExposure, and postExposure was left at
        ///     0.55, so Z2 moved from the brightest cave zone to a darker one than its neighbours.
        ///     UnderwaterZoneProfile blends the two as one coupled quantity for exactly this reason.
        /// Z5 is included because it shares the emissive treatment and has no instances in the intro.
        /// </summary>
        public static void VerifyPlaySceneBatch()
        {
            EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);

            var report = new StringBuilder();
            report.AppendLine("===== PLAY SCENE VERIFY (MainScene_final) =====");

            CaveDecorContext context = CaveDecorContext.Create();
            CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
            var director = Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (polyline == null || director == null)
            {
                Debug.Log(report.AppendLine("FAIL: missing MainRoute or UnderwaterZoneDirector").ToString());
                return;
            }

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder, "PlayScene");
            Directory.CreateDirectory(root);
            foreach (string stale in Directory.GetFiles(root, "*.png"))
                File.Delete(stale);

            var rig = new GameObject("~PlaySceneVerifyCamera");
            var camera = rig.AddComponent<Camera>();

            try
            {
                camera.enabled = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 3000f;
                camera.fieldOfView = 65f;
                camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

                foreach ((string label, float distance) in PlayViewpoints)
                {
                    polyline.Sample(distance, out Vector3 eye, out Vector3 tangent, out _);
                    camera.transform.position = eye;
                    camera.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
                    director.EvaluateAndApplyAt(eye);

                    string file = $"{label}.png";
                    Render(camera, Path.Combine(root, file));
                    report.AppendLine($"  {file}  zone={director.CurrentZoneId}  " +
                                      $"postExposure={director.CurrentProfile.postExposure}  " +
                                      $"ambientSky={director.CurrentProfile.ambientSky}");
                }

                report.AppendLine(Measure(root));
                report.AppendLine("PLAY_SCENE_VERIFY DONE");
            }
            finally
            {
                director.ClearUnderwaterEffect();
                Object.DestroyImmediate(rig);
            }

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Renders MainScene_Intro_Cinemachine and MainScene_final from the SAME world positions with the
        /// SAME camera settings, to answer why the two look different.
        ///
        /// The point is to remove every variable except the scenes themselves. The two scenes' own cameras
        /// are not comparable - the intro camera is authored at 17.4 degrees FOV against 60 in the play
        /// scene, and they sit in different places - so a like-for-like shot of each as authored tells you
        /// nothing about whether the atmosphere is configured differently. Here both get FOV 60 at
        /// identical route distances with the director applied, so anything still different afterwards is
        /// scene or asset configuration rather than framing.
        ///
        /// Known non-camera differences going in, both of which this will price:
        ///   - saved RenderSettings ambient differs (intro is blue, play scene is near-neutral). The
        ///     director overwrites it whenever it runs, so it should not survive into these captures - if
        ///     it does, driveAmbient is not doing what it claims.
        ///   - the intro scene sets previewInEditMode, so it grades in edit mode while the play scene only
        ///     grades in play mode. That alone makes an eyeball comparison of the two editors misleading.
        /// </summary>
        public static void CompareScenesBatch()
        {
            var report = new StringBuilder();
            report.AppendLine("===== SCENE PARITY: Intro vs final =====");
            report.AppendLine("identical camera (FOV 60) and identical route distances in both scenes");

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder, "SceneParity");
            Directory.CreateDirectory(root);
            foreach (string stale in Directory.GetFiles(root, "*.png"))
                File.Delete(stale);

            foreach ((string tag, string path) in new[]
                     {
                         ("INTRO", ScenePath),
                         ("FINAL", PlayScenePath)
                     })
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                CaveDecorContext context = CaveDecorContext.Create();
                CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
                var director = Object.FindFirstObjectByType<UnderwaterZoneDirector>();
                if (polyline == null || director == null)
                {
                    report.AppendLine($"{tag}: FAIL - missing MainRoute or UnderwaterZoneDirector");
                    continue;
                }

                var volume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
                report.AppendLine($"{tag}: volumeWeight={(volume != null ? volume.weight.ToString() : "none")} " +
                                  $"sceneAmbientSky={RenderSettings.ambientSkyColor}");

                var rig = new GameObject("~ParityCamera");
                var camera = rig.AddComponent<Camera>();

                try
                {
                    camera.enabled = false;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 3000f;
                    camera.fieldOfView = 60f;
                    camera.allowHDR = true;
                    camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

                    foreach ((string label, float distance) in PlayViewpoints)
                    {
                        polyline.Sample(distance, out Vector3 eye, out Vector3 tangent, out _);
                        camera.transform.position = eye;
                        camera.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
                        director.EvaluateAndApplyAt(eye);

                        Render(camera, Path.Combine(root, $"{label}__{tag}.png"));
                    }

                    report.AppendLine($"{tag}: appliedAmbientSky={RenderSettings.ambientSkyColor} " +
                                      $"zone={director.CurrentZoneId} postExposure={director.CurrentProfile.postExposure}");
                }
                finally
                {
                    director.ClearUnderwaterEffect();
                    Object.DestroyImmediate(rig);
                }
            }

            report.AppendLine(Measure(root));
            report.AppendLine("SCENE_PARITY DONE");
            Debug.Log(report.ToString());
        }

        public static string Compare()
        {
            var report = new StringBuilder();
            report.AppendLine("===== Z2 GLOW VARIANT COMPARE =====");

            CaveDecorContext context = CaveDecorContext.Create();
            CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
            if (polyline == null)
                return report.AppendLine("FAIL: no MainRoute in the scene").ToString();

            var director = Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                return report.AppendLine("FAIL: no UnderwaterZoneDirector in the scene").ToString();

            // Map each target material to every renderer/slot using it, so a variant can be applied and
            // then put back exactly. Scene instances override the prefab material slot, so this has to be
            // driven off the live renderers rather than off the prefabs.
            var originals = new List<Material>();
            var bindings = new List<(Renderer renderer, int slot, Material original)>();

            foreach (string path in TargetMaterials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    report.AppendLine($"WARN: missing material {path}");
                    continue;
                }

                originals.Add(material);
            }

            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null && originals.Contains(shared[i]))
                        bindings.Add((renderer, i, shared[i]));
                }
            }

            report.AppendLine($"targets={originals.Count} renderer slots={bindings.Count}");
            if (bindings.Count == 0)
                return report.AppendLine("FAIL: no renderer uses the Z2 emissive materials").ToString();

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            Directory.CreateDirectory(root);

            // Old captures would otherwise be re-measured alongside the new ones and read as results.
            foreach (string stale in Directory.GetFiles(root, "*.png"))
                File.Delete(stale);

            polyline.Sample(Z2Distance, out Vector3 centre, out Vector3 tangent, out _);

            var rig = new GameObject("~GlowCompareCamera");
            var camera = rig.AddComponent<Camera>();
            var spawned = new List<GameObject>();
            var clones = new List<Material>();
            Texture2D haloTexture = null;
            Material haloMaterial = null;

            var manifest = new StringBuilder();
            manifest.AppendLine("file\tvariant\tnote\temissionScale\tfollowsAlbedo\tpointLights\thalos");

            try
            {
                camera.enabled = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 3000f;
                camera.fieldOfView = 65f;
                camera.allowHDR = true;
                camera.transform.position = centre;
                camera.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);

                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                // The Intro scene's camera ships with m_RenderPostProcessing: 1, so ACES and the zone
                // grading are part of the frame the player sees. Capturing with post on is faithful here.
                data.renderPostProcessing = true;

                // Round 1 framed only the route centreline, where the props are 30-60 m away and a few
                // pixels across. That is the wrong image for judging either silhouette detail or a light
                // pool on the rock. The close-up reproduces the framing the complaint was made from.
                bool hasClose = TryFrameCloseUp(bindings, centre, out Vector3 closeEye, out Quaternion closeRot);
                report.AppendLine(hasClose
                    ? $"close-up eye={closeEye}"
                    : "WARN: no close-up target found, wide shot only");

                foreach (Variant variant in Variants)
                {
                    if (!variant.useShippedMaterials)
                        ApplyVariant(variant, bindings, clones);

                    if (variant.pointLights)
                        SpawnLights(bindings, variant, spawned);

                    if (variant.halos)
                    {
                        EnsureHalo(ref haloTexture, ref haloMaterial);
                        SpawnHalos(bindings, haloMaterial, camera, spawned);
                    }

                    foreach (bool close in new[] { false, true })
                    {
                        if (close && !hasClose)
                            continue;

                        Vector3 eye = close ? closeEye : centre;
                        camera.transform.position = eye;
                        camera.transform.rotation = close
                            ? closeRot
                            : Quaternion.LookRotation(tangent, Vector3.up);

                        director.EvaluateAndApplyAt(eye);

                        // After the director, never before: EvaluateAndApplyAt writes the zone's ambient
                        // itself, so scaling first would just be overwritten.
                        // Scaling in place is safe because the director rewrites all three every call, so
                        // each variant starts from the zone's authored values rather than compounding.
                        if (!Mathf.Approximately(variant.ambientScale, 1f))
                        {
                            RenderSettings.ambientSkyColor *= variant.ambientScale;
                            RenderSettings.ambientEquatorColor *= variant.ambientScale;
                            RenderSettings.ambientGroundColor *= variant.ambientScale;
                        }

                        string file = $"{variant.id}__{(close ? "CLOSE" : "WIDE")}.png";
                        Render(camera, Path.Combine(root, file));
                        manifest.AppendLine($"{file}\t{variant.id}\t{variant.note}\t{variant.emissionScale}\t" +
                                            $"{variant.followsAlbedo}\t{variant.pointLights}\t{variant.halos}");
                        report.AppendLine($"  {file}  zone={director.CurrentZoneId}  {variant.note}");
                    }

                    RestoreBindings(bindings);
                    DestroySpawned(spawned);
                    DestroyClones(clones);
                }

                File.WriteAllText(Path.Combine(root, "manifest.tsv"), manifest.ToString());
                report.AppendLine(Measure(root));
                report.AppendLine("Z2_GLOW_COMPARE DONE");
            }
            finally
            {
                RestoreBindings(bindings);
                DestroySpawned(spawned);
                DestroyClones(clones);
                director.ClearUnderwaterEffect();

                if (haloMaterial != null) Object.DestroyImmediate(haloMaterial);
                if (haloTexture != null) Object.DestroyImmediate(haloTexture);
                Object.DestroyImmediate(rig);
            }

            return report.ToString();
        }

        /// <summary>
        /// Frames the biggest glowing prop near the Z2 sample point from about three of its own radii
        /// away, standing off along the prop's surface normal-ish direction (back toward open water) so
        /// the rock stays behind it. The rock behind is the point: a light pool is only visible if there
        /// is a surface in frame to catch it.
        /// </summary>
        private static bool TryFrameCloseUp(List<(Renderer renderer, int slot, Material original)> bindings,
            Vector3 near, out Vector3 eye, out Quaternion rotation)
        {
            eye = default;
            rotation = default;

            Renderer best = null;
            float bestScore = float.NegativeInfinity;

            foreach ((Renderer renderer, int _, Material _) in bindings)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                    continue;

                Bounds bounds = renderer.bounds;
                float distance = Vector3.Distance(bounds.center, near);
                if (distance > 120f)
                    continue;

                // Favour large props that are also close to the sample point.
                float score = bounds.size.magnitude - distance * 0.05f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = renderer;
                }
            }

            if (best == null)
                return false;

            Bounds target = best.bounds;
            float radius = Mathf.Max(target.size.magnitude * 0.5f, 0.5f);

            // Stand off toward the route centre, which is open water, so the camera looks at the prop
            // with the wall it is attached to filling the background.
            Vector3 outward = (near - target.center);
            outward.y = 0f;
            outward = outward.sqrMagnitude < 0.01f ? Vector3.forward : outward.normalized;

            eye = target.center + outward * (radius * 3f) + Vector3.up * (radius * 0.4f);
            rotation = Quaternion.LookRotation(target.center - eye, Vector3.up);
            return true;
        }

        /// <summary>
        /// Clones each target material once per variant and swaps it onto every renderer slot that used
        /// the original. Cloning rather than editing the asset keeps the comparison free of side effects -
        /// CaveDecorAssetPrep owns those .mat files and would overwrite hand edits anyway.
        /// </summary>
        private static void ApplyVariant(Variant variant,
            List<(Renderer renderer, int slot, Material original)> bindings, List<Material> clones)
        {
            var map = new Dictionary<Material, Material>();

            foreach ((Renderer renderer, int slot, Material original) in bindings)
            {
                if (!map.TryGetValue(original, out Material clone))
                {
                    clone = new Material(original) { name = original.name + "__" + variant.id };

                    Color emission = original.GetColor("_EmissionColor") * variant.emissionScale;
                    clone.SetColor("_EmissionColor", emission);
                    clone.SetTexture("_EmissionMap",
                        variant.followsAlbedo ? original.GetTexture("_BaseMap") : null);
                    clone.EnableKeyword("_EMISSION");

                    map[original] = clone;
                    clones.Add(clone);
                }

                Material[] shared = renderer.sharedMaterials;
                shared[slot] = clone;
                renderer.sharedMaterials = shared;
            }
        }

        private static void RestoreBindings(List<(Renderer renderer, int slot, Material original)> bindings)
        {
            foreach ((Renderer renderer, int slot, Material original) in bindings)
            {
                if (renderer == null)
                    continue;

                Material[] shared = renderer.sharedMaterials;
                if (slot < shared.Length)
                {
                    shared[slot] = original;
                    renderer.sharedMaterials = shared;
                }
            }
        }

        /// <summary>
        /// One shadowless point light per glowing prop, coloured from that prop's emission. Shadows are
        /// off deliberately: the additional-light shadow atlas is 2048 with a 256 tier, and a point light
        /// costs six faces, so roughly ten shadowed point lights fit. There are 58 glowing props in Z2.
        /// Deferred rendering (m_RenderingMode: 2) is what makes the shadowless count affordable -
        /// additionalLightsPerObjectLimit is a forward-path limit and does not bind here.
        /// </summary>
        private static void SpawnLights(List<(Renderer renderer, int slot, Material original)> bindings,
            Variant variant, List<GameObject> spawned)
        {
            foreach ((Renderer renderer, int _, Material original) in bindings)
            {
                if (renderer == null)
                    continue;

                Color emission = original.GetColor("_EmissionColor");
                float peak = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                if (peak <= 0.001f)
                    continue;

                var go = new GameObject("~GlowLight");
                go.transform.SetParent(renderer.transform, false);
                go.transform.localPosition = Vector3.zero;

                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                // Normalised so hue survives but the HDR magnitude does not become the intensity.
                light.color = new Color(emission.r / peak, emission.g / peak, emission.b / peak);
                light.intensity = variant.lightIntensity;
                light.range = variant.lightRange;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;

                spawned.Add(go);
            }
        }

        /// <summary>
        /// Additive camera-facing quads standing in for light scattering in water. URP 17 has no native
        /// volumetric fog, so this is the cheap honest option; the integrated alternative is local
        /// in-scatter inside UnderwaterFullScreen.shader, which is a much larger job.
        /// </summary>
        private static void SpawnHalos(List<(Renderer renderer, int slot, Material original)> bindings,
            Material haloMaterial, Camera camera, List<GameObject> spawned)
        {
            foreach ((Renderer renderer, int _, Material original) in bindings)
            {
                if (renderer == null)
                    continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "~GlowHalo";
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(renderer.transform, false);
                go.transform.localPosition = Vector3.zero;

                float size = renderer.bounds.size.magnitude * 1.8f;
                go.transform.localScale = Vector3.one * Mathf.Max(size, 1f);

                if (camera != null)
                    go.transform.rotation = Quaternion.LookRotation(go.transform.position - camera.transform.position);

                var quad = go.GetComponent<MeshRenderer>();
                var instance = new Material(haloMaterial);
                Color emission = original.GetColor("_EmissionColor");
                float peak = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                if (peak > 0.001f)
                    instance.SetColor("_BaseColor",
                        new Color(emission.r / peak, emission.g / peak, emission.b / peak, 1f) * 0.35f);

                quad.sharedMaterial = instance;
                quad.shadowCastingMode = ShadowCastingMode.Off;
                quad.receiveShadows = false;

                spawned.Add(go);
            }
        }

        private static void EnsureHalo(ref Texture2D texture, ref Material material)
        {
            if (material != null)
                return;

            const int size = 128;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    // Squared falloff, zero at the rim, so the quad edge is invisible.
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a *= a;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { name = "~GlowHaloAdditive" };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);                        // transparent
            material.SetFloat("_Blend", 2f);                          // additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void DestroySpawned(List<GameObject> spawned)
        {
            foreach (GameObject go in spawned)
            {
                if (go == null)
                    continue;

                var quad = go.GetComponent<MeshRenderer>();
                if (quad != null && quad.sharedMaterial != null)
                    Object.DestroyImmediate(quad.sharedMaterial);

                Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private static void DestroyClones(List<Material> clones)
        {
            foreach (Material clone in clones)
            {
                if (clone != null)
                    Object.DestroyImmediate(clone);
            }

            clones.Clear();
        }

        /// <summary>
        /// "Looks flat" is not a measurement. The number that matters here is how much luminance variation
        /// survives across the glowing pixels: a uniform emission that swamps the lit term drives it toward
        /// zero, and recovering it is the whole point of the fix. Clipped fraction is reported alongside
        /// because a blown-out prop and a flat-but-dim prop are different failures.
        /// </summary>
        private static string Measure(string root)
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("-- measurements (sRGB 0-255) --");
            text.AppendLine("glowPx  = pixels brighter than luma 90 (the coral)");
            text.AppendLine("glowSD  = luminance std-dev among those pixels. HIGHER = more readable form.");
            text.AppendLine("clipped = fraction of frame at/above 250 in all channels");
            text.AppendLine();
            text.AppendLine($"{"file",-22} {"R",6} {"G",6} {"B",6} {"glowPx",8} {"glowSD",7} {"clipped",8}");

            var files = new List<string>(Directory.GetFiles(root, "*.png"));
            files.Sort();

            foreach (string path in files)
            {
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes))
                {
                    Object.DestroyImmediate(texture);
                    continue;
                }

                Color32[] pixels = texture.GetPixels32();
                double r = 0, g = 0, b = 0;
                int clipped = 0;

                var glow = new List<double>();
                foreach (Color32 pixel in pixels)
                {
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;

                    if (pixel.r >= 250 && pixel.g >= 250 && pixel.b >= 250)
                        clipped++;

                    double luma = 0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b;
                    if (luma > 90.0)
                        glow.Add(luma);
                }

                int n = pixels.Length;
                double sd = 0.0;
                if (glow.Count > 1)
                {
                    double mean = 0.0;
                    foreach (double v in glow) mean += v;
                    mean /= glow.Count;

                    foreach (double v in glow) sd += (v - mean) * (v - mean);
                    sd = System.Math.Sqrt(sd / glow.Count);
                }

                text.AppendLine($"{Path.GetFileName(path),-22} {r / n,6:0.0} {g / n,6:0.0} {b / n,6:0.0} " +
                                $"{glow.Count,8} {sd,7:0.00} {clipped / (float)n,8:P2}");

                Object.DestroyImmediate(texture);
            }

            return text.ToString();
        }

        private static void Render(Camera camera, string path)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(texture);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
