using System;
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
    /// -executeMethod entry points, so the whole decor pipeline can be driven from a headless editor
    /// run. Every method opens MainMap.unity explicitly rather than assuming whatever scene batch mode
    /// happened to load.
    /// </summary>
    public static class CaveDecorBatch
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";

        /// <summary>
        /// Every scene holding a copy of the cave, in the order they should be visited.
        ///
        /// MainMap is where the decor tools author; MainScene_final is where it is played. The two
        /// carry separate baked copies of the same 392 instances, so anything that edits instances
        /// rather than prefabs or materials has to visit both. MainScene_young was a third copy and
        /// was archived to 01.Scenes/Old on 2026-08-13 - add a path here if another play scene ever
        /// adopts the cave, or these passes will report on a scene nobody ships.
        /// </summary>
        private static readonly string[] CaveScenePaths =
        {
            MainMapPath,
            "Assets/01.Scenes/MainScene_final.unity"
        };

        public static void PrepareAssets()
        {
            // Batch mode starts on an untitled unsaved scene, and Unity will not add a scene next to
            // one of those. Opening MainMap first gives the prep pass somewhere to measure.
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(CaveDecorAssetPrep.Prepare());
        }

        /// <summary>
        /// Reports the real world-space size of the dressing already in the scene. Worth running
        /// before adoption: the legacy instances were authored with raw FBX scale values in the
        /// hundreds, and whether that is correct depends entirely on the model's native unit size.
        /// </summary>
        public static void InspectLegacyDecor()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);

            var report = new StringBuilder();
            report.AppendLine("===== LEGACY DECOR INSPECTION =====");

            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            Transform decor = blockout != null ? blockout.transform.Find(CaveDecorNames.DecorRoot) : null;
            if (decor == null)
            {
                report.AppendLine("no CaveBlockout/Decor group");
                Debug.Log(report.ToString());
                return;
            }

            foreach (Transform child in decor.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                    continue;

                Bounds bounds = default;
                bool any = false;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject);
                report.AppendLine(
                    $"{child.name} src={(source != null ? source.name : "?")} " +
                    $"lossyScale={child.lossyScale.x:0.##} " +
                    $"worldSize={(any ? bounds.size.ToString("0.#") : "no renderer")} " +
                    $"center={(any ? bounds.center.ToString("0.#") : "-")}");
            }

            ReportGeneratedPrefabSizes(report);
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Instantiates each generated prefab at scale 1 and reports its world size. The whole
        /// placement pipeline assumes "scale 1 means one metre", and that assumption is only worth
        /// anything if it is checked against a real instantiation rather than the prep-time measurement
        /// that produced it.
        /// </summary>
        private static void ReportGeneratedPrefabSizes(StringBuilder report)
        {
            report.AppendLine("--- generated prefabs at scale 1 ---");

            var set = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            if (set == null)
            {
                report.AppendLine("no decor set");
                return;
            }

            foreach (CaveDecorPaletteEntry entry in set.Palette)
            {
                if (entry?.prefab == null)
                    continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                Bounds bounds = default;
                bool any = false;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                // Mesh LOD is enabled on the importer, but the flag being set is not evidence that
                // usable levels came out the other side. lodCount is.
                int lodLevels = 0;
                int triangles = 0;
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    lodLevels = Mathf.Max(lodLevels, filter.sharedMesh.lodCount);
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }

                report.AppendLine(any
                    ? $"{entry.id}: size={bounds.size:0.###} pivotOffset={bounds.center:0.###} " +
                      $"storedRadius={entry.boundingRadius:0.###} tris={triangles} meshLodLevels={lodLevels}"
                    : $"{entry.id}: no renderer");

                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Asset prep, adopt the hand-placed dressing, run the base pass, rebuild and validate.</summary>
        public static void BuildAll()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR BUILD ALL =====");

            // Before prep, not after: prep needs a saved scene open so it can add a scratch scene to
            // measure in, and batch mode starts on an untitled one.
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            report.AppendLine(CaveDecorAssetPrep.Prepare());

            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell MeshCollider in MainMap.");
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorAdoption.Result adopted = CaveDecorAdoption.Adopt(set, context, true);
            report.AppendLine("adopt: " + CaveDecorAdoption.Describe(adopted));

            CaveDecorAutoScatter.Result scattered = CaveDecorAutoScatter.Scatter(set, context, false);
            report.AppendLine("scatter: " + CaveDecorAutoScatter.Describe(scattered));

            CaveDecorSpawner.Result spawned = CaveDecorSpawner.Rebuild(set, context);
            report.AppendLine("rebuild: " + CaveDecorSpawner.Describe(spawned));

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            AppendValidation(set, context, report);
            Debug.Log(report.ToString());
        }

        /// <summary>Re-projects existing records onto the current shell. The blockout-regeneration recovery path.</summary>
        public static void RebuildFromData()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR REBUILD =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorContext context = CaveDecorContext.Create();
            report.AppendLine("rebuild: " + CaveDecorSpawner.Describe(CaveDecorSpawner.Rebuild(set, context)));
            EditorSceneManager.SaveOpenScenes();

            AppendValidation(set, context, report);
            Debug.Log(report.ToString());
        }

        public static void Validate()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR VALIDATE =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            AppendValidation(set, CaveDecorContext.Create(), report);
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Per-species collider coverage over the live instances in every scene that carries the cave.
        ///
        /// Asset prep works on prefabs, so "the prefab has a MeshCollider" is one step short of the
        /// thing that was actually reported - a player swimming through a rock in a play scene. This
        /// counts colliders on the spawned instances instead, and it does it in each scene, because
        /// MainMap is not the scene anyone plays.
        /// </summary>
        public static void ReportColliders()
        {
            string[] scenes = CaveScenePaths;

            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR COLLIDER COVERAGE =====");

            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    report.AppendLine($"{scenePath}: MISSING");
                    continue;
                }

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var withCollider = new SortedDictionary<string, int>();
                var without = new SortedDictionary<string, int>();
                int solid = 0;
                int swimThrough = 0;

                foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
                {
                    string id = string.IsNullOrEmpty(marker.paletteId) ? "<unknown>" : marker.paletteId;
                    if (marker.GetComponentInChildren<Collider>(true) != null)
                    {
                        withCollider.TryGetValue(id, out int n);
                        withCollider[id] = n + 1;
                        solid++;
                    }
                    else
                    {
                        without.TryGetValue(id, out int n);
                        without[id] = n + 1;
                        swimThrough++;
                    }
                }

                report.AppendLine($"{scenePath}: solid={solid} swimThrough={swimThrough} " +
                                  $"species={withCollider.Count}+{without.Count}");
                foreach (var pair in without)
                    report.AppendLine($"    no collider: {pair.Key} x{pair.Value}");
            }

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Species retired from the catalogue, listed by palette id.
        ///
        /// Kept as an explicit list rather than derived from the catalogue by absence, because
        /// "not in the catalogue" is also true of every asset nobody has authored an entry for yet.
        /// </summary>
        private static readonly string[] RetiredPaletteIds = { "SciFiStorageTank", "BlueFacetedArch" };

        /// <summary>
        /// Removes a retired species from the record set and from every scene that carries the cave.
        ///
        /// Deleting the catalogue entry is not enough on its own, and neither is deleting the scene
        /// objects. The records in MainMapCaveDecor.asset would respawn the props on the next
        /// RebuildFromData, and MainScene_final holds its own baked copy that inherits nothing from
        /// MainMap - a decor rebuild never touches it. All three have to go.
        ///
        /// Idempotent, and it has to be: a teammate branching before the retirement lands brings the
        /// props back in their copy of the play scene, and the merge resolution is to re-run this.
        /// By then the records are already gone, so the count in the set is not a usable expectation
        /// for the scenes. What is checked instead is the invariant that matters - after the save,
        /// no scene holds an instance of a retired species - plus a report of how many each scene
        /// had, so a scene that has drifted out of step with MainMap is visible rather than silent.
        /// </summary>
        public static void RetireSpecies()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR RETIRE SPECIES =====");
            report.AppendLine("retiring: " + string.Join(", ", RetiredPaletteIds));

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            var retired = new HashSet<string>(RetiredPaletteIds);
            var perSpecies = new SortedDictionary<string, int>();
            foreach (CaveDecorPlacement placement in set.Placements)
            {
                if (placement == null || !retired.Contains(placement.paletteId))
                    continue;

                perSpecies.TryGetValue(placement.paletteId, out int n);
                perSpecies[placement.paletteId] = n + 1;
            }

            foreach (var pair in perSpecies)
                report.AppendLine($"    records: {pair.Key} x{pair.Value}");

            int paletteRemoved = set.EditablePalette.RemoveAll(e => e != null && retired.Contains(e.id));
            int placementsRemoved = set.Placements.RemoveAll(p => p != null && retired.Contains(p.paletteId));
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            report.AppendLine($"decorSet: palette -{paletteRemoved}, placements -{placementsRemoved}, " +
                              $"placements kept={set.Placements.Count}");

            string[] scenes = CaveScenePaths;

            bool allMatched = true;
            foreach (string scenePath in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    report.AppendLine($"{scenePath}: MISSING");
                    allMatched = false;
                    continue;
                }

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var doomed = new List<GameObject>();
                foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
                {
                    if (marker != null && retired.Contains(marker.paletteId))
                        doomed.Add(marker.gameObject);
                }

                if (doomed.Count == 0)
                {
                    report.AppendLine($"{scenePath}: already clean");
                    continue;
                }

                foreach (GameObject instance in doomed)
                    UnityEngine.Object.DestroyImmediate(instance);

                // DestroyImmediate outside the undo system leaves the scene flagged clean, and
                // SaveOpenScenes writes nothing for a clean scene. The first run of this method
                // reported "deleted 8" three times and changed no file on disk.
                CaveDecorSpawner.MarkSceneDirty();
                if (!EditorSceneManager.SaveOpenScenes())
                {
                    report.AppendLine($"{scenePath}: FAIL save refused");
                    allMatched = false;
                    continue;
                }

                // Re-read after the save rather than trusting the delete: the count above came from
                // the objects in memory, and the point of this method is what lands in the file.
                int left = 0;
                foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
                {
                    if (marker != null && retired.Contains(marker.paletteId))
                        left++;
                }

                report.AppendLine($"{scenePath}: deleted {doomed.Count}, remaining {left}");
                if (left != 0)
                    allMatched = false;
            }

            report.AppendLine(allMatched ? "RETIRE PASS" : "RETIRE FAIL - see above");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// The regression the whole route-relative design exists for: regenerate the cave, rebuild the
        /// decor from records, and confirm nothing is left floating or blocking the channel.
        ///
        /// SIDE EFFECT: CaveBlockoutBuilder.RegenerateCurrentScene rewrites Assets/Generated/CaveBlockout/.
        /// Revert that folder afterwards unless a blockout rebuild was actually wanted:
        ///     git checkout -- Assets/Generated/CaveBlockout/
        /// </summary>
        public static void RegressionRegenerate()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR REGENERATION REGRESSION =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorValidator.Report before = CaveDecorValidator.Validate(set, CaveDecorContext.Create());
            report.AppendLine($"before: resolved={before.resolved} unresolved={before.unresolved} " +
                              $"corridorViolations={before.corridorViolations}");

            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            report.AppendLine("blockout regenerated");

            CaveDecorContext context = CaveDecorContext.Create();
            report.AppendLine("rebuild: " + CaveDecorSpawner.Describe(CaveDecorSpawner.Rebuild(set, context)));

            CaveDecorValidator.Report after = CaveDecorValidator.Validate(set, context);
            report.AppendLine($"after:  resolved={after.resolved} unresolved={after.unresolved} " +
                              $"corridorViolations={after.corridorViolations}");
            AssetDatabase.SaveAssets();

            bool pass = after.unresolved == 0 && after.corridorViolations == 0 && after.resolved == before.resolved;
            report.AppendLine(pass ? "REGRESSION PASS" : "REGRESSION FAIL");
            report.AppendLine("NOTE: this rewrote Assets/Generated/CaveBlockout/. " +
                              "Run 'git checkout -- Assets/Generated/CaveBlockout/' unless a rebuild was intended.");

            Debug.Log(report.ToString());
        }

        private const string PositionDumpPath = "Artifacts/CaveDecor/pre-rebuild-positions.tsv";

        /// <summary>
        /// Records where every decor instance currently sits, keyed by placement id.
        ///
        /// Run this BEFORE regenerating the blockout. Placements are stored route-relative, so once the
        /// old cave surface is gone there is no way to reconstruct where a prop used to be - and that
        /// is exactly the number needed to judge whether "keep the Z1 records" preserved the authored
        /// composition or quietly rearranged it.
        /// </summary>
        public static void DumpInstancePositions()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);

            var text = new StringBuilder();
            int count = 0;
            foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
            {
                if (string.IsNullOrEmpty(marker.placementId))
                    continue;
                Vector3 p = marker.transform.position;
                text.AppendLine($"{marker.placementId}\t{p.x:R}\t{p.y:R}\t{p.z:R}\t{marker.zoneId}");
                count++;
            }

            string path = Path.Combine(ProjectRoot(), PositionDumpPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.ToString());
            Debug.Log($"===== DECOR POSITION DUMP =====\nwrote {count} positions to {path}");
        }

        /// <summary>
        /// Brings the decor set back in line after the cave has been rebuilt with a different route.
        ///
        /// Keeps Z1 and re-scatters Z2-Z6. Z1 is the only zone holding hand-authored work (the 23
        /// entrance props absorbed from the original scene); everything else was produced by the
        /// rule-based pass, so regenerating it is strictly better than re-projecting it.
        ///
        /// The first attempt kept Z1-Z4 on the theory that their zone boundaries barely move. The drift
        /// measurement disproved it: Z2 props landed 19.6 m from where they were on average, Z3 38.7 m
        /// and Z4 55.4 m, because the heading changes accumulate along the route. Re-projected
        /// auto-scatter that far off is just badly distributed auto-scatter.
        /// </summary>
        public static void RezoneAfterBlockoutRebuild()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR REZONE =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell MeshCollider in MainMap.");
                Debug.LogError(report.ToString());
                return;
            }

            var liveRoutes = new HashSet<string>(context.RouteIds);
            report.AppendLine("routes now present: " + string.Join(", ", liveRoutes));

            // Branch 3 is renamed Z6_Branch -> Z5_Branch by the 6-zone preset. A record pointing at a
            // route that no longer exists fails TryGetRoute and would otherwise be reported as
            // "found no surface", which hides the real cause.
            int staleRoute = set.Placements.RemoveAll(p => p != null && !liveRoutes.Contains(p.routeId));
            report.AppendLine($"dropped {staleRoute} placements referencing a route that no longer exists");

            ReportDrift(set, context, report);

            var kept = new HashSet<string> { "Z1" };
            int outOfScope = set.Placements.RemoveAll(p => p != null && !kept.Contains(p.zoneId));
            report.AppendLine($"dropped {outOfScope} placements outside Z1 (re-scattered below)");

            // Z1 is excluded from the filter so the pass does not stack a fresh quota on top of the
            // records that were just kept.
            var refill = new HashSet<string> { "Z2", "Z3", "Z4", "Z5", "Z6" };
            CaveDecorAutoScatter.Result scattered = CaveDecorAutoScatter.Scatter(set, context, false, refill);
            report.AppendLine("scatter: " + CaveDecorAutoScatter.Describe(scattered));

            CaveDecorSpawner.Result spawned = CaveDecorSpawner.Rebuild(set, context);
            report.AppendLine("rebuild: " + CaveDecorSpawner.Describe(spawned));

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            AppendValidation(set, context, report);
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// How far the surviving props moved when they were re-projected onto the new cave surface.
        /// Silence here would be the failure mode: "kept" means the record survived, not that the prop
        /// stayed put.
        /// </summary>
        private static void ReportDrift(CaveDecorSet set, CaveDecorContext context, StringBuilder report)
        {
            string path = Path.Combine(ProjectRoot(), PositionDumpPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                report.AppendLine("drift: no pre-rebuild position dump found, skipping " +
                                  "(run CaveDecorBatch.DumpInstancePositions before rebuilding next time)");
                return;
            }

            var previous = new Dictionary<string, Vector3>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 4)
                    continue;
                if (float.TryParse(parts[1], out float x) && float.TryParse(parts[2], out float y) &&
                    float.TryParse(parts[3], out float z))
                    previous[parts[0]] = new Vector3(x, y, z);
            }

            var perZone = new Dictionary<string, (int count, float sum, float max)>();
            int unresolved = 0;
            int matched = 0;

            foreach (CaveDecorPlacement placement in set.Placements)
            {
                if (!previous.TryGetValue(placement.id, out Vector3 before))
                    continue;

                matched++;
                if (!CaveDecorProjector.TryResolve(context, placement, out Vector3 after, out _, out _))
                {
                    unresolved++;
                    continue;
                }

                float distance = Vector3.Distance(before, after);
                string zone = placement.zoneId ?? "?";
                perZone.TryGetValue(zone, out var stats);
                perZone[zone] = (stats.count + 1, stats.sum + distance, Mathf.Max(stats.max, distance));
            }

            report.AppendLine($"drift: matched {matched} placements against the pre-rebuild dump, {unresolved} no longer land on a surface");
            var zones = new List<string>(perZone.Keys);
            zones.Sort(System.StringComparer.Ordinal);
            foreach (string zone in zones)
            {
                var stats = perZone[zone];
                report.AppendLine($"  {zone}: n={stats.count} mean={stats.sum / Mathf.Max(1, stats.count):0.00} m max={stats.max:0.00} m");
            }
        }

        /// <summary>
        /// Dresses the map with palette entries that have no placements yet, leaving everything already
        /// authored exactly where it is.
        ///
        /// This is the "new assets arrived" path. A plain top-up will not do: autoScatterCountPerZone is
        /// a quota to reach, not a cap on what is already there, so re-walking every zone for every
        /// entry hands each of the species already in the map a second full allocation. The filter is
        /// derived from the set rather than hard-coded to a folder, so it stays correct whichever
        /// entries turn out to be new.
        /// </summary>
        public static void ScatterNewPaletteEntries()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR SCATTER NEW ENTRIES =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorSet set = LoadSet(report);
            if (set == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell MeshCollider in MainMap.");
                Debug.LogError(report.ToString());
                return;
            }

            var placed = new HashSet<string>();
            foreach (CaveDecorPlacement placement in set.Placements)
            {
                if (placement != null && !string.IsNullOrEmpty(placement.paletteId))
                    placed.Add(placement.paletteId);
            }

            var fresh = new HashSet<string>();
            foreach (CaveDecorPaletteEntry entry in set.Palette)
            {
                if (entry != null && !placed.Contains(entry.id))
                    fresh.Add(entry.id);
            }

            report.AppendLine($"palette={set.Palette.Count} alreadyPlaced={placed.Count} toScatter={fresh.Count}");
            report.AppendLine("new entries: " + (fresh.Count > 0 ? string.Join(", ", fresh) : "none"));

            if (fresh.Count == 0)
            {
                report.AppendLine("nothing to do");
                Debug.Log(report.ToString());
                return;
            }

            int before = set.Placements.Count;
            CaveDecorAutoScatter.Result scattered =
                CaveDecorAutoScatter.Scatter(set, context, false, null, fresh);
            report.AppendLine("scatter: " + CaveDecorAutoScatter.Describe(scattered));
            report.AppendLine($"placements {before} -> {set.Placements.Count}");

            CaveDecorSpawner.Result spawned = CaveDecorSpawner.Rebuild(set, context);
            report.AppendLine("rebuild: " + CaveDecorSpawner.Describe(spawned));

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            ReportTriangleBudget(report);
            AppendValidation(set, context, report);
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// A tripwire, not a platform limit. Nothing else in the pipeline bounds the cost of the
        /// dressing - the &lt;50,000 triangle test everyone reaches for is the cave shell mesh - so this
        /// exists to make growth visible rather than to certify a frame budget. No profiling went into
        /// the figure: it is roughly double the 4.18M the six-zone pass measured, chosen so an asset
        /// drop of this size lands inside it and the one after has to be an explicit decision.
        ///
        /// It counts LOD0 across every instance in the map at once, which is never what gets drawn:
        /// mesh LOD is on for the whole library and zone visibility runs from 4 m to 40 m. Tripping it
        /// means "go look", not "thin the flora" - the first place to look is that Blue Crystal
        /// Formation, Violet Lowpoly Sea Fan and Dark Faceted Icicles hold a third of the total in
        /// fifty props, and Dark Faceted Icicles has already been through one decimation pass.
        /// </summary>
        private const int DecorTriangleBudget = 8_000_000;

        public static void ReportTriangleBudget(StringBuilder report)
        {
            long triangles = 0;
            int meshes = 0;
            var perZone = new Dictionary<string, long>();

            foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
            {
                long instanceTriangles = 0;
                foreach (MeshFilter filter in marker.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    instanceTriangles += filter.sharedMesh.triangles.Length / 3;
                    meshes++;
                }

                triangles += instanceTriangles;
                string zone = marker.zoneId ?? "?";
                perZone.TryGetValue(zone, out long zoneTriangles);
                perZone[zone] = zoneTriangles + instanceTriangles;
            }

            var zones = new List<string>(perZone.Keys);
            zones.Sort(StringComparer.Ordinal);
            var breakdown = new StringBuilder();
            foreach (string zone in zones)
                breakdown.Append($" {zone}={perZone[zone] / 1000}k");

            report.AppendLine($"decor LOD0 triangles: {triangles} / {DecorTriangleBudget} budget " +
                              $"({meshes} meshes) -" + breakdown);
            report.AppendLine(triangles <= DecorTriangleBudget
                ? "TRIANGLE BUDGET PASS"
                : "TRIANGLE BUDGET FAIL - thin autoScatterCountPerZone or drop a 50k set piece");
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }

        private static CaveDecorSet LoadSet(StringBuilder report)
        {
            var set = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            if (set == null)
                report.AppendLine($"FAIL: no CaveDecorSet at {CaveDecorCatalog.DecorSetPath}. Run PrepareAssets first.");
            return set;
        }

        private static void AppendValidation(CaveDecorSet set, CaveDecorContext context, StringBuilder report)
        {
            CaveDecorValidator.Report validation = CaveDecorValidator.Validate(set, context);
            string path = CaveDecorValidator.Write(validation, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            report.AppendLine($"validation report: {path}");
            report.AppendLine(CaveDecorValidator.Format(validation));
        }
    }
}
