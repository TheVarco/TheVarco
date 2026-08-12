using System;
using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>A point on the cave shell plus the frame decor is oriented in there.</summary>
    public readonly struct CaveDecorSurface
    {
        public readonly Vector3 point;
        public readonly Vector3 normal;
        public readonly Vector3 tangent;
        public readonly Vector3 centerline;
        public readonly Quaternion frame;
        public readonly CaveSurfaceKind kind;

        public CaveDecorSurface(Vector3 point, Vector3 normal, Vector3 tangent, Vector3 centerline,
            Quaternion frame, CaveSurfaceKind kind)
        {
            this.point = point;
            this.normal = normal;
            this.tangent = tangent;
            this.centerline = centerline;
            this.frame = frame;
            this.kind = kind;
        }

        /// <summary>How far this surface point sits from the route centre line, in metres.</summary>
        public float CenterlineDistance => Vector3.Distance(point, centerline);
    }

    /// <summary>
    /// Converts between route-relative placement records and world transforms, in both directions.
    ///
    /// Everything that touches decor geometry goes through here: the brush, the auto-scatter, the
    /// rebuild and the validator. Sharing one implementation is what makes "rebuild after regenerating
    /// the blockout" produce the same answer the brush produced when the art was authored.
    /// </summary>
    public static class CaveDecorProjector
    {
        /// <summary>Above this the normal reads as floor, below its negative as ceiling, between as wall.</summary>
        public const float SurfaceNormalThreshold = 0.5f;

        public static CaveSurfaceKind Classify(Vector3 normal)
        {
            if (normal.y > SurfaceNormalThreshold)
                return CaveSurfaceKind.Floor;
            if (normal.y < -SurfaceNormalThreshold)
                return CaveSurfaceKind.Ceiling;
            return CaveSurfaceKind.Wall;
        }

        /// <summary>
        /// Reference frame around the tunnel axis. <paramref name="up"/> is the direction an angle of
        /// zero casts in, so zero points at the ceiling and 180 at the floor.
        /// </summary>
        public static void BuildAxisFrame(Vector3 tangent, out Vector3 up, out Vector3 right)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;

            right = Vector3.Cross(tangent, reference).normalized;
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(tangent, Vector3.right).normalized;

            up = Vector3.Cross(right, tangent).normalized;
        }

        /// <summary>
        /// Casts from the route centre line towards the shell. Returns false when the ray misses,
        /// which happens at portal openings and cap ends where there is simply no surface in that
        /// direction - the caller should drop the candidate rather than invent a position.
        /// </summary>
        public static bool TryCast(CaveDecorContext context, string routeId, float routeDistance,
            float angleDegrees, out CaveDecorSurface surface)
        {
            surface = default;
            if (context == null || context.Shell == null)
                return false;
            if (!context.TryGetRoute(routeId, out CaveRoute route, out CaveRoutePolyline polyline, out _))
                return false;

            polyline.Sample(routeDistance, out Vector3 origin, out Vector3 tangent, out float parameter);
            BuildAxisFrame(tangent, out Vector3 up, out _);
            Vector3 direction = Quaternion.AngleAxis(angleDegrees, tangent) * up;

            float maxDistance = MaxCastDistance(route, polyline.SplineIndex, parameter);
            if (!context.Shell.Raycast(new Ray(origin, direction), out RaycastHit hit, maxDistance))
                return false;

            Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : -direction;
            surface = new CaveDecorSurface(hit.point, normal, tangent, origin, BuildSurfaceFrame(tangent, normal),
                Classify(normal));
            return true;
        }

        /// <summary>
        /// Ray length for a cast from the centre line. Derived from the authored tunnel profile rather
        /// than a constant: Z5 is 70 m wide and Z7 is 85 m, so any fixed value short enough to be
        /// sensible in the 18 m Z4 fault would miss the floor entirely in the big chambers.
        /// </summary>
        public static float MaxCastDistance(CaveRoute route, int splineIndex, float parameter)
        {
            float width = route.EvaluateWidth(splineIndex, parameter);
            float height = route.EvaluateHeight(splineIndex, parameter);
            return Mathf.Max(width, height) * 1.1f + 6f;
        }

        /// <summary>
        /// Orientation basis at a surface point: forward follows the tunnel, up is the surface normal.
        /// A prop's authored lean is stored relative to this, so the lean is preserved when the wall
        /// underneath it moves during a blockout rebuild.
        /// </summary>
        public static Quaternion BuildSurfaceFrame(Vector3 tangent, Vector3 normal)
        {
            Vector3 forward = Vector3.ProjectOnPlane(tangent, normal);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(Vector3.right, normal);
            return Quaternion.LookRotation(forward.normalized, normal);
        }

        /// <summary>Record to world transform. False means the surface is gone and the record should be reported, not silently skipped.</summary>
        public static bool TryResolve(CaveDecorContext context, CaveDecorPlacement placement,
            out Vector3 position, out Quaternion rotation, out CaveDecorSurface surface)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            surface = default;

            if (placement == null ||
                !TryCast(context, placement.routeId, placement.routeDistance, placement.angleDegrees, out surface))
                return false;

            position = surface.point + surface.normal * placement.surfaceOffset;
            rotation = surface.frame * placement.surfaceRotation;
            return true;
        }

        /// <summary>
        /// World point to record. Used by the brush for every stroke and by the adoption step that
        /// converts the hand-placed Z1 entrance dressing into records.
        ///
        /// <paramref name="surfaceOffset"/> is derived from a second cast rather than assumed, so
        /// re-resolving the returned values reproduces <paramref name="worldPoint"/> exactly. That
        /// property is what makes adoption a no-op rather than a silent nudge of authored art.
        /// </summary>
        public static bool TryUnproject(CaveDecorContext context, Vector3 worldPoint,
            out string routeId, out float routeDistance, out float angleDegrees, out float surfaceOffset,
            out CaveDecorSurface surface)
        {
            routeId = null;
            routeDistance = 0f;
            angleDegrees = 0f;
            surfaceOffset = 0f;
            surface = default;

            if (context == null || !context.IsValid)
                return false;

            routeId = context.FindNearestRouteId(worldPoint, out routeDistance, out _);
            if (routeId == null)
                return false;

            CaveRoutePolyline polyline = context.GetPolyline(routeId);
            polyline.Sample(routeDistance, out Vector3 origin, out Vector3 tangent, out _);

            Vector3 offset = Vector3.ProjectOnPlane(worldPoint - origin, tangent);
            if (offset.sqrMagnitude < 1e-6f)
                return false;

            BuildAxisFrame(tangent, out Vector3 up, out _);
            angleDegrees = Vector3.SignedAngle(up, offset.normalized, tangent);

            if (!TryCast(context, routeId, routeDistance, angleDegrees, out surface))
                return false;

            surfaceOffset = Vector3.Dot(worldPoint - surface.point, surface.normal);
            return true;
        }

        /// <summary>
        /// Turns an authored world rotation into the surface-relative rotation stored on the record.
        /// </summary>
        public static Quaternion ToSurfaceRotation(CaveDecorSurface surface, Quaternion worldRotation)
        {
            return Quaternion.Inverse(surface.frame) * worldRotation;
        }

        /// <summary>
        /// Randomised orientation for a freshly placed prop, expressed in the surface frame.
        /// <see cref="CaveDecorPaletteEntry.normalAlignment"/> blends between standing on the surface
        /// normal and standing world-upright, because a boulder that slavishly follows a 40-degree
        /// wall normal reads as stuck to the wall rather than resting against it.
        /// </summary>
        public static Quaternion BuildRandomSurfaceRotation(CaveDecorSurface surface,
            CaveDecorPaletteEntry entry, System.Random random)
        {
            Quaternion aligned = surface.frame;

            Vector3 uprightForward = Vector3.ProjectOnPlane(surface.tangent, Vector3.up);
            if (uprightForward.sqrMagnitude < 1e-6f)
                uprightForward = Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(uprightForward.normalized, Vector3.up);

            Quaternion blended = Quaternion.Slerp(upright, aligned, Mathf.Clamp01(entry.normalAlignment));

            float yaw = (float)random.NextDouble() * 360f;
            float tilt = Mathf.Max(0f, entry.maxTiltDegrees);
            float tiltX = ((float)random.NextDouble() * 2f - 1f) * tilt;
            float tiltZ = ((float)random.NextDouble() * 2f - 1f) * tilt;

            Quaternion world = blended * Quaternion.Euler(tiltX, yaw, tiltZ);
            return ToSurfaceRotation(surface, world);
        }
    }
}
