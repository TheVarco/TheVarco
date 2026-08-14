using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    /// <summary>
    /// Grows every cross-section on an already-baked route so the submarine that is actually in the game
    /// fits through the cave.
    ///
    /// Why: the cave was dimensioned from MAP_GUIDE.md, whose reference submarine is 6 x 3 x 3 m. The
    /// submarine in the game has been scaled 2x on its prefab root since it was created, making its
    /// collision hull 6.55 m across and 16.23 m long. Measured against the cave as authored, that hull
    /// has no flyable path anywhere - it would have to shrink to 0.67x - and every zone boundary blocks
    /// it. The Z2 -> Z3 boundary admits only 0.74x, which is where it wedged; Z1 -> Z2 admits 0.99x,
    /// which is why the route felt fine right up to that point.
    ///
    /// Mechanism: the cross-section lives in per-knot SplineData channels (cave.width / cave.height,
    /// smoothstep-interpolated). Every knot's pair is rewritten in place. Nothing touches the knot
    /// positions, so route length, rise, slope and turn radius must come out unchanged - the verification
    /// compares them by value, not against a range.
    ///
    /// Deliberately not a rebuild from CaveBlockoutPreset. A rebuild re-authors the spline from scratch,
    /// which would discard the two knots CaveExitShrinkBatch inserted at ~549 m and ~564 m, the authored
    /// noise settings, and the exit taper the exterior scene is built around. An in-place edit keeps all
    /// of it. The preset's tables are updated to the same numbers so a future fresh build agrees.
    ///
    /// Runs on BOTH MainMap and MainScene_final: each scene carries its own copy of the route, while the
    /// generated shell mesh (Assets/Generated/CaveBlockout/CaveShell.asset) is one shared asset rewritten
    /// in place. If the two scenes' routes disagreed, whichever regenerated last would silently reshape
    /// the other scene's cave.
    /// </summary>
    public static class CaveCrossSectionBatch
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const float Tolerance = 0.05f;

        /// <summary>
        /// The full authored profile, before and after, one row per knot of the 15-knot baked route.
        ///
        /// Spelled out rather than computed as "current * 1.5" so the edit is auditable and exactly
        /// idempotent: a knot must read as either its before or its after value, and anything else stops
        /// the run rather than being scaled a second time.
        ///
        /// Chamber values are the guide's zone sizes times CrossSectionGrowth. Boundary knots (2, 4, 6,
        /// 8, 10) come from CaveBlockoutPreset.ZoneBoundaries instead, which carries the difficulty
        /// gradient. Knots 11-13 hold the Z6 chamber and knot 14 is the exit mouth.
        /// </summary>
        private static readonly (int knot, string what, float fromWidth, float fromHeight, float toWidth, float toHeight)[] Profile =
        {
            (0,  "Z1 start",   35f, 20f, 52.5f, 30f),
            (1,  "Z1 chamber", 35f, 20f, 52.5f, 30f),
            (2,  "Z1-Z2",      16f, 12f, 27f,   19f),
            (3,  "Z2 chamber", 65f, 30f, 97.5f, 45f),
            (4,  "Z2-Z3",      16f, 12f, 26f,   19f),
            (5,  "Z3 chamber", 25f, 25f, 37.5f, 37.5f),
            (6,  "Z3-Z4",      16f, 12f, 25f,   19f),
            (7,  "Z4 chamber", 18f, 18f, 27f,   27f),
            (8,  "Z4-Z5",      16f, 12f, 25f,   19f),
            (9,  "Z5 chamber", 30f, 60f, 45f,   90f),
            (10, "Z5-Z6",      16f, 12f, 24f,   18f),
            (11, "Z6 chamber", 85f, 50f, 127.5f, 75f),
            (12, "Z6 hold",    85f, 50f, 127.5f, 75f),
            (13, "Z6 hold",    85f, 50f, 127.5f, 75f),
            // The exit mouth grows with everything else. It has to: the win condition is driving the
            // submarine out through it, and 24 x 16 m does not admit a 6.55 m hull that cannot pitch on a
            // 27 degree climb. This does move geometry the exterior beach and cutscene framing were built
            // against (see HANDOFF-exterior.md), so the exterior exit gate needs re-running afterwards.
            (14, "exit mouth", 24f, 16f, 36f,   24f)
        };

        [MenuItem("Tools/Underwater Cave/Grow Cross-Sections For Real Hull (MainMap)")]
        public static void GrowMainMapInteractive() => GrowMainMapBatch();

        public static void AlignBranchEntriesMainMapBatch() => RunGuarded(() => AlignBranchEntries(MainMapPath, false));

        public static void AlignBranchEntriesMainSceneFinalBatch() => RunGuarded(() => AlignBranchEntries(PlayScenePath, true));

        private static void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_CROSS_SECTION FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Pushes each resource branch out to the widened main tunnel's wall.
        ///
        /// CaveBlockoutPreset.CreateRoutes starts a branch at max(5, mainWidth / 2) from the centreline,
        /// so the branch mouth lands on the main tunnel's wall. Growing the main cross-sections in place
        /// moved that wall outward but left the branch splines where they were, which buried the first
        /// stretch of each branch inside the main chamber - Z2's mouth sat 32.5 m out against a wall now
        /// 48.8 m out. The junction weld then has nothing sane to weld, and a swimmer entering the Z2
        /// branch hits shell 2.7 m in. Z2/Z4/Z5 each hold three oxygen canisters, so a broken branch is a
        /// broken supply line, not just bad geometry.
        ///
        /// Translates the whole branch spline along its existing outward direction, which reproduces what
        /// a fresh build would author: the branch keeps its length, bend and rise, and only its mouth
        /// moves. Recomputing the branch from scratch would instead discard any hand edits to it.
        /// </summary>
        private static void AlignBranchEntries(string scenePath, bool fusionDoubleSave)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            CaveRoute[] routes = UnityEngine.Object.FindObjectsByType<CaveRoute>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            CaveRoute mainRoute = routes.FirstOrDefault(
                candidate => candidate.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute branches = routes.FirstOrDefault(candidate => candidate != mainRoute);
            if (mainRoute == null || branches == null)
                throw new InvalidOperationException($"no main route or branch container in {scenePath}");

            List<string> applied = new List<string>();
            List<string> skipped = new List<string>();

            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                float t = mainRoute.ResolvePortalT(portal);
                Vector3 center = mainRoute.transform.TransformPoint(
                    mainRoute.Container[0].EvaluatePosition(t));
                float required = Mathf.Max(5f, mainRoute.EvaluateWidth(0, t) * 0.5f);

                Spline branch = branches.Container[portal.branchSplineIndex];
                Vector3 entry = branches.transform.TransformPoint(branch[0].Position);
                Vector3 outward = entry - center;
                float current = outward.magnitude;
                if (current < 0.01f)
                    throw new InvalidOperationException(
                        $"{portal.zoneId} branch starts on the main centreline; no outward direction to follow");

                if (Mathf.Abs(current - required) < 0.05f)
                {
                    skipped.Add($"{portal.zoneId} branch mouth already {current:0.##} m out");
                    continue;
                }

                Vector3 shift = branches.transform.InverseTransformVector(
                    outward.normalized * (required - current));
                for (int i = 0; i < branch.Count; i++)
                {
                    BezierKnot knot = branch[i];
                    knot.Position += (Unity.Mathematics.float3)shift;
                    branch[i] = knot;
                }

                applied.Add($"{portal.zoneId} branch mouth {current:0.##} -> {required:0.##} m out " +
                            $"(main tunnel is {mainRoute.EvaluateWidth(0, t):0.#} m wide here)");
            }

            if (applied.Count == 0)
            {
                Debug.Log($"CAVE_BRANCH_ENTRY SKIP scene={scenePath} [{string.Join(", ", skipped)}]");
                return;
            }

            EditorUtility.SetDirty(branches);
            EditorUtility.SetDirty(branches.Container);
            EditorSceneManager.MarkSceneDirty(branches.gameObject.scene);

            CaveValidationResult result = CaveBlockoutBuilder.RegenerateCurrentScene(true);

            if (fusionDoubleSave)
            {
                EditorSceneManager.MarkSceneDirty(branches.gameObject.scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("second save refused");
            }

            Debug.Log($"CAVE_BRANCH_ENTRY {(result.Passed ? "PASS" : "WARN")} scene={scenePath} " +
                      $"moved={applied.Count} skipped={skipped.Count}");
            foreach (string line in applied)
                Debug.Log("CAVE_BRANCH_ENTRY " + line);
            foreach (string issue in result.issues)
                Debug.LogWarning("CAVE_BRANCH_ENTRY: " + issue);
            if (!result.Passed)
                throw new InvalidOperationException("blockout validation failed after moving the branch mouths");
        }

        public static void GrowMainMapBatch()
        {
            try
            {
                Grow(MainMapPath, fusionDoubleSave: false);
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_CROSS_SECTION FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void GrowMainSceneFinalBatch()
        {
            try
            {
                // MainScene_final carries Fusion NetworkObjects whose SortKeys are baked from
                // GlobalObjectId on sceneSaving. A freshly serialised object has no file id until the
                // first save, so the first save writes provisional keys and only the second settles them.
                // Regeneration itself saves once; the extra save below is the second.
                Grow(PlayScenePath, fusionDoubleSave: true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"CAVE_CROSS_SECTION FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        private static void Grow(string scenePath, bool fusionDoubleSave)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            CaveRoute route = UnityEngine.Object
                .FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.Definitions.Any(definition => definition.isMainRoute));
            if (route == null)
                throw new InvalidOperationException($"no main CaveRoute in {scenePath}");

            SplineContainer container = route.Container;
            Spline spline = container[0];
            if (spline.Count != Profile.Length)
                throw new InvalidOperationException(
                    $"{scenePath} has {spline.Count} knots but this edit was written against " +
                    $"{Profile.Length}; refusing to guess which knot is which");

            List<string> applied = new List<string>();
            List<string> skipped = new List<string>();

            foreach ((int knot, string what, float fromWidth, float fromHeight, float toWidth, float toHeight) row in Profile)
            {
                float width = EvaluateKnotFloat(spline, CaveRoute.WidthDataKey, row.knot);
                float height = EvaluateKnotFloat(spline, CaveRoute.HeightDataKey, row.knot);

                // Per-knot rather than one check up front: a run that died partway leaves some knots grown
                // and some not, and that is a state this has to be able to finish rather than refuse.
                if (Near(width, row.toWidth) && Near(height, row.toHeight))
                {
                    skipped.Add($"[{row.knot}] {row.what} already {width:0.#}x{height:0.#}");
                    continue;
                }

                if (!Near(width, row.fromWidth) || !Near(height, row.fromHeight))
                    throw new InvalidOperationException(
                        $"knot {row.knot} ({row.what}) is {width:0.##}x{height:0.##}, expected either " +
                        $"{row.fromWidth:0.#}x{row.fromHeight:0.#} or {row.toWidth:0.#}x{row.toHeight:0.#} - " +
                        "the route is not in the state this edit was written against; refusing to guess");

                SetKnotFloat(spline, CaveRoute.WidthDataKey, row.knot, row.toWidth);
                SetKnotFloat(spline, CaveRoute.HeightDataKey, row.knot, row.toHeight);
                applied.Add($"[{row.knot}] {row.what} {width:0.#}x{height:0.#} -> {row.toWidth:0.#}x{row.toHeight:0.#}");
            }

            if (applied.Count == 0)
            {
                Debug.Log($"CAVE_CROSS_SECTION SKIP scene={scenePath} all {skipped.Count} knots already grown");
                // Still needed: the branch mouths sit at the main tunnel's wall, which this moved.
                AlignBranchEntries(scenePath, fusionDoubleSave);
                return;
            }

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

            Debug.Log($"CAVE_CROSS_SECTION {(result.Passed ? "PASS" : "WARN")} scene={scenePath} " +
                      $"grown={applied.Count} skipped={skipped.Count} growth={CaveBlockoutPreset.CrossSectionGrowth:0.##}x");
            Debug.Log($"CAVE_CROSS_SECTION_METRICS scene={scenePath} length={result.routeLength:F4} " +
                      $"rise={result.totalRise:F4} slope={result.maximumSlope:F4} " +
                      $"radius={result.minimumTurnRadius:F4} minWidth={result.minimumWidth:F4} " +
                      $"minHeight={result.minimumHeight:F4}");
            foreach (string line in applied)
                Debug.Log("CAVE_CROSS_SECTION " + line);
            foreach (string line in skipped)
                Debug.Log("CAVE_CROSS_SECTION skipped " + line);
            foreach (string issue in result.issues)
                Debug.LogWarning("CAVE_CROSS_SECTION: " + issue);
            if (!result.Passed)
                throw new InvalidOperationException("blockout validation failed after growing the cross-sections");

            // The branch mouths are anchored to the main tunnel's wall, which just moved.
            AlignBranchEntries(scenePath, fusionDoubleSave);
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < Tolerance;

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

            throw new InvalidOperationException($"no '{key}' data point at knot {knotIndex}");
        }

        private static float EvaluateKnotFloat(Spline spline, string key, int knotIndex)
        {
            if (!spline.TryGetFloatData(key, out SplineData<float> data))
                throw new InvalidOperationException($"route has no '{key}' channel");
            return data.Evaluate(spline, knotIndex, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
        }
    }
}
