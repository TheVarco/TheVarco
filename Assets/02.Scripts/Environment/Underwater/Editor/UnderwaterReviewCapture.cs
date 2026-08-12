using System;
using System.Collections.Generic;
using System.IO;
using CaveBlockout;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Renders one frame per cave zone with the underwater atmosphere evaluated at that position, for
    /// side-by-side comparison against UnderwaterCaveLevelGuide/04_zone_01_deep_start.png through
    /// 09_zone_06_exit_throat.png.
    ///
    /// CaveReviewCapture cannot be reused for this: it attaches its own bright directional light to the
    /// capture camera and renders with HDR and post-processing off, because its job is to make blockout
    /// geometry legible. That is the opposite of what verifying a dark underwater look needs.
    /// </summary>
    public static class UnderwaterReviewCapture
    {
        public const string ArtifactFolder = "Artifacts/UnderwaterReview";
        private const int CaptureWidth = 640;
        private const int CaptureHeight = 360;
        private const int ContactSheetColumns = 3;

        private readonly struct Viewpoint
        {
            public readonly string name;
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public Viewpoint(string name, Vector3 position, Quaternion rotation)
            {
                this.name = name;
                this.position = position;
                this.rotation = rotation;
            }
        }

        [MenuItem("Tools/Underwater Cave/Capture Underwater Review")]
        public static void CaptureInteractive()
        {
            string contactSheet = Capture();
            if (!string.IsNullOrEmpty(contactSheet))
                EditorUtility.RevealInFinder(contactSheet);
        }

        public static string Capture(string explicitOutputDirectory = null)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Underwater review capture needs a graphics device. Run batchmode without -nographics.");

            CaveRoute mainRoute = UnderwaterEnvironmentBuilder.FindMainRoute();
            if (mainRoute == null)
                throw new InvalidOperationException("No main CaveRoute found in the active scene.");

            var director = UnityEngine.Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                throw new InvalidOperationException(
                    "No UnderwaterZoneDirector in the scene. Run Tools/Underwater Cave/Apply Underwater Environment first.");

            List<Viewpoint> viewpoints = BuildViewpoints(mainRoute);
            if (viewpoints.Count == 0)
                throw new InvalidOperationException("The main route has no zone sections to capture.");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string outputDirectory = explicitOutputDirectory ?? Path.Combine(projectRoot, ArtifactFolder, runId);
            Directory.CreateDirectory(outputDirectory);

            GameObject cameraObject = null;
            RenderTexture renderTexture = null;
            Texture2D readable = null;
            var frames = new List<Texture2D>();
            var files = new List<string>();

            try
            {
                cameraObject = new GameObject("UnderwaterReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = cameraObject.AddComponent<Camera>();

                // Deliberately mirrors MainMap's game camera: HDR on, post-processing on, no extra light.
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
                    name = "UnderwaterReviewRT",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                readable = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                for (int i = 0; i < viewpoints.Count; i++)
                {
                    Viewpoint viewpoint = viewpoints[i];
                    cameraObject.transform.SetPositionAndRotation(viewpoint.position, viewpoint.rotation);

                    // Evaluate the zone atmosphere for the shot position, not for wherever the game
                    // camera happens to be parked.
                    director.EvaluateAndApplyAt(viewpoint.position);

                    // The director drives the scene's game camera, not this throwaway one, so the
                    // zone's background colour has to be copied across by hand.
                    camera.backgroundColor = director.CurrentProfile.backgroundColor;

                    camera.Render();

                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = renderTexture;
                    readable.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                    readable.Apply(false, false);
                    RenderTexture.active = previous;

                    string fileName = $"{i + 1:D2}_{Sanitize(viewpoint.name)}.png";
                    string filePath = Path.Combine(outputDirectory, fileName);
                    File.WriteAllBytes(filePath, readable.EncodeToPNG());
                    files.Add(filePath);

                    var frame = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    frame.SetPixels32(readable.GetPixels32());
                    frame.Apply(false, false);
                    frames.Add(frame);
                }

                string contactSheetPath = Path.Combine(outputDirectory, "00_contact_sheet.png");
                WriteContactSheet(frames, contactSheetPath);

                Debug.Log($"UNDERWATER_REVIEW_CAPTURE shots={files.Count} directory={outputDirectory}");
                return contactSheetPath;
            }
            finally
            {
                // Put the scene's fog and ambient back where they were before the sweep.
                director.ClearUnderwaterEffect();

                foreach (Texture2D frame in frames)
                {
                    if (frame != null)
                        UnityEngine.Object.DestroyImmediate(frame);
                }
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

        private static List<Viewpoint> BuildViewpoints(CaveRoute mainRoute)
        {
            var viewpoints = new List<Viewpoint>();

            CaveRouteSplineDefinition definition = null;
            foreach (CaveRouteSplineDefinition candidate in mainRoute.Definitions)
            {
                if (!candidate.isMainRoute || candidate.sections == null || candidate.sections.Count == 0)
                    continue;
                definition = candidate;
                break;
            }

            if (definition == null)
                return viewpoints;

            List<CaveRouteSection> sections = definition.sections;
            for (int i = 0; i < sections.Count; i++)
            {
                CaveRouteSection section = sections[i];
                float startT = mainRoute.ResolveSectionStartT(definition, section);
                float endT = mainRoute.ResolveSectionEndT(definition, section);

                viewpoints.Add(CreateViewpoint(mainRoute, definition.splineIndex,
                    Mathf.Lerp(startT, endT, 0.5f), section.zoneId + "_Mid_Forward"));

                if (i < sections.Count - 1)
                {
                    viewpoints.Add(CreateViewpoint(mainRoute, definition.splineIndex, endT,
                        section.zoneId + "_to_" + sections[i + 1].zoneId + "_Boundary"));
                }
            }

            return viewpoints;
        }

        private static Viewpoint CreateViewpoint(CaveRoute route, int splineIndex, float t, string name)
        {
            Vector3 position = route.Container.EvaluatePosition(splineIndex, t);
            Vector3 tangent = ((Vector3)route.Container.EvaluateTangent(splineIndex, t)).normalized;
            if (tangent.sqrMagnitude < 1e-6f)
                tangent = Vector3.forward;

            // Sit a little above the centre line so the floor is in frame, matching how the zone
            // concept art is composed.
            float height = route.EvaluateHeight(splineIndex, t);
            Vector3 eye = position + Vector3.up * (height * 0.12f);
            return new Viewpoint(name, eye, Quaternion.LookRotation(tangent, Vector3.up));
        }

        private static void WriteContactSheet(List<Texture2D> frames, string path)
        {
            if (frames.Count == 0)
                return;

            int columns = Mathf.Min(ContactSheetColumns, frames.Count);
            int rows = Mathf.CeilToInt(frames.Count / (float)columns);
            int width = columns * CaptureWidth;
            int height = rows * CaptureHeight;

            var sheet = new Texture2D(width, height, TextureFormat.RGB24, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var background = new Color32[width * height];
            for (int i = 0; i < background.Length; i++)
                background[i] = new Color32(8, 10, 14, 255);
            sheet.SetPixels32(background);

            for (int i = 0; i < frames.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                // Texture rows run bottom-up, so fill from the top of the sheet downwards.
                int originY = height - (row + 1) * CaptureHeight;
                sheet.SetPixels32(column * CaptureWidth, originY, CaptureWidth, CaptureHeight, frames[i].GetPixels32());
            }

            sheet.Apply(false, false);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        private static string Sanitize(string value)
        {
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetterOrDigit(characters[i]) && characters[i] != '_' && characters[i] != '-')
                    characters[i] = '_';
            }
            return new string(characters);
        }
    }
}
