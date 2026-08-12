using System;
using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Rule-based base pass: walks every route section and drops props on the surfaces the palette
    /// says they belong on.
    ///
    /// Painting 691 metres of tunnel by hand is not a realistic starting point, so this produces the
    /// bulk and the brush is for correction and set-piece work. The difference from the art-pass
    /// builder this replaces is that every candidate is a real raycast against the cave shell: that
    /// tool offset from a hard-coded zone centre inside an axis-aligned box and openly documented that
    /// props end up "floating in mid-air" and "outside cave walls".
    /// </summary>
    public static class CaveDecorAutoScatter
    {
        /// <summary>Zone length that a palette entry's autoScatterCountPerZone is calibrated for.</summary>
        private const float NominalZoneLength = 90f;

        public sealed class Result
        {
            public int emitted;
            public int rejectedNoSurface;
            public int rejectedWrongSurface;
            public int rejectedCorridor;
            public int rejectedSpacing;
            public readonly Dictionary<string, int> perZone = new Dictionary<string, int>();
        }

        /// <summary>
        /// Fills zones with props. <paramref name="zoneFilter"/> restricts the pass to a subset of
        /// zones and <paramref name="paletteFilter"/> to a subset of species; without them every zone
        /// is walked for every entry, which double-dresses anything already placed - the per-entry
        /// target is a quota to reach, not a cap on what is already there, so only the spacing check
        /// pushes back and it does not push back nearly hard enough.
        ///
        /// Adding new assets to an already-dressed map is the paletteFilter case: the zones are all in
        /// scope, but only the species that are not in the map yet should be emitting anything.
        /// </summary>
        public static Result Scatter(CaveDecorSet set, CaveDecorContext context, bool replaceExisting,
            ICollection<string> zoneFilter = null, ICollection<string> paletteFilter = null)
        {
            var result = new Result();
            if (set == null || context == null || !context.IsValid)
                return result;

            if (replaceExisting)
                set.Placements.Clear();

            // Spacing is checked against everything already in the set, not just this run, so a
            // top-up pass respects the props a previous pass or the brush already placed.
            var occupied = new List<(Vector3 position, string paletteId, float radius)>();
            CollectExisting(set, context, occupied);

            foreach (string routeId in new List<string>(context.RouteIds))
            {
                if (!context.TryGetRoute(routeId, out _, out CaveRoutePolyline polyline,
                        out CaveRouteSplineDefinition definition))
                    continue;

                foreach (CaveRouteSection section in definition.sections)
                {
                    if (zoneFilter != null && !zoneFilter.Contains(section.zoneId))
                        continue;

                    float start = section.startDistanceMeters >= 0f ? section.startDistanceMeters : 0f;
                    float end = section.endDistanceMeters >= 0f ? section.endDistanceMeters : polyline.Length;
                    ScatterSection(set, context, routeId, section.zoneId, start, end, occupied, result,
                        paletteFilter);
                }
            }

            EditorUtility.SetDirty(set);
            return result;
        }

        private static void CollectExisting(CaveDecorSet set, CaveDecorContext context,
            List<(Vector3, string, float)> occupied)
        {
            foreach (CaveDecorPlacement placement in set.Placements)
            {
                CaveDecorPaletteEntry entry = set.FindEntry(placement.paletteId);
                if (entry == null)
                    continue;
                if (CaveDecorProjector.TryResolve(context, placement, out Vector3 position, out _, out _))
                    occupied.Add((position, placement.paletteId, entry.boundingRadius * placement.scale));
            }
        }

        private static void ScatterSection(CaveDecorSet set, CaveDecorContext context, string routeId,
            string zoneId, float startDistance, float endDistance,
            List<(Vector3 position, string paletteId, float radius)> occupied, Result result,
            ICollection<string> paletteFilter)
        {
            float length = Mathf.Max(1f, endDistance - startDistance);

            foreach (CaveDecorPaletteEntry entry in set.Palette)
            {
                if (entry == null || entry.prefab == null || !entry.AllowsZone(zoneId))
                    continue;
                if (paletteFilter != null && !paletteFilter.Contains(entry.id))
                    continue;

                int wanted = Mathf.Max(1, Mathf.RoundToInt(
                    entry.autoScatterCountPerZone * (length / NominalZoneLength) * set.autoScatterDensity));

                var random = new System.Random(StableHash(set.autoScatterSeed, zoneId, entry.id, routeId));

                for (int i = 0; i < wanted; i++)
                {
                    if (TryEmit(set, context, routeId, zoneId, startDistance, endDistance, entry, random,
                            occupied, result))
                    {
                        result.emitted++;
                        result.perZone.TryGetValue(zoneId, out int zoneCount);
                        result.perZone[zoneId] = zoneCount + 1;
                    }
                }
            }
        }

        private static bool TryEmit(CaveDecorSet set, CaveDecorContext context, string routeId, string zoneId,
            float startDistance, float endDistance, CaveDecorPaletteEntry entry, System.Random random,
            List<(Vector3 position, string paletteId, float radius)> occupied, Result result)
        {
            for (int attempt = 0; attempt < set.autoScatterAttempts; attempt++)
            {
                float distance = Mathf.Lerp(startDistance, endDistance, (float)random.NextDouble());
                float angle = SampleAngle(entry.allowedSurfaces, random);

                if (!CaveDecorProjector.TryCast(context, routeId, distance, angle, out CaveDecorSurface surface))
                {
                    result.rejectedNoSurface++;
                    continue;
                }

                if (!entry.AllowsSurface(surface.kind))
                {
                    result.rejectedWrongSurface++;
                    continue;
                }

                float scale = Mathf.Lerp(entry.scaleRange.x, entry.scaleRange.y, (float)random.NextDouble());
                float radius = entry.boundingRadius * scale;
                float embed = Mathf.Lerp(entry.embedFractionRange.x, entry.embedFractionRange.y,
                    (float)random.NextDouble()) * radius;
                Vector3 position = surface.point + surface.normal * embed;

                if (!set.ClearsCorridor(position, surface.centerline, radius))
                {
                    result.rejectedCorridor++;
                    continue;
                }

                if (ViolatesSpacing(occupied, position, entry, radius))
                {
                    result.rejectedSpacing++;
                    continue;
                }

                set.Placements.Add(new CaveDecorPlacement
                {
                    id = Guid.NewGuid().ToString("N"),
                    paletteId = entry.id,
                    routeId = routeId,
                    routeDistance = distance,
                    angleDegrees = angle,
                    surfaceOffset = embed,
                    surfaceRotation = CaveDecorProjector.BuildRandomSurfaceRotation(surface, entry, random),
                    scale = scale,
                    zoneId = zoneId
                });

                occupied.Add((position, entry.id, radius));
                return true;
            }

            return false;
        }

        private static bool ViolatesSpacing(List<(Vector3 position, string paletteId, float radius)> occupied,
            Vector3 position, CaveDecorPaletteEntry entry, float radius)
        {
            for (int i = 0; i < occupied.Count; i++)
            {
                var other = occupied[i];
                // Same-kind props need the authored spacing so a species does not clump; different
                // kinds only need to not intersect, which is what makes a cluster read as one feature.
                float required = other.paletteId == entry.id
                    ? entry.minSpacing
                    : radius + other.radius;

                if ((position - other.position).sqrMagnitude < required * required)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Picks a cast angle biased towards the surfaces this entry is allowed on. Angle 0 casts at
        /// the ceiling and 180 at the floor. The result is still classified and re-checked after the
        /// cast, because the tunnel is noisy enough that "cast downwards" does not guarantee "hit
        /// something that reads as floor".
        /// </summary>
        private static float SampleAngle(CaveSurfaceKind allowed, System.Random random)
        {
            var windows = new List<Vector2>();
            if ((allowed & CaveSurfaceKind.Ceiling) != 0)
                windows.Add(new Vector2(-55f, 55f));
            if ((allowed & CaveSurfaceKind.Wall) != 0)
            {
                windows.Add(new Vector2(50f, 130f));
                windows.Add(new Vector2(-130f, -50f));
            }
            if ((allowed & CaveSurfaceKind.Floor) != 0)
                windows.Add(new Vector2(125f, 235f));

            if (windows.Count == 0)
                return (float)random.NextDouble() * 360f;

            Vector2 window = windows[random.Next(windows.Count)];
            return Mathf.Lerp(window.x, window.y, (float)random.NextDouble());
        }

        /// <summary>
        /// FNV-1a rather than string.GetHashCode, which is not guaranteed stable between runtimes.
        /// A base pass that reshuffles itself on a different machine would make review pointless.
        /// </summary>
        private static int StableHash(int seed, params string[] parts)
        {
            unchecked
            {
                uint hash = 2166136261u ^ (uint)seed;
                foreach (string part in parts)
                {
                    if (part == null)
                        continue;
                    foreach (char c in part)
                    {
                        hash ^= c;
                        hash *= 16777619u;
                    }
                    hash ^= '|';
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        public static string Describe(Result result)
        {
            var text = new StringBuilder();
            text.Append($"emitted={result.emitted}");
            text.Append($" rejected(noSurface={result.rejectedNoSurface}, wrongSurface={result.rejectedWrongSurface}");
            text.Append($", corridor={result.rejectedCorridor}, spacing={result.rejectedSpacing})");
            foreach (var pair in result.perZone)
                text.Append($" | {pair.Key}={pair.Value}");
            return text.ToString();
        }
    }
}
