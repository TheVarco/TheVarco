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
    /// dome in review cut 2. The collar sweeps out from the rim to cover it.
    ///
    /// 🔴 THIS DOES NOT TOUCH CaveShell.asset. That mesh is shared with
    /// MainScene_final_Cinemachine.unity, which is taehuni's. The collar is a separate mesh owned by the
    /// exterior builder, welded to the rim by position only.
    /// </summary>
    internal static class ExteriorExitCollar
    {
        /// <summary>How far the collar reaches out from the rim before clearance clamps it.</summary>
        private const float ReachMeters = 60f;

        /// <summary>Below this the collar is not worth emitting at all.</summary>
        private const float MinReachMeters = 10f;

        private const int RingCount = 8;

        /// <summary>Metres of slack demanded beyond the forbidden volumes.</summary>
        private const float ClearanceMargin = 3f;

        /// <summary>
        /// Ceiling for collar vertices. The shell funnel tops out near 285, so 292 covers it with margin
        /// while stopping the upper rim from shooting off into a spike - the rim plane is tilted 27
        /// degrees, so unclamped in-plane extrusion climbs fast on the high side.
        /// </summary>
        private const float CrestY = 292f;

        /// <summary>Amplitude of the surface break-up. Ramped in from zero so the rim weld stays exact.</summary>
        private const float NoiseAmplitude = 3.5f;

        private const float NoiseWavelength = 26f;

        /// <summary>
        /// How far the collar sweeps BACK along the exit axis, AS A FRACTION OF ITS REACH.
        ///
        /// Without a setback the collar is a flat disc in the rim plane and the shell funnel it is meant
        /// to hide - which flares to +-42.5 m over the last 15.5 m - simply pokes through it. Sweeping
        /// back turns the collar into a shallow cone that follows that flare.
        ///
        /// 🔴 IT MUST BE A RATIO, NOT A FIXED DISTANCE. The funnel gains 1.97 m of half-width per metre
        /// travelled back, so the collar only stays outside it while it gains outward distance faster
        /// than that. With a fixed setback the ratio collapses as soon as the clearance solver shortens
        /// the reach - and since shortening the reach then makes the intrusion WORSE, the solver drives
        /// every vertex to zero and no collar is ever emitted. That is exactly what the first two
        /// attempts did. At 0.3 the cone runs out 3.3 : 1, clear of the funnel at every reach.
        ///
        /// Linear in t, not eased: an eased setback leaves the first rings sitting in the mouth plane,
        /// which is the one place the escape cylinder is measured.
        ///
        /// Evaluate() is the single position function used by both the solver and the loft, so what gets
        /// tested is exactly what gets built.
        /// </summary>
        private const float SetbackRatio = 0.3f;

        /// <summary>The shell's tube has 32 sides, so its single open boundary loop has 32 vertices.</summary>
        private const int ExpectedRimVertexCount = 32;

        /// <summary>How far the extracted loop's centroid may sit from the measured exit position.</summary>
        private const float RimCentroidTolerance = 1f;

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
        /// Lofts the collar outward from the rim, in the rim's own plane.
        ///
        /// In-plane extrusion is what makes this read as land rather than as a disc lying on the water:
        /// the rim plane is tilted 27 degrees up, so its "up" side sweeps up and BACK over the cave -
        /// the hillside the tunnel bores through - while its "down" side sweeps forward and down towards
        /// the seabed. Y is clamped at both ends so neither runs away.
        ///
        /// <paramref name="reachSolver"/> returns the usable reach for one rim vertex; the caller wires
        /// it to the clearance test, which is what keeps the collar out of the escape corridor.
        /// </summary>
        public static Mesh Build(List<Vector3> rim, Vector3 exitCentre, Vector3 exitDirection,
            float floorY, Func<Vector3, Vector3, float, float> reachSolver,
            out float minReach, out float maxReach)
        {
            int loop = rim.Count;
            var outward = new Vector3[loop];
            var reach = new float[loop];

            Vector3 axis = exitDirection.normalized;
            for (int i = 0; i < loop; i++)
            {
                Vector3 radial = rim[i] - exitCentre;
                radial -= axis * Vector3.Dot(radial, axis); // keep it in the rim plane
                outward[i] = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector3.up;

                // Stop a downward spoke where it MEETS the seabed instead of letting the y-clamp slide
                // it along at floor level. That clamp built a flat rock apron spreading forward from the
                // mouth's bottom lip, and a sheet lying on the floor in front of the opening is exactly
                // what the escape-corridor probes are there to catch - it failed on a triangle crossing
                // at y=252.50, the clamp value itself. The seabed plane and the island skirt already
                // cover that ground; the collar has no business duplicating it.
                float descent = axis.y * SetbackRatio - outward[i].y;
                float toFloor = descent > 1e-4f
                    ? Mathf.Max(0f, (rim[i].y - floorY - NoiseAmplitude) / descent)
                    : float.PositiveInfinity;

                reach[i] = reachSolver(rim[i], outward[i], Mathf.Min(ReachMeters, toFloor));
            }

            // Smooth the clamped reach around the loop so the clearance bite does not show as a notch,
            // but never let smoothing RAISE a vertex above the reach proven safe for it.
            var solved = (float[])reach.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                var smoothed = new float[loop];
                for (int i = 0; i < loop; i++)
                {
                    float blended =
                        (reach[(i - 1 + loop) % loop] + 2f * reach[i] + reach[(i + 1) % loop]) * 0.25f;
                    smoothed[i] = Mathf.Min(blended, solved[i]);
                }
                reach = smoothed;
            }

            // 🔴 RE-VALIDATE. Reach scales BOTH the outward and the setback term, so shortening it does
            // not walk a vertex back along the ray it was tested on - it puts it on a different ray.
            // Smoothing therefore invalidates the first solve even though it only ever lowers values,
            // which is how a ring-3 vertex ended up 0.18 m inside the tunnel. Solving again from the
            // smoothed value is what makes the tested set and the built set the same set.
            for (int i = 0; i < loop; i++)
                reach[i] = reachSolver(rim[i], outward[i], reach[i]);

            minReach = reach.Min();
            maxReach = reach.Max();

            int rings = RingCount + 1;
            var vertices = new Vector3[loop * rings];
            var uvs = new Vector2[loop * rings];
            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)RingCount;
                for (int i = 0; i < loop; i++)
                {
                    Vector3 point = Evaluate(rim[i], outward[i], axis, reach[i], t, floorY);
                    int index = ring * loop + i;
                    vertices[index] = point;
                    uvs[index] = new Vector2(point.x, point.z) / 30f;
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

        /// <summary>
        /// The collar's surface, as one function of (rim vertex, outward direction, reach, ring
        /// parameter). Both the loft and the clearance solver call this, so what gets tested is exactly
        /// what gets built.
        /// </summary>
        public static Vector3 Evaluate(Vector3 rimPoint, Vector3 outward, Vector3 axis,
            float reach, float t, float floorY)
        {
            Vector3 point = rimPoint + outward * (reach * t) - axis * (reach * SetbackRatio * t);

            // Displacement ramps from zero at the rim, so ring 0 stays exactly on the shell, AND scales
            // with the spoke's own reach.
            //
            // 🔴 The reach term is not cosmetic. Without it the break-up is a fixed +-3.5 m no matter how
            // short the spoke is, so a spoke the clearance solver has shortened to nothing still gets
            // displaced - and half the time inwards, straight into the tunnel. That is a violation the
            // solver cannot fix by shortening further, because shortening does not reduce it. It showed
            // up as a ring-3 vertex sitting 0.78 m deeper into the tunnel than its own rim.
            float amplitude = NoiseAmplitude * Mathf.SmoothStep(0f, 1f, t)
                              * Mathf.Clamp01(reach / ReachMeters);
            float noise = Mathf.PerlinNoise(
                point.x / NoiseWavelength + 13.7f, point.z / NoiseWavelength + 5.1f) - 0.5f;
            point += outward * (noise * 2f * amplitude);
            point.y = Mathf.Clamp(point.y, floorY, CrestY);
            return point;
        }

        public static float MinimumReach => MinReachMeters;
        public static float DesiredReach => ReachMeters;
        public static float Margin => ClearanceMargin;
        public static int Rings => RingCount;

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
