using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    /// <summary>
    /// Shrinks the cave exit mouth from 85 x 50 to 24 x 16 metres, confining the taper to roughly the
    /// last 15 m of the route so the Z6 climax stretch (490-564 m, whose whirlpool spacing was designed
    /// against an 85 m chamber) keeps its full width.
    ///
    /// Mechanism: the cross-section lives in per-knot SplineData channels (cave.width / cave.height,
    /// smoothstep-interpolated), and both knot 11 (~519 m) and the final knot (579.56 m) are authored
    /// at 85/50 - which is why the entire second half of Z6 is a constant giant tube. Two midpoint
    /// inserts (~549 m and ~564 m) pinned at 85/50 hold the chamber, and only the final knot drops to
    /// 24/16.
    ///
    /// Runs on BOTH MainMap and MainScene_final: each scene carries its own copy of the route, while
    /// the generated shell mesh (Assets/Generated/CaveBlockout/CaveShell.asset) is one shared asset
    /// rewritten in place. If the two scenes' routes disagreed, whichever regenerated last would
    /// silently reshape the other scene's cave - the pipeline therefore runs this method on both and
    /// verifies the second regeneration produces no further shell diff.
    ///
    /// Known metadata trap handled here: CaveRouteEditingUtility.RemapLegacyMetadata only bumps
    /// section.endKnot when endDistanceMeters is negative. Every main section stores real distances
    /// (Z6 ends at 579.5636), so after inserting knots the final section still points at the old final
    /// knot index and CaveBlockoutValidator fails with "Main centreline does not terminate at the
    /// final spline knot." The section is re-pointed explicitly after the inserts.
    /// </summary>
    public static class CaveExitShrinkBatch
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";

        private const float ExitWidth = 24f;
        private const float ExitHeight = 16f;
        private const float HoldWidth = 85f;
        private const float HoldHeight = 50f;

        public static void ShrinkExitMainMapBatch()
        {
            try
            {
                ShrinkExit(MainMapPath, fusionDoubleSave: false);
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_EXIT_SHRINK FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void ShrinkExitMainSceneFinalBatch()
        {
            try
            {
                // MainScene_final carries 81 Fusion NetworkObjects whose SortKeys are baked from
                // GlobalObjectId on sceneSaving. A freshly serialised object has no file id until the
                // first save, so the first save writes provisional keys and only the second settles
                // them. Regeneration itself saves once; the extra save below is the second.
                ShrinkExit(PlayScenePath, fusionDoubleSave: true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_EXIT_SHRINK FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        private static void ShrinkExit(string scenePath, bool fusionDoubleSave)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            CaveRoute route = UnityEngine.Object
                .FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.Definitions.Any(definition => definition.isMainRoute));
            if (route == null)
                throw new InvalidOperationException($"no main CaveRoute in {scenePath}");

            SplineContainer container = route.Container;
            Spline spline = container[0];
            int lastKnot = spline.Count - 1;

            // Idempotency: safe to re-run. The final knot is 85 wide before the edit and 24 after.
            float finalWidth = EvaluateKnotFloat(spline, CaveRoute.WidthDataKey, lastKnot);
            if (Mathf.Abs(finalWidth - ExitWidth) < 0.01f)
            {
                Debug.Log($"CAVE_EXIT_SHRINK SKIP scene={scenePath} exit already {ExitWidth}x{ExitHeight}");
                return;
            }
            if (Mathf.Abs(finalWidth - HoldWidth) > 0.01f)
                throw new InvalidOperationException(
                    $"final knot width is {finalWidth}, expected {HoldWidth} - the route is not in the " +
                    "state this edit was written against; refusing to guess");

            // Two midpoint inserts in the last segment: 519->579.6 becomes 519->549->564->579.6.
            // The first insert alone would let smoothstep start pulling width down from 549 m, which
            // reaches the whirlpool station at 554 m; holding 85/50 until ~564 m confines the taper
            // to the final ~15 m.
            CaveRouteKnotInsertionResult first =
                CaveRouteEditingUtility.InsertKnotAtSegmentMidpoint(route, 0, lastKnot - 1, selectInsertedKnot: false);
            CaveRouteKnotInsertionResult second =
                CaveRouteEditingUtility.InsertKnotAtSegmentMidpoint(route, 0, lastKnot, selectInsertedKnot: false);

            int finalIndex = spline.Count - 1;
            SetKnotProfile(spline, first.knotIndex, HoldWidth, HoldHeight);
            SetKnotProfile(spline, second.knotIndex, HoldWidth, HoldHeight);
            SetKnotProfile(spline, finalIndex, ExitWidth, ExitHeight);

            RepointFinalSection(route, finalIndex);

            EditorUtility.SetDirty(route);
            EditorUtility.SetDirty(container);
            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);

            CaveValidationResult result = CaveBlockoutBuilder.RegenerateCurrentScene(true);

            if (fusionDoubleSave)
            {
                EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("second save refused");
            }

            Debug.Log($"CAVE_EXIT_SHRINK {(result.Passed ? "PASS" : "WARN")} scene={scenePath} " +
                      $"knots={spline.Count} inserted=[{first.knotIndex}@{Round(first.worldPosition)}, " +
                      $"{second.knotIndex}@{Round(second.worldPosition)}] " +
                      $"exit={ExitWidth}x{ExitHeight} " +
                      $"length={result.routeLength:F1}m rise={result.totalRise:F1}m " +
                      $"slope={result.maximumSlope:F1} radius={result.minimumTurnRadius:F1}");
            Debug.Log(DescribeProfile(spline));
            foreach (string issue in result.issues)
                Debug.LogWarning("CAVE_EXIT_SHRINK: " + issue);
            if (!result.Passed)
                throw new InvalidOperationException("blockout validation failed after the exit shrink");
        }

        /// <summary>
        /// Points the last main-route section at the (new) final knot, and refuses to continue if any
        /// other knot-indexed metadata sits in the edited range - nothing should, because every main
        /// section and portal stores real distances, but a silent mismatch here costs hours downstream.
        /// </summary>
        private static void RepointFinalSection(CaveRoute route, int finalIndex)
        {
            foreach (CaveRouteSplineDefinition definition in route.Definitions)
            {
                if (!definition.isMainRoute)
                    continue;

                for (int i = 0; i < definition.sections.Count; i++)
                {
                    CaveRouteSection section = definition.sections[i];
                    bool isLast = i == definition.sections.Count - 1;
                    if (isLast)
                        section.endKnot = finalIndex;
                    else if (section.endKnot > finalIndex - 3)
                        throw new InvalidOperationException(
                            $"section {section.zoneId} endKnot={section.endKnot} reaches into the " +
                            "inserted range; expected only the final section to");
                }
            }
        }

        private static void SetKnotProfile(Spline spline, int knotIndex, float width, float height)
        {
            SetKnotFloat(spline, CaveRoute.WidthDataKey, knotIndex, width);
            SetKnotFloat(spline, CaveRoute.HeightDataKey, knotIndex, height);
        }

        private static void SetKnotFloat(Spline spline, string key, int knotIndex, float value)
        {
            if (!spline.TryGetFloatData(key, out SplineData<float> data))
                throw new InvalidOperationException($"route has no '{key}' channel");

            for (int i = 0; i < data.Count; i++)
            {
                if (Mathf.RoundToInt(data[i].Index) != knotIndex)
                    continue;
                data.SetDataPoint(i, new DataPoint<float>(data[i].Index, value));
                return;
            }

            // The inserted knot got its data point from InsertKnotAtSegmentMidpoint; the pre-existing
            // final knot has had one since authoring. Missing means the channel is malformed.
            throw new InvalidOperationException($"no '{key}' data point at knot {knotIndex}");
        }

        private static float EvaluateKnotFloat(Spline spline, string key, int knotIndex)
        {
            if (!spline.TryGetFloatData(key, out SplineData<float> data))
                throw new InvalidOperationException($"route has no '{key}' channel");
            return data.Evaluate(spline, knotIndex, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
        }

        private static string DescribeProfile(Spline spline)
        {
            var text = new System.Text.StringBuilder("CAVE_EXIT_SHRINK profile:");
            for (int knot = 0; knot < spline.Count; knot++)
            {
                float w = EvaluateKnotFloat(spline, CaveRoute.WidthDataKey, knot);
                float h = EvaluateKnotFloat(spline, CaveRoute.HeightDataKey, knot);
                text.Append($" [{knot}] {w:0.#}x{h:0.#}");
            }
            return text.ToString();
        }

        private static string Round(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";
    }
}
