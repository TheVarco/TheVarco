using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Instantiates, re-instantiates and removes decor in the scene from the records held in a
    /// <see cref="CaveDecorSet"/>. The brush, the auto-scatter and the rebuild all go through here so
    /// there is exactly one definition of what a placement looks like once it is in the scene.
    /// </summary>
    public static class CaveDecorSpawner
    {
        public sealed class Result
        {
            public int spawned;
            public int missingSurface;
            public int missingPrefab;
            public int removed;
            public int reseated;
            public int stillBlocking;
            public readonly List<string> notes = new List<string>();
        }

        /// <summary>Destroys every tool-owned instance, then re-creates all of them from the records.</summary>
        public static Result Rebuild(CaveDecorSet set, CaveDecorContext context, bool reseatViolations = true)
        {
            var result = new Result();
            if (set == null)
            {
                result.notes.Add("no CaveDecorSet");
                return result;
            }
            if (context == null || !context.IsValid)
            {
                result.notes.Add("no CaveRoute or CaveShell collider in the scene");
                return result;
            }

            result.removed = ClearInstances();

            if (reseatViolations)
                Reseat(set, context, result);

            foreach (CaveDecorPlacement placement in set.Placements)
            {
                if (Spawn(set, context, placement) != null)
                    result.spawned++;
                else if (set.FindEntry(placement.paletteId)?.prefab == null)
                    result.missingPrefab++;
                else
                    result.missingSurface++;
            }

            PruneEmptyZoneGroups();
            MarkSceneDirty();
            return result;
        }

        /// <summary>
        /// Removes zone groups left holding nothing. Without this, renaming or removing a zone leaves
        /// an empty "Z7_Decor" sitting in the hierarchy forever, which reads as "this zone exists but
        /// is undressed" rather than "this zone is gone".
        /// </summary>
        private static void PruneEmptyZoneGroups()
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            Transform decor = blockout != null ? blockout.transform.Find(CaveDecorNames.DecorRoot) : null;
            if (decor == null)
                return;

            for (int i = decor.childCount - 1; i >= 0; i--)
            {
                Transform group = decor.GetChild(i);
                if (group.childCount == 0)
                    Undo.DestroyObjectImmediate(group.gameObject);
            }
        }

        /// <summary>
        /// Pulls props back out of the swimmable channel after the cave shell has moved under them.
        ///
        /// This is what makes a blockout regeneration a non-event. Regenerating reseeds the surface
        /// noise, so a prop authored right at the clearance limit can end up a little inside it - a
        /// measured 3 out of 293 on the first regeneration test. Sinking it deeper into the rock keeps
        /// the authored size and silhouette; shrinking towards the palette minimum is the fallback.
        ///
        /// Placements that still do not fit are left untouched and counted, never deleted: silently
        /// discarding a hand-placed prop because the geometry shifted is not a trade this should make
        /// on its own.
        /// </summary>
        private static void Reseat(CaveDecorSet set, CaveDecorContext context, Result result)
        {
            // Reseating rewrites authored surfaceOffset and scale values. Recording the asset up front
            // means an artist who raises minCorridorRadius, hits rebuild and dislikes the result can
            // Ctrl+Z out of it instead of losing the sizes they tuned.
            Undo.RecordObject(set, "Reseat cave decor");

            foreach (CaveDecorPlacement placement in set.Placements)
            {
                CaveDecorPaletteEntry entry = set.FindEntry(placement.paletteId);
                if (entry == null)
                    continue;
                if (!CaveDecorProjector.TryCast(context, placement.routeId, placement.routeDistance,
                        placement.angleDegrees, out CaveDecorSurface surface))
                    continue;

                float radius = entry.boundingRadius * placement.scale;
                if (set.ClearsCorridor(surface.point + surface.normal * placement.surfaceOffset,
                        surface.centerline, radius))
                    continue;

                if (TrySink(set, surface, radius, out float sunkOffset))
                {
                    placement.surfaceOffset = sunkOffset;
                    result.reseated++;
                    EditorUtility.SetDirty(set);
                    continue;
                }

                if (TryShrink(set, entry, surface, placement.scale, out float newScale, out float newOffset))
                {
                    placement.scale = newScale;
                    placement.surfaceOffset = newOffset;
                    result.reseated++;
                    EditorUtility.SetDirty(set);
                    continue;
                }

                result.stillBlocking++;
            }
        }

        private static bool TrySink(CaveDecorSet set, CaveDecorSurface surface, float radius, out float offset)
        {
            for (int step = 1; step <= 6; step++)
            {
                offset = -radius * (0.3f + 0.1f * step);
                if (set.ClearsCorridor(surface.point + surface.normal * offset, surface.centerline, radius))
                    return true;
            }
            offset = 0f;
            return false;
        }

        private static bool TryShrink(CaveDecorSet set, CaveDecorPaletteEntry entry, CaveDecorSurface surface,
            float currentScale, out float scale, out float offset)
        {
            for (int step = 1; step <= 5; step++)
            {
                scale = Mathf.Lerp(currentScale, entry.scaleRange.x, step / 5f);
                float radius = entry.boundingRadius * scale;
                if (TrySink(set, surface, radius, out offset))
                    return true;
            }

            scale = currentScale;
            offset = 0f;
            return false;
        }

        public static GameObject Spawn(CaveDecorSet set, CaveDecorContext context, CaveDecorPlacement placement)
        {
            CaveDecorPaletteEntry entry = set.FindEntry(placement.paletteId);
            if (entry == null || entry.prefab == null)
                return null;

            if (!CaveDecorProjector.TryResolve(context, placement, out Vector3 position, out Quaternion rotation, out _))
                return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
            if (instance == null)
                return null;

            Transform group = FindOrCreateZoneGroup(placement.zoneId);
            instance.transform.SetParent(group, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * placement.scale;
            instance.name = $"{placement.zoneId}_{placement.paletteId}_{ShortId(placement.id)}";

            Material material = entry.ResolveMaterial(placement.zoneId);
            if (material != null)
                ApplyMaterial(instance, material);

            var marker = instance.GetComponent<CaveDecorInstance>() ?? instance.AddComponent<CaveDecorInstance>();
            marker.placementId = placement.id;
            marker.paletteId = placement.paletteId;
            marker.zoneId = placement.zoneId;

            Undo.RegisterCreatedObjectUndo(instance, "Place cave decor");
            return instance;
        }

        /// <summary>
        /// Removes tool-owned instances only. Anything without a <see cref="CaveDecorInstance"/> is
        /// hand-placed art that predates this system, and clearing it here would silently destroy work
        /// the records cannot recreate.
        /// </summary>
        public static int ClearInstances()
        {
            int removed = 0;
            foreach (CaveDecorInstance marker in FindAllInstances())
            {
                Undo.DestroyObjectImmediate(marker.gameObject);
                removed++;
            }
            return removed;
        }

        public static int RemoveInstance(string placementId)
        {
            int removed = 0;
            foreach (CaveDecorInstance marker in FindAllInstances())
            {
                if (marker.placementId != placementId)
                    continue;
                Undo.DestroyObjectImmediate(marker.gameObject);
                removed++;
            }
            return removed;
        }

        public static List<CaveDecorInstance> FindAllInstances()
        {
            var found = new List<CaveDecorInstance>();
            foreach (CaveDecorInstance marker in
                     Object.FindObjectsByType<CaveDecorInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker != null)
                    found.Add(marker);
            }
            return found;
        }

        public static void ApplyMaterial(GameObject instance, Material material)
        {
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// CaveBlockout/Decor/&lt;Zone&gt;_Decor. Deliberately a sibling of the five groups
        /// CaveBlockoutBuilder.ClearToolOwnedChildren destroys, so regenerating the blockout leaves the
        /// dressing hierarchy standing.
        /// </summary>
        public static Transform FindOrCreateZoneGroup(string zoneId)
        {
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            if (blockout == null)
            {
                blockout = new GameObject(CaveDecorNames.BlockoutRoot);
                Undo.RegisterCreatedObjectUndo(blockout, "Create CaveBlockout root");
            }

            Transform decor = FindOrCreateChild(blockout.transform, CaveDecorNames.DecorRoot);
            return FindOrCreateChild(decor, CaveDecorNames.ZoneGroup(zoneId));
        }

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            return child.transform;
        }

        public static void MarkSceneDirty()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid())
                EditorSceneManager.MarkSceneDirty(active);
        }

        public static string ShortId(string id)
        {
            return string.IsNullOrEmpty(id) ? "0000" : id.Substring(0, Mathf.Min(6, id.Length));
        }

        public static string Describe(Result result)
        {
            var text = new StringBuilder();
            text.Append($"spawned={result.spawned} removed={result.removed}");
            if (result.reseated > 0)
                text.Append($" reseated={result.reseated}");
            if (result.stillBlocking > 0)
                text.Append($" stillBlocking={result.stillBlocking}");
            if (result.missingSurface > 0)
                text.Append($" missingSurface={result.missingSurface}");
            if (result.missingPrefab > 0)
                text.Append($" missingPrefab={result.missingPrefab}");
            foreach (string note in result.notes)
                text.Append(" | ").Append(note);
            return text.ToString();
        }
    }
}
