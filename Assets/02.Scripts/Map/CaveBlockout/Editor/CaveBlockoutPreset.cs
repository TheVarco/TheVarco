using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public static class CaveBlockoutPreset
    {
        public readonly struct ZoneSpec
        {
            public readonly string id;
            public readonly float length;
            public readonly float rise;
            public readonly Vector2 guideSize;
            public readonly float firstHeading;
            public readonly float secondHeading;

            public ZoneSpec(string id, float length, float rise, Vector2 guideSize, float firstHeading, float secondHeading)
            {
                this.id = id;
                this.length = length;
                this.rise = rise;
                this.guideSize = guideSize;
                this.firstHeading = firstHeading;
                this.secondHeading = secondHeading;
            }
        }

        /// <summary>
        /// Lengths and guideSize come straight from MAP_GUIDE.md's zone table and are not negotiable -
        /// they are what 01_geometry_blockout_guide.png dimensions. Rises and headings are, and both
        /// were re-derived after the first 6-zone bake failed validation at slope 38.3 deg (limit 30.5)
        /// and turn radius 8.9 m (limit 14).
        ///
        /// Rise is distributed proportionally to length, so every zone sits at the same analytic slope
        /// (26.88 deg). For a fixed 260 m total that is the distribution which minimises the worst
        /// slope; the previous hand-set rises put Z5 at exactly asin(55/110) = 30.000 deg, leaving the
        /// spline no room before the 30.5 ceiling.
        ///
        /// Headings were softened because both failures had one cause: a +100 deg heading reversal at
        /// the Z4-Z5 boundary. A horizontal reversal collapses the horizontal component while the climb
        /// continues, which spikes atan2(dy, horizontal), and it corners far tighter than 14 m. A search
        /// over the heading set found nothing feasible within +-25 deg of the authored values - with only
        /// two knots per zone the guide's zigzag simply cannot hold a 14 m radius - so these are the
        /// closest feasible headings, capped at +-30 deg of deviation.
        ///
        /// Modelled result: 584 m, rise 260.0, slope 28.8, radius 23.4, reverse drop 0.
        /// </summary>
        /// <summary>
        /// How much every cross-section grew over the values in MAP_GUIDE.md's zone table.
        ///
        /// The guide's dimensions were derived from a 6 x 3 x 3 m reference submarine. The submarine that
        /// is actually in the game has been scaled 2x on its prefab root since it was created
        /// (Submarine_final.prefab, m_LocalScale 2, commit 36788de - two cockpits and a walkable
        /// interior), which makes its collision hull 6.55 m across and 16.23 m long with 14.11 m of reach
        /// behind its pivot. Measured against the cave as authored, that hull has to shrink to 0.67x
        /// before any flyable path exists at all, and every zone boundary blocks it outright
        /// (Z1-Z2 admits 0.99x, Z2-Z3 only 0.74x - which is exactly where it wedged).
        ///
        /// The level owner chose to grow the cave rather than shrink the submarine, since the 2x scale is
        /// what makes the interior habitable. 1/0.67 is 1.49, so the cross-sections grow by 1.5.
        ///
        /// The centreline is untouched: length, rise, slope and turn radius are properties of the spline
        /// and must come out of a rebuild bit-identical.
        /// </summary>
        public const float CrossSectionGrowth = 1.5f;

        public static readonly ZoneSpec[] Zones =
        {
            new ZoneSpec("Z1", 55f, 24.9f, new Vector2(52.5f, 30f), -7.5f, 13.5f),
            new ZoneSpec("Z2", 90f, 40.7f, new Vector2(97.5f, 45f), 18.5f, 56.5f),
            new ZoneSpec("Z3", 120f, 54.3f, new Vector2(37.5f, 37.5f), 34.5f, -11.5f),
            new ZoneSpec("Z4", 80f, 36.2f, new Vector2(27f, 27f), -42.5f, -40f),
            new ZoneSpec("Z5", 110f, 49.7f, new Vector2(45f, 90f), 0f, 45.5f),
            new ZoneSpec("Z6", 120f, 54.2f, new Vector2(127.5f, 75f), 56.5f, 11f)
        };

        /// <summary>The guide's original cross-sections, kept so the growth factor stays auditable.</summary>
        public static readonly Vector2[] GuideZoneSizes =
        {
            new Vector2(35f, 20f),
            new Vector2(65f, 30f),
            new Vector2(25f, 25f),
            new Vector2(18f, 18f),
            new Vector2(30f, 60f),
            new Vector2(85f, 50f)
        };

        /// <summary>
        /// The cross-section at a zone boundary, where the tunnel pinches between two chambers.
        /// </summary>
        public readonly struct BoundarySpec
        {
            /// <summary>Index of the shared knot on the main spline.</summary>
            public readonly int knotIndex;
            public readonly string boundaryId;
            public readonly float width;
            public readonly float height;
            /// <summary>Heading change across the knot, in degrees. Documentation, not an input.</summary>
            public readonly float bendDegrees;

            public BoundarySpec(int knotIndex, string boundaryId, float width, float height, float bendDegrees)
            {
                this.knotIndex = knotIndex;
                this.boundaryId = boundaryId;
                this.width = width;
                this.height = height;
                this.bendDegrees = bendDegrees;
            }
        }

        /// <summary>
        /// Every zone boundary was a flat 16 x 12 m throat, and the submarine could not get through the
        /// Z2 -> Z3 one: it wedged and stopped. Measured admissible hull scale per boundary, before this
        /// change: Z1-Z2 0.99x, Z2-Z3 0.74x, Z3-Z4 0.82x, Z4-Z5 0.76x, Z5-Z6 0.87x. Z1-Z2 missing by a
        /// hair is why the route felt passable right up to the point it wasn't.
        ///
        /// Widths fall with zone number so the route tightens as it climbs, per the difficulty gradient
        /// the level owner asked for. The bend column is why that gradient is not the whole story: the
        /// hull pivots ahead of its own tail by 14.11 m, so a turn of A degrees sweeps the tail
        /// 14.11 * sin(A) sideways, and knot 8 is the hardest corner on the route despite not being the
        /// narrowest. It holds a floor rather than continuing the taper.
        ///
        /// Read by BuildMainSpline for fresh blockouts and by CaveCrossSectionBatch for the two scenes
        /// that already carry a baked route. One table, or the scenes and a rebuild disagree.
        /// </summary>
        public static readonly BoundarySpec[] ZoneBoundaries =
        {
            new BoundarySpec(2, "Z1-Z2", 27f, 19f, 5f),
            new BoundarySpec(4, "Z2-Z3", 26f, 19f, 22f),
            new BoundarySpec(6, "Z3-Z4", 25f, 19f, 31f),
            new BoundarySpec(8, "Z4-Z5", 25f, 19f, 40f),
            new BoundarySpec(10, "Z5-Z6", 24f, 18f, 11f)
        };

        /// <summary>The cross-section this table replaced, kept so the batch can recognise an un-widened route.</summary>
        public static readonly Vector2 LegacyBoundarySize = new Vector2(16f, 12f);

        public static bool TryGetBoundary(int knotIndex, out BoundarySpec spec)
        {
            foreach (BoundarySpec candidate in ZoneBoundaries)
            {
                if (candidate.knotIndex == knotIndex)
                {
                    spec = candidate;
                    return true;
                }
            }
            spec = default;
            return false;
        }

        public static void CreateRoutes(Transform routesRoot, out CaveRoute mainRoute, out CaveRoute branchRoutes)
        {
            GameObject mainObject = new GameObject("MainRoute");
            mainObject.transform.SetParent(routesRoot, false);
            SplineContainer mainContainer = mainObject.AddComponent<SplineContainer>();
            mainRoute = mainObject.AddComponent<CaveRoute>();

            Spline mainSpline = BuildMainSpline(out List<CaveRouteSection> sections);
            mainContainer.Splines = new[] { mainSpline };
            foreach (CaveRouteSection section in sections)
            {
                section.startDistanceMeters = mainSpline.ConvertIndexUnit(section.startKnot, PathIndexUnit.Knot, PathIndexUnit.Distance);
                section.endDistanceMeters = mainSpline.ConvertIndexUnit(section.endKnot, PathIndexUnit.Knot, PathIndexUnit.Distance);
            }
            List<CaveRouteSplineDefinition> mainDefinitions = new List<CaveRouteSplineDefinition>
            {
                new CaveRouteSplineDefinition
                {
                    routeId = "MainRoute",
                    splineIndex = 0,
                    isMainRoute = true,
                    startTrimMeters = 0f,
                    sections = sections
                }
            };

            GameObject branchesObject = new GameObject("Branches_Z2_Z4_Z5");
            branchesObject.transform.SetParent(routesRoot, false);
            SplineContainer branchesContainer = branchesObject.AddComponent<SplineContainer>();
            branchRoutes = branchesObject.AddComponent<CaveRoute>();

            int[] portalKnots = { 3, 7, 9 };
            int[] zoneIndexes = { 1, 3, 4 };
            float[] branchLengths = { 40f, 45f, 45f };
            List<Spline> branchSplines = new List<Spline>();
            List<CaveRouteSplineDefinition> branchDefinitions = new List<CaveRouteSplineDefinition>();
            List<CavePortalDefinition> portals = new List<CavePortalDefinition>();

            for (int i = 0; i < portalKnots.Length; i++)
            {
                int mainKnot = portalKnots[i];
                float normalizedT = mainSpline.ConvertIndexUnit(mainKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                Vector3 center = mainSpline.EvaluatePosition(normalizedT);
                Vector3 tangent = ((Vector3)mainSpline.EvaluateTangent(normalizedT)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized * (i == 1 ? -1f : 1f);
                float mainWidth = Zones[zoneIndexes[i]].guideSize.x;
                // Start the optional branch at the main-tunnel boundary.  The old one-metre inset,
                // together with startTrimMeters, left the branch's playable centreline visually detached
                // from its portal even though the mesh junction was welded.
                float entryDistance = Mathf.Max(5f, mainWidth * 0.5f);
                Vector3 entry = center + right * entryDistance;

                Spline branch = BuildBranchSpline(entry, right, branchLengths[i], i, out Vector3 openingDirection);
                branchSplines.Add(branch);

                string branchId = Zones[zoneIndexes[i]].id + "_Branch";
                branchDefinitions.Add(new CaveRouteSplineDefinition
                {
                    routeId = branchId,
                    splineIndex = i,
                    isMainRoute = false,
                    // The spline begins at the portal boundary, so the resource branch has no hidden
                    // centreline segment between the player path and the generated junction.
                    startTrimMeters = 0f,
                    sections = new List<CaveRouteSection>
                    {
                        new CaveRouteSection
                        {
                            zoneId = branchId,
                            startKnot = 0,
                            endKnot = 2,
                            nominalLength = branchLengths[i],
                            guideSize = new Vector2(14f, i == 2 ? 14f : 10f),
                            capStart = false,
                            capEnd = true
                        }
                    }
                });

                float openingHalfWidth = 7f;
                float angularHalfSize = Mathf.Max(30f, Mathf.Asin(Mathf.Clamp(openingHalfWidth / (mainWidth * 0.5f), 0.1f, 0.95f)) * Mathf.Rad2Deg);
                portals.Add(new CavePortalDefinition
                {
                    zoneId = Zones[zoneIndexes[i]].id,
                    mainKnot = mainKnot,
                    mainDistanceMeters = mainSpline.ConvertIndexUnit(mainKnot, PathIndexUnit.Knot, PathIndexUnit.Distance),
                    branchSplineIndex = i,
                    direction = openingDirection,
                    longitudinalHalfSize = Mathf.Max(6f, Zones[zoneIndexes[i]].guideSize.y * 0.2f),
                    angularHalfSize = angularHalfSize
                });
            }

            branchesContainer.Splines = branchSplines;
            branchRoutes.SetDefinitions(branchDefinitions, new List<CavePortalDefinition>());
            mainRoute.SetDefinitions(mainDefinitions, portals);
        }

        public static Spline BuildMainSpline(out List<CaveRouteSection> sections)
        {
            List<Vector3> points = new List<Vector3> { Vector3.zero };
            List<float> widths = new List<float> { Zones[0].guideSize.x };
            List<float> heights = new List<float> { Zones[0].guideSize.y };
            sections = new List<CaveRouteSection>();
            Vector3 current = Vector3.zero;

            for (int zoneIndex = 0; zoneIndex < Zones.Length; zoneIndex++)
            {
                ZoneSpec zone = Zones[zoneIndex];
                float halfLength = zone.length * 0.5f;
                float halfRise = zone.rise * 0.5f;
                float horizontalLength = Mathf.Sqrt(Mathf.Max(0f, halfLength * halfLength - halfRise * halfRise));
                float[] headings = { zone.firstHeading, zone.secondHeading };

                for (int half = 0; half < 2; half++)
                {
                    float radians = headings[half] * Mathf.Deg2Rad;
                    current += new Vector3(Mathf.Sin(radians) * horizontalLength, halfRise, Mathf.Cos(radians) * horizontalLength);
                    points.Add(current);

                    bool midpoint = half == 0;
                    bool finalPoint = zoneIndex == Zones.Length - 1 && half == 1;
                    if (midpoint || finalPoint)
                    {
                        widths.Add(zone.guideSize.x);
                        heights.Add(zone.guideSize.y);
                    }
                    else
                    {
                        // Zone boundary. The throat cross-section comes from ZoneBoundaries so a fresh
                        // build and an already-baked scene end up with the same tunnel.
                        int knotIndex = points.Count - 1;
                        if (!TryGetBoundary(knotIndex, out BoundarySpec boundary))
                            throw new InvalidOperationException(
                                $"No ZoneBoundaries entry for main spline knot {knotIndex}. The zone list " +
                                "and the boundary table have drifted apart.");
                        widths.Add(boundary.width);
                        heights.Add(boundary.height);
                    }
                }

                sections.Add(new CaveRouteSection
                {
                    zoneId = zone.id,
                    startKnot = zoneIndex * 2,
                    endKnot = zoneIndex * 2 + 2,
                    nominalLength = zone.length,
                    guideSize = zone.guideSize,
                    capStart = zoneIndex == 0,
                    capEnd = false
                });
            }

            Spline spline = new Spline();
            for (int i = 0; i < points.Count; i++)
                spline.Add(new BezierKnot((float3)points[i]), TangentMode.AutoSmooth, 0.25f);

            AddEmbeddedData(spline, widths, heights, true);
            return spline;
        }

        private static Spline BuildBranchSpline(Vector3 entry, Vector3 direction, float outsideLength, int branchIndex, out Vector3 openingDirection)
        {
            openingDirection = direction.normalized;
            float verticalRise = branchIndex == 2 ? 10f : 4f;
            Vector3 bentDirection = Quaternion.AngleAxis(branchIndex == 1 ? -18f : 18f, Vector3.up) * openingDirection;
            Vector3 midpoint = entry + bentDirection * (outsideLength * 0.5f) + Vector3.up * (verticalRise * 0.5f);
            Vector3 end = entry + bentDirection * outsideLength + Vector3.up * verticalRise;

            Vector3[] points = { entry, midpoint, end };
            Spline spline = new Spline();
            foreach (Vector3 point in points)
                spline.Add(new BezierKnot((float3)point), TangentMode.AutoSmooth, 0.25f);

            float branchHeight = branchIndex == 2 ? 14f : 10f;
            AddEmbeddedData(spline,
                new List<float> { 14f, 14f, 12f },
                new List<float> { branchHeight, branchHeight, 10f },
                false);
            return spline;
        }

        private static void AddEmbeddedData(Spline spline, IReadOnlyList<float> widths, IReadOnlyList<float> heights, bool addMainMetadata)
        {
            SplineData<float> widthData = spline.GetOrCreateFloatData(CaveRoute.WidthDataKey);
            SplineData<float> heightData = spline.GetOrCreateFloatData(CaveRoute.HeightDataKey);
            SplineData<float> rollData = spline.GetOrCreateFloatData(CaveRoute.RollDataKey);
            SplineData<int> zoneData = spline.GetOrCreateIntData(CaveRoute.ZoneDataKey);
            SplineData<int> portalData = spline.GetOrCreateIntData(CaveRoute.PortalDataKey);

            widthData.PathIndexUnit = PathIndexUnit.Knot;
            heightData.PathIndexUnit = PathIndexUnit.Knot;
            rollData.PathIndexUnit = PathIndexUnit.Knot;
            zoneData.PathIndexUnit = PathIndexUnit.Knot;
            portalData.PathIndexUnit = PathIndexUnit.Knot;

            for (int i = 0; i < spline.Count; i++)
            {
                widthData.Add(i, widths[i]);
                heightData.Add(i, heights[i]);
                rollData.Add(i, 0f);
                zoneData.Add(i, addMainMetadata ? Mathf.Clamp(i / 2 + 1, 1, 6) : 0);
                int portalIndex = addMainMetadata ? (i == 3 ? 1 : i == 7 ? 2 : i == 9 ? 3 : 0) : 0;
                portalData.Add(i, portalIndex);
            }
        }
    }
}
