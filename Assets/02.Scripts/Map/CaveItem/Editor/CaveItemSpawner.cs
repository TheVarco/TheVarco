using System.Collections.Generic;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Turns the layout into scene objects.
    ///
    /// Everything the tool owns lives under CaveBlockout/Items. CaveBlockoutBuilder.ClearToolOwnedChildren
    /// destroys only Routes, Generated, Markers, Validation and Playtest, so this group survives a
    /// blockout regeneration the same way Decor and Hazards do, and re-running the layout re-projects it
    /// onto whatever surface now exists.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// This file, and every other file in CaveItem.EditorTools, must never load, reference or modify
    /// CaveDecorSet or Assets/Settings/CaveDecor/MainMapCaveDecor.asset.
    ///
    /// CaveHazardBatch.RemoveOverlappingDecor deletes decor RECORDS that collide with a hazard mount.
    /// It is cumulative and it is not undoable, and it is why the decor set now holds 392 placements
    /// rather than the 425 its own comments still cite. This tool was written by copying that one, so
    /// the method is sitting in the file being copied from. Overlap with decor is reported by reading
    /// CaveDecorInstance markers in the scene and then MOVING THE ITEM - the decor set is not ours.
    /// ---------------------------------------------------------------------------------------------
    /// </summary>
    public static class CaveItemSpawner
    {
        public const string ItemRoot = "Items";

        public static string ZoneGroup(string zoneId) => (zoneId ?? "Unzoned") + "_Items";

        public sealed class Result
        {
            public int spawned;
            public int unresolved;
            public int clearedToolOwned;
            public int removedLegacy;
            public readonly List<GameObject> instances = new List<GameObject>();
            public readonly List<string> notes = new List<string>();
        }

        public static Result Rebuild(IReadOnlyList<CaveItemPlacement> placements, CaveDecorContext context)
        {
            var result = new Result();

            if (context == null || !context.IsValid)
            {
                result.notes.Add("no CaveRoute or CaveShell collider in the scene");
                return result;
            }

            var prefabs = new Dictionary<CaveItemKind, GameObject>();
            foreach (CaveItemCatalog.Species species in CaveItemCatalog.All)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(species.prefabPath);
                if (asset == null)
                {
                    result.notes.Add($"missing prefab for {species.kind}: {species.prefabPath}");
                    return result;
                }
                prefabs[species.kind] = asset;
            }

            Transform submarine = FindSubmarine();
            if (submarine == null)
                result.notes.Add("no 'Submarine_final' in the scene - interior placements will fail");

            result.clearedToolOwned = Clear();
            result.removedLegacy = RemoveLegacyLooseInstances(result);

            Transform items = GetOrCreateItemRoot();

            foreach (CaveItemPlacement placement in placements)
            {
                if (!CaveItemResolver.TryResolve(context, placement, submarine,
                        out CaveItemResolver.Result resolved, out string failure))
                {
                    result.unresolved++;
                    result.notes.Add($"{placement.id}: {failure}");
                    continue;
                }

                Transform group = GetOrCreateChild(items, ZoneGroup(placement.zoneId));
                var spawned = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[placement.kind], group);

                // InstantiatePrefab, never Object.Instantiate: only the former produces a prefab instance
                // whose overrides serialise as m_Modifications, which is the form Fusion's scene-object
                // baker writes SortKey into and the form all 39 existing scene NetworkObjects use.
                spawned.transform.SetPositionAndRotation(resolved.position, resolved.rotation);
                spawned.transform.localScale = placement.scale;
                spawned.name = placement.id;

                result.instances.Add(spawned);
                result.spawned++;
            }

            PruneEmptyGroups(items);
            return result;
        }

        /// <summary>
        /// Removes the loose item instances the scene shipped with.
        ///
        /// MainScene_final carries one each of OxygenItem, Octopus, Urchin, Shark, Gun, Hammer, RopeItem
        /// and Tonado as unparented roots near the world origin. They are a smoke test, not a layout: the
        /// player spawns inside the submarine and PlayerInteractor reaches 3 m, while the loose hammer
        /// sits 7.9 m outside the hull. The same eight coordinates appear verbatim in four archived
        /// scenes, which is what "dropped in to check it compiles" looks like.
        ///
        /// They are deleted rather than adopted because a prefab instance carries no per-instance data
        /// beyond its transform, so deleting and respawning is the same operation as moving - and it
        /// keeps this method idempotent, which adoption is not. The undo is one command:
        /// git checkout -- Assets/01.Scenes/MainScene_final.unity
        /// </summary>
        private static int RemoveLegacyLooseInstances(Result result)
        {
            var owned = new HashSet<string>();
            foreach (CaveItemCatalog.Species species in CaveItemCatalog.All)
                owned.Add(species.prefabPath);

            Transform itemRoot = FindItemRoot();
            var doomed = new List<GameObject>();

            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                         .GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (itemRoot != null && candidate.IsChildOf(itemRoot))
                        continue;

                    GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(candidate.gameObject);
                    if (instanceRoot == null || instanceRoot != candidate.gameObject)
                        continue;

                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                    if (string.IsNullOrEmpty(path) || !owned.Contains(path))
                        continue;

                    doomed.Add(instanceRoot);
                }
            }

            foreach (GameObject victim in doomed)
            {
                result.notes.Add($"removed legacy loose instance '{victim.name}' at " +
                                 $"{victim.transform.position}");
                Object.DestroyImmediate(victim);
            }

            return doomed.Count;
        }

        /// <summary>
        /// Destroys tool-owned items and nothing else.
        ///
        /// Scoped to the Items subtree deliberately. Three tools now share the CaveBlockout parent -
        /// CaveDecorSpawner.PruneEmptyZoneGroups walks Decor and CaveHazardSpawner.Clear walks Hazards -
        /// so a prune that resolved the root instead of its own branch would delete the other two tools'
        /// work. CaveHazardSpawner carries the same warning for the same reason.
        /// </summary>
        public static int Clear()
        {
            Transform items = FindItemRoot();
            if (items == null)
                return 0;

            int removed = 0;
            for (int i = items.childCount - 1; i >= 0; i--)
            {
                Transform group = items.GetChild(i);
                removed += group.childCount;
                Object.DestroyImmediate(group.gameObject);
            }
            return removed;
        }

        public static Transform FindSubmarine()
        {
            GameObject submarine = GameObject.Find("Submarine_final");
            return submarine != null ? submarine.transform : null;
        }

        public static Transform FindItemRoot()
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            return blockout != null ? blockout.transform.Find(ItemRoot) : null;
        }

        public static Transform GetOrCreateItemRoot()
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot) ??
                                  new GameObject(CaveDecorNames.BlockoutRoot);
            return GetOrCreateChild(blockout.transform, ItemRoot);
        }

        private static void PruneEmptyGroups(Transform items)
        {
            for (int i = items.childCount - 1; i >= 0; i--)
            {
                if (items.GetChild(i).childCount == 0)
                    Object.DestroyImmediate(items.GetChild(i).gameObject);
            }
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
