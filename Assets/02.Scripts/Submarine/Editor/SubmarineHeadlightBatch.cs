using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.SubmarineTools.EditorTools
{
    /// <summary>
    /// Diagnostics for the submarine's lighting rig.
    ///
    /// Exists because the submarine hull is a nested .glb prefab instance: every transform between the
    /// prefab root and the "Spot Light" node is a STRIPPED transform in Submarine_final.prefab, so the
    /// lamp's world pose cannot be read out of the YAML at all. The glb carries its own rotations and a
    /// 0.33 scale node, and the model root is overridden to scale 2 on top of that. The only reliable
    /// way to learn where that lamp actually points is to let Unity build the hierarchy and ask it.
    ///
    /// Batch entry point:
    ///   Unity.exe -batchmode -quit -projectPath "D:\NCAI_Project\TheVarco" \
    ///     -executeMethod Varco.SubmarineTools.EditorTools.SubmarineHeadlightBatch.DiagnoseBatch \
    ///     -logFile headlight-diagnose.log
    /// </summary>
    public static class SubmarineHeadlightBatch
    {
        public const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        public const string SubmarinePrefabPath = "Assets/03.Prefabs/Submarine/Submarine_final.prefab";

        [MenuItem("Tools/Submarine/Diagnose Headlight Rig")]
        public static void DiagnoseInteractive()
        {
            Debug.Log(Diagnose());
        }

        public static void DiagnoseBatch()
        {
            EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
            Debug.Log(Diagnose());
        }

        /// <summary>
        /// Reports the submarine's world-space geometry and every Light under it. Everything is also
        /// expressed in the submarine root's LOCAL frame, because that is the frame a headlight has to
        /// be authored in - "6 m ahead of the bow" is meaningless in world space once the sub moves.
        /// </summary>
        public static string Diagnose()
        {
            var report = new StringBuilder();
            report.AppendLine("SUBMARINE_HEADLIGHT_DIAGNOSE");

            GameObject submarine = FindSubmarineRoot();
            if (submarine == null)
                return report.AppendLine("  ERROR no Submarine_final root found in the open scene").ToString();

            Transform root = submarine.transform;
            report.AppendLine($"  root name={submarine.name} pos={V(root.position)} euler={V(root.eulerAngles)} lossyScale={V(root.lossyScale)}");

            if (TryGetWorldBounds(submarine, out Bounds bounds))
            {
                report.AppendLine($"  hullBounds center={V(bounds.center)} size={V(bounds.size)}");
                Vector3 localCenter = root.InverseTransformPoint(bounds.center);
                Vector3 localExtents = new Vector3(
                    bounds.extents.x / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x)),
                    bounds.extents.y / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.y)),
                    bounds.extents.z / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.z)));
                report.AppendLine($"  hullBounds localCenter={V(localCenter)} localExtents={V(localExtents)}");
            }

            // Which way is the bow? The damage regions are named 전면 / 후면, so their positions in the
            // root's local frame settle the sign of the forward axis without guessing.
            foreach (string marker in new[] { "전면", "후면", "좌측 전방", "우측 전방", "상단 전방", "하단 전방" })
            {
                Transform found = FindChildByName(root, marker);
                if (found != null)
                    report.AppendLine($"  marker '{marker}' world={V(found.position)} local={V(root.InverseTransformPoint(found.position))}");
            }

            foreach (Transform seat in FindChildrenByPrefix(root, "SeatPoint"))
            {
                report.AppendLine($"  seat '{seat.name}' world={V(seat.position)} local={V(root.InverseTransformPoint(seat.position))} " +
                                  $"forwardLocal={V(root.InverseTransformDirection(seat.forward))}");
            }

            Light[] lights = submarine.GetComponentsInChildren<Light>(true);
            report.AppendLine($"  lightCount={lights.Length}");
            foreach (Light light in lights)
            {
                Transform t = light.transform;
                report.AppendLine($"  LIGHT path={Path(t, root)}");
                report.AppendLine($"    type={light.type} enabled={light.enabled} activeInHierarchy={t.gameObject.activeInHierarchy}" +
                                  $" intensity={F(light.intensity)} range={F(light.range)} spotAngle={F(light.spotAngle)} inner={F(light.innerSpotAngle)}" +
                                  $" shadows={light.shadows} color={light.color} cullingMask={light.cullingMask} renderingLayerMask={light.renderingLayerMask}");
                report.AppendLine($"    worldPos={V(t.position)} worldForward={V(t.forward)} lossyScale={V(t.lossyScale)}");
                report.AppendLine($"    localPosInRoot={V(root.InverseTransformPoint(t.position))} localForwardInRoot={V(root.InverseTransformDirection(t.forward))}");
                report.AppendLine($"    parentChainScale={V(t.parent != null ? t.parent.lossyScale : Vector3.one)} localPosition={V(t.localPosition)} localEuler={V(t.localEulerAngles)}");
            }

            Camera[] cameras = submarine.GetComponentsInChildren<Camera>(true);
            report.AppendLine($"  cameraCount={cameras.Length}");
            foreach (Camera camera in cameras)
                report.AppendLine($"  CAMERA path={Path(camera.transform, root)} world={V(camera.transform.position)} local={V(root.InverseTransformPoint(camera.transform.position))}");

            return report.ToString();
        }

        public static GameObject FindSubmarineRoot()
        {
            foreach (GameObject candidate in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.transform.parent != null)
                    continue;
                if (candidate.name.StartsWith("Submarine_final", StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        /// <summary>Union of every renderer bound under <paramref name="root"/>, in world space.</summary>
        public static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                    continue;
                }
                bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        public static Transform FindChildByName(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        public static List<Transform> FindChildrenByPrefix(Transform root, string prefix)
        {
            var found = new List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                    found.Add(child);
            }
            return found;
        }

        public static string Path(Transform node, Transform stopAt)
        {
            var parts = new List<string>();
            Transform cursor = node;
            while (cursor != null && cursor != stopAt)
            {
                parts.Insert(0, cursor.name);
                cursor = cursor.parent;
            }
            return string.Join("/", parts);
        }

        private static string V(Vector3 v)
        {
            return $"({v.x.ToString("0.###", CultureInfo.InvariantCulture)}, " +
                   $"{v.y.ToString("0.###", CultureInfo.InvariantCulture)}, " +
                   $"{v.z.ToString("0.###", CultureInfo.InvariantCulture)})";
        }

        private static string F(float f)
        {
            return f.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
