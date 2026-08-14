using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Varco.SubmarineTools.EditorTools
{
    /// <summary>
    /// Writes <see cref="SubmarineHeadlightTuning"/> onto the spot light inside
    /// Assets/03.Prefabs/Submarine/Submarine_final.prefab.
    ///
    /// Edits the PREFAB, not the scene instance, so MainScene_final and MainMap both pick the change up
    /// and neither needs a sync pass. MainScene_final's submarine instance carries no Light overrides
    /// (checked: its only override is m_Name), so nothing in the scene shadows what is written here.
    ///
    /// This re-aims cseo0dev's existing lamp rather than adding a second one - the user asked for the
    /// existing light to be re-tuned, and HANDOFF §7 asks that his work not be replaced silently. The
    /// original values are printed on every run so the change stays reversible by hand:
    ///   local position (0, 0.99, 0.301), pitch 15° down, intensity 50, range 30, spot 50/30.
    ///
    /// Batch entry point:
    ///   Unity.exe -batchmode -quit -projectPath "D:\NCAI_Project\TheVarco" \
    ///     -executeMethod Varco.SubmarineTools.EditorTools.SubmarineHeadlightInstaller.InstallBatch \
    ///     -logFile headlight-install.log
    /// </summary>
    public static class SubmarineHeadlightInstaller
    {
        [MenuItem("Tools/Submarine/Install Headlight Tuning")]
        public static void InstallInteractive()
        {
            Debug.Log(Install());
        }

        public static void InstallBatch()
        {
            Debug.Log(Install());
            AssetDatabase.SaveAssets();
        }

        public static string Install()
        {
            var report = new StringBuilder();
            report.AppendLine("SUBMARINE_HEADLIGHT_INSTALL");

            GameObject contents = PrefabUtility.LoadPrefabContents(SubmarineHeadlightBatch.SubmarinePrefabPath);
            if (contents == null)
                throw new InvalidOperationException($"Could not load {SubmarineHeadlightBatch.SubmarinePrefabPath}.");

            try
            {
                Transform root = contents.transform;

                Light lamp = null;
                foreach (Light candidate in contents.GetComponentsInChildren<Light>(true))
                {
                    if (candidate.type != LightType.Spot)
                        continue;
                    if (lamp != null)
                        throw new InvalidOperationException(
                            "More than one spot light under the submarine. This tool re-aims exactly one lamp; " +
                            "resolve the ambiguity before running it again.");
                    lamp = candidate;
                }

                if (lamp == null)
                    throw new InvalidOperationException("No spot Light found under the submarine prefab.");

                report.AppendLine($"  lamp path={SubmarineHeadlightBatch.Path(lamp.transform, root)}");
                report.AppendLine($"  before localPosInRoot={Fmt(root.InverseTransformPoint(lamp.transform.position))} " +
                                  $"forwardLocal={Fmt(root.InverseTransformDirection(lamp.transform.forward))} " +
                                  $"intensity={lamp.intensity} range={lamp.range} spot={lamp.spotAngle}/{lamp.innerSpotAngle} " +
                                  $"shadows={lamp.shadows} color={lamp.color}");

                // Set world pose rather than local: the lamp hangs off a glb node whose accumulated
                // scale is 0.66 and whose accumulated rotation is +90° about X, so a local-space write
                // would land somewhere unrelated to the metres these numbers are authored in.
                lamp.transform.position = root.TransformPoint(SubmarineHeadlightTuning.LocalPosition);
                lamp.transform.rotation = root.rotation * Quaternion.Euler(SubmarineHeadlightTuning.PitchDegrees, 0f, 0f);

                lamp.type = LightType.Spot;
                lamp.intensity = SubmarineHeadlightTuning.Intensity;
                lamp.range = SubmarineHeadlightTuning.Range;
                lamp.spotAngle = SubmarineHeadlightTuning.SpotAngle;
                lamp.innerSpotAngle = SubmarineHeadlightTuning.InnerSpotAngle;
                lamp.color = SubmarineHeadlightTuning.Color;
                lamp.shadows = SubmarineHeadlightTuning.Shadows;
                lamp.enabled = true;
                lamp.gameObject.SetActive(true);
                lamp.gameObject.name = "Headlight";

                report.AppendLine($"  after  localPosInRoot={Fmt(root.InverseTransformPoint(lamp.transform.position))} " +
                                  $"forwardLocal={Fmt(root.InverseTransformDirection(lamp.transform.forward))} " +
                                  $"intensity={lamp.intensity} range={lamp.range} spot={lamp.spotAngle}/{lamp.innerSpotAngle} " +
                                  $"shadows={lamp.shadows} color={lamp.color}");

                if (SubmarineHeadlightBatch.TryGetWorldBounds(contents, out Bounds bounds))
                {
                    bool inside = bounds.Contains(lamp.transform.position);
                    report.AppendLine($"  hullBounds center={Fmt(bounds.center)} size={Fmt(bounds.size)} " +
                                      $"lampInsideHullBounds={inside}");
                    if (inside)
                        report.AppendLine("  WARNING lamp is still inside the hull bounding box - the beam will hit the sub's own nose first.");
                }

                PrefabUtility.SaveAsPrefabAsset(contents, SubmarineHeadlightBatch.SubmarinePrefabPath, out bool saved);
                report.AppendLine($"  saved={saved}");
                if (!saved)
                    throw new InvalidOperationException("SaveAsPrefabAsset reported failure.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return report.ToString();
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x.ToString("0.###", CultureInfo.InvariantCulture)}, " +
                   $"{v.y.ToString("0.###", CultureInfo.InvariantCulture)}, " +
                   $"{v.z.ToString("0.###", CultureInfo.InvariantCulture)})";
        }
    }
}
