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

        public static readonly ZoneSpec[] Zones =
        {
            new ZoneSpec("Z1", 55f, 15f, new Vector2(35f, 20f), -15f, 15f),
            new ZoneSpec("Z2", 90f, 30f, new Vector2(65f, 30f), 35f, 70f),
            new ZoneSpec("Z3", 120f, 45f, new Vector2(25f, 25f), 45f, 5f),
            new ZoneSpec("Z4", 80f, 22f, new Vector2(18f, 18f), -35f, -70f),
            new ZoneSpec("Z5", 100f, 35f, new Vector2(70f, 35f), -45f, -5f),
            new ZoneSpec("Z6", 110f, 55f, new Vector2(30f, 60f), 30f, 65f),
            new ZoneSpec("Z7", 120f, 58f, new Vector2(85f, 50f), 40f, 5f)
        };

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

            GameObject branchesObject = new GameObject("Branches_Z2_Z4_Z6");
            branchesObject.transform.SetParent(routesRoot, false);
            SplineContainer branchesContainer = branchesObject.AddComponent<SplineContainer>();
            branchRoutes = branchesObject.AddComponent<CaveRoute>();

            int[] portalKnots = { 3, 7, 11 };
            int[] zoneIndexes = { 1, 3, 5 };
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
                float entryDistance = Mathf.Max(5f, mainWidth * 0.5f - 1f);

                Spline branch = BuildBranchSpline(center, right, entryDistance, branchLengths[i], i, out Vector3 openingDirection);
                branchSplines.Add(branch);

                string branchId = Zones[zoneIndexes[i]].id + "_Branch";
                branchDefinitions.Add(new CaveRouteSplineDefinition
                {
                    routeId = branchId,
                    splineIndex = i,
                    isMainRoute = false,
                    startTrimMeters = entryDistance,
                    sections = new List<CaveRouteSection>
                    {
                        new CaveRouteSection
                        {
                            zoneId = branchId,
                            startKnot = 0,
                            endKnot = 3,
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
                // Z6 carries the steepest nominal rise. A small amount of extra horizontal travel keeps
                // the generated smooth curve below the 30 degree traversal limit without changing its
                // documented 110m pacing target.
                float geometryLength = zone.length + (zoneIndex == 5 ? 10f : 0f);
                float halfLength = geometryLength * 0.5f;
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
                    widths.Add(midpoint || finalPoint ? zone.guideSize.x : 16f);
                    heights.Add(midpoint || finalPoint ? zone.guideSize.y : 12f);
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

        private static Spline BuildBranchSpline(Vector3 center, Vector3 direction, float entryDistance, float outsideLength, int branchIndex, out Vector3 openingDirection)
        {
            openingDirection = direction.normalized;
            float verticalRise = branchIndex == 2 ? 10f : 4f;
            Vector3 entry = center + openingDirection * entryDistance;
            Vector3 bentDirection = Quaternion.AngleAxis(branchIndex == 1 ? -18f : 18f, Vector3.up) * openingDirection;
            Vector3 midpoint = entry + bentDirection * (outsideLength * 0.5f) + Vector3.up * (verticalRise * 0.5f);
            Vector3 end = entry + bentDirection * outsideLength + Vector3.up * verticalRise;

            Vector3[] points = { center, entry, midpoint, end };
            Spline spline = new Spline();
            foreach (Vector3 point in points)
                spline.Add(new BezierKnot((float3)point), TangentMode.AutoSmooth, 0.25f);

            float branchHeight = branchIndex == 2 ? 14f : 10f;
            AddEmbeddedData(spline,
                new List<float> { 14f, 14f, 14f, 12f },
                new List<float> { branchHeight, branchHeight, branchHeight, 10f },
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
                zoneData.Add(i, addMainMetadata ? Mathf.Clamp(i / 2 + 1, 1, 7) : 0);
                int portalIndex = addMainMetadata ? (i == 3 ? 1 : i == 7 ? 2 : i == 11 ? 3 : 0) : 0;
                portalData.Add(i, portalIndex);
            }
        }
    }
}
