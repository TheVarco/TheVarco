using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Renders the dressing itself: two views per zone, taken from the route centre line with the
    /// underwater fog switched off and a neutral key light.
    ///
    /// Neither existing capture answers "does this zone read as well dressed". CaveReviewCapture is a
    /// geometry review - flat cyan, half its shots wireframe - and UnderwaterReviewCapture renders the
    /// shipping look, which in Z4 is four metres of visibility by design. Density, orientation and
    /// silhouette are the things a scatter pass gets wrong, and they are exactly what those two hide.
    /// </summary>
    public static class CaveDecorReviewCapture
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string OutputDirectory = "Artifacts/CaveDecor/review";
        private const int ShotWidth = 640;
        private const int ShotHeight = 360;
        private const int SheetColumns = 2;

        [MenuItem("Tools/Underwater Cave/Decor/6 - 데코 리뷰 캡처 (안개 없음)")]
        public static void CaptureInteractive()
        {
            Debug.Log(Capture());
        }

        public static void CaptureBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Capture());
        }

        /// <summary>
        /// Renders what the scene view itself will show: the scene's own fog and ambient, the global
        /// post-processing volume, and no underwater screen pass (that one is gated on a global only a
        /// playing director raises). Use it to check an atmosphere change against the editing
        /// experience, not just against the shipping look.
        /// </summary>
        public static void CaptureSceneViewProxyBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Capture(sceneViewProxy: true));
        }

        private readonly struct Shot
        {
            public readonly string name;
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public Shot(string name, Vector3 position, Quaternion rotation)
            {
                this.name = name;
                this.position = position;
                this.rotation = rotation;
            }
        }

        public static string Capture(bool sceneViewProxy = false)
        {
            var report = new StringBuilder();
            report.AppendLine(sceneViewProxy
                ? "===== CAVE DECOR REVIEW CAPTURE (scene view proxy) ====="
                : "===== CAVE DECOR REVIEW CAPTURE =====");

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell collider in the scene");
                return report.ToString();
            }

            List<Shot> shots = BuildShots(context, report);
            if (shots.Count == 0)
            {
                report.AppendLine("FAIL: no viewpoints");
                return report.ToString();
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string directory = Path.Combine(root,
                (sceneViewProxy ? OutputDirectory + "-sceneview" : OutputDirectory)
                    .Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);

            // The whole point is to see past the fog, so it comes off - and goes back on in the finally,
            // because RenderSettings is scene state and leaving it off would silently ship a clear cave.
            bool fog = RenderSettings.fog;
            Color ambient = RenderSettings.ambientLight;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;

            var files = new List<string>();
            GameObject rig = null;
            RenderTexture target = null;
            Texture2D readable = null;

            try
            {
                if (!sceneViewProxy)
                {
                    RenderSettings.fog = false;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.42f, 1f);
                }

                rig = new GameObject("CaveDecorReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = rig.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
                camera.fieldOfView = 70f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 600f;
                camera.allowHDR = sceneViewProxy;

                if (sceneViewProxy)
                {
                    // The scene view renders the global volume like any other camera, so the proxy has
                    // to as well - otherwise it reports on a post stack the artist will never see.
                    var cameraData = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    cameraData.renderPostProcessing = true;
                    cameraData.volumeLayerMask = ~0;
                }

                Light key = rig.AddComponent<Light>();
                key.type = LightType.Point;
                key.color = new Color(0.85f, 0.93f, 1f, 1f);
                key.intensity = 6f;
                key.range = 140f;
                key.shadows = LightShadows.None;

                target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                camera.targetTexture = target;
                readable = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                for (int i = 0; i < shots.Count; i++)
                {
                    rig.transform.SetPositionAndRotation(shots[i].position, shots[i].rotation);
                    camera.Render();

                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readable.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0, false);
                    readable.Apply(false, false);
                    RenderTexture.active = previous;

                    string file = $"{i + 1:D2}_{shots[i].name}.png";
                    File.WriteAllBytes(Path.Combine(directory, file), readable.EncodeToPNG());
                    files.Add(file);
                    report.AppendLine($"[{i + 1:D2}] {shots[i].name}");
                }
            }
            finally
            {
                RenderTexture.active = null;
                if (readable != null) Object.DestroyImmediate(readable);
                if (target != null)
                {
                    if (rig != null)
                    {
                        Camera camera = rig.GetComponent<Camera>();
                        if (camera != null) camera.targetTexture = null;
                    }
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (rig != null) Object.DestroyImmediate(rig);

                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
            }

            string sheet = Path.Combine(directory, "00_contact_sheet.png");
            CreateContactSheet(directory, files, sheet);
            report.AppendLine($"CAVE_DECOR_REVIEW PASS shots={files.Count} contactSheet={sheet}");
            return report.ToString();
        }

        /// <summary>
        /// Two shots per zone, at a third and two thirds along it, both looking down the route. Sampling
        /// inside the section rather than at its boundary keeps the camera out of the transition where
        /// one zone's dressing and the next zone's are both in frame.
        /// </summary>
        private static List<Shot> BuildShots(CaveDecorContext context, StringBuilder report)
        {
            var shots = new List<Shot>();

            const string mainRoute = "MainRoute";
            if (!context.TryGetRoute(mainRoute, out _, out CaveRoutePolyline polyline,
                    out CaveRouteSplineDefinition definition))
            {
                report.AppendLine($"FAIL: no route '{mainRoute}'");
                return shots;
            }

            foreach (CaveRouteSection section in definition.sections)
            {
                float start = section.startDistanceMeters >= 0f ? section.startDistanceMeters : 0f;
                float end = section.endDistanceMeters >= 0f ? section.endDistanceMeters : polyline.Length;

                foreach (float fraction in new[] { 0.33f, 0.66f })
                {
                    float distance = Mathf.Lerp(start, end, fraction);
                    polyline.Sample(distance, out Vector3 position, out Vector3 tangent, out _);

                    // Backed off and lifted a little: standing exactly on the centre line looking along
                    // it frames the far wall and nothing else.
                    Vector3 forward = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
                    Vector3 eye = position - forward * 6f + Vector3.up * 2.5f;
                    shots.Add(new Shot($"{section.zoneId}_{Mathf.RoundToInt(fraction * 100f)}",
                        eye, Quaternion.LookRotation(forward, Vector3.up)));
                }
            }

            return shots;
        }

        private static void CreateContactSheet(string directory, List<string> files, string outputPath)
        {
            if (files.Count == 0)
                return;

            int rows = Mathf.CeilToInt(files.Count / (float)SheetColumns);
            var sheet = new Texture2D(SheetColumns * ShotWidth, rows * ShotHeight, TextureFormat.RGB24, false);
            var background = new Color[sheet.width * sheet.height];
            for (int i = 0; i < background.Length; i++)
                background[i] = new Color(0.02f, 0.03f, 0.05f, 1f);
            sheet.SetPixels(background);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        tile.LoadImage(File.ReadAllBytes(Path.Combine(directory, files[i])), false);
                        int column = i % SheetColumns;
                        int row = rows - 1 - i / SheetColumns;
                        sheet.SetPixels(column * ShotWidth, row * ShotHeight, ShotWidth, ShotHeight, tile.GetPixels());
                    }
                    finally
                    {
                        Object.DestroyImmediate(tile);
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
    }
}
