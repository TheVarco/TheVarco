using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CaveBlockout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Varco.Underwater;

namespace Varco.SubmarineTools.EditorTools
{
    /// <summary>
    /// Measures the submarine headlight from the pilot's seat, at real route positions, with the lamp
    /// off and on.
    ///
    /// 🔴 WHY NOT UnderwaterReviewCapture. That tool shoots from the route centre line with no submarine
    /// in frame - it grades the water, and the submarine is not part of what it measures. A headlight
    /// only exists relative to the hull it is bolted to and the cockpit it is seen from, so this moves
    /// the actual submarine onto the route and shoots from the seat. The scene is never saved: the
    /// submarine is put back where it was in a finally block.
    ///
    /// Batch entry points:
    ///   ...SubmarineHeadlightCapture.CaptureBatch            - lamp off/on across every zone
    ///   ...SubmarineHeadlightCapture.InstallAndCaptureBatch  - write the tuning first, then capture
    ///   ...SubmarineHeadlightCapture.BeamSurveyBatch         - how much room is there around the sub
    /// </summary>
    public static class SubmarineHeadlightCapture
    {
        public const string ArtifactFolder = "Artifacts/HeadlightReview";
        private const int CaptureWidth = 960;
        private const int CaptureHeight = 540;

        /// <summary>Eye height above the seat anchor, in metres. The seats sit at world y -0.5.</summary>
        private const float EyeHeightMeters = 1.1f;

        public readonly struct Measurement
        {
            public readonly Vector3 mean;
            public readonly Vector3 max;
            public readonly float nonBlackFraction;

            public Measurement(Vector3 mean, Vector3 max, float nonBlackFraction)
            {
                this.mean = mean;
                this.max = max;
                this.nonBlackFraction = nonBlackFraction;
            }
        }

        private readonly struct Shot
        {
            public readonly string name;
            public readonly float routeDistanceMeters;

            public Shot(string name, float routeDistanceMeters)
            {
                this.name = name;
                this.routeDistanceMeters = routeDistanceMeters;
            }
        }

        /// <summary>
        /// Z4 runs 266.9 - 347.5 m; 285 is a straight stretch and 307 is the branch portal. The other
        /// four exist to catch the opposite failure from the one Z4 has: a lamp strong enough for the
        /// dark zone blowing out rock that sits a few metres from the hull everywhere else.
        /// </summary>
        private static readonly Shot[] Shots =
        {
            new Shot("Z1_spawn_30m", 30f),
            new Shot("Z2_bright_100m", 100f),
            new Shot("Z3_canyon_200m", 200f),
            // The two Z4 boundaries. postExposure swings 0.45 -> 5.10 -> 0.32 across them, which the
            // director lerps over zoneBlendMeters, so these are the frames where a dark-adaptation ramp
            // either reads as intent or as a bug. Zone midpoints cannot show it.
            new Shot("Z3_to_Z4_boundary_267m", 267f),
            new Shot("Z4_entry_285m", 285f),
            new Shot("Z4_portal_307m", 307f),
            new Shot("Z4_to_Z5_boundary_347m", 347f),
            new Shot("Z5_vents_400m", 400f),
        };

        public static void InstallAndCaptureBatch()
        {
            SubmarineHeadlightInstaller.Install();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(SubmarineHeadlightBatch.PlayScenePath, OpenSceneMode.Single);
            Debug.Log(Capture());
        }

        public static void CaptureBatch()
        {
            EditorSceneManager.OpenScene(SubmarineHeadlightBatch.PlayScenePath, OpenSceneMode.Single);
            Debug.Log(Capture());
        }

        /// <summary>
        /// How much room is there around the submarine, and what is inside the beam?
        ///
        /// 🔴 WHY THIS IS A TOOL AND NOT A ONE-OFF. Every conclusion about the headlight depends on the
        /// distance to the nearest surface, and that number is not in any design document - MAP_GUIDE
        /// gives Z4 an 18x18 cross-section, but measured from the bow the nearest rock is 17-21 m at
        /// 285, 307 and 340 m alike, because a straight corridor seen along its own axis has nothing
        /// near it. Anyone re-tuning the lamp needs to re-measure this before trusting an intensity.
        /// </summary>
        public static void BeamSurveyBatch()
        {
            EditorSceneManager.OpenScene(SubmarineHeadlightBatch.PlayScenePath, OpenSceneMode.Single);
            Debug.Log(BeamSurvey());
        }

        [MenuItem("Tools/Submarine/Capture Headlight Review")]
        public static void CaptureInteractive()
        {
            Debug.Log(Capture());
        }

        [MenuItem("Tools/Submarine/Survey Headlight Beam")]
        public static void BeamSurveyInteractive()
        {
            Debug.Log(BeamSurvey());
        }

        public static string Capture()
        {
            RequireGraphicsDevice();

            GameObject submarine = SubmarineHeadlightBatch.FindSubmarineRoot();
            if (submarine == null)
                throw new InvalidOperationException("No Submarine_final root in the open scene.");

            var director = UnityEngine.Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                throw new InvalidOperationException("No UnderwaterZoneDirector in the open scene.");

            Light headlight = FindHeadlight(submarine);
            if (headlight == null)
                throw new InvalidOperationException("No spot Light under the submarine.");

            Transform seat = ResolveSeat(submarine.transform);
            string outputDirectory = CreateOutputDirectory(string.Empty);

            var report = new StringBuilder();
            report.AppendLine("SUBMARINE_HEADLIGHT_CAPTURE");
            report.AppendLine($"  directory={outputDirectory}");
            report.AppendLine($"  lamp localPosInRoot={Fmt(submarine.transform.InverseTransformPoint(headlight.transform.position))} " +
                              $"forwardLocal={Fmt(submarine.transform.InverseTransformDirection(headlight.transform.forward))} " +
                              $"intensity={headlight.intensity} range={headlight.range} " +
                              $"spot={headlight.spotAngle}/{headlight.innerSpotAngle} color={headlight.color}");

            Vector3 restorePosition = submarine.transform.position;
            Quaternion restoreRotation = submarine.transform.rotation;
            bool restoreLight = headlight.enabled;

            CaptureRig rig = null;
            try
            {
                rig = new CaptureRig();

                foreach (Shot shot in Shots)
                {
                    (Vector3 position, Quaternion rotation) = ResolveRoutePose(shot.routeDistanceMeters);
                    submarine.transform.SetPositionAndRotation(position, rotation);

                    Vector3 eye = seat.position + Vector3.up * EyeHeightMeters;
                    rig.camera.transform.SetPositionAndRotation(eye, submarine.transform.rotation);

                    director.EvaluateAndApplyAt(eye);
                    rig.camera.backgroundColor = director.CurrentProfile.backgroundColor;

                    headlight.enabled = false;
                    Measurement off = rig.RenderAndMeasure(Path.Combine(outputDirectory, $"{shot.name}_lamp_off.png"));

                    headlight.enabled = true;
                    Measurement on = rig.RenderAndMeasure(Path.Combine(outputDirectory, $"{shot.name}_lamp_on.png"));

                    report.AppendLine($"  {shot.name} zone={director.CurrentProfile.zoneId} " +
                                      $"visibility={director.CurrentProfile.visibilityMeters:0.0}m " +
                                      $"postExposure={director.CurrentProfile.postExposure:0.00}");
                    report.AppendLine($"    lampOff mean={Fmt(off.mean)} luma={Luma(off.mean):0.00} " +
                                      $"max={Fmt(off.max)} nonBlack={off.nonBlackFraction:P1}");
                    report.AppendLine($"    lampOn  mean={Fmt(on.mean)} luma={Luma(on.mean):0.00} " +
                                      $"max={Fmt(on.max)} nonBlack={on.nonBlackFraction:P1}");
                }

                report.Append(CaptureNearestProp(rig, submarine, director, headlight, seat, outputDirectory));
                return report.ToString();
            }
            finally
            {
                headlight.enabled = restoreLight;
                submarine.transform.SetPositionAndRotation(restorePosition, restoreRotation);
                director.ClearUnderwaterEffect();
                rig?.Dispose();
            }
        }

        /// <summary>
        /// Parks the submarine so a placed Z4 prop sits a few metres off the bow, and shoots it lamp-off
        /// and lamp-on.
        ///
        /// 🔴 WHY THIS SHOT EXISTS. Every other shot looks down a corridor whose nearest rock is 17-21 m
        /// out, which is past anything the lamp can reach - so they can only ever show the lamp doing
        /// nothing, and they cannot test the one claim Z4's design actually makes: that things become
        /// visible when you get close to them. HANDOFF §4-D also records that Z4's placed items (성게 3,
        /// 문어 1) have never been checked under the current lighting by anybody. This answers both.
        ///
        /// Props are found by elimination rather than by name: everything in the cave is CaveShell
        /// except the placed content, so any other collider near the route is a prop.
        /// </summary>
        private static string CaptureNearestProp(CaptureRig rig, GameObject submarine, UnderwaterZoneDirector director,
            Light headlight, Transform seat, string outputDirectory)
        {
            var report = new StringBuilder();

            const float StandOffMeters = 6f;
            Collider prop = null;
            float propRouteDistance = 0f;

            for (float distance = 270f; distance <= 345f && prop == null; distance += 5f)
            {
                (Vector3 probePosition, Quaternion _) = ResolveRoutePose(distance);
                foreach (Collider candidate in Physics.OverlapSphere(probePosition, 22f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (candidate.name.StartsWith("CaveShell", StringComparison.Ordinal))
                        continue;
                    if (candidate.transform.IsChildOf(submarine.transform))
                        continue;

                    prop = candidate;
                    propRouteDistance = distance;
                    break;
                }
            }

            if (prop == null)
            {
                report.AppendLine("  Z4_prop: no placed prop found within 22 m of the route between 270 and 345 m");
                return report.ToString();
            }

            (Vector3 routePosition, Quaternion routeRotation) = ResolveRoutePose(propRouteDistance);
            Vector3 propPosition = prop.bounds.center;

            // Sit the sub back along the line from the prop so the bow is StandOff metres short of it,
            // pointing straight at it. Height comes from the route so the pose stays plausible.
            Vector3 approach = propPosition - routePosition;
            approach.y = 0f;
            if (approach.sqrMagnitude < 1e-4f)
                approach = routeRotation * Vector3.forward;
            approach.Normalize();

            // The bow tip is 1.058 local units ahead of the root, and the root is scaled 2.
            const float BowOffsetMeters = 2.116f;
            Vector3 subPosition = propPosition - approach * (StandOffMeters + BowOffsetMeters);
            subPosition.y = routePosition.y;
            submarine.transform.SetPositionAndRotation(subPosition,
                Quaternion.LookRotation((propPosition - subPosition).normalized, Vector3.up));

            Vector3 eye = seat.position + Vector3.up * EyeHeightMeters;
            rig.camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((propPosition - eye).normalized, Vector3.up));

            director.EvaluateAndApplyAt(eye);
            rig.camera.backgroundColor = director.CurrentProfile.backgroundColor;

            headlight.enabled = false;
            Measurement off = rig.RenderAndMeasure(Path.Combine(outputDirectory, "Z4_prop_lamp_off.png"));
            headlight.enabled = true;
            Measurement on = rig.RenderAndMeasure(Path.Combine(outputDirectory, "Z4_prop_lamp_on.png"));

            report.AppendLine($"  Z4_prop '{prop.name}' at route {propRouteDistance:0} m, " +
                              $"{Vector3.Distance(headlight.transform.position, propPosition):0.0} m from the lamp, " +
                              $"{Vector3.Distance(eye, propPosition):0.0} m from the eye");
            report.AppendLine($"    lampOff mean={Fmt(off.mean)} max={Fmt(off.max)} nonBlack={off.nonBlackFraction:P1}");
            report.AppendLine($"    lampOn  mean={Fmt(on.mean)} max={Fmt(on.max)} nonBlack={on.nonBlackFraction:P1}");
            return report.ToString();
        }

        public static string BeamSurvey()
        {
            GameObject submarine = SubmarineHeadlightBatch.FindSubmarineRoot();
            if (submarine == null)
                throw new InvalidOperationException("No Submarine_final root in the open scene.");

            Light headlight = FindHeadlight(submarine);
            if (headlight == null)
                throw new InvalidOperationException("No spot Light under the submarine.");

            Transform seat = ResolveSeat(submarine.transform);

            var report = new StringBuilder();
            report.AppendLine("SUBMARINE_HEADLIGHT_SURVEY");

            Vector3 restorePosition = submarine.transform.position;
            Quaternion restoreRotation = submarine.transform.rotation;

            try
            {
                foreach (Shot shot in Shots)
                {
                    (Vector3 position, Quaternion rotation) = ResolveRoutePose(shot.routeDistanceMeters);
                    submarine.transform.SetPositionAndRotation(position, rotation);

                    report.AppendLine($"  --- {shot.name} ---");
                    report.Append(ProbeBeamContents(headlight));

                    // Lateral clearance answers "can I see the wall I am about to hit", which is a
                    // different question from anything a forward-facing beam reports.
                    Vector3 eye = seat.position + Vector3.up * EyeHeightMeters;
                    foreach ((string label, Vector3 direction) side in new[]
                             {
                                 ("left90", -submarine.transform.right),
                                 ("right90", submarine.transform.right),
                                 ("down90", -submarine.transform.up),
                             })
                    {
                        report.AppendLine(Physics.Raycast(eye, side.direction, out RaycastHit hit, 200f, ~0,
                                              QueryTriggerInteraction.Ignore)
                            ? $"    lateral {side.label}: {hit.distance:0.0} m to {hit.collider.name}"
                            : $"    lateral {side.label}: no hit within 200 m");
                    }
                }

                return report.ToString();
            }
            finally
            {
                submarine.transform.SetPositionAndRotation(restorePosition, restoreRotation);
            }
        }

        /// <summary>
        /// What the beam actually reaches, and whether that surface is allowed to receive this light.
        /// URP light layers are enabled on PC_RPAsset, so a renderer whose renderingLayerMask does not
        /// overlap the lamp's is never lit however bright the lamp gets - which looks identical to a
        /// lamp that is merely too weak. Reporting both separates them.
        /// </summary>
        private static string ProbeBeamContents(Light lamp)
        {
            var report = new StringBuilder();
            Transform t = lamp.transform;

            report.AppendLine($"  beamProbe origin={Fmt(t.position)} spotAngle={lamp.spotAngle} " +
                              $"renderingLayerMask={lamp.renderingLayerMask}");

            foreach ((string label, Vector3 direction) ray in new[]
                     {
                         ("centre", t.forward),
                         ("down", Quaternion.AngleAxis(lamp.spotAngle * 0.5f, t.right) * t.forward),
                         ("up", Quaternion.AngleAxis(-lamp.spotAngle * 0.5f, t.right) * t.forward),
                         ("left", Quaternion.AngleAxis(-lamp.spotAngle * 0.5f, t.up) * t.forward),
                         ("right", Quaternion.AngleAxis(lamp.spotAngle * 0.5f, t.up) * t.forward),
                     })
            {
                if (!Physics.Raycast(t.position, ray.direction, out RaycastHit hit, lamp.range, ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    report.AppendLine($"    {ray.label}: no hit within {lamp.range} m");
                    continue;
                }

                var renderer = hit.collider.GetComponentInParent<Renderer>();
                string rendererInfo = renderer == null
                    ? "renderer=none"
                    : $"renderer={renderer.name} renderingLayerMask={renderer.renderingLayerMask} " +
                      $"material={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null")}";

                report.AppendLine($"    {ray.label}: {hit.distance:0.0} m collider={hit.collider.name} {rendererInfo}");
            }

            return report.ToString();
        }

        /// <summary>Camera, render texture and readback buffer, torn down together.</summary>
        private sealed class CaptureRig : IDisposable
        {
            public readonly Camera camera;
            private readonly GameObject cameraObject;
            private readonly RenderTexture renderTexture;
            private readonly Texture2D readable;

            public CaptureRig()
            {
                cameraObject = new GameObject("HeadlightReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 1200f;
                camera.allowHDR = true;
                camera.allowMSAA = true;

                var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
                cameraData.requiresDepthOption = CameraOverrideOption.On;

                renderTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "HeadlightReviewRT",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                readable = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            /// <summary>
            /// Renders one frame, writes it, and measures it.
            ///
            /// 🔴 MEAN IS NOT ENOUGH, and this cost a whole diagnostic pass to learn. A headlight makes
            /// a small bright pool in a dark frame: turning a 6000 cd lamp on moved the whole-frame mean
            /// from 2.78 to 2.79 while moving the brightest pixel from 25 to 214. Reporting only the
            /// mean says "the lamp does nothing" about a lamp that is plainly working, so max and the
            /// non-black fraction ship alongside it.
            /// </summary>
            public Measurement RenderAndMeasure(string path)
            {
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                readable.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                readable.Apply(false, false);
                RenderTexture.active = previous;

                File.WriteAllBytes(path, readable.EncodeToPNG());

                Color32[] pixels = readable.GetPixels32();
                double r = 0d, g = 0d, b = 0d;
                byte mr = 0, mg = 0, mb = 0;
                int nonBlack = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    if (pixel.r > mr) mr = pixel.r;
                    if (pixel.g > mg) mg = pixel.g;
                    if (pixel.b > mb) mb = pixel.b;
                    if (pixel.r > 0 || pixel.g > 0 || pixel.b > 0)
                        nonBlack++;
                }

                return new Measurement(
                    new Vector3((float)(r / pixels.Length), (float)(g / pixels.Length), (float)(b / pixels.Length)),
                    new Vector3(mr, mg, mb),
                    nonBlack / (float)pixels.Length);
            }

            public void Dispose()
            {
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Position and heading on the main route at <paramref name="distanceMeters"/>.</summary>
        private static (Vector3, Quaternion) ResolveRoutePose(float distanceMeters)
        {
            // UnderwaterEnvironmentBuilder.FindMainRoute is internal to Varco.Underwater.Editor, so the
            // same search is repeated here rather than widening that assembly's surface for a tool.
            foreach (CaveRoute candidate in UnityEngine.Object.FindObjectsByType<CaveRoute>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (CaveRouteSplineDefinition definition in candidate.Definitions)
                {
                    if (!definition.isMainRoute || definition.sections == null || definition.sections.Count == 0)
                        continue;

                    float t = candidate.EvaluateTAtDistance(definition.splineIndex, distanceMeters);
                    Vector3 position = candidate.Container.EvaluatePosition(definition.splineIndex, t);
                    Vector3 tangent = ((Vector3)candidate.Container.EvaluateTangent(definition.splineIndex, t)).normalized;
                    if (tangent.sqrMagnitude < 1e-6f)
                        tangent = Vector3.forward;

                    // The hull's local +Z is the bow (전면 sits at +z, 후면 at -z), so aiming the root
                    // down the tangent is the same as flying the route.
                    return (position, Quaternion.LookRotation(tangent, Vector3.up));
                }
            }
            throw new InvalidOperationException("No main CaveRoute with sections in the open scene.");
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Headlight capture needs a graphics device. Run batchmode WITHOUT -nographics.");
        }

        private static string CreateOutputDirectory(string suffix)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string directory = Path.Combine(projectRoot, ArtifactFolder,
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + suffix);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static Light FindHeadlight(GameObject submarine)
        {
            foreach (Light light in submarine.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Spot)
                    return light;
            }
            return null;
        }

        /// <summary>
        /// The pilot's viewpoint. Solo seat first - it is the centre one, so a shot from it is not
        /// biased towards either wall.
        /// </summary>
        private static Transform ResolveSeat(Transform root)
        {
            Transform solo = SubmarineHeadlightBatch.FindChildByName(root, "SeatPoint_Solo");
            if (solo != null)
                return solo;

            List<Transform> seats = SubmarineHeadlightBatch.FindChildrenByPrefix(root, "SeatPoint");
            if (seats.Count > 0)
                return seats[0];

            throw new InvalidOperationException("No SeatPoint under the submarine to shoot from.");
        }

        private static float Luma(Vector3 rgb)
        {
            return 0.2126f * rgb.x + 0.7152f * rgb.y + 0.0722f * rgb.z;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                   $"{v.y.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                   $"{v.z.ToString("0.##", CultureInfo.InvariantCulture)})";
        }
    }
}
