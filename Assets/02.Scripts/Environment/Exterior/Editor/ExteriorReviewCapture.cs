using System;
using System.IO;
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

        public static void CaptureBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
                Capture();
                Debug.Log("EXTERIOR_CAPTURE PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"EXTERIOR_CAPTURE FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/Exterior/외부 환경 리뷰 캡처")]
        public static void CaptureInteractive()
        {
            Capture();
        }

        private static void Capture()
        {
            var director = UnityEngine.Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            if (director == null)
                throw new InvalidOperationException("no UnderwaterZoneDirector in the scene");

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            Directory.CreateDirectory(root);

            // (label, camera position, look target). Surfacing point ~29 m past the mouth at the
            // 27-degree exit pitch; island centre 190 m out along the bearing.
            Vector3 surfacing = ExitPosition + ExitDirection * 29f;
            Vector3 islandCentre = ExitPosition + Bearing * 190f;
            (string label, Vector3 position, Vector3 target)[] shots =
            {
                ("1_inside_looking_out", ExitPosition - ExitDirection * 40f, ExitPosition + ExitDirection * 30f),
                ("2_outside_looking_back", ExitPosition + ExitDirection * 30f, ExitPosition),
                ("3_surfacing_underwater", surfacing + Vector3.down * 2f, islandCentre),
                ("4_surfaced_above_water", surfacing + Vector3.up * 3f, islandCentre + Vector3.up * 5f),
                ("5_beach_from_water", islandCentre - Bearing * 90f + Vector3.up * 4f, islandCentre + Vector3.up * 10f),
                ("6_overview", ExitPosition + Bearing * 120f + Right * 160f + Vector3.up * 110f,
                    (ExitPosition + islandCentre) * 0.5f)
            };

            var rig = new GameObject("~ExteriorCaptureCamera");
            var camera = rig.AddComponent<Camera>();
            var report = new StringBuilder("===== EXTERIOR REVIEW CAPTURE =====\n");

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

                foreach ((string label, Vector3 position, Vector3 target) in shots)
                {
                    camera.transform.position = position;
                    camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
                    director.EvaluateAndApplyAt(position);

                    string file = $"{label}.png";
                    Render(camera, Path.Combine(root, file));
                    report.AppendLine($"  {file}  zone={director.CurrentZoneId} y={position.y:0.#}");
                }
            }
            finally
            {
                director.ClearUnderwaterEffect();
                UnityEngine.Object.DestroyImmediate(rig);
            }

            Debug.Log(report.ToString());
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
                UnityEngine.Object.DestroyImmediate(texture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
