using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// One approach shot per hazard station plus a wide profile, taken with the underwater fog off.
    ///
    /// The numeric checks in CaveHazardValidator answer "is there a gap"; they cannot answer "does this
    /// read as a rockfall gate" or "is the vent chimney sunk into the floor at a believable depth".
    /// Shots are written full size as well as into a contact sheet because the last decor pass had a
    /// depth-of-field bug that the contact sheet's downscale hid completely.
    /// </summary>
    public static class CaveHazardReviewCapture
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string OutputDirectory = "Artifacts/CaveHazard/review";
        private const int ShotWidth = 960;
        private const int ShotHeight = 540;
        private const int SheetColumns = 3;

        [MenuItem("Tools/Underwater Cave/Hazards/2 - 장애물 리뷰 캡처")]
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
        /// Same shots with the dressing switched off, written to a separate folder.
        ///
        /// Worth its own pass because Z5's decor includes grey rock spires and red seaweed that read at
        /// a glance exactly like a hydrothermal chimney. A dressed shot cannot tell you whether the
        /// cone you are looking at is the vent you placed or a boulder that happens to be cone-shaped,
        /// and "it looks right" is not a verification if you cannot identify the subject.
        /// </summary>
        public static void CaptureIsolatedBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Capture(hideDecor: true));
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

        public static string Capture(bool hideDecor = false)
        {
            var report = new StringBuilder();
            report.AppendLine(hideDecor
                ? "===== CAVE HAZARD REVIEW CAPTURE (decor hidden) ====="
                : "===== CAVE HAZARD REVIEW CAPTURE =====");

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell collider in the scene");
                return report.ToString();
            }

            List<Shot> shots = BuildShots(context);
            if (shots.Count == 0)
            {
                report.AppendLine("FAIL: no stations to photograph");
                return report.ToString();
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string directory = Path.Combine(root,
                (hideDecor ? OutputDirectory + "-isolated" : OutputDirectory)
                    .Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);

            GameObject decorRoot = null;
            if (hideDecor)
            {
                GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
                Transform decor = blockout != null
                    ? blockout.transform.Find(CaveDecorNames.DecorRoot)
                    : null;
                if (decor != null)
                {
                    decorRoot = decor.gameObject;
                    decorRoot.SetActive(false);
                }
            }

            bool fog = RenderSettings.fog;
            Color ambient = RenderSettings.ambientLight;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;

            var files = new List<string>();
            GameObject rig = null;
            RenderTexture target = null;
            Texture2D readable = null;
            List<Light> litForReview = null;

            try
            {
                litForReview = ShowWarningLights();
                report.AppendLine($"warningLights: {litForReview.Count} switched on for the review only");

                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                // Bright enough to read shape. The first pass at 0.34 produced near-black plates:
                // the cave material is dark volcanic rock and there is no directional light in Z5.
                RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.72f, 1f);

                rig = new GameObject("CaveHazardReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = rig.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
                camera.fieldOfView = 70f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 600f;

                Light key = rig.AddComponent<Light>();
                key.type = LightType.Point;
                key.color = new Color(0.85f, 0.93f, 1f, 1f);
                key.intensity = 14f;
                key.range = 220f;
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
                HideWarningLights(litForReview);
                if (decorRoot != null) decorRoot.SetActive(true);

                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
            }

            CreateContactSheet(directory, files, Path.Combine(directory, "00_contact_sheet.png"));
            report.AppendLine($"CAVE_HAZARD_REVIEW PASS shots={files.Count} directory={directory}");
            return report.ToString();
        }

        /// <summary>
        /// Switches the rockfall warning lights on for the duration of the capture, and reports how
        /// many it found.
        ///
        /// Without this a Z3 shot proves nothing. FallingRockSpawner has no mesh and CaveHazardBatch
        /// disables its spot light at author time, so a correctly placed rock station and a missing one
        /// render as exactly the same picture of bare cave - a check that cannot fail is not a check.
        /// Lit, each cone shows where its spawner hangs and what patch of floor the rock is heading for,
        /// which is also the warning state the player actually reacts to.
        /// </summary>
        private static List<Light> ShowWarningLights()
        {
            var switched = new List<Light>();
            Transform hazards = CaveHazardSpawner.GetOrCreateHazardRoot();
            foreach (Light light in hazards.GetComponentsInChildren<Light>(true))
            {
                if (light == null || light.enabled)
                    continue;
                light.enabled = true;
                switched.Add(light);
            }
            return switched;
        }

        /// <summary>
        /// Puts them back. Leaving them on would re-flood the Scene view and silently undo the reason
        /// CaveHazardBatch turned them off in the first place.
        /// </summary>
        private static void HideWarningLights(List<Light> switched)
        {
            if (switched == null)
                return;
            foreach (Light light in switched)
            {
                if (light != null)
                    light.enabled = false;
            }
        }

        /// <summary>
        /// The player's view of each gate, plus a close look at the obstacles themselves.
        ///
        /// Both shots aim at the mount centroid rather than at the route centre line. Framing the
        /// centre line seems natural and is wrong for Z5: its vents sit 8-25 m below the swim line, so
        /// a centre-line shot of station S4 was 960x540 pixels of bare wall with the subject out of
        /// frame entirely. The centroid is recomputed with the same cast the spawner used, so what the
        /// camera looks at is exactly where the obstacle went.
        /// </summary>
        private static List<Shot> BuildShots(CaveDecorContext context)
        {
            var shots = new List<Shot>();
            CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
            if (polyline == null)
                return shots;

            foreach (CaveHazardStation station in CaveHazardLayout.Build())
            {
                if (station.instances.Count == 0)
                    continue;

                var centroid = Vector3.zero;
                int resolved = 0;
                float spread = 0f;
                foreach (CaveHazardInstance instance in station.instances)
                {
                    if (!CaveDecorProjector.TryCast(context, instance.routeId, instance.routeDistance,
                            instance.angleDegrees, out CaveDecorSurface surface))
                        continue;
                    centroid += surface.point + surface.normal * instance.surfaceOffset;
                    spread = Mathf.Max(spread, instance.scale);
                    resolved++;
                }

                if (resolved == 0)
                    continue;
                centroid /= resolved;

                float distance = station.instances[0].routeDistance;
                polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out _);
                Vector3 forward = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;

                // Approach: what the player sees coming into the gate, from the swim line.
                Vector3 approach = centre - forward * 24f;
                shots.Add(new Shot($"{station.id}_approach", approach,
                    Quaternion.LookRotation((centroid - approach).normalized, Vector3.up)));

                // Detail: close and off-axis, the only way to judge how deep a vent chimney is buried or
                // how far a rock spawner hangs clear of the ceiling.
                //
                // The vertical offset has to follow the mount, not a fixed "up". Rock stations hang from
                // the ceiling, so lifting the camera above them puts it outside the shell and the first
                // attempt photographed the *outside* of the cave: a picture of blue rock against black
                // that looks plausible until you notice there is no cave in it. Vents sit on the floor
                // and need the opposite.
                CaveDecorProjector.BuildAxisFrame(forward, out _, out Vector3 right);
                float back = 10f + spread * 5f;
                float lift = station.kind == CaveHazardKind.FallingRock ? -back * 0.75f : back * 0.35f;
                Vector3 detail = centroid + right * back * 0.7f - forward * back * 0.4f + Vector3.up * lift;
                detail = KeepInsideCave(context, centre, detail);
                shots.Add(new Shot($"{station.id}_detail", detail,
                    Quaternion.LookRotation((centroid - detail).normalized, Vector3.up)));
            }

            return shots;
        }

        /// <summary>
        /// Pulls a camera position back inside the shell if the wall is between it and the route.
        ///
        /// The detail viewpoints are offsets from a mount, and a mount sits on the surface by
        /// definition, so an offset that looks generous in a wide chamber puts the camera inside rock in
        /// a narrow one. Z5_Vent_S4 did exactly that and rendered a screen of wall interior with the
        /// subject nowhere in it - a shot that is easy to skim past as "dark but fine".
        /// </summary>
        private static Vector3 KeepInsideCave(CaveDecorContext context, Vector3 anchor, Vector3 desired)
        {
            Vector3 offset = desired - anchor;
            float length = offset.magnitude;
            if (length < 0.01f)
                return desired;

            if (!context.Shell.Raycast(new Ray(anchor, offset / length), out RaycastHit hit, length))
                return desired;

            // Stop a couple of metres short of the rock face, and never end up behind the anchor.
            return anchor + offset / length * Mathf.Max(2f, hit.distance - 2f);
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
