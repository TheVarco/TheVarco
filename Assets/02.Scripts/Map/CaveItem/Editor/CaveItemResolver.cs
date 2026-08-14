using CaveBlockout;
using CaveBlockout.Decor;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Turns a placement record into a world transform. The single implementation shared by the spawner,
    /// the validator and the review capture, for the same reason CaveDecorProjector is shared by the
    /// brush, the scatter and the rebuild: if authoring and validation each did their own arithmetic,
    /// "the validator passed" would stop meaning "what the spawner wrote is correct".
    /// </summary>
    public static class CaveItemResolver
    {
        private const string HammerZoneName = "HammerZone";

        /// <summary>
        /// How much daylight a Centerline body needs between itself and the wall.
        ///
        /// A Surface placement fails loudly when its cast misses, which makes bad geometry obvious. A
        /// Centerline placement has no cast to fail on - it is just centreline plus an offset - so a
        /// 14 m whirlpool or a 14 m shark can be authored entirely inside solid rock and every
        /// resolve-count check in the tool will still report success. This margin plus the containment
        /// cast in <see cref="TryResolve"/> is what closes that hole.
        /// </summary>
        public const float CenterlineWallMarginMeters = 2f;

        public readonly struct Result
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            /// <summary>Populated for Surface anchors. Default for the other two.</summary>
            public readonly CaveDecorSurface surface;

            /// <summary>Centre line point this placement was measured from. Zero for the submarine anchor.</summary>
            public readonly Vector3 centerline;

            /// <summary>Distance from the anchor to the shell along the placement angle. Centerline anchors only.</summary>
            public readonly float wallDistance;

            public Result(Vector3 position, Quaternion rotation, CaveDecorSurface surface,
                Vector3 centerline, float wallDistance)
            {
                this.position = position;
                this.rotation = rotation;
                this.surface = surface;
                this.centerline = centerline;
                this.wallDistance = wallDistance;
            }
        }

        /// <summary>
        /// False means the placement has no valid world transform and the caller must report it, never
        /// invent one. <paramref name="failure"/> carries the reason for the report.
        /// </summary>
        public static bool TryResolve(CaveDecorContext context, CaveItemPlacement placement,
            Transform submarine, out Result result, out string failure)
        {
            result = default;
            failure = null;

            CaveItemCatalog.Species species = CaveItemCatalog.Get(placement.kind);
            if (species == null)
            {
                failure = $"no catalog entry for {placement.kind}";
                return false;
            }

            switch (placement.anchor)
            {
                case CaveItemAnchor.Surface:
                    return TryResolveSurface(context, placement, species, out result, out failure);
                case CaveItemAnchor.Centerline:
                    return TryResolveCenterline(context, placement, species, out result, out failure);
                case CaveItemAnchor.SubmarineInterior:
                    return TryResolveInterior(placement, submarine, out result, out failure);
                default:
                    failure = $"unhandled anchor {placement.anchor}";
                    return false;
            }
        }

        private static bool TryResolveSurface(CaveDecorContext context, CaveItemPlacement placement,
            CaveItemCatalog.Species species, out Result result, out string failure)
        {
            result = default;
            failure = null;

            if (!CaveDecorProjector.TryCast(context, placement.routeId, placement.routeDistance,
                    placement.angleDegrees, out CaveDecorSurface surface))
            {
                failure = $"no surface on {placement.routeId} at d={placement.routeDistance:0.0} " +
                          $"a={placement.angleDegrees:0}";
                return false;
            }

            // Positive, unlike decor. Decor embeds props so they read as part of the rock; a pickup that
            // does the same is unobtainable, because PlayerInteractor's obstruction ray hits the shell
            // before it reaches the item and drops the candidate with no feedback at all.
            Vector3 position = surface.point + surface.normal * placement.surfaceOffset;
            Quaternion rotation = BuildRotation(placement, species, surface, surface.tangent);
            result = new Result(position, rotation, surface, surface.centerline, surface.CenterlineDistance);
            return true;
        }

        private static bool TryResolveCenterline(CaveDecorContext context, CaveItemPlacement placement,
            CaveItemCatalog.Species species, out Result result, out string failure)
        {
            result = default;
            failure = null;

            if (!context.TryGetRoute(placement.routeId, out CaveRoute route,
                    out CaveRoutePolyline polyline, out _))
            {
                failure = $"route '{placement.routeId}' is not bound in the context";
                return false;
            }

            polyline.Sample(placement.routeDistance, out Vector3 centre, out Vector3 tangent,
                out float parameter);
            CaveDecorProjector.BuildAxisFrame(tangent, out Vector3 up, out _);

            // Same polar convention as TryCast, so an angle means the same thing in both anchor modes.
            Vector3 direction = Quaternion.AngleAxis(placement.angleDegrees, tangent) * up;
            Vector3 position = centre + direction * placement.lateralOffset;

            // Containment. Collider.Raycast against the held shell, never Physics.Raycast - decor,
            // hazards and items all sit on layers a masked scene query would happily return as
            // "the cave surface" (CaveDecorContext documents this at its Shell property).
            float maxCast = CaveDecorProjector.MaxCastDistance(route, polyline.SplineIndex, parameter);
            if (!context.Shell.Raycast(new Ray(centre, direction), out RaycastHit hit, maxCast))
            {
                failure = $"no shell on {placement.routeId} at d={placement.routeDistance:0.0} " +
                          $"a={placement.angleDegrees:0} - portal or cap end";
                return false;
            }

            float needed = placement.lateralOffset + species.clearanceRadius + CenterlineWallMarginMeters;
            if (needed > hit.distance)
            {
                failure = $"body would reach the wall: lateral {placement.lateralOffset:0.0} + radius " +
                          $"{species.clearanceRadius:0.0} + margin {CenterlineWallMarginMeters:0.0} = " +
                          $"{needed:0.0} m against {hit.distance:0.0} m of rock";
                return false;
            }

            Quaternion rotation = BuildRotation(placement, species, default, tangent);
            result = new Result(position, rotation, default, centre, hit.distance);
            return true;
        }

        private static bool TryResolveInterior(CaveItemPlacement placement, Transform submarine,
            out Result result, out string failure)
        {
            result = default;
            failure = null;

            if (submarine == null)
            {
                failure = "no 'Submarine_final' in the scene, so the interior anchor has no origin";
                return false;
            }

            Vector3 position;
            if (placement.kind == CaveItemKind.Hammer)
            {
                Transform hammerZone = FindDescendant(submarine, HammerZoneName);
                if (hammerZone == null)
                {
                    failure = $"no '{HammerZoneName}' under the submarine";
                    return false;
                }

                BoxCollider spawnCollider = hammerZone.GetComponentInChildren<BoxCollider>(true);
                if (spawnCollider == null)
                {
                    failure = $"'{HammerZoneName}' has no BoxCollider in its hierarchy";
                    return false;
                }

                // Respect the collider's Center as well as every parent transform and scale.
                position = spawnCollider.transform.TransformPoint(spawnCollider.center);
            }
            else
            {
                // World-axis offset rather than TransformPoint: the submarine sits unrotated and at scale 2,
                // and a local offset would silently double every distance the layout states in metres.
                position = submarine.position + placement.interiorOffset;
            }

            Quaternion rotation = Quaternion.Euler(0f, placement.yawDegrees, 0f);
            result = new Result(position, rotation, default, submarine.position, 0f);
            return true;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                    return candidate;
            }

            return null;
        }

        private static Quaternion BuildRotation(CaveItemPlacement placement,
            CaveItemCatalog.Species species, CaveDecorSurface surface, Vector3 tangent)
        {
            Quaternion yaw = Quaternion.Euler(0f, placement.yawDegrees, 0f);

            switch (placement.orientation)
            {
                case CaveItemOrientation.SurfaceFrame:
                {
                    // Blend upright against normal-aligned exactly the way
                    // CaveDecorProjector.BuildRandomSurfaceRotation does, and for the same reason: a prop
                    // that slavishly follows a 40-degree wall normal reads as glued to the wall rather
                    // than resting against it.
                    Vector3 uprightForward = Vector3.ProjectOnPlane(tangent, Vector3.up);
                    if (uprightForward.sqrMagnitude < 1e-6f)
                        uprightForward = Vector3.forward;
                    Quaternion upright = Quaternion.LookRotation(uprightForward.normalized, Vector3.up);
                    Quaternion blended = Quaternion.Slerp(upright, surface.frame,
                        Mathf.Clamp01(species.normalAlignment));
                    return blended * yaw;
                }

                case CaveItemOrientation.AlongTunnel:
                {
                    Vector3 flat = Vector3.ProjectOnPlane(tangent, Vector3.up);
                    if (flat.sqrMagnitude < 1e-6f)
                        flat = Vector3.forward;
                    return Quaternion.LookRotation(flat.normalized, Vector3.up) * yaw;
                }

                default:
                    return yaw;
            }
        }
    }
}
