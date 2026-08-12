using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>Per-zone material swap for one palette entry.</summary>
    [Serializable]
    public sealed class CaveDecorZoneMaterial
    {
        public string zoneId;
        public Material material;
    }

    /// <summary>
    /// One kind of prop the brush and the auto-scatter can place, plus the rules that decide where it
    /// is allowed to land.
    ///
    /// Materials are resolved here rather than stored per placement, because the art contract is a
    /// property of the zone, not of the individual rock: Z2 is the only bioluminescent zone and Z4
    /// must have zero emission. Keeping the mapping in one list means the validator checks the same
    /// data the placer used.
    /// </summary>
    [Serializable]
    public sealed class CaveDecorPaletteEntry
    {
        [Header("Identity")]
        [Tooltip("Stable key stored in CaveDecorPlacement.paletteId.")]
        public string id;

        public GameObject prefab;

        [Header("Placement Rules")]
        public CaveSurfaceKind allowedSurfaces = CaveSurfaceKind.Any;

        [Tooltip("Zones this entry may appear in. Empty means every zone.")]
        public List<string> zones = new List<string>();

        [Tooltip("Relative pick probability during auto-scatter and weighted brush strokes.")]
        [Min(0f)] public float weight = 1f;

        public Vector2 scaleRange = new Vector2(1f, 1.6f);

        [Tooltip("Metres between two instances of this entry. The brush rejects candidates that land " +
                 "closer than this to an existing placement.")]
        [Min(0f)] public float minSpacing = 4f;

        [Tooltip("Approximate radius in metres at scale 1. Used for the corridor clearance guard, so " +
                 "an under-estimate here is what lets a prop poke into the swimmable channel.")]
        [Min(0.1f)] public float boundingRadius = 2f;

        [Tooltip("How far the prop sinks into the rock, as a fraction of its scaled bounding radius. " +
                 "Negative values embed. Randomised per placement inside this range.")]
        public Vector2 embedFractionRange = new Vector2(-0.30f, -0.08f);

        [Header("Orientation")]
        [Tooltip("0 keeps the prop world-upright, 1 stands it on the surface normal. Wall and ceiling " +
                 "props want 1; floor boulders often read better slightly below 1.")]
        [Range(0f, 1f)] public float normalAlignment = 1f;

        [Tooltip("Random lean applied after alignment, in degrees.")]
        [Range(0f, 45f)] public float maxTiltDegrees = 8f;

        [Header("Materials")]
        [Tooltip("Applied when no zone override matches. Leave null to keep the imported FBX material.")]
        public Material defaultMaterialOverride;

        public List<CaveDecorZoneMaterial> zoneMaterialOverrides = new List<CaveDecorZoneMaterial>();

        [Header("Auto Scatter")]
        [Tooltip("Roughly how many instances the rule-based base pass emits per eligible zone.")]
        [Min(0)] public int autoScatterCountPerZone = 3;

        public bool AllowsZone(string zoneId)
        {
            if (zones == null || zones.Count == 0)
                return true;
            for (int i = 0; i < zones.Count; i++)
            {
                if (string.Equals(zones[i], zoneId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool AllowsSurface(CaveSurfaceKind surface)
        {
            return (allowedSurfaces & surface) != 0;
        }

        /// <summary>Material this entry should render with in <paramref name="zoneId"/>, or null to keep the FBX one.</summary>
        public Material ResolveMaterial(string zoneId)
        {
            if (zoneMaterialOverrides != null)
            {
                for (int i = 0; i < zoneMaterialOverrides.Count; i++)
                {
                    CaveDecorZoneMaterial candidate = zoneMaterialOverrides[i];
                    if (candidate != null && candidate.material != null &&
                        string.Equals(candidate.zoneId, zoneId, StringComparison.Ordinal))
                        return candidate.material;
                }
            }
            return defaultMaterialOverride;
        }
    }
}
