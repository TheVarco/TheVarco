using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CaveBlockout.Editor
{
    [Serializable]
    public sealed class CaveReviewShotManifest
    {
        public string name;
        public string file;
        public string sha256;
        public Vector3 position;
        public Vector3 eulerAngles;
        public bool wireframe;
    }

    [Serializable]
    public sealed class CaveReviewNoiseManifest
    {
        public bool enabled;
        public int seed;
        public float amplitudeMeters;
        public float strengthGain;
        public float maximumDisplacementRatio;
        public float wavelengthMeters;
        public int octaves;
        public float lacunarity;
        public float persistence;
        public float floorMultiplier;
        public float wallMultiplier;
        public float ceilingMultiplier;
        public float portalFadeDistance;
        public bool visualDetailEnabled;
        public float visualDetailAmplitude;
        public float visualDetailWavelength;
    }

    [Serializable]
    public sealed class CaveReviewManifest
    {
        public string createdUtc;
        public string unityVersion;
        public string gitCommit;
        public string scenePath;
        public bool routeValidationPassed;
        public bool clearanceValidationPassed;
        public string clearanceDetails;
        public int triangleCount;
        public CaveReviewNoiseManifest noise;
        public List<CaveReviewShotManifest> shots = new List<CaveReviewShotManifest>();
    }

    public readonly struct CaveReviewViewpoint
    {
        public readonly string name;
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly bool wireframe;

        public CaveReviewViewpoint(string name, Vector3 position, Quaternion rotation, bool wireframe = false)
        {
            this.name = name;
            this.position = position;
            this.rotation = rotation;
            this.wireframe = wireframe;
        }
    }

    public readonly struct CaveReviewCaptureResult
    {
        public readonly string outputDirectory;
        public readonly string manifestPath;
        public readonly string contactSheetPath;
        public readonly int shotCount;

        public CaveReviewCaptureResult(string outputDirectory, string manifestPath, string contactSheetPath, int shotCount)
        {
            this.outputDirectory = outputDirectory;
            this.manifestPath = manifestPath;
            this.contactSheetPath = contactSheetPath;
            this.shotCount = shotCount;
        }
    }

    public static class CaveReviewCapture
    {
        public const int CaptureWidth = 640;
        public const int CaptureHeight = 360;
        public const string ArtifactFolder = "Artifacts/CaveReview";

        [MenuItem("Tools/Underwater Cave/Capture Review Sweep")]
        public static void CaptureCurrentSceneMenu()
        {
            CaveReviewCaptureResult result = CaptureCurrentScene();
            EditorUtility.RevealInFinder(result.contactSheetPath);
        }

        public static void CaptureMainMapBatch()
        {
            EditorSceneManager.OpenScene(CaveBlockoutBuilder.MainMapPath, OpenSceneMode.Single);
            CaveReviewCaptureResult result = CaptureCurrentScene();
            Debug.Log($"CAVE_REVIEW_CAPTURE PASS shots={result.shotCount} manifest={result.manifestPath} contactSheet={result.contactSheetPath}");
        }

        public static CaveReviewCaptureResult CaptureCurrentScene(string explicitOutputDirectory = null)
        {
            Scene scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Review capture requires a graphics device. Run batchmode without -nographics.");
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            if (mainRoute == null || branches == null)
                throw new InvalidOperationException("Cave routes were not found in the active scene.");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + ShortCommit(projectRoot);
            string outputDirectory = explicitOutputDirectory ?? Path.Combine(projectRoot, ArtifactFolder, runId);
            Directory.CreateDirectory(outputDirectory);

            List<CaveReviewViewpoint> viewpoints = BuildViewpoints(mainRoute, branches);
            CaveValidationResult validation = CaveBlockoutValidator.Validate(mainRoute, branches);
            bool clearancePassed = CaveClearanceValidator.ValidateAll(mainRoute, branches, out string clearanceDetails);
            CaveValidationSummary summary = Object.FindFirstObjectByType<CaveValidationSummary>(FindObjectsInactive.Include);
            CaveReviewManifest manifest = new CaveReviewManifest
            {
                createdUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                gitCommit = FullCommit(projectRoot),
                scenePath = scene.path,
                routeValidationPassed = validation.Passed,
                clearanceValidationPassed = clearancePassed,
                clearanceDetails = clearanceDetails,
                triangleCount = summary != null ? summary.triangleCount : -1,
                noise = BuildNoiseManifest(mainRoute.NoiseSettings)
            };

            GameObject cameraObject = null;
            RenderTexture renderTexture = null;
            Texture2D readable = null;
            bool previousWireframe = GL.wireframe;
            try
            {
                cameraObject = new GameObject("CaveReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f, 1f);
                camera.fieldOfView = 78f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 1400f;
                camera.allowHDR = false;
                camera.allowMSAA = true;
                Light reviewLight = cameraObject.AddComponent<Light>();
                reviewLight.type = LightType.Directional;
                reviewLight.color = new Color(0.65f, 0.8f, 1f, 1f);
                reviewLight.intensity = 1.8f;
                reviewLight.shadows = LightShadows.None;

                renderTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "CaveReviewRT",
                    antiAliasing = 4,
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
                    CaveReviewViewpoint viewpoint = viewpoints[i];
                    cameraObject.transform.SetPositionAndRotation(viewpoint.position, viewpoint.rotation);
                    string fileName = $"{i + 1:D2}_{Sanitize(viewpoint.name)}.png";
                    string filePath = Path.Combine(outputDirectory, fileName);
                    GL.wireframe = viewpoint.wireframe;
                    camera.Render();
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = renderTexture;
                    readable.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                    readable.Apply(false, false);
                    RenderTexture.active = previous;
                    byte[] png = readable.EncodeToPNG();
                    File.WriteAllBytes(filePath, png);
                    manifest.shots.Add(new CaveReviewShotManifest
                    {
                        name = viewpoint.name,
                        file = fileName,
                        sha256 = ComputeSha256(png),
                        position = viewpoint.position,
                        eulerAngles = viewpoint.rotation.eulerAngles,
                        wireframe = viewpoint.wireframe
                    });
                }
            }
            finally
            {
                GL.wireframe = previousWireframe;
                RenderTexture.active = null;
                if (readable != null) Object.DestroyImmediate(readable);
                if (renderTexture != null)
                {
                    if (cameraObject != null)
                    {
                        Camera camera = cameraObject.GetComponent<Camera>();
                        if (camera != null) camera.targetTexture = null;
                    }
                    renderTexture.Release();
                    Object.DestroyImmediate(renderTexture);
                }
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            }

            if (!wasDirty && scene.isDirty)
                throw new InvalidOperationException("Review capture dirtied the active scene.");

            string contactSheetPath = Path.Combine(outputDirectory, "contact_sheet.png");
            CreateContactSheet(outputDirectory, manifest.shots.Select(shot => shot.file).ToList(), contactSheetPath);
            string manifestPath = Path.Combine(outputDirectory, "manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            return new CaveReviewCaptureResult(outputDirectory, manifestPath, contactSheetPath, manifest.shots.Count);
        }

        private static CaveReviewNoiseManifest BuildNoiseManifest(CaveNoiseSettings settings)
        {
            return new CaveReviewNoiseManifest
            {
                enabled = settings.enabled,
                seed = settings.seed,
                amplitudeMeters = settings.amplitudeMeters,
                strengthGain = settings.strengthGain,
                maximumDisplacementRatio = settings.maximumDisplacementRatio,
                wavelengthMeters = settings.wavelengthMeters,
                octaves = settings.octaves,
                lacunarity = settings.lacunarity,
                persistence = settings.persistence,
                floorMultiplier = settings.floorMultiplier,
                wallMultiplier = settings.wallMultiplier,
                ceilingMultiplier = settings.ceilingMultiplier,
                portalFadeDistance = settings.portalFadeDistance,
                visualDetailEnabled = settings.visualDetailEnabled,
                visualDetailAmplitude = settings.visualDetailAmplitude,
                visualDetailWavelength = settings.visualDetailWavelength
            };
        }

        public static List<CaveReviewViewpoint> BuildViewpoints(CaveRoute mainRoute, CaveRoute branches)
        {
            List<CaveReviewViewpoint> views = new List<CaveReviewViewpoint>();
            Bounds generatedBounds = CalculateGeneratedBounds();
            Vector3 overviewPosition = generatedBounds.center + new Vector3(generatedBounds.extents.x * 1.2f, generatedBounds.extents.y * 1.8f + 80f, -generatedBounds.extents.z * 0.8f);
            views.Add(LookAt("Overview_Isometric", overviewPosition, generatedBounds.center, Vector3.up));

            CaveRouteSplineDefinition mainDefinition = mainRoute.Definitions.First(definition => definition.isMainRoute);
            foreach (CaveRouteSection section in mainDefinition.sections)
            {
                float t = Mathf.Lerp(mainRoute.ResolveSectionStartT(mainDefinition, section),
                    mainRoute.ResolveSectionEndT(mainDefinition, section), 0.5f);
                views.Add(InsideSpline(mainRoute, mainDefinition.splineIndex, t, section.zoneId + "_Mid_Forward"));
            }

            for (int i = 0; i < mainDefinition.sections.Count - 1; i++)
            {
                CaveRouteSection current = mainDefinition.sections[i];
                CaveRouteSection next = mainDefinition.sections[i + 1];
                float t = mainRoute.ResolveSectionEndT(mainDefinition, current);
                Vector3 center = mainRoute.Container.EvaluatePosition(mainDefinition.splineIndex, t);
                Vector3 tangent = ((Vector3)mainRoute.Container.EvaluateTangent(mainDefinition.splineIndex, t)).normalized;
                Vector3 up = ((Vector3)mainRoute.Container.EvaluateUpVector(mainDefinition.splineIndex, t)).normalized;
                Vector3 cameraPosition = center - tangent * 8f;
                string boundaryName = current.zoneId + "_to_" + next.zoneId + "_Boundary";
                views.Add(LookAt(boundaryName, cameraPosition, center + tangent * 3f, up));
                views.Add(LookAt(boundaryName + "_Wire", cameraPosition, center + tangent * 3f, up, true));
            }

            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                float t = mainRoute.ResolvePortalT(portal);
                Vector3 center = mainRoute.Container.EvaluatePosition(mainDefinition.splineIndex, t);
                Vector3 tangent = ((Vector3)mainRoute.Container.EvaluateTangent(mainDefinition.splineIndex, t)).normalized;
                Vector3 up = ((Vector3)mainRoute.Container.EvaluateUpVector(mainDefinition.splineIndex, t)).normalized;
                Vector3 direction = mainRoute.transform.TransformDirection(portal.direction).normalized;
                Vector3 cameraPosition = center - tangent * 9f;
                views.Add(LookAt(portal.zoneId + "_Portal_FromMain", cameraPosition, center + direction * 10f, up));
                views.Add(LookAt(portal.zoneId + "_Portal_Wire", cameraPosition, center + direction * 10f, up, true));
                Vector3 profilePosition = center + tangent * 7f + up * 4f;
                views.Add(LookAt(portal.zoneId + "_Portal_Profile_Wire", profilePosition,
                    center + direction * CaveMeshGenerator.BranchCollarDepth, up, true));

                CaveRouteSplineDefinition branchDefinition = branches.Definitions.First(definition => definition.splineIndex == portal.branchSplineIndex);
                float branchLength = branches.Container.CalculateLength(portal.branchSplineIndex);
                float branchStartDistance = Mathf.Min(branchLength - 3f,
                    branchDefinition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth);
                float reviewDistance = Mathf.Min(branchLength - 2f, branchStartDistance + 8f);
                float branchReviewT = CaveMeshGenerator.FindTAtDistance(branches.Container,
                    portal.branchSplineIndex, 0f, 1f, reviewDistance);
                // Use the same safely traversable centerline region as the clearance test, beyond the welded collar.
                // The extra setback keeps the full aperture frame inside the review camera's field of view.
                Vector3 branchPosition = branches.Container.EvaluatePosition(portal.branchSplineIndex, branchReviewT);
                views.Add(LookAt(portal.zoneId + "_Portal_FromBranch_Wire", branchPosition, center, up, true));
            }

            foreach (CaveRouteSplineDefinition branchDefinition in branches.Definitions)
            {
                views.Add(DeadEndView(branches, branchDefinition, branchDefinition.routeId + "_DeadEnd"));
                views.Add(DeadEndView(branches, branchDefinition, branchDefinition.routeId + "_DeadEnd_Wire", true));
            }
            views.Add(InsideSpline(mainRoute, mainDefinition.splineIndex, 0.985f, "Z7_OpenExit"));
            return views;
        }

        private static CaveReviewViewpoint DeadEndView(CaveRoute route, CaveRouteSplineDefinition definition,
            string name, bool wireframe = false)
        {
            float length = route.Container.CalculateLength(definition.splineIndex);
            float reviewDistance = Mathf.Max(definition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 2f,
                length - 10f);
            float reviewT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex,
                0f, 1f, reviewDistance);
            Vector3 position = route.Container.EvaluatePosition(definition.splineIndex, reviewT);
            Vector3 endCenter = route.Container.EvaluatePosition(definition.splineIndex, 1f);
            Vector3 endTangent = ((Vector3)route.Container.EvaluateTangent(definition.splineIndex, 1f)).normalized;
            Vector3 up = ((Vector3)route.Container.EvaluateUpVector(definition.splineIndex, reviewT)).normalized;
            return LookAt(name, position, endCenter + endTangent * 2f, up, wireframe);
        }

        private static CaveReviewViewpoint InsideSpline(CaveRoute route, int splineIndex, float t, string name)
        {
            Vector3 position = route.Container.EvaluatePosition(splineIndex, t);
            Vector3 tangent = ((Vector3)route.Container.EvaluateTangent(splineIndex, t)).normalized;
            Vector3 up = ((Vector3)route.Container.EvaluateUpVector(splineIndex, t)).normalized;
            return new CaveReviewViewpoint(name, position, Quaternion.LookRotation(tangent, up));
        }

        private static CaveReviewViewpoint LookAt(string name, Vector3 position, Vector3 target, Vector3 up, bool wireframe = false)
        {
            Vector3 forward = (target - position).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            if (Vector3.Cross(forward, up).sqrMagnitude < 0.001f) up = Vector3.up;
            return new CaveReviewViewpoint(name, position, Quaternion.LookRotation(forward, up), wireframe);
        }

        private static Bounds CalculateGeneratedBounds()
        {
            GameObject root = GameObject.Find(CaveBlockoutBuilder.RootName);
            Renderer[] renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 100f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void CreateContactSheet(string directory, IReadOnlyList<string> files, string outputPath)
        {
            const int columns = 4;
            const int thumbWidth = 320;
            const int thumbHeight = 180;
            int rows = Mathf.CeilToInt(files.Count / (float)columns);
            Texture2D sheet = new Texture2D(columns * thumbWidth, rows * thumbHeight, TextureFormat.RGB24, false);
            Color32[] background = Enumerable.Repeat(new Color32(8, 12, 18, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(background);
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    Texture2D source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        source.LoadImage(File.ReadAllBytes(Path.Combine(directory, files[i])), false);
                        Color[] pixels = new Color[thumbWidth * thumbHeight];
                        for (int y = 0; y < thumbHeight; y++)
                        for (int x = 0; x < thumbWidth; x++)
                            pixels[y * thumbWidth + x] = source.GetPixelBilinear((x + 0.5f) / thumbWidth, (y + 0.5f) / thumbHeight);
                        int column = i % columns;
                        int row = rows - 1 - i / columns;
                        sheet.SetPixels(column * thumbWidth, row * thumbHeight, thumbWidth, thumbHeight, pixels);
                    }
                    finally
                    {
                        Object.DestroyImmediate(source);
                    }
                }
                sheet.Apply(false, false);
                File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        private static string FullCommit(string projectRoot)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory = projectRoot,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using Process process = Process.Start(info);
                string output = process?.StandardOutput.ReadToEnd().Trim();
                process?.WaitForExit(3000);
                return string.IsNullOrWhiteSpace(output) ? "unknown" : output;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ShortCommit(string projectRoot)
        {
            string commit = FullCommit(projectRoot);
            return commit.Length >= 8 ? commit.Substring(0, 8) : commit;
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        private static void FindRoutes(out CaveRoute mainRoute, out CaveRoute branches)
        {
            CaveRoute[] routes = Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            mainRoute = routes.FirstOrDefault(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute capturedMain = mainRoute;
            branches = routes.FirstOrDefault(route => route != capturedMain);
        }
    }
}
