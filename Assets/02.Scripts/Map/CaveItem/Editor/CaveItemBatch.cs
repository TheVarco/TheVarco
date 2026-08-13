using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Entry points for the item pass. Following the hazard convention, the *Batch methods carry no
    /// [MenuItem] and exist to be named on a -executeMethod command line; the *Interactive wrappers
    /// carry the menu item.
    /// </summary>
    public static class CaveItemBatch
    {
        /// <summary>
        /// The only scene this tool may touch.
        ///
        /// MainScene_final is the sole entry in the build settings and the only scene holding both the
        /// submarine and the authored 579.56 m route. Running this against MainMap would author 52
        /// objects there, fail every interior placement for want of a submarine, and save the file.
        /// </summary>
        public const string TargetScenePath = "Assets/01.Scenes/MainScene_final.unity";

        /// <summary>
        /// Fusion bakes SortKey from GlobalObjectId on EditorSceneManager.sceneSaving, and part of that
        /// id is the local file id, which does not exist until the object has been serialised once. The
        /// first save therefore hashes an id that is about to change, and the keys it writes are
        /// provisional. Two saves settle them; the third is the assertion that they have settled.
        ///
        /// MarkSceneDirty before every save is mandatory, not defensive: SaveOpenScenes is a no-op on a
        /// clean scene, so without it the second and third saves never happen and sceneSaving never
        /// fires. That exact mistake once made a deletion pass report success while writing nothing.
        /// </summary>
        private const int SortKeySettleSaves = 3;

        [MenuItem("Tools/Underwater Cave/Items/1 - 아이템 배치와 검증")]
        public static void BuildAllInteractive()
        {
            Debug.Log(BuildAll(save: true));
        }

        public static void BuildAllBatch()
        {
            EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            Debug.Log(BuildAll(save: true));
        }

        [MenuItem("Tools/Underwater Cave/Items/2 - 검증만")]
        public static void ValidateInteractive()
        {
            Debug.Log(ValidateOnly());
        }

        /// <summary>
        /// Re-opens the saved scene and validates what is on disk, so a PASS is a property of the file
        /// rather than of the build session's in-memory state.
        /// </summary>
        public static void ValidateBatch()
        {
            EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            Debug.Log(ValidateOnly());
        }

        public static string BuildAll(bool save)
        {
            var text = new StringBuilder();
            text.AppendLine("===== CAVE ITEM BUILD =====");

            string guard = GuardScene();
            if (guard != null)
                return text.Append(guard).ToString();

            CaveDecorContext context = CaveDecorContext.Create();
            List<CaveItemPlacement> placements = CaveItemLayout.Build();

            CaveItemSpawner.Result spawn = CaveItemSpawner.Rebuild(placements, context);
            text.AppendLine($"cleared {spawn.clearedToolOwned} tool-owned, " +
                            $"removed {spawn.removedLegacy} legacy loose, " +
                            $"spawned {spawn.spawned}, unresolved {spawn.unresolved}");
            foreach (string note in spawn.notes)
                text.AppendLine("  " + note);

            CaveItemValidator.Report report =
                CaveItemValidator.Validate(placements, context, spawn.instances);
            text.AppendLine();
            text.Append(CaveItemValidator.Format(report));

            if (!save)
            {
                text.AppendLine("(dry run - nothing saved)");
                return text.ToString();
            }

            text.AppendLine();
            text.Append(SettleAndSave(spawn.instances));
            return text.ToString();
        }

        public static string ValidateOnly()
        {
            var text = new StringBuilder();

            string guard = GuardScene();
            if (guard != null)
                return text.Append(guard).ToString();

            CaveDecorContext context = CaveDecorContext.Create();
            List<CaveItemPlacement> placements = CaveItemLayout.Build();

            // Collect what is actually in the scene rather than what a build would have made, so the
            // component and layer checks describe the file.
            var instances = new List<GameObject>();
            Transform itemRoot = CaveItemSpawner.FindItemRoot();
            if (itemRoot != null)
            {
                foreach (Transform group in itemRoot)
                {
                    foreach (Transform child in group)
                        instances.Add(child.gameObject);
                }
            }

            text.AppendLine($"scene holds {instances.Count} tool-owned items under " +
                            $"CaveBlockout/{CaveItemSpawner.ItemRoot}");
            text.Append(CaveItemValidator.Format(
                CaveItemValidator.Validate(placements, context, instances)));
            return text.ToString();
        }

        private static string GuardScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == TargetScenePath)
                return null;

            // Guarded here rather than only in the batch wrapper: the -executeMethod path opens the
            // right scene itself, so the real exposure is the menu item fired from whatever a designer
            // happens to have open.
            return $"FAIL: CaveItem targets {TargetScenePath} only. Active scene is '{active.path}'. " +
                   "Refusing - MainMap has no submarine and its geometry is a separate baked copy.\n";
        }

        // ---------------------------------------------------------------- SortKey

        private static string SettleAndSave(IReadOnlyList<GameObject> instances)
        {
            var text = new StringBuilder();
            var networked = new List<Object>();

            foreach (GameObject instance in instances)
            {
                if (instance == null)
                    continue;
                var networkObject = instance.GetComponent("NetworkObject");
                if (networkObject != null)
                    networked.Add(networkObject);
            }

            text.AppendLine($"-- SortKey settle: {networked.Count} of {instances.Count} spawned items " +
                            "carry a NetworkObject --");

            for (int pass = 1; pass <= SortKeySettleSaves; pass++)
            {
                Dictionary<Object, long> before = ReadSortKeys(networked);

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveOpenScenes())
                    return text.AppendLine($"FAIL: save {pass} was refused").ToString();

                Dictionary<Object, long> after = ReadSortKeys(networked);

                int changed = 0;
                foreach (var pair in after)
                {
                    if (!before.TryGetValue(pair.Key, out long old) || old != pair.Value)
                        changed++;
                }
                text.AppendLine($"  save {pass}: {changed} SortKey(s) changed");

                if (pass == SortKeySettleSaves && changed != 0)
                {
                    return text.AppendLine($"FAIL: SortKeys still moving after {SortKeySettleSaves} saves")
                        .ToString();
                }
            }

            // Zero means the baker never ran for that object; a duplicate means two scene NetworkObjects
            // sort equal, and NetworkSceneManagerDefault orders scene objects by exactly this key - so a
            // collision is a host/client ordering mismatch rather than a cosmetic problem.
            var seen = new Dictionary<long, string>();
            int zero = 0;
            int duplicates = 0;

            foreach (Object networkObject in AllSceneNetworkObjects())
            {
                long key = ReadSortKey(networkObject);
                string name = ((Component)networkObject).gameObject.name;

                if (key == 0)
                {
                    zero++;
                    text.AppendLine($"  FAIL: '{name}' has SortKey 0");
                }
                else if (seen.TryGetValue(key, out string other))
                {
                    duplicates++;
                    text.AppendLine($"  FAIL: duplicate SortKey {key} on '{name}' and '{other}'");
                }
                else
                {
                    seen[key] = name;
                }
            }

            AssetDatabase.SaveAssets();
            text.AppendLine($"SORTKEY {(zero == 0 && duplicates == 0 ? "PASS" : "FAIL")} " +
                            $"scene NetworkObjects={seen.Count + zero + duplicates} zero={zero} " +
                            $"duplicates={duplicates}");
            return text.ToString();
        }

        private static IEnumerable<Object> AllSceneNetworkObjects()
        {
            var found = new List<Object>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Component networkObject = transform.GetComponent("NetworkObject");
                    if (networkObject != null)
                        found.Add(networkObject);
                }
            }
            return found;
        }

        private static Dictionary<Object, long> ReadSortKeys(IReadOnlyList<Object> networked)
        {
            var map = new Dictionary<Object, long>();
            foreach (Object networkObject in networked)
            {
                if (networkObject != null)
                    map[networkObject] = ReadSortKey(networkObject);
            }
            return map;
        }

        /// <summary>
        /// Read through SerializedObject rather than the C# property. The field is Fusion's to own and
        /// its accessibility has changed between versions; the serialised name has not, and this is
        /// exactly the value that lands in the scene YAML.
        /// </summary>
        private static long ReadSortKey(Object networkObject)
        {
            var serialized = new SerializedObject(networkObject);
            SerializedProperty property = serialized.FindProperty("SortKey");
            if (property == null)
                return 0;
            return property.propertyType == SerializedPropertyType.Integer
                ? property.longValue
                : 0;
        }
    }
}
