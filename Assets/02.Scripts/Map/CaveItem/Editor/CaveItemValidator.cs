using System.Collections.Generic;
using System.Text;
using CaveBlockout;
using CaveBlockout.Decor;
using CaveHazard.EditorTools;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Mechanical checks on the placed items. Mirrors CaveHazardValidator: failures gate, warnings only
    /// report, and every threshold is a named constant so difficulty and safety margins are edited in
    /// one place rather than hunted through the code.
    ///
    /// The checks worth understanding before changing a number:
    ///
    ///   Reachability. None of the four pickup prefabs has a Rigidbody, so nothing ever settles - the
    ///   transform the tool writes is exactly what ships. There is no physics safety net, and a tank
    ///   half-sunk into rock stays half-sunk forever, silently un-pickable because PlayerInteractor's
    ///   obstruction ray hits the shell first.
    ///
    ///   Hazard keep-out re-derives the hazard mounts by calling CaveHazardLayout.Build() and casting
    ///   them, rather than hard-coding the distances the item layout was written against. If someone
    ///   retunes the rockfall stations, this fails instead of quietly overlapping.
    /// </summary>
    public static class CaveItemValidator
    {
        // ---- pickup reachability ----
        /// <summary>A pickup's body must stand this far proud of the rock it was cast onto.</summary>
        public const float MinShellClearanceMeters = 0.15f;

        /// <summary>Free straight line from a pickup back towards the centre line, so the player can see it.</summary>
        public const float MinPickupApproachMeters = 1.5f;

        /// <summary>
        /// Beyond this a pickup is a long dive off the swim line rather than a pickup. Z5 and Z6 are
        /// 30x60 and 85x50, so their floors run 20-28 m down - this is what keeps consumables out of
        /// the parts of those chambers nobody swims through.
        /// </summary>
        public const float MaxPickupCentrelineMeters = 20f;

        // ---- coexistence with the hazard pass ----
        public const float HazardKeepOutMeters = 3f;
        public const float RockColumnClearMeters = 12f;
        public const float VentCapsuleRadius = 1.15f;
        public const float VentCapsuleHeight = 6f;

        // ---- geometry ----
        /// <summary>Added to each portal's authored half-sizes. Portals are holes, so casts near them lie.</summary>
        public const float PortalKeepOutMarginMeters = 4f;

        public const float MinBranchEntryMeters = 8f;
        public const float MinBranchTailMeters = 3f;
        public const float MinItemSeparationMeters = 2.5f;

        /// <summary>Nothing hostile parked on the spawn point.</summary>
        public const float SubmarineKeepOutMeters = 12f;

        // ---- Z6 climax ----
        /// <summary>Same bar CaveHazardValidator uses, so "passable" means one thing across both tools.</summary>
        public const float MinDodgeChannelMeters = 6f;

        /// <summary>Two whirlpool outer radii plus 2 m of daylight.</summary>
        public const float MinTornadoSeparationMeters = 16f;

        public sealed class Report
        {
            public int placements;
            public int resolved;
            public readonly List<string> failures = new List<string>();
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> detail = new List<string>();
            public bool Passed => failures.Count == 0;
        }

        private readonly struct Placed
        {
            public readonly CaveItemPlacement placement;
            public readonly CaveItemCatalog.Species species;
            public readonly CaveItemResolver.Result resolved;
            public readonly GameObject instance;

            public Placed(CaveItemPlacement placement, CaveItemCatalog.Species species,
                CaveItemResolver.Result resolved, GameObject instance)
            {
                this.placement = placement;
                this.species = species;
                this.resolved = resolved;
                this.instance = instance;
            }
        }

        public static Report Validate(IReadOnlyList<CaveItemPlacement> placements,
            CaveDecorContext context, IReadOnlyList<GameObject> instances)
        {
            var report = new Report { placements = placements.Count };

            if (context == null || !context.IsValid)
            {
                report.failures.Add("no CaveRoute or CaveShell collider in the scene");
                return report;
            }

            Transform submarine = CaveItemSpawner.FindSubmarine();
            var placed = new List<Placed>();
            var byId = new Dictionary<string, GameObject>();
            if (instances != null)
            {
                foreach (GameObject instance in instances)
                {
                    if (instance != null)
                        byId[instance.name] = instance;
                }
            }

            foreach (CaveItemPlacement placement in placements)
            {
                if (!CaveItemResolver.TryResolve(context, placement, submarine,
                        out CaveItemResolver.Result resolved, out string failure))
                {
                    report.failures.Add($"C1 {placement.id}: {failure}");
                    continue;
                }

                report.resolved++;
                byId.TryGetValue(placement.id, out GameObject instance);
                placed.Add(new Placed(placement, CaveItemCatalog.Get(placement.kind), resolved, instance));
            }

            CheckRouteRanges(report, context, placed);
            CheckPickupReachability(report, context, placed);
            CheckPortals(report, context, placed);
            CheckHazardKeepOut(report, context, placed);
            CheckSeparation(report, placed);
            CheckDecorOverlap(report, placed);
            CheckTornadoes(report, placed);
            CheckDodgeChannel(report, context, placed);
            CheckCounts(report, placements);
            CheckDarkZone(report, placed);
            CheckSubmarineKeepOut(report, placed, submarine);
            CheckComponentContract(report, placed);

            return report;
        }

        // ---------------------------------------------------------------- C2

        private static void CheckRouteRanges(Report report, CaveDecorContext context, List<Placed> placed)
        {
            foreach (Placed p in placed)
            {
                if (p.placement.anchor == CaveItemAnchor.SubmarineInterior)
                    continue;

                CaveRoutePolyline polyline = context.GetPolyline(p.placement.routeId);
                if (polyline == null)
                {
                    report.failures.Add($"C2 {p.placement.id}: route '{p.placement.routeId}' has no polyline");
                    continue;
                }

                bool isBranch = p.placement.routeId != "MainRoute";
                float low = isBranch ? MinBranchEntryMeters : 1.5f;
                float high = polyline.Length - (isBranch ? MinBranchTailMeters : 1.5f);

                if (p.placement.routeDistance < low || p.placement.routeDistance > high)
                {
                    report.failures.Add($"C2 {p.placement.id}: d={p.placement.routeDistance:0.0} outside " +
                                        $"[{low:0.0}, {high:0.0}] on '{p.placement.routeId}' " +
                                        $"(length {polyline.Length:0.0})");
                }
            }
        }

        // ---------------------------------------------------------------- C3, C4, C5

        private static void CheckPickupReachability(Report report, CaveDecorContext context, List<Placed> placed)
        {
            foreach (Placed p in placed)
            {
                if (!p.species.isPickup || p.placement.anchor != CaveItemAnchor.Surface)
                    continue;

                // C3 - is it actually clear of the rock? Re-cast from the placed position back into the
                // surface rather than trusting the authored offset, because the offset is measured from
                // the cast point and the shell is noisy between samples.
                Vector3 back = -p.resolved.surface.normal;
                float clearance = context.Shell.Raycast(new Ray(p.resolved.position, back),
                    out RaycastHit hit, 10f)
                    ? hit.distance
                    : float.PositiveInfinity;

                if (clearance < MinShellClearanceMeters)
                {
                    report.failures.Add($"C3 {p.placement.id}: only {clearance:0.###} m clear of the shell " +
                                        $"(need {MinShellClearanceMeters}); it would be un-pickable");
                }

                // C4 - PlayerInteractor casts from the player towards the item and drops the candidate if
                // anything on the Default layer is in the way. Approximate that with a cast from the item
                // towards the centre line, which is where the player will be.
                Vector3 toward = p.resolved.centerline - p.resolved.position;
                if (toward.sqrMagnitude > 1e-4f &&
                    context.Shell.Raycast(new Ray(p.resolved.position, toward.normalized),
                        out RaycastHit blocker, MinPickupApproachMeters))
                {
                    report.failures.Add($"C4 {p.placement.id}: rock {blocker.distance:0.##} m towards the " +
                                        $"swim line blocks the approach (need {MinPickupApproachMeters} m clear)");
                }

                // C5 - how far off the swim line is it?
                float offLine = Vector3.Distance(p.resolved.position, p.resolved.centerline);
                if (offLine > MaxPickupCentrelineMeters)
                {
                    report.failures.Add($"C5 {p.placement.id}: {offLine:0.#} m from the centre line " +
                                        $"(max {MaxPickupCentrelineMeters}); nobody swims that far off route " +
                                        "for a consumable");
                }
                else
                {
                    report.detail.Add($"    {p.placement.id}: {offLine:0.#} m off line, " +
                                      $"{clearance:0.##} m clear");
                }
            }
        }

        // ---------------------------------------------------------------- C6

        /// <summary>
        /// Portals are holes in the wall where a branch meets the main route. A cast aimed across one
        /// either misses entirely or lands on the far side of the junction, so a placement inside a
        /// portal window is authored against geometry that is not there.
        ///
        /// Half-sizes are read from the scene's CaveRoute.portals rather than hard-coded, because Z4's
        /// angular half-size is 51 degrees against 30 for the other two and that asymmetry is authored
        /// data, not a constant anyone should be retyping.
        /// </summary>
        private static void CheckPortals(Report report, CaveDecorContext context, List<Placed> placed)
        {
            if (!context.TryGetRoute("MainRoute", out CaveRoute route, out CaveRoutePolyline polyline, out _))
                return;

            IReadOnlyList<CavePortalDefinition> portals = route.Portals;
            if (portals == null || portals.Count == 0)
                return;

            foreach (Placed p in placed)
            {
                if (p.placement.routeId != "MainRoute" || p.placement.anchor == CaveItemAnchor.SubmarineInterior)
                    continue;

                foreach (CavePortalDefinition portal in portals)
                {
                    if (portal.mainDistanceMeters < 0f)
                        continue;

                    float longitudinal = Mathf.Abs(p.placement.routeDistance - portal.mainDistanceMeters);
                    if (longitudinal >= portal.longitudinalHalfSize + PortalKeepOutMarginMeters)
                        continue;

                    polyline.Sample(portal.mainDistanceMeters, out _, out Vector3 tangent, out _);
                    CaveDecorProjector.BuildAxisFrame(tangent, out Vector3 up, out _);
                    Vector3 flatDirection = Vector3.ProjectOnPlane(portal.direction, tangent);
                    if (flatDirection.sqrMagnitude < 1e-6f)
                        continue;

                    float portalAngle = Vector3.SignedAngle(up, flatDirection.normalized, tangent);
                    float delta = Mathf.Abs(Mathf.DeltaAngle(p.placement.angleDegrees, portalAngle));
                    if (delta >= portal.angularHalfSize + PortalKeepOutMarginMeters)
                        continue;

                    report.failures.Add($"C6 {p.placement.id}: inside the {portal.zoneId} portal window " +
                                        $"(d {longitudinal:0.#} m of {portal.longitudinalHalfSize + PortalKeepOutMarginMeters:0.#}, " +
                                        $"angle {delta:0.#} of {portal.angularHalfSize + PortalKeepOutMarginMeters:0.#})");
                }
            }
        }

        // ---------------------------------------------------------------- C7

        private static void CheckHazardKeepOut(Report report, CaveDecorContext context, List<Placed> placed)
        {
            var rocks = new List<Vector3>();
            var vents = new List<(Vector3 position, float scale)>();

            foreach (CaveHazardStation station in CaveHazardLayout.Build())
            {
                foreach (CaveHazardInstance instance in station.instances)
                {
                    if (!CaveDecorProjector.TryCast(context, instance.routeId, instance.routeDistance,
                            instance.angleDegrees, out CaveDecorSurface surface))
                        continue;

                    Vector3 position = surface.point + surface.normal * instance.surfaceOffset;
                    if (instance.kind == CaveHazardKind.Vent)
                        vents.Add((position, instance.scale));
                    else
                        rocks.Add(position);
                }
            }

            report.detail.Add($"    hazard mounts re-derived from CaveHazardLayout: " +
                              $"{rocks.Count} rock spawners, {vents.Count} vents");

            foreach (Placed p in placed)
            {
                if (p.placement.anchor == CaveItemAnchor.SubmarineInterior)
                    continue;

                float radius = p.species.clearanceRadius;

                foreach (Vector3 rock in rocks)
                {
                    // The danger is the column of falling rock, not the spawner: horizontal distance
                    // inside the column, and anywhere from the spawner down to the drop limit.
                    Vector3 delta = p.resolved.position - rock;
                    float horizontal = new Vector2(delta.x, delta.z).magnitude;
                    if (horizontal > 0.45f + radius + HazardKeepOutMeters)
                        continue;
                    if (delta.y > 0.5f || delta.y < -RockColumnClearMeters)
                        continue;

                    report.failures.Add($"C7 {p.placement.id}: inside a falling-rock column " +
                                        $"({horizontal:0.##} m across, {-delta.y:0.##} m below the spawner)");
                }

                foreach ((Vector3 position, float scale) in vents)
                {
                    float ventRadius = VentCapsuleRadius * scale + radius + HazardKeepOutMeters;
                    Vector3 delta = p.resolved.position - position;
                    float horizontal = new Vector2(delta.x, delta.z).magnitude;
                    if (horizontal > ventRadius)
                        continue;
                    if (delta.y < -0.5f || delta.y > VentCapsuleHeight * scale)
                        continue;

                    report.failures.Add($"C7 {p.placement.id}: inside a vent plume " +
                                        $"({horizontal:0.##} m across, {delta.y:0.##} m up)");
                }
            }
        }

        // ---------------------------------------------------------------- C8

        private static void CheckSeparation(Report report, List<Placed> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    // The tornado pair rule is stricter and handled separately; skip it here so one
                    // overlap is not reported twice under two different names.
                    if (placed[i].placement.kind == CaveItemKind.Tornado &&
                        placed[j].placement.kind == CaveItemKind.Tornado)
                        continue;

                    float needed = MinItemSeparationMeters +
                                   placed[i].species.clearanceRadius + placed[j].species.clearanceRadius;
                    float actual = Vector3.Distance(placed[i].resolved.position, placed[j].resolved.position);
                    if (actual < needed)
                    {
                        report.failures.Add($"C8 {placed[i].placement.id} and {placed[j].placement.id} are " +
                                            $"{actual:0.##} m apart, need {needed:0.##}");
                    }
                }
            }
        }

        // ---------------------------------------------------------------- C8b

        /// <summary>
        /// Items must not be inside a decor prop. 392 of them are scattered across the same surfaces
        /// this tool casts onto, and a tank swallowed by a boulder is invisible and un-pickable.
        ///
        /// Read from the scene's CaveDecorInstance markers, never from CaveDecorSet. When the hazard
        /// pass hit this same problem it solved it by DELETING the offending decor records, which is
        /// cumulative, un-undoable, and why the decor set now holds 392 placements instead of 425. The
        /// decor is not this tool's to edit: an overlap is reported, and the item moves.
        ///
        /// What is GATED is the thing the game actually tests: PlayerInteractor drops a candidate when
        /// its obstruction raycast against the Default layer hits anything that is not the candidate
        /// itself. Decor sits on that layer and 238 of the props carry solid colliders, so a boulder
        /// between the player and a tank makes the tank permanently un-pickable. That is reproduced here
        /// literally, with a real Physics query against real colliders.
        ///
        /// What is only REPORTED is bounding-box overlap. A first version of this check gated on it and
        /// produced 21 failures, most of them urchins reading as 0.50 m inside a rock - which is the
        /// urchin's own clearance radius, i.e. its position was inside the rock's AABB. That is exactly
        /// where an urchin belongs. Renderer bounds are axis-aligned boxes, so a jagged spire claims a
        /// volume several times its own, and gating on it would push every prop into open water for no
        /// gameplay reason. The numbers are still printed, because a pickup deep inside a box is worth
        /// looking at in the review capture even when it is technically reachable.
        /// </summary>
        private static void CheckDecorOverlap(Report report, List<Placed> placed)
        {
            var decor = new List<(string name, Bounds bounds)>();
            foreach (CaveDecorInstance marker in
                     Object.FindObjectsByType<CaveDecorInstance>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (marker == null)
                    continue;

                bool has = false;
                var bounds = new Bounds();
                foreach (Renderer renderer in marker.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    if (!has) { bounds = renderer.bounds; has = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }

                if (has)
                    decor.Add((marker.gameObject.name, bounds));
            }

            report.detail.Add($"    decor props read from the scene: {decor.Count} " +
                              "(CaveDecorInstance markers; the decor asset is never opened)");

            foreach (Placed p in placed)
            {
                if (p.placement.anchor == CaveItemAnchor.SubmarineInterior || p.instance == null)
                    continue;

                // Reported, never gated: how deep inside a decor bounding box this sits.
                //
                // Bounds.ClosestPoint returns the query point unchanged when it is inside, so distance
                // is 0 for every containing box and cannot rank them. Penetration is the smallest
                // distance to a face instead, which is the depth a nudge would have to overcome.
                float deepest = 0f;
                string deepestName = null;
                foreach ((string name, Bounds bounds) in decor)
                {
                    if (!bounds.Contains(p.resolved.position))
                        continue;

                    Vector3 delta = p.resolved.position - bounds.center;
                    Vector3 extents = bounds.extents;
                    float penetration = Mathf.Min(extents.x - Mathf.Abs(delta.x),
                        Mathf.Min(extents.y - Mathf.Abs(delta.y), extents.z - Mathf.Abs(delta.z)));

                    if (penetration > deepest)
                    {
                        deepest = penetration;
                        deepestName = name;
                    }
                }
                if (deepestName != null && deepest > 0.3f && p.species.isPickup)
                {
                    report.warnings.Add($"{p.placement.id} sits {deepest:0.##} m inside the bounds of " +
                                        $"'{deepestName}'. Bounds are axis-aligned so this may be nothing, " +
                                        "but check it in the review capture.");
                }

                if (!p.species.isPickup)
                    continue;

                // Gated: the real obstruction test. Cast from the item towards the swim line against
                // every collider, then discard hits belonging to the item itself - which is precisely
                // what PlayerInteractor does before it will offer the pickup.
                Vector3 toward = p.resolved.centerline - p.resolved.position;
                if (toward.sqrMagnitude < 1e-4f)
                    continue;

                RaycastHit[] hits = Physics.RaycastAll(p.resolved.position, toward.normalized,
                    MinPickupApproachMeters, 1 << 0, QueryTriggerInteraction.Ignore);

                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider == null || hit.collider.transform.IsChildOf(p.instance.transform))
                        continue;
                    if (hit.collider.gameObject.name == CaveDecorNames.ShellObject)
                        continue; // already reported by C4

                    report.failures.Add($"C8b {p.placement.id}: '{hit.collider.name}' blocks the approach " +
                                        $"{hit.distance:0.##} m towards the swim line, so PlayerInteractor " +
                                        "would never offer this pickup");
                }
            }
        }

        // ---------------------------------------------------------------- C9

        private static void CheckTornadoes(Report report, List<Placed> placed)
        {
            var tornadoes = placed.FindAll(p => p.placement.kind == CaveItemKind.Tornado);

            for (int i = 0; i < tornadoes.Count; i++)
            {
                for (int j = i + 1; j < tornadoes.Count; j++)
                {
                    float actual = Vector3.Distance(tornadoes[i].resolved.position,
                        tornadoes[j].resolved.position);
                    if (actual < MinTornadoSeparationMeters)
                    {
                        report.failures.Add($"C9 {tornadoes[i].placement.id} and {tornadoes[j].placement.id} " +
                                            $"are {actual:0.#} m apart, need {MinTornadoSeparationMeters} " +
                                            "- their pull fields would merge into one inescapable well");
                    }
                }

                GameObject instance = tornadoes[i].instance;
                if (instance == null)
                    continue;

                var whirlpool = instance.GetComponent<Whirlpool>();
                if (whirlpool == null)
                {
                    report.failures.Add($"C9 {tornadoes[i].placement.id}: no Whirlpool component");
                    continue;
                }

                // Whirlpool.GetPullForce divides by (outer - inner). At zero or negative it returns
                // maxPullForce for every distance, and the guard comment in that file records that an
                // inspector slip here NaNs the player's Rigidbody and removes them from the world with
                // no in-session recovery. Cheap to assert, catastrophic to miss.
                if (whirlpool.innerRadius >= whirlpool.outerRadius)
                {
                    report.failures.Add($"C9 {tornadoes[i].placement.id}: innerRadius " +
                                        $"{whirlpool.innerRadius} >= outerRadius {whirlpool.outerRadius} " +
                                        "- GetPullForce would NaN the player's Rigidbody");
                }
            }
        }

        // ---------------------------------------------------------------- C10

        /// <summary>
        /// How wide a gap the player can steer through past the Z6 climax, measured the way
        /// CaveHazardValidator measures it: a polar grid of free and blocked samples across the section,
        /// then the largest free-sample-to-obstruction radius. Sampled rather than solved because the
        /// shell is noisy, so the cross-section is not an ellipse.
        ///
        /// Unlike the hazard version, every obstacle is live at once. Sharks and whirlpools have no
        /// alternating pattern to exploit - they are all always there.
        /// </summary>
        private static void CheckDodgeChannel(Report report, CaveDecorContext context, List<Placed> placed)
        {
            var climax = placed.FindAll(p => p.placement.anchor == CaveItemAnchor.Centerline);
            if (climax.Count == 0)
                return;

            CaveRoutePolyline polyline = context.GetPolyline("MainRoute");
            if (polyline == null)
                return;

            const int angleSteps = 72;
            const float radiusStep = 0.5f;

            foreach (Placed station in climax)
            {
                polyline.Sample(station.placement.routeDistance, out Vector3 centre,
                    out Vector3 tangent, out _);
                CaveDecorProjector.BuildAxisFrame(tangent, out Vector3 up, out _);

                var free = new List<Vector3>();
                var blocked = new List<Vector3>();

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
                        (IsBlocked(point, climax) ? blocked : free).Add(point);
                    }
                }

                if (free.Count == 0)
                {
                    report.failures.Add($"C10 {station.placement.id}: the section is fully blocked");
                    continue;
                }

                float best = 0f;
                foreach (Vector3 point in free)
                {
                    float nearest = float.PositiveInfinity;
                    foreach (Vector3 hitPoint in blocked)
                        nearest = Mathf.Min(nearest, Vector3.Distance(point, hitPoint));

                    Vector3 outward = point - centre;
                    if (outward.sqrMagnitude > 1e-4f &&
                        context.Shell.Raycast(new Ray(point, outward.normalized), out RaycastHit wallHit, 200f))
                        nearest = Mathf.Min(nearest, wallHit.distance);

                    best = Mathf.Max(best, nearest);
                }

                float channel = best * 2f;
                report.detail.Add($"    {station.placement.id}: dodge channel {channel:0.#} m");
                if (channel < MinDodgeChannelMeters)
                {
                    report.failures.Add($"C10 {station.placement.id}: dodge channel {channel:0.#} m " +
                                        $"< {MinDodgeChannelMeters} m");
                }
            }
        }

        private static bool IsBlocked(Vector3 point, List<Placed> obstacles)
        {
            foreach (Placed obstacle in obstacles)
            {
                if (Vector3.Distance(point, obstacle.resolved.position) <= obstacle.species.clearanceRadius)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- C12

        private static void CheckCounts(Report report, IReadOnlyList<CaveItemPlacement> placements)
        {
            var perZone = new Dictionary<string, (int oxygen, int octopus, int urchin)>();
            var extras = new Dictionary<(CaveItemKind, string), int>();

            foreach (CaveItemPlacement placement in placements)
            {
                switch (placement.kind)
                {
                    case CaveItemKind.OxygenTank:
                    case CaveItemKind.Octopus:
                    case CaveItemKind.Urchin:
                    {
                        perZone.TryGetValue(placement.zoneId, out var current);
                        if (placement.kind == CaveItemKind.OxygenTank) current.oxygen++;
                        else if (placement.kind == CaveItemKind.Octopus) current.octopus++;
                        else current.urchin++;
                        perZone[placement.zoneId] = current;
                        break;
                    }
                    default:
                    {
                        var key = (placement.kind, placement.zoneId);
                        extras.TryGetValue(key, out int n);
                        extras[key] = n + 1;
                        break;
                    }
                }
            }

            foreach (var expected in CaveItemLayout.ZoneQuota)
            {
                perZone.TryGetValue(expected.Key, out var actual);
                int total = actual.oxygen + actual.octopus + actual.urchin;
                int wanted = expected.Value.oxygen + expected.Value.octopus + expected.Value.urchin;

                report.detail.Add($"    {expected.Key}: 산소통 {actual.oxygen} 문어 {actual.octopus} " +
                                  $"성게 {actual.urchin} = {total}");

                if (total != wanted || actual != expected.Value)
                {
                    report.failures.Add($"C12 {expected.Key}: got " +
                                        $"({actual.oxygen}, {actual.octopus}, {actual.urchin}) = {total}, " +
                                        $"expected ({expected.Value.oxygen}, {expected.Value.octopus}, " +
                                        $"{expected.Value.urchin}) = {wanted}");
                }
            }

            foreach (string zoneId in perZone.Keys)
            {
                if (!CaveItemLayout.ZoneQuota.ContainsKey(zoneId))
                    report.failures.Add($"C12 unexpected zone '{zoneId}' in the quota family");
            }

            CheckExtra(report, extras, CaveItemKind.Rope, "Z2", 1);
            CheckExtra(report, extras, CaveItemKind.Rope, "Z5", 1);
            CheckExtra(report, extras, CaveItemKind.Gun, "Z2", 1);
            CheckExtra(report, extras, CaveItemKind.Hammer, "Submarine", 1);
            CheckExtra(report, extras, CaveItemKind.Shark, "Z6", 3);
            CheckExtra(report, extras, CaveItemKind.Tornado, "Z6", 3);

            foreach (var pair in extras)
            {
                if (pair.Value != 0)
                    report.failures.Add($"C12 unexpected {pair.Key.Item2} {pair.Key.Item1} x{pair.Value}");
            }
        }

        private static void CheckExtra(Report report,
            Dictionary<(CaveItemKind, string), int> extras, CaveItemKind kind, string zoneId, int expected)
        {
            extras.TryGetValue((kind, zoneId), out int actual);
            if (actual != expected)
                report.failures.Add($"C12 {zoneId} {kind}: {actual}, expected {expected}");
            extras[(kind, zoneId)] = 0;
        }

        // ---------------------------------------------------------------- C13

        /// <summary>
        /// Z4 is the total blackout zone. The design rule the decor validator also enforces is that it
        /// carries no emissive material and no light of any kind, because the flashlight being the only
        /// light source is the zone's entire mechanic.
        /// </summary>
        private static void CheckDarkZone(Report report, List<Placed> placed)
        {
            foreach (Placed p in placed)
            {
                if (p.placement.zoneId != "Z4" || p.instance == null)
                    continue;

                foreach (Light light in p.instance.GetComponentsInChildren<Light>(true))
                {
                    if (light != null)
                        report.failures.Add($"C13 {p.placement.id}: carries a {light.type} light into Z4");
                }

                foreach (Renderer renderer in p.instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null || !material.HasProperty("_EmissionColor"))
                            continue;
                        if (material.GetColor("_EmissionColor").maxColorComponent > 0.01f)
                        {
                            report.failures.Add($"C13 {p.placement.id}: emissive material " +
                                                $"'{material.name}' in the blackout zone");
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- C14

        private static void CheckSubmarineKeepOut(Report report, List<Placed> placed, Transform submarine)
        {
            if (submarine == null)
                return;

            foreach (Placed p in placed)
            {
                bool hostile = p.placement.kind == CaveItemKind.Octopus ||
                               p.placement.kind == CaveItemKind.Urchin ||
                               p.placement.kind == CaveItemKind.Shark ||
                               p.placement.kind == CaveItemKind.Tornado;
                if (!hostile)
                    continue;

                float distance = Vector3.Distance(p.resolved.position, submarine.position);
                if (distance < SubmarineKeepOutMeters)
                {
                    report.failures.Add($"C14 {p.placement.id}: {distance:0.#} m from the submarine " +
                                        $"(need {SubmarineKeepOutMeters}); players spawn inside it");
                }
            }
        }

        // ---------------------------------------------------------------- C16

        private static void CheckComponentContract(Report report, List<Placed> placed)
        {
            foreach (Placed p in placed)
            {
                if (p.instance == null)
                    continue;

                if (p.species.isPickup && p.instance.layer != CaveItemCatalog.InteractionLayer)
                {
                    report.failures.Add($"C16 {p.placement.id}: layer {p.instance.layer}, but " +
                                        $"PlayerInteractor only scans layer {CaveItemCatalog.InteractionLayer}");
                }

                if (p.species.isPickup && p.instance.GetComponent<Collider>() == null)
                    report.failures.Add($"C16 {p.placement.id}: no Collider, so OverlapSphere never finds it");
            }
        }

        // ---------------------------------------------------------------- report

        public static string Format(Report report)
        {
            var text = new StringBuilder();
            text.AppendLine("===== CAVE ITEM VALIDATION =====");
            text.AppendLine($"placements {report.placements}, resolved {report.resolved}");

            if (report.detail.Count > 0)
            {
                text.AppendLine("-- measurements --");
                foreach (string line in report.detail)
                    text.AppendLine(line);
            }

            if (report.warnings.Count > 0)
            {
                text.AppendLine("-- warnings --");
                foreach (string line in report.warnings)
                    text.AppendLine("  WARN " + line);
            }

            if (report.failures.Count > 0)
            {
                text.AppendLine("-- failures --");
                foreach (string line in report.failures)
                    text.AppendLine("  FAIL " + line);
            }

            text.AppendLine($"CAVE_ITEM_VALIDATION {(report.Passed ? "PASS" : "FAIL")} " +
                            $"placements={report.placements} resolved={report.resolved} " +
                            $"failures={report.failures.Count} warnings={report.warnings.Count}");
            return text.ToString();
        }
    }
}
