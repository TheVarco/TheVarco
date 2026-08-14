using System.Collections.Generic;
using System.Linq;
using CaveBlockout;
using CaveBlockout.Decor;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Answers one question for the exterior builder: does this mesh occupy space the submarine needs?
    /// Two volumes are forbidden.
    ///
    /// 1. The CAVE CORRIDOR - the tunnel itself, tested as the real elliptical cross-section sampled
    ///    from the spline. A circular corridor using the larger half-extent would be far too
    ///    conservative here: around the exit the profile is 85 wide by 50 tall, so a circle of radius
    ///    42.5 would reject a peak 30 m overhead that the ellipse (half-height 25) clears comfortably,
    ///    and would shove the whole headland so far out that it stops framing a 24 m mouth at all.
    /// 2. The ESCAPE CORRIDOR - a cylinder projected forward from the mouth along the exit direction,
    ///    matching the mouth radius. Past the mouth the spline constrains nothing, but the sub is
    ///    still climbing through that column of water.
    ///
    /// TWO tests, because either alone gives false passes on this geometry:
    ///
    /// - <see cref="Intrusion"/> checks a POINT, used per mesh vertex. It catches small features
    ///   poking into the corridor, but says nothing about the space between vertices.
    /// - <see cref="IntersectsProbes"/> threads line probes down the length of both volumes and looks
    ///   for triangle crossings. This is the one that matters for the LPN packs: their meshes are
    ///   1.6 m tall natively and get scaled up roughly x60, so single triangles span 100 m and can lie
    ///   straight across a 15 m corridor with all three of their vertices comfortably outside it. The
    ///   vertex test passed LPN_Mountain_03 with 0 m of push while the review capture's independent
    ///   ray gate found it 5.4 m in front of the mouth.
    ///
    /// The cross-section is pre-sampled over a window at the end of the route rather than projected
    /// per query: the solver calls this once per vertex per push step, and CaveRoutePolyline.Project
    /// walks all ~580 segments of the full route on every call.
    /// </summary>
    internal sealed class ExteriorClearance
    {
        private const string MainRouteId = "MainRoute";

        /// <summary>
        /// How far back from the route end the cross-section window reaches. The headland sits within
        /// ~120 m of the mouth; 300 m leaves a wide margin, and <see cref="ClampedToWindowStart"/>
        /// reports if anything was ever tested against the window's own edge.
        /// </summary>
        private const float WindowMeters = 300f;
        private const float SampleSpacingMeters = 1f;

        /// <summary>Length of tunnel the line probes cover. Only the mouth end can host a peak.</summary>
        private const float ProbeWindowMeters = 150f;

        /// <summary>Spacing of the probe polyline vertices along the tunnel.</summary>
        private const float ProbeStepMeters = 5f;

        /// <summary>
        /// Fractions of the cross-section the probe rings sit at. 0.9 hugs the wall where a peak would
        /// first appear, 0.5 catches something that has come much further in, and the centre line
        /// catches a slab lying clean across the tunnel.
        /// </summary>
        private static readonly float[] ProbeRadii = { 0f, 0.5f, 0.9f };
        private const int ProbeSpokes = 8;

        private readonly struct Section
        {
            public readonly Vector3 centre;
            public readonly Vector3 tangent;
            public readonly Vector3 right;
            public readonly Vector3 up;
            public readonly float halfWidth;
            public readonly float halfHeight;

            public Section(Vector3 centre, Vector3 tangent, float halfWidth, float halfHeight)
            {
                this.centre = centre;
                this.tangent = tangent;

                Vector3 sideways = Vector3.Cross(tangent, Vector3.up);
                if (sideways.sqrMagnitude < 1e-6f)
                    sideways = Vector3.Cross(tangent, Vector3.forward);
                right = sideways.normalized;
                up = Vector3.Cross(right, tangent).normalized;

                this.halfWidth = halfWidth;
                this.halfHeight = halfHeight;
            }

            public Vector3 At(float radius, float angle) => centre
                + right * (halfWidth * radius * Mathf.Cos(angle))
                + up * (halfHeight * radius * Mathf.Sin(angle));
        }

        private readonly Section[] sections;
        private readonly Vector3 exitPosition;
        private readonly Vector3 exitDirection;
        private readonly float escapeRadius;
        private readonly float escapeLength;

        /// <summary>Line probes threading both forbidden volumes, as (start, end) world pairs.</summary>
        private readonly (Vector3 from, Vector3 to)[] probes;

        /// <summary>True if any query landed on the oldest sample, meaning the window may be too short.</summary>
        public bool ClampedToWindowStart { get; private set; }

        public bool HasCaveCorridor => sections != null && sections.Length >= 2;
        public int ProbeCount => probes?.Length ?? 0;

        private ExteriorClearance(Section[] sections, Vector3 exitPosition, Vector3 exitDirection,
            float escapeRadius, float escapeLength)
        {
            this.sections = sections;
            this.exitPosition = exitPosition;
            this.exitDirection = exitDirection.normalized;
            this.escapeRadius = escapeRadius;
            this.escapeLength = escapeLength;
            probes = BuildProbes();
        }

        /// <param name="shellMargin">
        /// Extra metres demanded beyond the nominal cross-section. The generated shell is displaced by
        /// the blockout preset's noise, so the real rock face sits some way off the nominal ellipse;
        /// this has to cover that displacement or a peak judged "just clear" still pokes through.
        /// </param>
        public static ExteriorClearance Create(Vector3 exitPosition, Vector3 exitDirection,
            float escapeRadius, float escapeLength, float shellMargin)
        {
            CaveRoute route = Object
                .FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.Definitions != null &&
                                             candidate.Definitions.Any(d => d != null && d.routeId == MainRouteId));
            if (route == null)
            {
                Debug.LogWarning($"EXTERIOR clearance: no CaveRoute carrying '{MainRouteId}' - " +
                                 "the cave-corridor test is DISABLED, only the escape cylinder applies");
                return new ExteriorClearance(null, exitPosition, exitDirection, escapeRadius, escapeLength);
            }

            CaveRouteSplineDefinition definition =
                route.Definitions.First(d => d != null && d.routeId == MainRouteId);
            CaveRoutePolyline polyline = CaveRoutePolyline.Build(route, definition.splineIndex);
            if (polyline == null || !polyline.IsValid)
            {
                Debug.LogWarning($"EXTERIOR clearance: '{MainRouteId}' spline would not build - " +
                                 "the cave-corridor test is DISABLED");
                return new ExteriorClearance(null, exitPosition, exitDirection, escapeRadius, escapeLength);
            }

            float end = polyline.Length;
            float start = Mathf.Max(0f, end - WindowMeters);
            int count = Mathf.Max(2, Mathf.CeilToInt((end - start) / SampleSpacingMeters) + 1);
            var sections = new Section[count];

            for (int i = 0; i < count; i++)
            {
                float distance = Mathf.Lerp(start, end, i / (float)(count - 1));
                polyline.Sample(distance, out Vector3 centre, out Vector3 tangent, out float parameter);
                sections[i] = new Section(centre, tangent,
                    route.EvaluateWidth(definition.splineIndex, parameter) * 0.5f + shellMargin,
                    route.EvaluateHeight(definition.splineIndex, parameter) * 0.5f + shellMargin);
            }

            var clearance = new ExteriorClearance(sections, exitPosition, exitDirection,
                escapeRadius, escapeLength);

            Debug.Log($"EXTERIOR clearance: {count} sections over the last {end - start:0.#} m of " +
                      $"'{MainRouteId}' (end profile {sections[count - 1].halfWidth * 2f:0.#} x " +
                      $"{sections[count - 1].halfHeight * 2f:0.#} m incl. {shellMargin:0.#} m margin), " +
                      $"escape cylinder r={escapeRadius:0.#} m over {escapeLength:0.#} m, " +
                      $"{clearance.ProbeCount} line probes");
            return clearance;
        }

        private (Vector3, Vector3)[] BuildProbes()
        {
            var built = new List<(Vector3, Vector3)>();

            // Escape cylinder: straight runs from the mouth out along the exit direction, the same
            // shape the review capture's gate casts.
            foreach (float radius in ProbeRadii)
            {
                foreach (Vector3 origin in MouthRing(radius))
                    built.Add((origin, origin + exitDirection * escapeLength));
            }

            // Cave corridor: polylines that follow the tunnel and flare with it, so they stay at a
            // constant FRACTION of the cross-section rather than a constant distance.
            if (HasCaveCorridor)
            {
                int stride = Mathf.Max(1, Mathf.RoundToInt(ProbeStepMeters / SampleSpacingMeters));
                int first = Mathf.Max(0, sections.Length - 1 -
                                         Mathf.RoundToInt(ProbeWindowMeters / SampleSpacingMeters));

                foreach (float radius in ProbeRadii)
                {
                    int spokes = radius <= 0.001f ? 1 : ProbeSpokes;
                    for (int spoke = 0; spoke < spokes; spoke++)
                    {
                        float angle = spoke * Mathf.PI * 2f / spokes;
                        for (int i = first; i + stride < sections.Length; i += stride)
                        {
                            built.Add((sections[i].At(radius, angle),
                                sections[Mathf.Min(i + stride, sections.Length - 1)].At(radius, angle)));
                        }
                    }
                }
            }

            return built.ToArray();
        }

        private IEnumerable<Vector3> MouthRing(float radius)
        {
            Vector3 normal = exitDirection;
            Vector3 right = (Vector3.Cross(normal, Vector3.up)).normalized;
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(normal, Vector3.forward).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;

            if (radius <= 0.001f)
            {
                yield return exitPosition;
                yield break;
            }

            for (int spoke = 0; spoke < ProbeSpokes; spoke++)
            {
                float angle = spoke * Mathf.PI * 2f / ProbeSpokes;
                yield return exitPosition
                             + right * (escapeRadius * radius * Mathf.Cos(angle))
                             + up * (escapeRadius * radius * Mathf.Sin(angle));
            }
        }

        /// <summary>
        /// True if any triangle of the given world-space mesh crosses a probe line. This is what
        /// catches the huge scaled-up triangles the point test slips past.
        /// </summary>
        public bool IntersectsProbes(Vector3[] worldVertices, int[] triangles, out Vector3 where)
        {
            where = Vector3.zero;
            if (probes == null || worldVertices == null || triangles == null)
                return false;

            foreach ((Vector3 from, Vector3 to) in probes)
            {
                Vector3 delta = to - from;
                float length = delta.magnitude;
                if (length < 1e-4f)
                    continue;
                Vector3 direction = delta / length;

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (!RayTriangle(from, direction,
                            worldVertices[triangles[i]],
                            worldVertices[triangles[i + 1]],
                            worldVertices[triangles[i + 2]],
                            out float hit) || hit > length)
                        continue;

                    where = from + direction * hit;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Moller-Trumbore, double-sided: these meshes are not reliably closed or wound.</summary>
        private static bool RayTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            const float Epsilon = 1e-7f;
            distance = 0f;

            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 pvec = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, pvec);
            if (determinant > -Epsilon && determinant < Epsilon)
                return false;

            float inverse = 1f / determinant;
            Vector3 tvec = origin - a;
            float u = Vector3.Dot(tvec, pvec) * inverse;
            if (u < 0f || u > 1f)
                return false;

            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(direction, qvec) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            distance = Vector3.Dot(edge2, qvec) * inverse;
            return distance > Epsilon;
        }

        /// <summary>
        /// How far <paramref name="worldPoint"/> intrudes into forbidden space, in metres, or a value
        /// &lt;= 0 when it is clear. <paramref name="reason"/> names which volume was hit so the build
        /// log says what actually pushed a peak rather than only that something did.
        /// </summary>
        public float Intrusion(Vector3 worldPoint, out string reason)
        {
            float escape = EscapeIntrusion(worldPoint);
            float cave = CaveIntrusion(worldPoint);

            if (cave >= escape)
            {
                reason = cave > 0f ? "cave corridor" : null;
                return cave;
            }
            reason = escape > 0f ? "escape corridor" : null;
            return escape;
        }

        /// <summary>Depth inside the forward cylinder; negative outside it.</summary>
        private float EscapeIntrusion(Vector3 worldPoint)
        {
            Vector3 offset = worldPoint - exitPosition;
            float along = Vector3.Dot(offset, exitDirection);
            if (along < 0f || along > escapeLength)
                return float.NegativeInfinity;

            float radial = (offset - exitDirection * along).magnitude;
            return escapeRadius - radial;
        }

        /// <summary>
        /// Depth inside the tunnel's elliptical cross-section; negative outside it. A point beyond the
        /// end cap is not "inside the tunnel" however close to the centre line it sits - otherwise
        /// everything in front of the mouth, the island included, reads as a violation. The escape
        /// cylinder is what guards that region.
        /// </summary>
        private float CaveIntrusion(Vector3 worldPoint)
        {
            if (!HasCaveCorridor)
                return float.NegativeInfinity;

            int nearest = 0;
            float nearestSqr = float.PositiveInfinity;
            for (int i = 0; i < sections.Length; i++)
            {
                float sqr = (worldPoint - sections[i].centre).sqrMagnitude;
                if (sqr >= nearestSqr)
                    continue;
                nearestSqr = sqr;
                nearest = i;
            }

            Section section = sections[nearest];
            Vector3 offset = worldPoint - section.centre;
            float along = Vector3.Dot(offset, section.tangent);

            if (nearest == sections.Length - 1 && along > 0f)
                return float.NegativeInfinity;
            if (nearest == 0)
            {
                if (along < 0f)
                    return float.NegativeInfinity;
                ClampedToWindowStart = true;
            }

            float x = Vector3.Dot(offset, section.right) / section.halfWidth;
            float y = Vector3.Dot(offset, section.up) / section.halfHeight;
            float normalised = Mathf.Sqrt(x * x + y * y);

            // Scaled back into metres so the log reads in the same units as the offsets, using the
            // tighter half-extent to stay on the safe side.
            return (1f - normalised) * Mathf.Min(section.halfWidth, section.halfHeight);
        }
    }
}
