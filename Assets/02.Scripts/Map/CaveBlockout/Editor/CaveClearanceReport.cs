using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveBlockout.Editor
{
    /// <summary>
    /// Runs the same route against several hypotheses about the hull, so a clearance failure can be
    /// attributed instead of guessed at.
    ///
    /// The old validator swept a 3 x 3 x 6 m box that pitched with the tunnel and sat exactly on the
    /// centreline, and it passed. The real hull is 3.28 m across, 8.11 m long, pivots at its nose and
    /// never pitches. Swapping one for the other turns every zone red at once, which says nothing about
    /// which property is responsible. These four hypotheses isolate them:
    ///
    ///   retired proxy   - what used to be checked. Expected to pass; if it fails, the scene changed.
    ///   real, pitched   - real size, still allowed to tilt. Isolates "too big".
    ///   real, level     - real size, level, pinned to the centreline. Isolates "cannot pitch".
    ///   real, flyable   - real size, level, free to climb and sink as the vehicle actually can.
    ///                     This is the honest gate; the ones above are only for attribution.
    /// </summary>
    public static class CaveClearanceReport
    {
        public static void ReportMainMapBatch() => Report(CaveBlockoutBuilder.MainMapPath);

        public static void ReportMainSceneFinalBatch() => Report(CaveBlockoutBuilder.PlayScenePath);

        [MenuItem("Tools/Underwater Cave/Report Hull Clearance Hypotheses")]
        public static void ReportCurrentScene() => Report(null);

        private static void Report(string scenePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(scenePath))
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                CaveRoute[] routes = UnityEngine.Object.FindObjectsByType<CaveRoute>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                CaveRoute mainRoute = routes.FirstOrDefault(
                    route => route.Definitions.Any(definition => definition.isMainRoute));
                CaveRoute branches = routes.FirstOrDefault(route => route != mainRoute);
                if (mainRoute == null)
                    throw new InvalidOperationException("no main CaveRoute in the scene");

                CaveHullProbe real = CaveClearanceValidator.ResolveHullProbe();

                // Same construction the retired proxy used: a 6 x 3 x 3 m shape centred on the pivot,
                // expressed as the capsule this validator now sweeps.
                CaveHullProbe retired = new CaveHullProbe(1.5f, 6f, Vector3.zero, real.yawStepDegrees,
                    real.layerMask, real.verticalPerForwardMetre, "retired 6x3x3 proxy");

                var hypotheses = new List<(string label, CaveHullProbe hull, CaveClearanceValidator.Options options)>
                {
                    ("retired proxy, pitched, pinned", retired,
                        new CaveClearanceValidator.Options { allowPitch = true, verticalFreedomMeters = 0f, verticalStepMeters = 0.5f }),
                    ("real hull, pitched, pinned", real.WithSource("real hull"),
                        new CaveClearanceValidator.Options { allowPitch = true, verticalFreedomMeters = 0f, verticalStepMeters = 0.5f }),
                    ("real hull, level, pinned", real.WithSource("real hull"),
                        CaveClearanceValidator.Options.PinnedToCentreline),
                    ("real hull, level, flyable", real.WithSource("real hull"),
                        CaveClearanceValidator.Options.Realistic)
                };

                StringBuilder report = new StringBuilder();
                report.AppendLine("===== CAVE HULL CLEARANCE HYPOTHESES =====");
                report.AppendLine($"scene: {(string.IsNullOrEmpty(scenePath) ? "active" : scenePath)}");
                report.AppendLine($"real hull: {real}");
                report.AppendLine();

                foreach ((string label, CaveHullProbe hull, CaveClearanceValidator.Options options) in hypotheses)
                {
                    bool passed = CaveClearanceValidator.ValidateAll(
                        mainRoute, branches, hull, options, out _, out List<string> failures);
                    report.AppendLine($"-- {label}: {(passed ? "PASS" : $"FAIL ({failures.Count})")} [{options}]");
                    foreach (string failure in failures)
                        report.AppendLine("     " + failure);
                    report.AppendLine();
                }

                AppendMarginScan(report, mainRoute, branches, real);
                Debug.Log(report.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_CLEARANCE_REPORT FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Finds the largest multiple of the real hull that still has a flyable path, for the route as a
        /// whole and for each zone on its own.
        ///
        /// A value above 1 is headroom; below 1 means the hull as built does not fit and the number says
        /// by how much. Per-zone values name the binding constraint, which a single route-wide figure
        /// hides - the tightest zone sets the whole route and everything else is slack.
        /// </summary>
        private static void AppendMarginScan(
            StringBuilder report,
            CaveRoute mainRoute,
            CaveRoute branches,
            CaveHullProbe real)
        {
            CaveClearanceValidator.Options options = CaveClearanceValidator.Options.Realistic;
            report.AppendLine("-- margin scan (largest multiple of the real hull with a flyable path)");

            float whole = FindLargestPassingScale(
                scale => CaveClearanceValidator.ValidateAll(
                    mainRoute, branches, real.Scaled(scale), options, out _, out _));
            report.AppendLine($"     whole route: x{whole:F2}  " +
                              $"(hull would be {real.radius * 2f * whole:F2} m across, " +
                              $"{real.height * whole:F2} m long)");

            CaveRouteSplineDefinition mainDefinition = mainRoute.Definitions
                .First(definition => definition.isMainRoute);

            foreach (CaveRouteSection section in mainDefinition.sections)
            {
                CaveRouteSection only = section;
                float scale = FindLargestPassingScale(
                    candidate => CaveClearanceValidator.ValidateSection(
                        mainRoute, mainDefinition, only, real.Scaled(candidate), options, out _));
                report.AppendLine($"     {section.zoneId}: {Describe(scale)}  " +
                                  $"({section.startDistanceMeters:F1}-{section.endDistanceMeters:F1} m, " +
                                  $"authored {section.guideSize.x:0.#}x{section.guideSize.y:0.#} m)");
            }
            report.AppendLine();

            // The zone sections trim 0.5% off each end, and the boundary knots sit exactly on the seams,
            // so the tightest cross-sections on the route are in a hole neither zone sweep visits. These
            // windows straddle the seam with no trimming, which is the only way to get a number for the
            // pinch itself rather than for the chamber taper leading into it.
            report.AppendLine("-- boundary throat windows (+-15 m around each boundary knot, untrimmed)");
            foreach (CaveBlockoutPreset.BoundarySpec boundary in CaveBlockoutPreset.ZoneBoundaries)
            {
                CaveRouteSection before = mainDefinition.sections
                    .FirstOrDefault(section => section.endKnot == boundary.knotIndex);
                if (before == null)
                {
                    report.AppendLine($"     {boundary.boundaryId}: no section ends at knot {boundary.knotIndex}");
                    continue;
                }

                float centre = before.endDistanceMeters;
                float from = Mathf.Max(1f, centre - 15f);
                float to = centre + 15f;
                float scale = FindLargestPassingScale(
                    candidate => CaveClearanceValidator.ValidateWindow(
                        mainRoute, mainDefinition.splineIndex, from, to, real.Scaled(candidate), options, out _));

                CaveClearanceValidator.ValidateWindow(mainRoute, mainDefinition.splineIndex, from, to, real,
                    options, out List<string> atRealSize);
                report.AppendLine($"     {boundary.boundaryId} @ {centre:F1} m (bend {boundary.bendDegrees:0.#} deg): " +
                                  $"{Describe(scale)}, real hull {(atRealSize.Count == 0 ? "fits" : "BLOCKED")}");
                foreach (string failure in atRealSize)
                    report.AppendLine("          " + failure);
            }
            report.AppendLine();
        }

        private static string Describe(float scale)
        {
            if (scale >= Ceiling)
                return $"x{Ceiling:F1}+ (saturated, not measured further)";
            return scale <= 0f ? "x0 (no path at any size tried)" : $"x{scale:F2}";
        }

        private const float Ceiling = 4f;

        private static float FindLargestPassingScale(Func<float, bool> passes)
        {
            const float floor = 0.05f;

            if (passes(Ceiling))
                return Ceiling;
            if (!passes(floor))
                return 0f;

            float low = floor;
            float high = Ceiling;
            // Eight bisections resolve to under 2% of the hull, finer than any decision here needs.
            for (int i = 0; i < 8; i++)
            {
                float mid = (low + high) * 0.5f;
                if (passes(mid))
                    low = mid;
                else
                    high = mid;
            }
            return low;
        }
    }
}
