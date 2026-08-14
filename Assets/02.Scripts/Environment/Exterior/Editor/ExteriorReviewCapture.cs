using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Varco.Underwater;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Renders the shrunken exit and the exterior from the viewpoints that matter, with the real
    /// atmosphere applied via UnderwaterZoneDirector.EvaluateAndApplyAt - which routes through the
    /// same exterior-blend and above-water logic the game runs, so what these frames show is what a
    /// cutscene camera would get.
    ///
    /// Post processing is forced ON, matching the play camera (renderPostProcessing: 1 since PR #11).
    /// The POST_OFF honesty trap documented in UnderwaterGradingCompare does not apply here: these
    /// captures judge composition and the exterior blend, not tonemapper-dependent brightness.
    ///
    /// Alongside the frames this emits a NUMERIC gate (see <see cref="ReportExitCorridorGate"/>).
    /// A frame full of bright rock and a frame showing open water through the mouth are
    /// indistinguishable to any automated check that only looks at pixels, and the previous probe
    /// reported "clear" through a solid mountain because the mountains have no colliders. The gate
    /// is the part of this tool that survives into the next session; the pngs are for judging taste.
    /// </summary>
    public static class ExteriorReviewCapture
    {
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const string OutputFolder = "Artifacts/ExteriorReview";
        private const int Width = 1280;
        private const int Height = 720;

        private static readonly Vector3 ExitPosition = new Vector3(111.19f, 260.00f, 424.07f);
        private static readonly Vector3 ExitDirection = new Vector3(0.1702f, 0.4517f, 0.8758f);
        private static readonly Vector3 Bearing = new Vector3(0.1907f, 0f, 0.9816f);
        private static readonly Vector3 Right = new Vector3(0.9816f, 0f, -0.1907f);

        /// <summary>Half extents of the shrunken 24 x 16 m mouth, used to aim the gate's ray bundle.</summary>
        private const float MouthHalfWidth = 12f;
        private const float MouthHalfHeight = 8f;

        /// <summary>How far past the mouth the escape path must stay clear of exterior geometry.</summary>
        private const float OutboundProbeMeters = 120f;

        /// <summary>Viewpoint of shot 1, reused as the gate's inside-the-cave origin.</summary>
        private static readonly Vector3 InsideViewpoint = new Vector3(108.6f, 246.9f, 397.0f);

        public static void CaptureBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
                Capture(null);
                Debug.Log("EXTERIOR_CAPTURE PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"EXTERIOR_CAPTURE FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Renders the whole set twice, once with the headland group active and once without, into
        /// sibling folders. One run answers both open questions: whether the headland is what fills
        /// the inside-looking-out frame, and what the mouth surroundings look like from outside once
        /// it is gone (the shell is single-sided, so its absence is not simply "a hole").
        ///
        /// The scene is deliberately NOT saved - this only toggles activeSelf and restores it.
        /// </summary>
        public static void CaptureHeadlandAbBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);

                GameObject headland = GameObject.Find("Exterior/Headland");
                if (headland == null)
                    Debug.LogWarning("EXTERIOR_AB: no Exterior/Headland group, capturing once only");

                Capture("with_headland");

                if (headland != null)
                {
                    bool wasActive = headland.activeSelf;
                    headland.SetActive(false);
                    try
                    {
                        Capture("without_headland");
                    }
                    finally
                    {
                        headland.SetActive(wasActive);
                    }
                }

                Debug.Log("EXTERIOR_CAPTURE_AB PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"EXTERIOR_CAPTURE_AB FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/Exterior/외부 환경 리뷰 캡처")]
        public static void CaptureInteractive()
        {
            Capture(null);
        }

        /// <summary>
        /// Centre of the island's footprint, read from whatever Terrain is actually in the scene.
        ///
        /// This used to be a fixed offset along the bearing, shared with the builder's
        /// IslandDistanceMeters. The island is hand-placed now, so a constant would silently frame
        /// empty ocean the moment anyone nudged it. Terrain pivots at its corner, hence the half-size.
        /// </summary>
        private static Vector3 ResolveIsland(out float footprintMeters)
        {
            footprintMeters = 200f;

            Terrain terrain = UnityEngine.Object
                .FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.terrainData != null);

            if (terrain == null)
            {
                Debug.LogWarning("EXTERIOR capture: no Terrain in the scene - island shots fall back to " +
                                 "190 m along the bearing and may frame open water");
                return ExitPosition + Bearing * 190f;
            }

            Vector3 size = terrain.terrainData.size;
            Vector3 corner = terrain.transform.position;
            footprintMeters = Mathf.Max(size.x, size.z);
            var centre = new Vector3(corner.x + size.x * 0.5f, corner.y, corner.z + size.z * 0.5f);

            Debug.Log($"EXTERIOR capture: island '{terrain.name}' footprint {size.x:0.#} x {size.z:0.#} m, " +
                      $"corner {corner}, centre ({centre.x:0.#}, {centre.z:0.#}), base y={corner.y:0.#}, " +
                      $"nominal top y={corner.y + size.y:0.#} (real relief is a small fraction of size.y), " +
                      $"{Vector3.Dot(centre - ExitPosition, Bearing):0.#} m along the bearing from the exit");
            return centre;
        }

        private static (string label, Vector3 position, Vector3 target)[] BuildShots()
        {
            Vector3 surfacing = ExitPosition + ExitDirection * 29f;
            Vector3 islandCentre = ResolveIsland(out float footprint);

            // Anchored to the WATERLINE, not to islandCentre. A Terrain's transform sits at its base,
            // well below the surface, so aiming at islandCentre aims at the island's drowned flank.
            Vector3 islandSurface = new Vector3(islandCentre.x, ExteriorEnvironmentBuilder.SeaLevel, islandCentre.z);
            Vector3 midpoint = (ExitPosition + islandCentre) * 0.5f;

            // Standoff scales with the island instead of being a fixed 130 m: the hand-placed island is
            // a 50 m footprint, and a distance tuned for the old 200 m one renders it as a speck.
            float standoff = Mathf.Max(45f, footprint * 1.3f);

            // Shot 1 sits on the measured knot-12 centreline position (~549 m) rather than
            // exit - dir*40: the end tangent extended backwards leaves the curving tunnel and the
            // first framing attempt was a face full of rock.
            return new (string, Vector3, Vector3)[]
            {
                ("1_inside_looking_out", InsideViewpoint, ExitPosition + ExitDirection * 10f),
                // Below the mouth top (~268) so this is genuinely a submerged look back at the
                // opening; at the exit-tangent height it straddled the waterline and showed neither.
                ("2_outside_looking_back", ExitPosition + ExitDirection * 26f + Vector3.down * 8f, ExitPosition),
                ("3_surfacing_underwater", surfacing + Vector3.down * 2f, islandSurface),
                ("4_surfaced_above_water", surfacing + Vector3.up * 3f, islandSurface + Vector3.up * 8f),
                // Stood off SIDEWAYS, not back along the bearing. The island now sits only ~29 m past
                // the mouth, so any standoff worth having on the bearing axis puts the camera back
                // inside the cave - which is exactly the bug that made this shot a wall of rock before.
                // Lateral keeps it on open water at any island distance.
                ("5_beach_from_water", islandSurface - Right * standoff + Vector3.up * 4f,
                    islandSurface + Vector3.up * 2f),
                ("6_overview", ExitPosition + Bearing * 120f + Right * 160f + Vector3.up * 110f,
                    new Vector3(midpoint.x, ExteriorEnvironmentBuilder.SeaLevel, midpoint.z))
            };
        }

        private static void Capture(string subfolder)
        {
            var director = UnityEngine.Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                throw new InvalidOperationException("no UnderwaterZoneDirector in the scene");

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            if (!string.IsNullOrEmpty(subfolder))
                root = Path.Combine(root, subfolder);
            Directory.CreateDirectory(root);

            GameObject exteriorRoot = GameObject.Find("Exterior");
            Transform exterior = exteriorRoot != null ? exteriorRoot.transform : null;

            // Two sets. The per-shot probe reports everything, because "the water is in the way" is
            // useful when judging a frame. The corridor gate excludes the sea: the sub is meant to
            // surface through it, so it is not an obstacle.
            ExteriorOccluders occluders = ExteriorOccluders.Collect(exterior);
            ExteriorOccluders solids = ExteriorOccluders.Collect(exterior, "Sea");

            var rig = new GameObject("~ExteriorCaptureCamera");
            var camera = rig.AddComponent<Camera>();
            var report = new StringBuilder(
                $"===== EXTERIOR REVIEW CAPTURE ({subfolder ?? "default"}) =====\n");
            report.AppendLine($"  exterior renderers tracked: {occluders.Count} " +
                              $"({solids.Count} solid, sea excluded from the gate)");

            try
            {
                camera.enabled = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 3000f;
                camera.fieldOfView = 65f;
                camera.allowHDR = true;

                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                // The play camera the director actually drives. EvaluateAndApplyAt sets clearFlags and
                // backgroundColor on the director's own trackedCamera - a private serialized field -
                // NOT on this capture rig. Left alone, the rig keeps a fresh Camera's default Skybox
                // clear, so an underwater frame shows SKY where the game shows solid water colour:
                // that is why an earlier capture appeared to show clouds through the cave mouth.
                // Copying the flags off the driven camera is faithful by construction - no duplicated
                // logic to drift - and it is the only way these frames can judge the surface-from-below
                // effect at all. Same class of trap as the POST_OFF one in UnderwaterGradingCompare.
                Camera drivenCamera = Camera.main ?? UnityEngine.Object
                    .FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate != null && candidate != camera);
                if (drivenCamera == null)
                    Debug.LogWarning("EXTERIOR capture: no play camera found, so the rig keeps its " +
                                     "default Skybox clear and underwater frames will NOT match the game");
                else
                    report.AppendLine($"  clear flags mirrored from '{drivenCamera.name}'");

                foreach ((string label, Vector3 position, Vector3 target) in BuildShots())
                {
                    camera.transform.position = position;
                    camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
                    director.EvaluateAndApplyAt(position);

                    if (drivenCamera != null)
                    {
                        camera.clearFlags = drivenCamera.clearFlags;
                        camera.backgroundColor = drivenCamera.backgroundColor;
                    }

                    string file = $"{label}.png";
                    Render(camera, Path.Combine(root, file), out Vector3 mean, out float clipped);
                    report.AppendLine($"  {file}  zone={director.CurrentZoneId} y={position.y:0.#} " +
                                      $"rgb=({mean.x:000},{mean.y:000},{mean.z:000}) clip={clipped:0.0}% " +
                                      $"{DescribeLineOfSight(occluders, position, target)}");
                }

                report.Append(ReportExitCorridorGate(solids));
            }
            finally
            {
                director.ClearUnderwaterEffect();
                UnityEngine.Object.DestroyImmediate(rig);
            }

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// The pass condition, stated as geometry instead of as an impression of a screenshot: a
        /// bundle of rays spanning the 24 x 16 m mouth must reach the mouth from inside the cave, and
        /// must then travel <see cref="OutboundProbeMeters"/> into open water, without touching any
        /// renderer under the Exterior root. The exterior is a backdrop; if any of it is inside the
        /// tunnel or the escape path, the sub drives into it.
        /// </summary>
        private static string ReportExitCorridorGate(ExteriorOccluders occluders)
        {
            Vector3 normal = ExitDirection.normalized;
            Vector3 right = (Right - normal * Vector3.Dot(Right, normal)).normalized;
            Vector3 up = Vector3.Cross(normal, right);

            var mouthSamples = new Vector3[9];
            mouthSamples[0] = ExitPosition;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                mouthSamples[i + 1] = ExitPosition
                    + right * (MouthHalfWidth * 0.8f * Mathf.Cos(angle))
                    + up * (MouthHalfHeight * 0.8f * Mathf.Sin(angle));
            }

            string inboundBlocker = null;
            float inboundDistance = float.PositiveInfinity;
            string outboundBlocker = null;
            float outboundDistance = float.PositiveInfinity;

            foreach (Vector3 sample in mouthSamples)
            {
                Vector3 delta = sample - InsideViewpoint;
                float span = delta.magnitude;
                if (span > 0.01f &&
                    occluders.TryRaycast(InsideViewpoint, delta / span, span, out string name, out float hit) &&
                    hit < inboundDistance)
                {
                    inboundDistance = hit;
                    inboundBlocker = name;
                }

                if (occluders.TryRaycast(sample, normal, OutboundProbeMeters,
                        out string outName, out float outHit) &&
                    outHit < outboundDistance)
                {
                    outboundDistance = outHit;
                    outboundBlocker = outName;
                }
            }

            var report = new StringBuilder();
            report.AppendLine("  ---- EXIT CORRIDOR GATE (9 rays across the mouth) ----");
            report.AppendLine(inboundBlocker == null
                ? "  GATE inbound  (cave viewpoint -> mouth):     PASS  no exterior geometry in the tunnel"
                : $"  GATE inbound  (cave viewpoint -> mouth):     FAIL  '{inboundBlocker}' at {inboundDistance:0.#} m");
            report.AppendLine(outboundBlocker == null
                ? $"  GATE outbound (mouth -> {OutboundProbeMeters:0} m of open water): PASS  escape path clear"
                : $"  GATE outbound (mouth -> {OutboundProbeMeters:0} m of open water): FAIL  '{outboundBlocker}' at {outboundDistance:0.#} m");
            report.AppendLine(inboundBlocker == null && outboundBlocker == null
                ? "  EXIT_CORRIDOR PASS"
                : "  EXIT_CORRIDOR FAIL");
            return report.ToString();
        }

        /// <summary>
        /// What, if anything, stands between the camera and what it was aimed at. A frame full of rock
        /// looks identical whether the viewpoint is buried, a decor prop drifted into the tunnel, or
        /// the aim is simply wrong - this says which.
        ///
        /// Two probes, nearest wins: physics (the cave shell and the seabed have colliders) and
        /// renderers under the Exterior root (the headland has none - see ExteriorOccluders).
        /// </summary>
        private static string DescribeLineOfSight(ExteriorOccluders occluders, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return "los=degenerate";

            Vector3 direction = delta / distance;

            string nearestName = null;
            float nearestDistance = float.PositiveInfinity;

            if (Physics.Raycast(from, direction, out RaycastHit hit, distance, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                nearestName = hit.collider.name;
                nearestDistance = hit.distance;
            }

            if (occluders.TryRaycast(from, direction, distance, out string meshName, out float meshDistance) &&
                meshDistance < nearestDistance)
            {
                nearestName = meshName;
                nearestDistance = meshDistance;
            }

            return nearestName == null
                ? $"los=clear over {distance:0.#} m"
                : $"los=BLOCKED by '{nearestName}' at {nearestDistance:0.#} m of {distance:0.#}";
        }

        /// <summary>
        /// Renders one frame and measures it. The numbers exist because eyeballing exterior brightness
        /// has already overshot twice in this project (see HANDOFF 8.2): a mean alone hides clipping,
        /// so <paramref name="clippedPercent"/> reports the share of pixels with any channel at 250 or
        /// above - that is what "the island is a white blob" looks like as a number.
        /// </summary>
        private static void Render(Camera camera, string path, out Vector3 meanRgb,
            out float clippedPercent)
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

                Color32[] pixels = texture.GetPixels32();
                double sumR = 0d;
                double sumG = 0d;
                double sumB = 0d;
                int clipped = 0;
                foreach (Color32 pixel in pixels)
                {
                    sumR += pixel.r;
                    sumG += pixel.g;
                    sumB += pixel.b;
                    if (pixel.r >= 250 || pixel.g >= 250 || pixel.b >= 250)
                        clipped++;
                }

                int count = Mathf.Max(1, pixels.Length);
                meanRgb = new Vector3((float)(sumR / count), (float)(sumG / count), (float)(sumB / count));
                clippedPercent = clipped * 100f / count;

                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
