using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaveBlockout.Editor
{
    public static class CaveClearanceValidator
    {
        /// <summary>
        /// Supplies the shape that has to fit through the cave. Registered from
        /// Assets/02.Scripts/Submarine/Editor/SubmarineHullProbeProvider.cs, which reads it off
        /// Submarine_final.prefab.
        ///
        /// It is a hook rather than a direct call because an assembly definition cannot reference the
        /// predefined assemblies: CaveBlockout.Editor can never see SubmarineController. There is
        /// deliberately no fallback shape. The bug this file was rewritten to fix was exactly a
        /// plausible-looking hardcoded default (a 3 x 3 x 6 m box) quietly standing in for the real
        /// hull, so a missing provider throws instead of guessing.
        /// </summary>
        public static Func<CaveHullProbe> HullProvider;

        /// <summary>
        /// Colliders the sweep must not treat as walls, registered alongside <see cref="HullProvider"/>.
        ///
        /// The gate measures whether the cave admits the hull. The play scene also contains the submarine
        /// itself, 52 pickups, three sharks and three whirlpools, all on the same layers as the rock - so
        /// without this the answer depends on where a shark happens to be parked at author time. It did:
        /// MainMap passed on identical geometry while MainScene_final failed on Z6_Shark_3 sitting in the
        /// corridor at 555.9 m. Null means ignore nothing, which is correct for MainMap.
        /// </summary>
        public static Func<Collider, bool> IgnoreCollider;

        private static readonly RaycastHit[] SweepHits = new RaycastHit[64];
        private static readonly Collider[] OverlapHits = new Collider[64];

        private static bool Ignored(Collider candidate)
        {
            return candidate == null || (IgnoreCollider != null && IgnoreCollider(candidate));
        }

        /// <summary>Nearest hit that is actually rock, or false if the sweep only met gameplay actors.</summary>
        private static bool SweepHitsRock(
            Vector3 pointA,
            Vector3 pointB,
            float radius,
            Vector3 direction,
            float distance,
            int layerMask,
            out RaycastHit nearest)
        {
            int count = Physics.CapsuleCastNonAlloc(pointA, pointB, radius, direction, SweepHits, distance,
                layerMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            nearest = default;
            for (int i = 0; i < count; i++)
            {
                if (Ignored(SweepHits[i].collider))
                    continue;
                if (!found || SweepHits[i].distance < nearest.distance)
                {
                    nearest = SweepHits[i];
                    found = true;
                }
            }
            return found;
        }

        private static bool OverlapsRock(Vector3 pointA, Vector3 pointB, float radius, int layerMask)
        {
            int count = Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, OverlapHits, layerMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!Ignored(OverlapHits[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// What the optional resource branches are checked against. They are dead-end caves that
        /// players swim into for oxygen canisters, not places the submarine can turn around in, so
        /// they are held to a swimmer's clearance rather than the hull's.
        /// </summary>
        private static readonly CaveHullProbe SwimmerProbe =
            new CaveHullProbe(0.75f, 2.2f, Vector3.zero, 0f, 65, 1f, "swimming player");

        /// <summary>How the hull is allowed to hold itself while being swept along a route.</summary>
        public struct Options
        {
            /// <summary>
            /// Tilt the proxy to follow the tunnel's climb. False is the truth for the submarine, which
            /// only ever yaws about world up. True exists so the report can separate "too big" from
            /// "cannot tilt" - a distinction that decides whether the fix is geometry or the vehicle.
            /// </summary>
            public bool allowPitch;

            /// <summary>
            /// Metres above and below the centreline the hull may sit. Zero pins it to the centreline,
            /// which is a path no driver flies: the submarine has direct vertical control, so a pinned
            /// sweep reports blockages that simply are not there.
            /// </summary>
            public float verticalFreedomMeters;

            /// <summary>Altitude resolution of the search.</summary>
            public float verticalStepMeters;

            public static Options Realistic => new Options
            {
                allowPitch = false,
                verticalFreedomMeters = 4f,
                verticalStepMeters = 0.5f
            };

            public static Options PinnedToCentreline => new Options
            {
                allowPitch = false,
                verticalFreedomMeters = 0f,
                verticalStepMeters = 0.5f
            };

            public override string ToString()
            {
                return $"pitch={(allowPitch ? "follows tunnel" : "level")}, " +
                       $"altitude=+-{verticalFreedomMeters:0.#}m/{verticalStepMeters:0.##}m";
            }
        }

        public static CaveHullProbe ResolveHullProbe()
        {
            CaveHullProbe? probe = HullProvider?.Invoke();
            if (probe == null)
                throw new InvalidOperationException(
                    "No hull probe is registered. CaveClearanceValidator cannot invent a submarine - " +
                    "that is the bug it exists to catch. Assets/02.Scripts/Submarine/Editor/" +
                    "SubmarineHullProbeProvider.cs registers one on domain load; if this throws in a " +
                    "batch run, that file failed to compile.");
            return probe.Value;
        }

        public static bool ValidateAll(CaveRoute mainRoute, CaveRoute branches, out string details)
        {
            return ValidateAll(mainRoute, branches, ResolveHullProbe(), Options.Realistic, out details);
        }

        public static bool ValidateAll(CaveRoute mainRoute, CaveRoute branches, CaveHullProbe hull, out string details)
        {
            return ValidateAll(mainRoute, branches, hull, Options.Realistic, out details);
        }

        public static bool ValidateAll(
            CaveRoute mainRoute,
            CaveRoute branches,
            CaveHullProbe hull,
            Options options,
            out string details)
        {
            return ValidateAll(mainRoute, branches, hull, options, out details, out _);
        }

        public static bool ValidateAll(
            CaveRoute mainRoute,
            CaveRoute branches,
            CaveHullProbe hull,
            Options options,
            out string details,
            out List<string> failures)
        {
            Physics.SyncTransforms();
            failures = new List<string>();
            ValidateRoute(mainRoute, hull, options, failures);
            ValidateRoute(branches, SwimmerProbe, options, failures);
            ValidatePortals(mainRoute, branches, options, failures);
            details = failures.Count == 0
                ? $"All main paths clear {hull} in both directions [{options}], and every branch clears {SwimmerProbe}."
                : string.Join("\n", failures);
            return failures.Count == 0;
        }

        private static void ValidatePortals(CaveRoute mainRoute, CaveRoute branches, Options options, List<string> failures)
        {
            if (mainRoute == null || branches == null)
                return;

            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                CaveRouteSplineDefinition branchDefinition = null;
                foreach (CaveRouteSplineDefinition definition in branches.Definitions)
                {
                    if (definition.splineIndex == portal.branchSplineIndex)
                    {
                        branchDefinition = definition;
                        break;
                    }
                }
                if (branchDefinition == null)
                {
                    failures.Add(portal.zoneId + " portal: matching branch definition is missing.");
                    continue;
                }

                float connectionT = CaveMeshGenerator.FindTAtDistance(
                    branches.Container,
                    portal.branchSplineIndex,
                    0f,
                    1f,
                    branchDefinition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 8f);
                if (!ValidateDirection(branches, portal.branchSplineIndex, 0f, connectionT, SwimmerProbe, options, out string outwardIssue))
                    failures.Add(portal.zoneId + " portal outward: " + outwardIssue);
                if (!ValidateDirection(branches, portal.branchSplineIndex, connectionT, 0f, SwimmerProbe, options, out string inwardIssue))
                    failures.Add(portal.zoneId + " portal inward: " + inwardIssue);
            }
        }

        private static void ValidateRoute(CaveRoute route, CaveHullProbe hull, Options options, List<string> failures)
        {
            if (route == null)
                return;

            foreach (CaveRouteSplineDefinition definition in route.Definitions)
            {
                if (definition.sections.Count == 0)
                    continue;

                // One continuous sweep per spline rather than one per zone.
                //
                // Sweeping zone by zone left two problems. Each section trimmed 0.5% off both ends, and
                // the zone boundaries sit exactly on the seams, so the tightest cross-sections on the
                // route fell into a gap no sweep visited - the gate could not see the throat the
                // submarine actually wedges in. And reachable altitude was reset at every seam, letting
                // a zone start from the centreline even when the previous zone could only be flown
                // hugging its roof. A single pass fixes both: the hull has to arrive somewhere it can
                // continue from.
                CaveRouteSection first = definition.sections[0];
                CaveRouteSection last = definition.sections[definition.sections.Count - 1];
                float startT = route.ResolveSectionStartT(definition, first);
                float endT = route.ResolveSectionEndT(definition, last);

                if (definition.startTrimMeters > 0f)
                    startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT,
                        definition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 3.5f);
                if (first.capStart)
                    startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT, 4.5f);
                if (last.capEnd)
                    endT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, endT, startT, 4.5f);

                float safeStart = Mathf.Lerp(startT, endT, 0.002f);
                float safeEnd = Mathf.Lerp(startT, endT, 0.998f);

                if (!ValidateDirection(route, definition.splineIndex, safeStart, safeEnd, hull, options, out string forwardIssue))
                    failures.Add(definition.routeId + " forward: " + forwardIssue);
                if (!ValidateDirection(route, definition.splineIndex, safeEnd, safeStart, hull, options, out string reverseIssue))
                    failures.Add(definition.routeId + " reverse: " + reverseIssue);
            }
        }

        /// <summary>
        /// An explicit stretch of route by distance, both directions, with no end trimming.
        ///
        /// Needed because the per-zone sections trim 0.5% off each end, and the zone boundaries - the
        /// tightest cross-sections on the whole route - sit exactly on the seam between two sections.
        /// Z2 stops short of 145.6 m and Z3 starts past it, so the pinch fell in a hole that neither
        /// sweep visited and the margin scan was quietly measuring the chamber taper instead.
        /// </summary>
        public static bool ValidateWindow(
            CaveRoute route,
            int splineIndex,
            float startDistanceMeters,
            float endDistanceMeters,
            CaveHullProbe hull,
            Options options,
            out List<string> failures)
        {
            failures = new List<string>();
            float startT = route.EvaluateTAtDistance(splineIndex, startDistanceMeters);
            float endT = route.EvaluateTAtDistance(splineIndex, endDistanceMeters);
            if (!ValidateDirection(route, splineIndex, startT, endT, hull, options, out string forwardIssue))
                failures.Add($"[{startDistanceMeters:F1}-{endDistanceMeters:F1} m] forward: " + forwardIssue);
            if (!ValidateDirection(route, splineIndex, endT, startT, hull, options, out string reverseIssue))
                failures.Add($"[{startDistanceMeters:F1}-{endDistanceMeters:F1} m] reverse: " + reverseIssue);
            return failures.Count == 0;
        }

        /// <summary>
        /// One zone, both directions. Exposed so the margin scan can ask each zone separately - a single
        /// route-wide verdict hides which zone is actually binding.
        /// </summary>
        public static bool ValidateSection(
            CaveRoute route,
            CaveRouteSplineDefinition definition,
            CaveRouteSection section,
            CaveHullProbe hull,
            Options options,
            out List<string> failures)
        {
            failures = new List<string>();
            float startT = route.ResolveSectionStartT(definition, section);
            float endT = route.ResolveSectionEndT(definition, section);
            if (definition.startTrimMeters > 0f)
                startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT,
                    definition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 3.5f);

            if (section.capStart)
                startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT, 4.5f);
            if (section.capEnd)
                endT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, endT, startT, 4.5f);

            float safeStart = Mathf.Lerp(startT, endT, 0.005f);
            float safeEnd = Mathf.Lerp(startT, endT, 0.995f);
            startT = safeStart;
            endT = safeEnd;
            if (!ValidateDirection(route, definition.splineIndex, startT, endT, hull, options, out string forwardIssue))
                failures.Add(definition.routeId + " " + section.zoneId + " forward: " + forwardIssue);
            if (!ValidateDirection(route, definition.splineIndex, endT, startT, hull, options, out string reverseIssue))
                failures.Add(definition.routeId + " " + section.zoneId + " reverse: " + reverseIssue);
            return failures.Count == 0;
        }

        /// <summary>
        /// Sweeps the hull along one section and reports whether ANY flyable path exists.
        ///
        /// The submarine's degrees of freedom are forward/reverse, bodily climb and sink, and yaw about
        /// world up - no pitch, no roll. So the search space is one dimensional: how far above or below
        /// the centreline the hull sits at each station. This walks the section station by station and
        /// keeps the full set of altitudes still reachable, which makes the answer exact for the
        /// discretised problem rather than dependent on a lucky greedy choice. A single pinned path -
        /// what this used to do, with the proxy welded to the centreline - reports the route blocked
        /// wherever a rock leans in, even when the driver would simply fly a metre over it.
        /// </summary>
        private static bool ValidateDirection(
            CaveRoute route,
            int splineIndex,
            float startT,
            float endT,
            CaveHullProbe hull,
            Options options,
            out string issue)
        {
            // Fixed sample counts made station spacing depend on how long the swept stretch happened to
            // be: 80 samples across one 80 m zone is a metre apart, but across the whole 580 m route it
            // is seven, which steps straight over a throat. Spacing is what matters, so derive the count
            // from arc length.
            float startDistance = DistanceAtT(route, splineIndex, startT);
            float endDistance = DistanceAtT(route, splineIndex, endT);
            float sweptLength = Mathf.Abs(endDistance - startDistance);
            int samples = Mathf.Clamp(Mathf.CeilToInt(sweptLength / 1.5f), 40, 600);
            float direction = endDistance >= startDistance ? 1f : -1f;
            float travelled = 0f;

            float step = Mathf.Max(0.05f, options.verticalStepMeters);
            int bandRadius = Mathf.Max(0, Mathf.RoundToInt(options.verticalFreedomMeters / step));
            int bandSize = bandRadius * 2 + 1;

            bool[] reachable = new bool[bandSize];
            bool[] next = new bool[bandSize];
            reachable[bandRadius] = true;

            Vector3 previous = route.Container.EvaluatePosition(splineIndex, startT);
            Quaternion previousOrientation = Quaternion.identity;
            bool hasPreviousOrientation = false;
            string lastObstacle = "nothing (the section is clear at every altitude tried)";
            float lastObstacleDistance = -1f;

            for (int i = 1; i <= samples; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)samples);
                Vector3 current = route.Container.EvaluatePosition(splineIndex, t);
                Vector3 delta = current - previous;
                if (delta.sqrMagnitude < 0.0001f)
                    continue;

                travelled += delta.magnitude;
                float stationDistance = startDistance + direction * travelled;

                // The hull yaws about world up and never pitches or rolls (SubmarineController applies
                // Quaternion.AngleAxis(yaw, Vector3.up) only), so by default the proxy stays level. The
                // old LookRotation(delta, up) tilted it to follow the 27 degree climb, which quietly gave
                // a long hull a vertical footprint it can never actually achieve.
                Quaternion orientation;
                if (options.allowPitch)
                {
                    orientation = Quaternion.LookRotation(delta.normalized,
                        route.Container.EvaluateUpVector(splineIndex, t));
                }
                else
                {
                    Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
                    if (horizontal.sqrMagnitude < 0.000001f)
                    {
                        issue = $"centreline is vertical near {DescribeAt(route, splineIndex, t)}; a hull " +
                                "that cannot pitch has no heading here.";
                        return false;
                    }
                    orientation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                }

                // How far the hull may climb or sink over this step, from its own speed ratio.
                int reach = Mathf.Max(1, Mathf.FloorToInt(
                    hull.verticalPerForwardMetre * delta.magnitude / step));

                Array.Clear(next, 0, bandSize);
                bool anyReachable = false;

                for (int band = 0; band < bandSize; band++)
                {
                    if (!CanReach(reachable, band, reach))
                        continue;

                    Vector3 offset = Vector3.up * ((band - bandRadius) * step);
                    hull.GetWorldCapsule(previous + offset, orientation, out Vector3 pointA, out Vector3 pointB,
                        out float radius);

                    // Reject poses that start inside rock before sweeping from them.
                    //
                    // This is load-bearing, not a belt-and-braces check. A shape cast that begins in
                    // penetration does not report the collider it is already inside, so an oversized hull
                    // swallowing the whole tunnel registered as clear and the margin scan happily reported
                    // that a hull four times the real one fits through a 16 x 12 m throat. Overlap is a
                    // blocked pose, and saying so is what makes the numbers mean anything.
                    if (OverlapsRock(pointA, pointB, radius, hull.layerMask))
                    {
                        lastObstacle = "the hull is already inside rock at this altitude before it moves";
                        lastObstacleDistance = stationDistance;
                        continue;
                    }

                    if (SweepHitsRock(pointA, pointB, radius, delta.normalized, delta.magnitude,
                            hull.layerMask, out RaycastHit hit))
                    {
                        lastObstacle = $"{hit.collider.name} at {hit.point}, normal={hit.normal}" +
                                       DescribeHitTriangle(hit);
                        lastObstacleDistance = stationDistance;
                        continue;
                    }

                    // Wedge check: the runtime refuses any yaw that newly overlaps or deepens penetration
                    // (SubmarineController.WouldIntroduceOrDeepenRotationOverlap) and zeroes yaw velocity
                    // when it does. Once that trips mid-corner the hull can no longer turn at all, which
                    // is the "완전히 끼어서 멈춤" symptom. A sweep alone cannot see it, because sweeping
                    // never asks whether the shape could have rotated into that pose.
                    if (hasPreviousOrientation
                        && !CanYaw(hull, previous + offset, previousOrientation, orientation, out string wedge))
                    {
                        lastObstacle = $"no room to turn - {wedge}";
                        lastObstacleDistance = stationDistance;
                        continue;
                    }

                    next[band] = true;
                    anyReachable = true;
                }

                if (!anyReachable)
                {
                    string where = lastObstacleDistance >= 0f
                        ? $"{lastObstacleDistance:F1} m (t={t:F3})"
                        : $"{stationDistance:F1} m (t={t:F3})";
                    issue = $"no flyable altitude at {where} within +-{options.verticalFreedomMeters:0.#} m " +
                            $"of the centreline. Last obstacle: {lastObstacle}. The hull is " +
                            $"{hull.radius * 2f:F2} m across and {hull.height:F2} m long, stays level " +
                            $"through a climbing tunnel, and reaches {hull.AftReach:F2} m behind its pivot.";
                    return false;
                }

                Array.Copy(next, reachable, bandSize);
                previous = current;
                previousOrientation = orientation;
                hasPreviousOrientation = true;
            }

            issue = string.Empty;
            return true;
        }

        private static bool CanReach(bool[] reachable, int band, int reach)
        {
            int from = Mathf.Max(0, band - reach);
            int to = Mathf.Min(reachable.Length - 1, band + reach);
            for (int i = from; i <= to; i++)
            {
                if (reachable[i])
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Tests the two rotations that matter: the single tick the runtime gate actually evaluates, and
        /// the whole heading change across this step. The tick increment alone passes almost everywhere
        /// because it is a fraction of a degree; the cumulative turn alone would flag corners the driver
        /// takes gradually while translating. Both have to clear.
        /// </summary>
        private static bool CanYaw(
            CaveHullProbe hull,
            Vector3 position,
            Quaternion from,
            Quaternion to,
            out string detail)
        {
            float totalYaw = Quaternion.Angle(from, to);
            if (totalYaw <= 0.001f)
            {
                detail = string.Empty;
                return true;
            }

            // The caller has already rejected poses that start in penetration, so anything found below is
            // rock the rotation itself brings the hull into.
            float tickYaw = Mathf.Min(hull.yawStepDegrees, totalYaw);
            if (tickYaw > 0.001f)
            {
                Quaternion afterTick = Quaternion.RotateTowards(from, to, tickYaw);
                if (Overlaps(hull, position, afterTick))
                {
                    detail = $"one tick of yaw ({tickYaw:F3} deg) already puts the tail in rock";
                    return false;
                }
            }

            if (Overlaps(hull, position, to))
            {
                detail = $"the {totalYaw:F2} deg heading change across this step puts the tail in rock";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private static bool Overlaps(CaveHullProbe hull, Vector3 position, Quaternion rotation)
        {
            hull.GetWorldCapsule(position, rotation, out Vector3 pointA, out Vector3 pointB, out float radius);
            return OverlapsRock(pointA, pointB, radius, hull.layerMask);
        }

        /// <summary>
        /// Route distance rather than the normalised t the old report printed. A failure at "t=0.251"
        /// says nothing about which zone broke; "145.6 m" lands on the zone table in HANDOFF.md.
        /// </summary>
        private static string DescribeAt(CaveRoute route, int splineIndex, float t)
        {
            float distance = DistanceAtT(route, splineIndex, t);
            return $"{distance:F1} m (t={t:F3})";
        }

        private static float DistanceAtT(CaveRoute route, int splineIndex, float t)
        {
            const int samples = 256;
            float clamped = Mathf.Clamp01(t);
            float accumulated = 0f;
            Vector3 previous = route.Container.EvaluatePosition(splineIndex, 0f);
            for (int i = 1; i <= samples; i++)
            {
                float sampleT = clamped * i / samples;
                Vector3 current = route.Container.EvaluatePosition(splineIndex, sampleT);
                accumulated += Vector3.Distance(previous, current);
                previous = current;
            }
            return accumulated;
        }

        private static string DescribeHitTriangle(RaycastHit hit)
        {
            if (!(hit.collider is MeshCollider meshCollider) || meshCollider.sharedMesh == null || hit.triangleIndex < 0)
                return string.Empty;

            int triangleOffset = hit.triangleIndex * 3;
            int[] triangles = meshCollider.sharedMesh.triangles;
            Vector3[] vertices = meshCollider.sharedMesh.vertices;
            if (triangleOffset + 2 >= triangles.Length)
                return string.Empty;

            Transform meshTransform = meshCollider.transform;
            Vector3 a = meshTransform.TransformPoint(vertices[triangles[triangleOffset]]);
            Vector3 b = meshTransform.TransformPoint(vertices[triangles[triangleOffset + 1]]);
            Vector3 c = meshTransform.TransformPoint(vertices[triangles[triangleOffset + 2]]);
            return $", triangle={hit.triangleIndex}, triangleVertices=({a} | {b} | {c})";
        }
    }
}
