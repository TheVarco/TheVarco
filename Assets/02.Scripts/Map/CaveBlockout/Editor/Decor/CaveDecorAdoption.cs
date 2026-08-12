using System;
using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Converts the hand-placed dressing already sitting under CaveBlockout/Decor into placement
    /// records, so the whole map is driven by one system instead of two.
    ///
    /// MainMap.unity ships 28 such instances, all around the Z1 entrance, placed directly from the FBX
    /// model prefabs at scales between 205 and 1427. Left alone they would be invisible to the
    /// validator, unaffected by a rebuild, and silently destroyed by anyone who cleared the group.
    /// </summary>
    public static class CaveDecorAdoption
    {
        public sealed class Result
        {
            public int adopted;
            public int skippedUnknownPrefab;
            public int skippedNoSurface;
            public readonly List<string> notes = new List<string>();
        }

        public static Result Adopt(CaveDecorSet set, CaveDecorContext context, bool removeOriginals)
        {
            var result = new Result();
            if (set == null || context == null || !context.IsValid)
            {
                result.notes.Add("no decor set, route or shell collider");
                return result;
            }

            Dictionary<string, string> fbxToPalette = BuildFbxLookup(set);
            List<GameObject> legacy = FindLegacyInstances();

            foreach (GameObject instance in legacy)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                string fbxName = string.IsNullOrEmpty(sourcePath)
                    ? null
                    : System.IO.Path.GetFileNameWithoutExtension(sourcePath);

                if (fbxName == null || !fbxToPalette.TryGetValue(fbxName, out string paletteId))
                {
                    result.skippedUnknownPrefab++;
                    continue;
                }

                if (!TryMeasureWorldBounds(instance, out Bounds bounds))
                {
                    result.skippedUnknownPrefab++;
                    continue;
                }

                CaveDecorPaletteEntry entry = set.FindEntry(paletteId);
                bool pivotAtTop = entry != null && entry.allowedSurfaces == CaveSurfaceKind.Ceiling;

                // The generated prefabs pivot at the base of their bounds (top, for ceiling props),
                // while the raw FBX pivot is wherever the exporter left it. Re-deriving the anchor
                // from the bounds is what keeps an adopted prop sitting on the rock instead of
                // jumping by half its height.
                var anchor = new Vector3(bounds.center.x, pivotAtTop ? bounds.max.y : bounds.min.y, bounds.center.z);

                if (!CaveDecorProjector.TryUnproject(context, anchor, out string routeId, out float routeDistance,
                        out float angleDegrees, out float surfaceOffset, out CaveDecorSurface surface))
                {
                    result.skippedNoSurface++;
                    continue;
                }

                // Scale is metres of the largest dimension, matching how the generated prefabs are
                // normalised, so the adopted prop keeps the size it was authored at.
                float scale = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

                set.Placements.Add(new CaveDecorPlacement
                {
                    id = Guid.NewGuid().ToString("N"),
                    paletteId = paletteId,
                    routeId = routeId,
                    routeDistance = routeDistance,
                    angleDegrees = angleDegrees,
                    surfaceOffset = surfaceOffset,
                    surfaceRotation = CaveDecorProjector.ToSurfaceRotation(surface, instance.transform.rotation),
                    scale = scale,
                    zoneId = context.ResolveZoneId(routeId, routeDistance)
                });

                result.adopted++;

                if (removeOriginals)
                    Undo.DestroyObjectImmediate(instance);
            }

            EditorUtility.SetDirty(set);
            return result;
        }

        private static Dictionary<string, string> BuildFbxLookup(CaveDecorSet set)
        {
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CaveDecorCatalog.Entry entry in CaveDecorCatalog.Entries)
            {
                string paletteId = CaveDecorCatalog.ToPaletteId(entry.fbxName);
                if (set.FindEntry(paletteId) != null)
                    lookup[entry.fbxName] = paletteId;
            }
            return lookup;
        }

        /// <summary>
        /// Prefab-instance roots under CaveBlockout/Decor that this system does not already own.
        /// </summary>
        private static List<GameObject> FindLegacyInstances()
        {
            var found = new List<GameObject>();
            GameObject blockout = GameObject.Find(CaveDecorNames.BlockoutRoot);
            Transform decor = blockout != null ? blockout.transform.Find(CaveDecorNames.DecorRoot) : null;
            if (decor == null)
                return found;

            foreach (Transform child in decor.GetComponentsInChildren<Transform>(true))
            {
                GameObject candidate = child.gameObject;
                if (candidate.GetComponent<CaveDecorInstance>() != null)
                    continue;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
                    continue;
                found.Add(candidate);
            }

            return found;
        }

        private static bool TryMeasureWorldBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
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

            return any && bounds.size.sqrMagnitude > 1e-8f;
        }

        public static string Describe(Result result)
        {
            var text = new StringBuilder();
            text.Append($"adopted={result.adopted}");
            if (result.skippedUnknownPrefab > 0)
                text.Append($" skippedUnknownPrefab={result.skippedUnknownPrefab}");
            if (result.skippedNoSurface > 0)
                text.Append($" skippedNoSurface={result.skippedNoSurface}");
            foreach (string note in result.notes)
                text.Append(" | ").Append(note);
            return text.ToString();
        }
    }
}
