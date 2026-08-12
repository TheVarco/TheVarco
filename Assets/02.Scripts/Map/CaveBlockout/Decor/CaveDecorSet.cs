using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>
    /// The decor palette and every placement record for one map.
    ///
    /// This asset, not the scene, is the source of truth. The scene holds instantiated prefabs that
    /// can be thrown away and rebuilt from here at any time, which is what lets the cave blockout be
    /// regenerated without losing the art pass.
    /// </summary>
    [CreateAssetMenu(menuName = "Varco/Cave/Decor Set", fileName = "CaveDecorSet")]
    public sealed class CaveDecorSet : ScriptableObject
    {
        [Header("Palette")]
        [SerializeField] private List<CaveDecorPaletteEntry> palette = new List<CaveDecorPaletteEntry>();

        [Header("Placements")]
        [SerializeField] private List<CaveDecorPlacement> placements = new List<CaveDecorPlacement>();

        [Header("Clearance")]
        [Tooltip("Metres of swimmable channel that must stay free around the route centre line. " +
                 "MAP_GUIDE.md requires a 10 m minimum passable width and 8 m vertical clearance, so " +
                 "a single 5 m radius satisfies both. Raise it if the submarine starts snagging.")]
        [Min(0f)] public float minCorridorRadius = 5f;

        [Header("Auto Scatter")]
        [Tooltip("Overall density multiplier for the rule-based base pass. Each palette entry asks for " +
                 "autoScatterCountPerZone props in a nominal 90 m zone; this scales all of them at once.")]
        [Range(0.1f, 8f)] public float autoScatterDensity = 2f;

        [Tooltip("Fixed so a rerun reproduces the same base pass. Change it to reroll the whole map.")]
        public int autoScatterSeed = 20260810;

        [Tooltip("Candidate casts tried per wanted prop before giving up on it. Misses happen at portal " +
                 "openings and where the surface found is not one the entry is allowed to stand on.")]
        [Range(1, 64)] public int autoScatterAttempts = 12;

        public IReadOnlyList<CaveDecorPaletteEntry> Palette => palette;
        public List<CaveDecorPlacement> Placements => placements;
        public List<CaveDecorPaletteEntry> EditablePalette => palette;

        public CaveDecorPaletteEntry FindEntry(string paletteId)
        {
            if (string.IsNullOrEmpty(paletteId))
                return null;
            for (int i = 0; i < palette.Count; i++)
            {
                if (palette[i] != null && string.Equals(palette[i].id, paletteId, StringComparison.Ordinal))
                    return palette[i];
            }
            return null;
        }

        public CaveDecorPlacement FindPlacement(string placementId)
        {
            if (string.IsNullOrEmpty(placementId))
                return null;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i] != null && string.Equals(placements[i].id, placementId, StringComparison.Ordinal))
                    return placements[i];
            }
            return null;
        }

        public int RemovePlacement(string placementId)
        {
            return placements.RemoveAll(p => p != null && string.Equals(p.id, placementId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Does a prop of this size at this position leave the swimmable channel intact?
        /// Applied identically by the brush, the auto-scatter and the validator.
        /// </summary>
        public bool ClearsCorridor(Vector3 position, Vector3 centerline, float scaledRadius)
        {
            return Vector3.Distance(position, centerline) - scaledRadius >= minCorridorRadius;
        }
    }
}
