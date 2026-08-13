using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Renders the play scene with the volume stack off and on, so the underwater look can be judged
    /// against the real symptom instead of against a guess.
    ///
    /// Why this exists: MainScene_final's camera shipped with renderPostProcessing = 0, which disabled
    /// the entire volume at runtime. No ACES tonemapping meant every value above 1 hard-clipped, and
    /// with ambient authored up to 9.96 linear in green and blue, near surfaces flattened to white-cyan
    /// while the screen-space extinction pass - a renderer feature, not a volume component - kept sight
    /// lines short. Bright and murky at once.
    ///
    /// The trap this tool was built to escape: both editor capture paths in this project force
    /// renderPostProcessing = true, so every screenshot taken from the editor showed a correctly
    /// tonemapped image that no player has ever seen. Two separate diagnoses were built on those
    /// screenshots and both were wrong. POST_OFF here is the frame the game actually renders.
    ///
    /// Three passes:
    ///   POST_OFF      - volume stack disabled. Reproduces the shipped bug.
    ///   POST_ON       - volume stack enabled. What the fix looks like.
    ///   NO_DIRECTOR   - control. No extinction, no fog, whatever ambient the scene was saved with.
    ///
    /// Nothing is saved. The director's own ClearUnderwaterEffect restores the scene's fog and ambient.
    /// </summary>
    public static class UnderwaterGradingCompare
    {
        private const string ScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const string RenderPipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string OutputFolder = "Artifacts/UnderwaterGrading";
        private const int Width = 1280;
        private const int Height = 720;

        /// <summary>Route distances picked where the complaint is loudest: bright zones, near-field rock.</summary>
        private static readonly (string label, float distance)[] Viewpoints =
        {
            ("Z1_start", 25f),
            ("Z2_basin", 90f),
            ("Z4_blackout", 300f),
            ("Z5_chimney", 400f),
            ("Z6_throat", 470f)
        };

        [MenuItem("Tools/Underwater Cave/Lighting/포스트 프로세싱 OFF vs ON 비교")]
        public static void CompareInteractive()
        {
            Debug.Log(Compare());
        }

        public static void CompareBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log(Compare());
        }

        public static string Compare()
        {
            var report = new StringBuilder();
            report.AppendLine("===== UNDERWATER POST OFF/ON COMPARE =====");

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
            if (pipeline != null)
            {
                int mode = new SerializedObject(pipeline).FindProperty("m_ColorGradingMode").intValue;
                report.AppendLine($"m_ColorGradingMode = {mode} " +
                                  $"({(mode == 0 ? "LowDynamicRange" : "HighDynamicRange")})");
            }

            CaveDecorContext context = CaveDecorContext.Create();
            CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
            if (polyline == null)
                return report.AppendLine("FAIL: no MainRoute in the scene").ToString();

            var director = Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                return report.AppendLine("FAIL: no UnderwaterZoneDirector in the scene").ToString();

            Transform submarine = GameObject.Find("Submarine_final") != null
                ? GameObject.Find("Submarine_final").transform
                : null;

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            Directory.CreateDirectory(root);

            var rig = new GameObject("~PostCompareCamera");
            var camera = rig.AddComponent<Camera>();
            var manifest = new StringBuilder();
            manifest.AppendLine("file\tpass\tlabel\tzone\tpostExposure\tambientSky");

            try
            {
                camera.enabled = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 3000f;
                camera.fieldOfView = 65f;
                camera.allowHDR = true;

                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                foreach (bool post in new[] { false, true })
                {
                    data.renderPostProcessing = post;
                    string pass = post ? "POST_ON" : "POST_OFF";

                    foreach ((string label, float distance) in Viewpoints)
                    {
                        polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out _);
                        camera.transform.position = centre;
                        camera.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
                        director.EvaluateAndApplyAt(centre);

                        string file = $"{label}__{pass}.png";
                        Render(camera, Path.Combine(root, file));
                        manifest.AppendLine($"{file}\t{pass}\t{label}\t{director.CurrentZoneId}\t" +
                                            $"{director.CurrentProfile.postExposure}\t" +
                                            $"{director.CurrentProfile.ambientSky}");
                        report.AppendLine($"  {file}  zone={director.CurrentZoneId}");

                        // Two metres off the rock face, looking at it. This is the reported symptom that
                        // a centreline shot cannot show: extinction is exp(-k*d), so at 2 m almost
                        // nothing is absorbed and the surface receives ambient nearly raw. The complaint
                        // was specifically "it gets extremely bright when you go near something".
                        if (CaveDecorProjector.TryCast(context, "MainRoute", distance, 180f,
                                out CaveDecorSurface floor))
                        {
                            Vector3 near = floor.point + floor.normal * 2f;
                            camera.transform.position = near;
                            camera.transform.rotation = Quaternion.LookRotation(-floor.normal, tangent);
                            director.EvaluateAndApplyAt(near);

                            string nearFile = $"{label}_NEAR__{pass}.png";
                            Render(camera, Path.Combine(root, nearFile));
                            manifest.AppendLine($"{nearFile}\t{pass}\t{label}_NEAR\t" +
                                                $"{director.CurrentZoneId}\t" +
                                                $"{director.CurrentProfile.postExposure}\t" +
                                                $"{director.CurrentProfile.ambientSky}");
                        }
                    }

                    // The spawn point, and the fidelity check for this whole harness. Across a ~2 m cabin
                    // extinction is exp(-k*d) with d tiny, so transmittance is essentially 1 and the frame
                    // is very nearly pure ambient. If POST_OFF here does not come back blown-out white-cyan
                    // the way the reported screenshots do, then the preview path is not reproducing play
                    // mode and no value tuned against these images can be trusted.
                    if (submarine != null)
                    {
                        Vector3 eye = submarine.position + new Vector3(0f, 0.5f, -4.5f);
                        camera.transform.position = eye;
                        camera.transform.rotation = Quaternion.identity;
                        director.EvaluateAndApplyAt(eye);

                        string file = $"Submarine_interior__{pass}.png";
                        Render(camera, Path.Combine(root, file));
                        manifest.AppendLine($"{file}\t{pass}\tSubmarine_interior\t{director.CurrentZoneId}\t" +
                                            $"{director.CurrentProfile.postExposure}\t" +
                                            $"{director.CurrentProfile.ambientSky}");
                        report.AppendLine($"  {file}  zone={director.CurrentZoneId}");
                    }
                }

                // Control: no extinction, no fog, whatever ambient the scene was saved with. This is also
                // what the Scene view shows, since the director only runs while playing.
                data.renderPostProcessing = true;
                director.ClearUnderwaterEffect();
                foreach ((string label, float distance) in Viewpoints)
                {
                    polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out _);
                    camera.transform.position = centre;
                    camera.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);

                    string file = $"{label}__NO_DIRECTOR.png";
                    Render(camera, Path.Combine(root, file));
                    manifest.AppendLine($"{file}\tNO_DIRECTOR\t{label}\t-\t-\t-");
                }

                // Written to disk, unlike the previous version which logged the settings and lost them.
                File.WriteAllText(Path.Combine(root, "manifest.tsv"), manifest.ToString());

                report.AppendLine(Measure(root));
                report.AppendLine("UNDERWATER_POST_COMPARE DONE");
            }
            finally
            {
                director.ClearUnderwaterEffect();
                Object.DestroyImmediate(rig);
            }

            return report.ToString();
        }

        /// <summary>
        /// Numbers to go with the pictures: mean channel levels and what fraction of the frame is
        /// clipped. "Looks bright" is not a measurement, and the clipped fraction is the specific thing
        /// a missing tonemapper causes.
        /// </summary>
        private static string Measure(string root)
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("-- measurements (sRGB 0-255 means, clipped = fraction of pixels >= 250) --");
            text.AppendLine($"{"file",-34} {"R",6} {"G",6} {"B",6}  {"clipped",8}  hue");

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
                foreach (Color32 pixel in pixels)
                {
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    if (pixel.r >= 250 && pixel.g >= 250 && pixel.b >= 250)
                        clipped++;
                }

                int n = pixels.Length;
                double mr = r / n, mg = g / n, mb = b / n;
                // Cyan reads as green and blue close together and well above red; navy reads as blue
                // clearly above green. One number for which of the two this frame is.
                string hue = mg > 1f && mb > 1f
                    ? (mb > mg * 1.35 ? "navy" : mg > mb * 0.9 ? "CYAN" : "blue")
                    : "dark";

                text.AppendLine($"{Path.GetFileName(path),-34} {mr,6:0.0} {mg,6:0.0} {mb,6:0.0}  " +
                                $"{clipped / (float)n,8:P2}  {hue}");
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
