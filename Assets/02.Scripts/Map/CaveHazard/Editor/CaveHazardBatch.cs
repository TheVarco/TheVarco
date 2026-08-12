using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using CaveBlockout.Editor.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// Batch entry points for the Z3 rockfall and Z5 vent pass.
    /// </summary>
    public static class CaveHazardBatch
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";

        /// <summary>
        /// Metres of slack around a hazard mount that must be free of dressing. The prop itself is
        /// accounted for by its own boundingRadius on top of this.
        /// </summary>
        private const float DecorClearanceMeters = 2.5f;

        /// <summary>
        /// How much open air a falling rock is guaranteed. Only the shell is consulted by the drop
        /// check, so without this a boulder scattered three metres under a spawner would stop every
        /// rock on frame one and every automated check would still report a healthy 20 m drop.
        /// </summary>
        private const float RockColumnClearMeters = 12f;

        [MenuItem("Tools/Underwater Cave/Hazards/1 - 장애물 배치와 검증")]
        public static void BuildAllInteractive()
        {
            Debug.Log(BuildAll(save: true));
        }

        public static void BuildAllBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(BuildAll(save: true));
        }

        public static void ValidateBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            CaveDecorContext context = CaveDecorContext.Create();
            CaveHazardValidator.Report report = CaveHazardValidator.Validate(CaveHazardLayout.Build(), context);
            Debug.Log(CaveHazardValidator.Format(report));
        }

        /// <summary>
        /// Regenerates the blockout under the hazards and re-validates them on the new surface.
        ///
        /// This is the check the route-relative storage exists for, and it matters more here than it
        /// does for decor: the Z5 vent scales are hand-fitted to measured floor depths, and
        /// regenerating reseeds the surface noise. A vent that reaches the swim line by 1.5 m today can
        /// stop reaching it, or end up half-buried, without anything else in the scene looking wrong.
        ///
        /// SIDE EFFECT: rewrites Assets/Generated/CaveBlockout/. The scene is deliberately NOT saved,
        /// but that folder must still be reverted:
        ///     git restore Assets/Generated/CaveBlockout/
        /// </summary>
        public static void RegressionRegenerateBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);

            var text = new StringBuilder();
            text.AppendLine("===== CAVE HAZARD REGENERATION REGRESSION =====");

            List<CaveHazardStation> stations = CaveHazardLayout.Build();

            CaveHazardValidator.Report before =
                CaveHazardValidator.Validate(stations, CaveDecorContext.Create());
            text.AppendLine($"before: resolved={before.resolved}/{before.instances} " +
                            $"failures={before.failures.Count} warnings={before.warnings.Count}");

            CaveBlockout.Editor.CaveBlockoutBuilder.RegenerateCurrentScene(false);
            text.AppendLine("blockout regenerated (noise reseeded)");

            CaveDecorContext after = CaveDecorContext.Create();
            CaveHazardSpawner.Result respawn = CaveHazardSpawner.Rebuild(stations, after);
            text.AppendLine($"respawn: spawned={respawn.spawned} missingSurface={respawn.missingSurface}");

            CaveHazardValidator.Report afterReport = CaveHazardValidator.Validate(stations, after);
            text.Append(CaveHazardValidator.Format(afterReport));
            text.AppendLine(afterReport.Passed && respawn.missingSurface == 0
                ? "HAZARD_REGENERATION_REGRESSION PASS"
                : "HAZARD_REGENERATION_REGRESSION FAIL");
            text.AppendLine("NOTE: scene not saved; run 'git restore Assets/Generated/CaveBlockout/'.");

            Debug.Log(text.ToString());
        }

        public static string BuildAll(bool save)
        {
            var text = new StringBuilder();
            text.AppendLine("===== CAVE HAZARD BUILD =====");

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                text.AppendLine("FAIL: no CaveRoute or CaveShell collider in the scene");
                return text.ToString();
            }

            List<CaveHazardStation> stations = CaveHazardLayout.Build();

            CaveHazardSpawner.Result spawn = CaveHazardSpawner.Rebuild(stations, context);
            text.AppendLine($"spawn: stations={spawn.stations} spawned={spawn.spawned} " +
                            $"missingSurface={spawn.missingSurface} removed={spawn.removed}");
            foreach (string note in spawn.notes)
                text.AppendLine($"  {note}");

            int lights = QuietWarningLights();
            text.AppendLine($"warningLights: disabled {lights} at author time " +
                            "(RockSpawner.SetWarningVisible re-enables them at run time)");

            RemoveOverlappingDecor(context, stations, text);

            CaveHazardValidator.Report validation = CaveHazardValidator.Validate(stations, context);
            text.Append(CaveHazardValidator.Format(validation));

            if (save)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                text.AppendLine("saved scene and assets");
            }

            return text.ToString();
        }

        /// <summary>
        /// Turns off the rockfall warning spot lights on the freshly placed instances.
        ///
        /// FallingRockSpawner ships with its light enabled, and RockSpawner.Awake only switches it off
        /// at run time. Two dozen 1000-intensity spot lights burning in an unplayed scene is exactly the
        /// kind of thing that makes the Scene view unusable for map editing. Storing the off state as a
        /// prefab override costs nothing at run time because SetWarningVisible assigns enabled outright.
        /// </summary>
        private static int QuietWarningLights()
        {
            Transform hazards = CaveHazardSpawner.GetOrCreateHazardRoot();
            int count = 0;
            foreach (Light light in hazards.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !light.enabled)
                    continue;
                light.enabled = false;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Drops decor records that collide with the new hazards.
        ///
        /// Records, not scene objects. Deleting the GameObjects under Z3_Decor would look right until
        /// the next CaveDecorBatch.RebuildFromData, which re-creates all of them from
        /// MainMapCaveDecor.asset. This is the same removal path the DarkFloatingIsland pass used.
        ///
        /// WARNING: this is destructive and cumulative. Removing a record cannot be undone from inside
        /// the tool, so retuning CaveHazardLayout and rebuilding erodes the decor set a little further
        /// every pass - a station that moves leaves its old victims deleted and claims new ones. Before
        /// a final build, restore MainMapCaveDecor.asset from git so the removal set describes the
        /// layout that actually shipped rather than the union of every layout that was ever tried.
        /// </summary>
        private static void RemoveOverlappingDecor(CaveDecorContext context,
            List<CaveHazardStation> stations, StringBuilder text)
        {
            var set = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            if (set == null)
            {
                text.AppendLine($"decor: no CaveDecorSet at {CaveDecorCatalog.DecorSetPath}; nothing removed");
                return;
            }

            var mounts = new List<(CaveHazardInstance instance, Vector3 position)>();
            foreach (CaveHazardStation station in stations)
            {
                foreach (CaveHazardInstance instance in station.instances)
                {
                    if (CaveDecorProjector.TryCast(context, instance.routeId, instance.routeDistance,
                            instance.angleDegrees, out CaveDecorSurface surface))
                    {
                        mounts.Add((instance, surface.point + surface.normal * instance.surfaceOffset));
                    }
                }
            }

            int before = set.Placements.Count;
            var removedByZone = new Dictionary<string, int>();
            var doomed = new List<CaveDecorPlacement>();

            foreach (CaveDecorPlacement placement in set.Placements)
            {
                CaveDecorPaletteEntry entry = set.FindEntry(placement.paletteId);
                float radius = (entry != null ? entry.boundingRadius : 2f) * Mathf.Max(0.01f, placement.scale);

                if (!CaveDecorProjector.TryResolve(context, placement, out Vector3 position, out _, out _))
                    continue;

                if (Collides(position, radius, mounts))
                    doomed.Add(placement);
            }

            foreach (CaveDecorPlacement placement in doomed)
            {
                string zone = placement.zoneId ?? "?";
                removedByZone.TryGetValue(zone, out int count);
                removedByZone[zone] = count + 1;
                set.Placements.Remove(placement);
            }

            if (doomed.Count > 0)
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();

                CaveDecorSpawner.Result rebuild = CaveDecorSpawner.Rebuild(set, context);
                text.AppendLine($"decor: removed {doomed.Count} placements ({before} -> {set.Placements.Count}), " +
                                $"respawned {rebuild.spawned}");
                foreach (var pair in removedByZone)
                    text.AppendLine($"  {pair.Key}: -{pair.Value}");
            }
            else
            {
                text.AppendLine($"decor: nothing overlapped ({before} placements untouched)");
            }
        }

        private static bool Collides(Vector3 position, float radius,
            List<(CaveHazardInstance instance, Vector3 position)> mounts)
        {
            foreach ((CaveHazardInstance instance, Vector3 mount) in mounts)
            {
                float hazardRadius = instance.kind == CaveHazardKind.Vent ? 1.15f * instance.scale : 0.45f;
                if (Vector3.Distance(position, mount) < radius + hazardRadius + DecorClearanceMeters)
                    return true;

                if (instance.kind != CaveHazardKind.FallingRock)
                    continue;

                // Keep the first stretch of every rock's fall clear of dressing.
                if (position.y > mount.y || position.y < mount.y - RockColumnClearMeters)
                    continue;
                var flat = new Vector2(position.x - mount.x, position.z - mount.z);
                if (flat.magnitude < radius + 1.5f)
                    return true;
            }

            return false;
        }
    }
}
