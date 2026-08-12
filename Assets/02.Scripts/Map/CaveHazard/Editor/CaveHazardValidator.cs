using System.Collections.Generic;
using System.Text;
using CaveBlockout.Decor;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// Checks the things that are cheap to get wrong and expensive to notice by eye: obstacles that
    /// resolve onto a surface but face the wrong way, vents whose jet never reaches the swim line,
    /// rocks with nothing under them to fall through, and gates that leave no gap.
    ///
    /// Every threshold here is a difficulty knob. Lowering MinDodgeChannelMeters makes the map harder
    /// in one number and re-validates the whole layout against it.
    /// </summary>
    public static class CaveHazardValidator
    {
        /// <summary>
        /// MAP_GUIDE.md's floor is a 10 m passable width and 8 m of vertical clearance. Six metres of
        /// free channel at a hazard's worst moment is twice the 3 m reference submarine, which is the
        /// narrowest gap that still reads as a gap rather than as a wall with a seam in it.
        /// </summary>
        public const float MinDodgeChannelMeters = 6f;

        /// <summary>A rock that falls less than this lands on the wall beside the player, not past them.</summary>
        public const float MinRockDropMeters = 8f;

        /// <summary>
        /// How close a vent's jet has to come to the route centre line to count as a hazard. Z5's floor
        /// is up to 28.5 m below the swim line while a scale-1 jet is 6 m tall, so this is the check
        /// that catches a vent nobody can ever touch.
        /// </summary>
        public const float MaxVentReachToCentrelineMeters = 6f;

        private const float RockRadius = 0.45f;
        private const float VentCapsuleRadius = 1.15f;
        private const float VentCapsuleHeight = 6f;

        /// <summary>CaveSwimController.moveSpeed. dashSpeed is 16, but cruise is the honest baseline.</summary>
        private const float CruiseSpeed = 10f;

        /// <summary>Half the stretch either side of a gate in which its warning is readable and its drops matter.</summary>
        private const float ReactionWindowMeters = 15f;

        public sealed class Report
        {
            public readonly List<string> failures = new List<string>();
            public readonly List<string> warnings = new List<string>();
            public readonly StringBuilder detail = new StringBuilder();
            public int instances;
            public int resolved;
            public bool Passed => failures.Count == 0;
        }

        public static Report Validate(IReadOnlyList<CaveHazardStation> stations, CaveDecorContext context)
        {
            var report = new Report();

            if (context == null || !context.IsValid)
            {
                report.failures.Add("no CaveRoute or CaveShell collider in the scene");
                return report;
            }

            foreach (CaveHazardStation station in stations)
            {
                report.detail.AppendLine($"[{station.id}] {station.zoneId} " +
                                         $"{(station.useCrossPattern ? "Cross" : "Alone")} " +
                                         $"firstActive={station.FirstActiveTime:0.00}s " +
                                         $"period={station.CyclePeriod:0.00}s " +
                                         $"danger={station.DangerousSecondsPerCycle:0.00}s");

                var resolvedMounts = new List<(CaveHazardInstance instance, Vector3 position, Vector3 centre)>();

                foreach (CaveHazardInstance instance in station.instances)
                {
                    report.instances++;
                    if (!CaveDecorProjector.TryCast(context, instance.routeId, instance.routeDistance,
                            instance.angleDegrees, out CaveDecorSurface surface))
                    {
                        report.failures.Add($"{station.id}: no surface at d={instance.routeDistance:0.0} " +
                                            $"a={instance.angleDegrees:0}");
                        continue;
                    }

                    report.resolved++;
                    Vector3 position = surface.point + surface.normal * instance.surfaceOffset;
                    resolvedMounts.Add((instance, position, surface.centerline));

                    CheckMountSide(report, station, instance, position, surface);

                    if (instance.kind == CaveHazardKind.FallingRock)
                        CheckRockDrop(report, context, station, instance, position);
                    else
                        CheckVentReach(report, context, station, instance, position);
                }

                CheckStationOverlap(report, station, resolvedMounts);
                CheckDodgeChannel(report, context, station, resolvedMounts);
            }

            CheckTimingCoverage(report, stations);
            return report;
        }

        /// <summary>
        /// Rocks must hang above the swim line and vents must sit below it.
        ///
        /// Measured against world Y at the centre line rather than against CaveDecorProjector.Classify.
        /// Classify reads the hit normal, and with main-route noise at Strong the probe found ceiling
        /// casts reporting Wall at 0 degrees in six of twelve Z3 samples - the local facet is tilted
        /// even though the point is unambiguously overhead. Which side of the channel the mount is on
        /// is the property that actually matters, and it survives the noise.
        /// </summary>
        private static void CheckMountSide(Report report, CaveHazardStation station,
            CaveHazardInstance instance, Vector3 position, CaveDecorSurface surface)
        {
            float verticalOffset = position.y - surface.centerline.y;
            bool wantsAbove = instance.kind == CaveHazardKind.FallingRock;

            if (wantsAbove && verticalOffset <= 1f)
            {
                report.failures.Add($"{station.id}: rock mount at d={instance.routeDistance:0.0} " +
                                    $"a={instance.angleDegrees:0} is {verticalOffset:0.00} m above the " +
                                    "centre line; it must be overhead");
            }
            else if (!wantsAbove && verticalOffset >= -1f)
            {
                report.failures.Add($"{station.id}: vent mount at d={instance.routeDistance:0.0} " +
                                    $"a={instance.angleDegrees:0} is {verticalOffset:0.00} m above the " +
                                    "centre line; it must be below");
            }
        }

        /// <summary>
        /// How far the rock actually falls, cast along world -Y.
        ///
        /// The direction is not negotiable and not derived from the mount: FallingRock is a Rigidbody
        /// with useGravity, launched at zero velocity, so it falls on Physics.gravity no matter how the
        /// spawner is rotated. On a tunnel climbing at ~29 degrees a ceiling point does not necessarily
        /// have open water beneath it.
        /// </summary>
        private static void CheckRockDrop(Report report, CaveDecorContext context,
            CaveHazardStation station, CaveHazardInstance instance, Vector3 position)
        {
            if (!context.Shell.Raycast(new Ray(position, Vector3.down), out RaycastHit hit, 400f))
            {
                report.warnings.Add($"{station.id}: rock at d={instance.routeDistance:0.0} finds no " +
                                    "floor within 400 m; it will expire on maxLifetime instead of landing");
                return;
            }

            report.detail.AppendLine($"    rock d={instance.routeDistance,6:0.0} a={instance.angleDegrees,4:0} " +
                                     $"drop={hit.distance:0.00} m");

            if (hit.distance < MinRockDropMeters)
            {
                report.failures.Add($"{station.id}: rock at d={instance.routeDistance:0.0} " +
                                    $"a={instance.angleDegrees:0} falls only {hit.distance:0.00} m " +
                                    $"(need {MinRockDropMeters}); it lands on the wall, not across the channel");
            }
        }

        /// <summary>
        /// Closest approach of the vent's hazard capsule axis to the route centre line.
        ///
        /// The capsule stands along world up from the mount because the instance is world-aligned, so
        /// this is a segment-to-polyline distance and not something the seating check can imply.
        /// </summary>
        private static void CheckVentReach(Report report, CaveDecorContext context,
            CaveHazardStation station, CaveHazardInstance instance, Vector3 position)
        {
            Vector3 top = position + Vector3.up * (VentCapsuleHeight * instance.scale);
            CaveRoutePolyline polyline = context.GetPolyline(instance.routeId);

            float best = float.PositiveInfinity;
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 sample = Vector3.Lerp(position, top, i / (float)steps);
                polyline.Project(sample, out float lateral);
                best = Mathf.Min(best, lateral);
            }

            float clearance = best - VentCapsuleRadius * instance.scale;
            report.detail.AppendLine($"    vent d={instance.routeDistance,6:0.0} a={instance.angleDegrees,4:0} " +
                                     $"scale={instance.scale:0.0} jetTop={top.y - position.y:0.0} m " +
                                     $"reachToCentreline={clearance:0.00} m");

            if (clearance > MaxVentReachToCentrelineMeters)
            {
                report.failures.Add($"{station.id}: vent at d={instance.routeDistance:0.0} " +
                                    $"a={instance.angleDegrees:0} scale={instance.scale:0.0} gets no closer " +
                                    $"than {clearance:0.00} m to the swim line " +
                                    $"(need {MaxVentReachToCentrelineMeters}); nobody can touch it");
            }
        }

        private static void CheckStationOverlap(Report report, CaveHazardStation station,
            List<(CaveHazardInstance instance, Vector3 position, Vector3 centre)> mounts)
        {
            for (int i = 0; i < mounts.Count; i++)
            {
                for (int j = i + 1; j < mounts.Count; j++)
                {
                    float radiusI = Radius(mounts[i].instance);
                    float radiusJ = Radius(mounts[j].instance);
                    float distance = Vector3.Distance(mounts[i].position, mounts[j].position);
                    if (distance < radiusI + radiusJ)
                    {
                        report.failures.Add($"{station.id}: two hazards overlap " +
                                            $"({distance:0.00} m apart, radii {radiusI:0.00}+{radiusJ:0.00})");
                    }
                }
            }
        }

        private static float Radius(CaveHazardInstance instance)
        {
            return instance.kind == CaveHazardKind.FallingRock
                ? RockRadius
                : VentCapsuleRadius * instance.scale;
        }

        /// <summary>
        /// The widest free disc through the station's cross-section at its most dangerous instant.
        ///
        /// Sampled rather than solved: the shell is noisy so the section is not an ellipse, and the
        /// hazard volumes are a mix of vertical capsules and falling-rock columns. A polar grid of free
        /// and blocked samples, then the largest free-sample-to-obstruction radius, answers the actual
        /// question - how wide is the gap a player can steer through.
        ///
        /// A Cross station is evaluated with only its larger group live, because ObstaclePatternBase
        /// never has both groups active. An Alone station is evaluated with everything live.
        /// </summary>
        private static void CheckDodgeChannel(Report report, CaveDecorContext context,
            CaveHazardStation station,
            List<(CaveHazardInstance instance, Vector3 position, Vector3 centre)> mounts)
        {
            if (mounts.Count == 0)
                return;

            List<(CaveHazardInstance instance, Vector3 position, Vector3 centre)> live = mounts;
            if (station.useCrossPattern)
            {
                var groupA = mounts.FindAll(m => m.instance.group == CaveHazardGroup.A);
                var groupB = mounts.FindAll(m => m.instance.group == CaveHazardGroup.B);
                live = groupA.Count >= groupB.Count ? groupA : groupB;
            }

            float distance = station.instances[0].routeDistance;
            CaveRoutePolyline polyline = context.GetPolyline(station.instances[0].routeId);
            polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out _);
            CaveDecorProjector.BuildAxisFrame(tangent, out Vector3 up, out Vector3 right);

            const int angleSteps = 72;
            const float radiusStep = 0.5f;

            var freeSamples = new List<Vector3>();
            var blockedSamples = new List<Vector3>();

            for (int a = 0; a < angleSteps; a++)
            {
                float angle = a / (float)angleSteps * 360f;
                Vector3 direction = Quaternion.AngleAxis(angle, tangent) * up;

                float wall = context.Shell.Raycast(new Ray(centre, direction), out RaycastHit hit, 200f)
                    ? hit.distance
                    : 0f;

                for (float r = radiusStep; r < wall; r += radiusStep)
                {
                    Vector3 point = centre + direction * r;
                    if (IsBlocked(point, live))
                        blockedSamples.Add(point);
                    else
                        freeSamples.Add(point);
                }
            }

            if (freeSamples.Count == 0)
            {
                report.failures.Add($"{station.id}: cross-section is fully blocked");
                return;
            }

            // Largest free disc: for every free sample, the distance to the nearest blocked sample or
            // to the wall, whichever is closer. The best such radius is the half-width of the channel.
            float bestRadius = 0f;
            foreach (Vector3 free in freeSamples)
            {
                float nearest = float.PositiveInfinity;
                foreach (Vector3 blocked in blockedSamples)
                    nearest = Mathf.Min(nearest, Vector3.Distance(free, blocked));

                Vector3 outward = free - centre;
                float wallDistance = float.PositiveInfinity;
                if (outward.sqrMagnitude > 1e-4f &&
                    context.Shell.Raycast(new Ray(free, outward.normalized), out RaycastHit wallHit, 200f))
                {
                    wallDistance = wallHit.distance;
                }

                bestRadius = Mathf.Max(bestRadius, Mathf.Min(nearest, wallDistance));
            }

            float channel = bestRadius * 2f;
            report.detail.AppendLine($"    dodgeChannel={channel:0.00} m " +
                                     $"(live hazards={live.Count}, free={freeSamples.Count}, " +
                                     $"blocked={blockedSamples.Count})");

            if (channel < MinDodgeChannelMeters)
            {
                report.failures.Add($"{station.id}: widest free channel is {channel:0.00} m " +
                                    $"(need {MinDodgeChannelMeters})");
            }
        }

        private static bool IsBlocked(Vector3 point,
            List<(CaveHazardInstance instance, Vector3 position, Vector3 centre)> live)
        {
            foreach ((CaveHazardInstance instance, Vector3 position, Vector3 _) in live)
            {
                if (instance.kind == CaveHazardKind.FallingRock)
                {
                    // A falling rock sweeps a vertical column below its spawn point.
                    if (point.y > position.y)
                        continue;
                    Vector2 flat = new Vector2(point.x - position.x, point.z - position.z);
                    if (flat.magnitude <= RockRadius)
                        return true;
                }
                else
                {
                    float top = position.y + VentCapsuleHeight * instance.scale;
                    if (point.y < position.y || point.y > top)
                        continue;
                    Vector2 flat = new Vector2(point.x - position.x, point.z - position.z);
                    if (flat.magnitude <= VentCapsuleRadius * instance.scale)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Arithmetic, not simulation.
        ///
        /// Batch mode never enters play mode, so the coroutines in ObstaclePatternBase and
        /// VentController do not run - the same reason UnderwaterZoneDirector.LateUpdate returns early
        /// outside play. Every timing field on these prefabs is a plain duration in seconds, so the
        /// schedule can be solved directly instead of observed.
        /// </summary>
        private static void CheckTimingCoverage(Report report, IReadOnlyList<CaveHazardStation> stations)
        {
            report.detail.AppendLine("--- timing (analytic, cruise 10 m/s) ---");

            foreach (string zone in new[] { "Z3", "Z5" })
            {
                float ventDuty = 0f;
                int ventCount = 0;
                int rockCount = 0;
                float dropExposure = 0f;

                foreach (CaveHazardStation station in stations)
                {
                    if (station.zoneId != zone)
                        continue;

                    if (station.kind == CaveHazardKind.FallingRock)
                    {
                        rockCount++;
                        // Seconds inside the gate at cruise: the approach is visible for roughly
                        // ReactionWindowMeters before the station and the same again past it.
                        dropExposure += station.DropsPerSecond * (ReactionWindowMeters * 2f / CruiseSpeed);
                    }
                    else
                    {
                        ventCount++;
                        ventDuty += station.DangerousSecondsPerCycle / station.CyclePeriod;
                    }
                }

                if (ventCount == 0 && rockCount == 0)
                {
                    report.failures.Add($"{zone} has no hazard stations");
                    continue;
                }

                if (rockCount > 0)
                {
                    report.detail.AppendLine($"  {zone}: {rockCount} rock stations, " +
                                             $"~{dropExposure:0.0} drop events per traversal at " +
                                             $"{CruiseSpeed:0} m/s");
                    if (dropExposure < 1f)
                        report.warnings.Add($"{zone}: a player crossing at cruise meets fewer than one " +
                                            "rock drop; the gates are decorative");
                }

                if (ventCount > 0)
                {
                    float dutyAverage = ventDuty / ventCount;
                    report.detail.AppendLine($"  {zone}: {ventCount} vent stations, " +
                                             $"mean duty cycle {dutyAverage:P0}");
                    if (dutyAverage >= 0.999f)
                        report.warnings.Add($"{zone}: every vent station is dangerous 100% of the time, " +
                                            "so waiting never helps and the dodge channel is the only " +
                                            "way through");
                }
            }
        }

        public static string Format(Report report)
        {
            var text = new StringBuilder();
            text.AppendLine("===== CAVE HAZARD VALIDATION =====");
            text.Append(report.detail);
            foreach (string warning in report.warnings)
                text.AppendLine($"WARN {warning}");
            foreach (string failure in report.failures)
                text.AppendLine($"FAIL {failure}");
            text.AppendLine($"CAVE_HAZARD_VALIDATION {(report.Passed ? "PASS" : "FAIL")} " +
                            $"instances={report.instances} resolved={report.resolved} " +
                            $"failures={report.failures.Count} warnings={report.warnings.Count}");
            return text.ToString();
        }
    }
}
