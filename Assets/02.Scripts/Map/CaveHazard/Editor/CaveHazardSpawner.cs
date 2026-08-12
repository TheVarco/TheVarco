using System.Collections.Generic;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// Turns hazard stations into scene objects and wires their timetables.
    ///
    /// Everything the tool owns lives under CaveBlockout/Hazards. CaveBlockoutBuilder's
    /// ClearToolOwnedChildren only destroys Routes, Generated, Markers, Validation and Playtest, so
    /// this group survives a blockout regeneration exactly the way Decor does, and re-running the
    /// layout re-projects it onto whatever surface now exists.
    /// </summary>
    public static class CaveHazardSpawner
    {
        public const string HazardRoot = "Hazards";

        public const string VentPrefabPath = "Assets/03.Prefabs/Obstacle/Vent/Vent.prefab";
        public const string RockSpawnerPrefabPath = "Assets/03.Prefabs/Obstacle/FallingRock/FallingRockSpawner.prefab";

        public sealed class Result
        {
            public int stations;
            public int spawned;
            public int missingSurface;
            public int removed;
            public readonly List<string> notes = new List<string>();
        }

        public static string ZoneGroup(string zoneId) => (zoneId ?? "Unzoned") + "_Hazards";

        /// <summary>
        /// Destroys every tool-owned hazard, then rebuilds all of them from the station list.
        /// </summary>
        public static Result Rebuild(IReadOnlyList<CaveHazardStation> stations, CaveDecorContext context)
        {
            var result = new Result();

            if (context == null || !context.IsValid)
            {
                result.notes.Add("no CaveRoute or CaveShell collider in the scene");
                return result;
            }

            GameObject ventPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VentPrefabPath);
            GameObject rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockSpawnerPrefabPath);
            if (ventPrefab == null || rockPrefab == null)
            {
                result.notes.Add($"missing prefab: vent={ventPrefab != null} rockSpawner={rockPrefab != null}");
                return result;
            }

            result.removed = Clear();
            Transform hazards = GetOrCreateHazardRoot();

            foreach (CaveHazardStation station in stations)
            {
                result.stations++;
                Transform zoneGroup = GetOrCreateChild(hazards, ZoneGroup(station.zoneId));
                var stationRoot = new GameObject(station.id);
                stationRoot.transform.SetParent(zoneGroup, false);

                var groupA = new List<Component>();
                var groupB = new List<Component>();

                for (int i = 0; i < station.instances.Count; i++)
                {
                    CaveHazardInstance instance = station.instances[i];
                    if (!CaveDecorProjector.TryCast(context, instance.routeId, instance.routeDistance,
                            instance.angleDegrees, out CaveDecorSurface surface))
                    {
                        result.missingSurface++;
                        result.notes.Add($"{station.id}: no surface at d={instance.routeDistance:0.0} " +
                                         $"a={instance.angleDegrees:0}");
                        continue;
                    }

                    GameObject source = instance.kind == CaveHazardKind.Vent ? ventPrefab : rockPrefab;
                    var spawned = (GameObject)PrefabUtility.InstantiatePrefab(source, stationRoot.transform);

                    // Position from the cast, rotation from the world. See CaveHazardInstance: both
                    // prefabs are gravity-coupled, so the surface frame is exactly the wrong basis.
                    spawned.transform.position = surface.point + surface.normal * instance.surfaceOffset;
                    spawned.transform.rotation = Quaternion.Euler(0f, instance.yawDegrees, 0f);
                    spawned.transform.localScale = Vector3.one * instance.scale;
                    spawned.name = $"{source.name}_{instance.group}{i}";

                    Component target = instance.kind == CaveHazardKind.Vent
                        ? (Component)spawned.GetComponent<VentController>()
                        : spawned.GetComponent<RockSpawner>();

                    if (target == null)
                    {
                        result.notes.Add($"{station.id}: {source.name} has no controller component");
                        Object.DestroyImmediate(spawned);
                        result.missingSurface++;
                        continue;
                    }

                    if (instance.group == CaveHazardGroup.A) groupA.Add(target);
                    else groupB.Add(target);

                    result.spawned++;
                }

                ConfigureStation(station, stationRoot, groupA, groupB, result);
            }

            PruneEmptyGroups(hazards);
            return result;
        }

        /// <summary>
        /// Attaches and fills in the timetable. A Cross station gets a pattern component on its root;
        /// a single-instance Alone station does not, because VentController and RockSpawner each run
        /// their own Alone loop when no pattern has claimed them and an extra component would only add
        /// a layer that has to agree with itself.
        /// </summary>
        private static void ConfigureStation(CaveHazardStation station, GameObject stationRoot,
            List<Component> groupA, List<Component> groupB, Result result)
        {
            if (station.useCrossPattern)
            {
                if (groupA.Count == 0 || groupB.Count == 0)
                {
                    result.notes.Add($"{station.id}: cross station lost a group " +
                                     $"(A={groupA.Count} B={groupB.Count}); pattern not attached");
                    return;
                }

                Component pattern = station.kind == CaveHazardKind.Vent
                    ? (Component)stationRoot.AddComponent<VentPattern>()
                    : stationRoot.AddComponent<RockPattern>();
                var serialized = new SerializedObject(pattern);
                serialized.FindProperty("patternType").enumValueIndex = 1; // Cross
                FillList(serialized.FindProperty("groupA"), groupA);
                FillList(serialized.FindProperty("crossGroupB"), groupB);
                serialized.FindProperty("startDelay").floatValue = station.startDelaySeconds;
                serialized.FindProperty("initialWarningDuration").floatValue = station.initialWarningDuration;
                // VentPattern calls it activeDuration, RockPattern calls it dropInterval; both feed
                // ObstaclePatternBase.ActiveDuration.
                SerializedProperty active = serialized.FindProperty("activeDuration")
                                            ?? serialized.FindProperty("dropInterval");
                if (active != null) active.floatValue = station.activeDuration;
                serialized.FindProperty("crossWarningLeadTime").floatValue = station.crossWarningLeadTime;
                serialized.FindProperty("aloneRecoveryDuration").floatValue = station.aloneRecoveryDuration;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            foreach (Component target in groupA)
                ConfigureAloneTarget(station, target);
            foreach (Component target in groupB)
                ConfigureAloneTarget(station, target);
        }

        /// <summary>Writes the self-run timetable onto a VentController or RockSpawner instance.</summary>
        private static void ConfigureAloneTarget(CaveHazardStation station, Component target)
        {
            var serialized = new SerializedObject(target);
            SetFloat(serialized, "aloneStartDelay", station.startDelaySeconds);
            SetFloat(serialized, "aloneWarningDuration", station.initialWarningDuration);
            // RockSpawner has no active phase - it spawns one rock and rests - so this is vent-only.
            SetFloat(serialized, "aloneActiveDuration", station.activeDuration);
            SetFloat(serialized, "aloneRecoveryDuration", station.aloneRecoveryDuration);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(SerializedObject serialized, string path, float value)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property != null)
                property.floatValue = value;
        }

        private static void FillList(SerializedProperty list, List<Component> values)
        {
            if (list == null)
                return;

            list.ClearArray();
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Destroys tool-owned hazards and nothing else.
        ///
        /// Scoped to the Hazards subtree on purpose. CaveDecorSpawner.PruneEmptyZoneGroups walks
        /// CaveBlockout/Decor doing the same job for dressing; the two share a parent, so a prune that
        /// resolved the root instead of its own branch would delete the other tool's work.
        /// </summary>
        public static int Clear()
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            Transform hazards = blockout != null ? blockout.transform.Find(HazardRoot) : null;
            if (hazards == null)
                return 0;

            int removed = 0;
            for (int i = hazards.childCount - 1; i >= 0; i--)
            {
                Transform zoneGroup = hazards.GetChild(i);
                removed += CountDescendantStations(zoneGroup);
                Object.DestroyImmediate(zoneGroup.gameObject);
            }
            return removed;
        }

        private static int CountDescendantStations(Transform zoneGroup)
        {
            int count = 0;
            foreach (Transform station in zoneGroup)
                count += station.childCount;
            return count;
        }

        private static void PruneEmptyGroups(Transform hazards)
        {
            for (int i = hazards.childCount - 1; i >= 0; i--)
            {
                Transform zoneGroup = hazards.GetChild(i);
                for (int j = zoneGroup.childCount - 1; j >= 0; j--)
                {
                    if (zoneGroup.GetChild(j).childCount == 0)
                        Object.DestroyImmediate(zoneGroup.GetChild(j).gameObject);
                }
                if (zoneGroup.childCount == 0)
                    Object.DestroyImmediate(zoneGroup.gameObject);
            }
        }

        public static Transform GetOrCreateHazardRoot()
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot) ??
                                  new GameObject(CaveDecorNames.BlockoutRoot);
            return GetOrCreateChild(blockout.transform, HazardRoot);
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }
    }
}
