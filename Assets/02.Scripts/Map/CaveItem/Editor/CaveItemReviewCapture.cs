using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Renders one close-up per placed item plus a per-zone overview.
    ///
    /// The validator can prove an item is a measured distance clear of the shell; it cannot tell you the
    /// tank is tucked behind a boulder from every angle a player will approach from, or that an urchin
    /// is sunk into a rock face at a bad angle. The last art pass in this project learned the hard way
    /// that a contact sheet hides that class of problem - a depth-of-field bug survived review and was
    /// only caught by opening one full-size shot - so this writes individual files and the reviewer is
    /// expected to open them.
    ///
    /// Fog and the underwater screen pass are off. UnderwaterZoneDirector only runs in play mode anyway,
    /// but RenderSettings still carries whatever the scene was saved with, and reviewing placement
    /// through 12 m of blue haze answers a different question than "is it seated correctly".
    /// </summary>
    public static class CaveItemReviewCapture
    {
        private const string OutputFolder = "Artifacts/CaveItem/review";
        private const int ShotWidth = 960;
        private const int ShotHeight = 540;
        private const int OverviewWidth = 1600;
        private const int OverviewHeight = 900;

        [MenuItem("Tools/Underwater Cave/Items/3 - 아이템 리뷰 캡처")]
        public static void CaptureInteractive()
        {
            Debug.Log(Capture());
        }

        public static void CaptureBatch()
        {
            EditorSceneManager.OpenScene(CaveItemBatch.TargetScenePath, OpenSceneMode.Single);
            Debug.Log(Capture());
        }

        public static string Capture()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE ITEM REVIEW CAPTURE =====");

            Transform itemRoot = CaveItemSpawner.FindItemRoot();
            if (itemRoot == null)
                return report.AppendLine("FAIL: no CaveBlockout/Items in the scene").ToString();

            // Needed only for the shell collider, which is what keeps the camera indoors.
            CaveDecorContext context = CaveDecorContext.Create();

            string root = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
            Directory.CreateDirectory(root);

            // Save and restore, so a capture run never leaves the scene's lighting altered. The scene is
            // not marked dirty and nothing here is saved.
            bool fog = RenderSettings.fog;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;

            var rig = new GameObject("~CaveItemReviewRig");
            var camera = rig.AddComponent<Camera>();
            var lightObject = new GameObject("~CaveItemReviewLight");
            var light = lightObject.AddComponent<Light>();

            var manifest = new StringBuilder();
            manifest.AppendLine("file\tid\tzone\tposition");

            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.72f);
                RenderSettings.ambientIntensity = 1f;

                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.color = Color.white;
                lightObject.transform.rotation = Quaternion.Euler(38f, -140f, 0f);

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.09f, 0.13f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 4000f;
                camera.enabled = false;

                int shots = 0;
                foreach (Transform group in itemRoot)
                {
                    var members = new List<Transform>();
                    foreach (Transform child in group)
                        members.Add(child);

                    foreach (Transform item in members)
                    {
                        // Frame from the tunnel side rather than from directly above: the question is
                        // what the player sees swimming past, and a top-down shot makes everything look
                        // seated whether it is or not.
                        float radius = Mathf.Max(0.6f, FrameRadius(item));
                        Vector3 focus = FrameCentre(item);
                        Vector3 direction = new Vector3(0.75f, 0.45f, -0.75f).normalized;

                        camera.transform.position = PullInside(context, focus, direction, radius * 3.2f);
                        camera.transform.LookAt(focus);
                        camera.fieldOfView = 50f;

                        string file = $"{group.name}__{item.name}.png";
                        Render(camera, Path.Combine(root, file), ShotWidth, ShotHeight);
                        manifest.AppendLine($"{file}\t{item.name}\t{group.name}\t{item.position}");
                        shots++;
                    }

                    if (members.Count == 0)
                        continue;

                    Bounds groupBounds = new Bounds(FrameCentre(members[0]), Vector3.zero);
                    foreach (Transform item in members)
                        groupBounds.Encapsulate(FrameCentre(item));

                    float span = Mathf.Max(8f, groupBounds.size.magnitude);
                    camera.transform.position = PullInside(context, groupBounds.center,
                        new Vector3(0.6f, 0.55f, -0.6f).normalized, span);
                    camera.transform.LookAt(groupBounds.center);
                    camera.fieldOfView = 60f;

                    string overview = $"{group.name}__overview.png";
                    Render(camera, Path.Combine(root, overview), OverviewWidth, OverviewHeight);
                    manifest.AppendLine($"{overview}\t(overview)\t{group.name}\t{groupBounds.center}");
                    shots++;

                    report.AppendLine($"  {group.name}: {members.Count} items + 1 overview");
                }

                File.WriteAllText(Path.Combine(root, "manifest.tsv"), manifest.ToString());
                report.AppendLine($"wrote {shots} PNGs to {OutputFolder}");
                report.AppendLine("NOTE: the three Tornado shots frame the placement but show no effect. " +
                                  "Tonado.prefab is two ParticleSystems and no solid renderer, and " +
                                  "particles do not simulate in an unplayed editor instance - an empty " +
                                  "frame there is the capture's limit, not a missing object. Its real " +
                                  "footprint is the Whirlpool pull field, which the validator checks.");
                report.AppendLine("CAVE_ITEM_CAPTURE DONE");
            }
            finally
            {
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(lightObject);
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
            }

            return report.ToString();
        }

        /// <summary>
        /// Camera position that is <paramref name="distance"/> back from the focus but still inside the
        /// cave.
        ///
        /// Without this the first capture run backed the camera straight through the shell and rendered
        /// the outside of the map: the shark frames at ~26 m, and there is nowhere in Z6 that far from
        /// the swim line that is not solid rock. Every shark shot came back as a flat wall of stone,
        /// which reads exactly like "the shark is buried" and is not what was wrong.
        /// </summary>
        private static Vector3 PullInside(CaveDecorContext context, Vector3 focus, Vector3 direction,
            float distance)
        {
            if (context?.Shell != null &&
                context.Shell.Raycast(new Ray(focus, direction), out RaycastHit hit, distance))
            {
                // Stop short of the rock face rather than on it, so the near clip plane does not slice
                // into the wall and show the inside of the shell.
                distance = Mathf.Max(1f, hit.distance - 1.5f);
            }
            return focus + direction * distance;
        }

        /// <summary>
        /// Centre of the visible geometry, not the pivot. Several of these prefabs pivot at their base
        /// or, in the shark's case, near the nose with nine metres of body behind it, so aiming a camera
        /// at transform.position frames empty water.
        /// </summary>
        private static Vector3 FrameCentre(Transform item)
        {
            bool has = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer)
                    continue;
                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return has ? bounds.center : item.position;
        }

        private static float FrameRadius(Transform item)
        {
            bool has = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer)
                    continue;
                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            // The tornado has no solid renderer at all; frame it at its pull radius instead.
            return has ? bounds.extents.magnitude : 7f;
        }

        private static void Render(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
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
