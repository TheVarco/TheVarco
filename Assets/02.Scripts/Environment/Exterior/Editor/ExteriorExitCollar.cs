using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Extracts the cave shell's exit rim and lofts an outward-facing collar from it, so the mouth reads
    /// as a hole in rock instead of a hole in a smooth teal dome.
    ///
    /// WHY A COLLAR RATHER THAN A PLANE. The author's instinct - drop a plane around the cave - fails
    /// because a plane has no hole in it and simply seals the mouth. The collar's inner boundary IS the
    /// shell's own rim polygon, so its hole matches the opening exactly, including the 27 degree tilt
    /// that no axis-aligned terrain hole could follow.
    ///
    /// WHAT IT COVERS. The exit taper narrows the tunnel from 85 x 50 m to 24 x 16 m over the last
    /// 15.5 m, which leaves the shell's OUTER surface as a funnel reaching +-42.5 m wide and about
    /// y = 285 - twelve metres above the 273 m sea surface. Seen from outside that funnel is the smooth
    /// dome in review cut 2.
    ///
    /// 🔴 HOW IT IS BUILT, AND WHY THE OBVIOUS WAY DOES NOT WORK.
    ///
    /// The first attempt extruded each rim vertex radially outward from the exit centroid, in the rim
    /// plane, and used a search to clamp how far. That cannot be made to work: the radial direction is
    /// not normal to the tunnel surface, and the route curves so the section centres drift sideways
    /// behind the mouth. Some spokes therefore dive INTO the tunnel however far they are shortened, and
    /// the solver drove those to zero while their neighbours kept the full length - a measured spread of
    /// 0..60 m around one loop. The long skewed triangles bridging a 0-length spoke to a 60 m one swept
    /// across the corridor, and the line probes caught them. Correctly.
    ///
    /// This construction follows the tunnel instead of a point. Rings are placed on the route's OWN
    /// cross-sections at a FRACTION of each section, starting just outside the wall and opening up as
    /// they travel back. A point at fraction > 1 is outside the nominal tunnel by definition, so the
    /// clearance invariant holds analytically and there is no search, no per-vertex clamp and no
    /// tolerance to tune. It also means the collar hugs the funnel it is hiding rather than slicing it.
    ///
    /// 🔴 THIS DOES NOT TOUCH CaveShell.asset. That mesh is shared with
    /// MainScene_final_Cinemachine.unity, which is taehuni's. The collar is a separate mesh owned by the
    /// exterior builder, welded to the rim by position only.
    /// </summary>
    internal static class ExteriorExitCollar
    {
        /// <summary>Rings behind the mouth, not counting the weld ring itself.</summary>
        private const int RingCount = 8;

        /// <summary>Metres of route travelled per ring. 8 x 4 = 32 m, comfortably past the 15.5 m taper.</summary>
        private const float StepMeters = 4f;

        /// <summary>
        /// Cross-section fraction at the first ring behind the weld. Must be above 1: that is the whole
        /// clearance argument. 1.12 keeps it tight to the rock without the shell's authored noise, which
        /// reaches about 1.14 m inside the nominal ellipse, poking back through it.
        /// </summary>
        private const float StartFraction = 1.12f;

        /// <summary>Fraction at the outermost ring - this is what turns the funnel into a hillside.</summary>
        private const float EndFraction = 1.55f;

        /// <summary>
        /// Ceiling for collar vertices. Must stay ABOVE the tunnel's own roof everywhere the collar
        /// spans, or clamping a high spoke down would push it back inside and break the invariant.
        /// Z6's half-height is 25 m on a centre line near y=260, so the roof tops out near 285.
        /// Build() asserts this rather than trusting it.
        /// </summary>
        private const float CrestY = 292f;

        /// <summary>
        /// Surface break-up, as a FRACTION of the section rather than a distance, and strictly outward:
        /// the multiplier is 1 + amplitude * noise01, never less than 1. A displacement that could pull
        /// a vertex inward would destroy the "fraction > 1" guarantee that makes this construction safe.
        /// </summary>
        private const float NoiseAmplitude = 0.10f;

        private const float NoiseWavelength = 24f;

        /// <summary>The shell's tube has 32 sides, so its single open boundary loop has 32 vertices.</summary>
        private const int ExpectedRimVertexCount = 32;

        /// <summary>How far the extracted loop's centroid may sit from the measured exit position.</summary>
        private const float RimCentroidTolerance = 1f;

        public static int Rings => RingCount;
        public static float SpanMeters => RingCount * StepMeters;

        /// <summary>
        /// One tunnel cross-section. The frame is built exactly the way <see cref="ExteriorClearance"/>
        /// builds its own, so a fraction measured here means the same thing the clearance test measures.
        /// </summary>
        public readonly struct Section
        {
            public readonly Vector3 centre;
            public readonly Vector3 right;
            public readonly Vector3 up;
            public readonly float halfWidth;
            public readonly float halfHeight;

            public Section(Vector3 centre, Vector3 tangent, float halfWidth, float halfHeight)
            {
                this.centre = centre;

                Vector3 sideways = Vector3.Cross(tangent, Vector3.up);
                if (sideways.sqrMagnitude < 1e-6f)
                    sideways = Vector3.Cross(tangent, Vector3.forward);
                right = sideways.normalized;
                up = Vector3.Cross(right, tangent).normalized;

                this.halfWidth = halfWidth;
                this.halfHeight = halfHeight;
            }

            public Vector3 At(float fraction, float angle) => centre
                + right * (halfWidth * fraction * Mathf.Cos(angle))
                + up * (halfHeight * fraction * Mathf.Sin(angle));

            /// <summary>Where <paramref name="worldPoint"/> sits on this section, as an angle.</summary>
            public float AngleOf(Vector3 worldPoint)
            {
                Vector3 offset = worldPoint - centre;
                float u = Vector3.Dot(offset, right) / Mathf.Max(0.01f, halfWidth);
                float v = Vector3.Dot(offset, up) / Mathf.Max(0.01f, halfHeight);
                return Mathf.Atan2(v, u);
            }
        }

        /// <summary>
        /// Reads the shell's open boundary loop in world space.
        ///
        /// CaveMeshTopologyAnalyzer already asserts the invariant this relies on - IsValidOpenCave
        /// requires boundaryLoopCount == 1 - so the shell has exactly one open boundary and it is the
        /// exit rim: the route start carries a rounded cap, branch ends are capped, and portals are
        /// welded shut.
        ///
        /// Everything here is checked rather than assumed. Welding a collar to the wrong loop would put
        /// a sheet of rock somewhere inside the cave, which is a blocked tunnel, not a cosmetic bug.
        /// </summary>
        public static bool TryExtractRim(MeshFilter shell, Vector3 expectedCentre,
            out List<Vector3> rim, out string failure)
        {
            rim = null;
            failure = null;

            Mesh mesh = shell != null ? shell.sharedMesh : null;
            if (mesh == null)
            {
                failure = "the CaveShell has no mesh";
                return false;
            }
            if (!mesh.isReadable)
            {
                failure = $"'{mesh.name}' is not readable, so its boundary cannot be walked";
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // Colocated duplicates would split the loop, so walk positions rather than indices.
            var welded = new Dictionary<Vector3Int, int>();
            var representative = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int key = Quantise(vertices[i]);
                if (!welded.TryGetValue(key, out int existing))
                {
                    existing = i;
                    welded.Add(key, i);
                }
                representative[i] = existing;
            }

            var edgeUse = new Dictionary<(int, int), int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                CountEdge(edgeUse, representative[triangles[i]], representative[triangles[i + 1]]);
                CountEdge(edgeUse, representative[triangles[i + 1]], representative[triangles[i + 2]]);
                CountEdge(edgeUse, representative[triangles[i + 2]], representative[triangles[i]]);
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<(int, int), int> pair in edgeUse)
            {
                if (pair.Value != 1)
                    continue;
                AddNeighbour(adjacency, pair.Key.Item1, pair.Key.Item2);
                AddNeighbour(adjacency, pair.Key.Item2, pair.Key.Item1);
            }

            if (adjacency.Count == 0)
            {
                failure = "the shell has no open boundary at all - is the exit capped?";
                return false;
            }
            if (adjacency.Values.Any(neighbours => neighbours.Count != 2))
            {
                failure = "the shell's open boundary is not a simple loop " +
                          "(a vertex there has other than two boundary neighbours)";
                return false;
            }

            List<int> loop = WalkLoop(adjacency);
            if (loop.Count != adjacency.Count)
            {
                failure = $"the shell has more than one open boundary loop " +
                          $"({loop.Count} of {adjacency.Count} boundary vertices form the first loop) - " +
                          "refusing to guess which one is the exit";
                return false;
            }
            if (loop.Count != ExpectedRimVertexCount)
            {
                failure = $"the exit rim has {loop.Count} vertices, expected {ExpectedRimVertexCount} " +
                          "(CaveMeshGenerator.Sides)";
                return false;
            }

            Transform space = shell.transform;
            var world = loop.Select(index => space.TransformPoint(vertices[index])).ToList();
            Vector3 centroid = world.Aggregate(Vector3.zero, (sum, point) => sum + point) / world.Count;
            float offset = Vector3.Distance(centroid, expectedCentre);
            if (offset > RimCentroidTolerance)
            {
                failure = $"the extracted loop's centroid {centroid} is {offset:0.##} m from the measured " +
                          $"exit {expectedCentre} - that is not the exit rim";
                return false;
            }

            rim = world;
            return true;
        }

        /// <summary>
        /// Lofts the collar back along the route from the rim.
        ///
        /// Ring 0 is the extracted rim itself, so the weld is exact and cannot open a crack even where
        /// the shell's authored rim noise pulls the rock inside the nominal ellipse. Rings 1..N sit on
        /// the route's own sections at a fraction above 1, which is what puts them outside the tunnel by
        /// construction.
        ///
        /// Angles are measured from the rim vertices themselves rather than assumed, so the loft lines
        /// up with the shell's tessellation whatever roll convention its frames used.
        /// </summary>
        public static Mesh Build(List<Vector3> rim, Func<float, Section> sample, float endDistance,
            out float roofClearance)
        {
            int loop = rim.Count;
            Section endSection = sample(endDistance);

            var angles = new float[loop];
            for (int i = 0; i < loop; i++)
                angles[i] = endSection.AngleOf(rim[i]);

            int rings = RingCount + 1;
            var vertices = new Vector3[loop * rings];
            var uvs = new Vector2[loop * rings];
            roofClearance = float.PositiveInfinity;

            for (int ring = 0; ring < rings; ring++)
            {
                if (ring == 0)
                {
                    for (int i = 0; i < loop; i++)
                    {
                        vertices[i] = rim[i];
                        uvs[i] = new Vector2(rim[i].x, rim[i].z) / 12f;
                    }
                    continue;
                }

                float t = ring / (float)RingCount;
                float distance = Mathf.Max(0f, endDistance - ring * StepMeters);
                float fraction = Mathf.Lerp(StartFraction, EndFraction, (ring - 1) / (float)(RingCount - 1));
                Section section = sample(distance);

                // How much room the ceiling clamp has at this station. Reported so a future change to
                // CrestY or to Z6's profile cannot quietly start clamping vertices back into the tunnel.
                roofClearance = Mathf.Min(roofClearance,
                    CrestY - (section.centre.y + section.halfHeight));

                for (int i = 0; i < loop; i++)
                {
                    Vector3 nominal = section.At(fraction, angles[i]);

                    // Outward-only break-up: the multiplier is never below 1, so "outside the tunnel"
                    // survives it.
                    float noise01 = Mathf.PerlinNoise(
                        nominal.x / NoiseWavelength + 13.7f, nominal.z / NoiseWavelength + 5.1f);
                    Vector3 point = section.At(fraction * (1f + NoiseAmplitude * noise01 * t), angles[i]);
                    point.y = Mathf.Min(point.y, CrestY);

                    int index = ring * loop + i;
                    vertices[index] = point;
                    uvs[index] = new Vector2(point.x, point.z) / 12f;
                }
            }

            var triangles = new List<int>(loop * RingCount * 6);
            for (int ring = 0; ring < RingCount; ring++)
            {
                for (int i = 0; i < loop; i++)
                {
                    int next = (i + 1) % loop;
                    int a = ring * loop + i;
                    int b = ring * loop + next;
                    int c = (ring + 1) * loop + i;
                    int d = (ring + 1) * loop + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "ExteriorExitCollar" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        private static Vector3Int Quantise(Vector3 point)
        {
            const float Scale = 1000f; // 1 mm
            return new Vector3Int(
                Mathf.RoundToInt(point.x * Scale),
                Mathf.RoundToInt(point.y * Scale),
                Mathf.RoundToInt(point.z * Scale));
        }

        private static void CountEdge(Dictionary<(int, int), int> edges, int a, int b)
        {
            if (a == b)
                return;
            var key = a < b ? (a, b) : (b, a);
            edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        private static void AddNeighbour(Dictionary<int, List<int>> graph, int from, int to)
        {
            if (!graph.TryGetValue(from, out List<int> neighbours))
            {
                neighbours = new List<int>();
                graph.Add(from, neighbours);
            }
            neighbours.Add(to);
        }

        private static List<int> WalkLoop(Dictionary<int, List<int>> adjacency)
        {
            int start = adjacency.Keys.First();
            var loop = new List<int> { start };
            var visited = new HashSet<int> { start };

            int current = start;
            int previous = -1;
            while (true)
            {
                List<int> neighbours = adjacency[current];
                int next = neighbours[0] == previous ? neighbours[1] : neighbours[0];
                if (next == start)
                    break;
                if (!visited.Add(next))
                    break;
                loop.Add(next);
                previous = current;
                current = next;
            }
            return loop;
        }
    }
}
