using System.Collections.Generic;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Ray tests against the exterior set's RENDERERS rather than its colliders.
    ///
    /// Why this exists: every LPN headland prefab ships with a MeshFilter and a MeshRenderer and
    /// nothing else - zero colliders. A Physics.Raycast therefore cannot hit a mountain at all, so
    /// the review capture's original line-of-sight probe reported "clear" straight through a peak
    /// that filled the frame. The probe was not merely inaccurate, it was structurally incapable of
    /// ever naming the headland, which is exactly the object it needed to catch.
    ///
    /// A renderer-level test also covers the mixed case uniformly: the seabed is a CreatePrimitive
    /// plane and DOES carry a MeshCollider, the mountains carry none, and the island is a Terrain
    /// (TerrainCollider, no MeshFilter). Callers pair this with a physics raycast and report
    /// whichever hit is nearer, so all three kinds of geometry can be named.
    ///
    /// All LPN source FBXs import with isReadable: 1, so vertex access is available; meshes that are
    /// not readable degrade to a bounds test and are flagged in the hit description rather than
    /// silently skipped.
    /// </summary>
    internal sealed class ExteriorOccluders
    {
        private readonly struct Blocker
        {
            public readonly string name;
            public readonly Bounds bounds;
            public readonly Vector3[] worldVertices;
            public readonly int[] triangles;

            public Blocker(string name, Bounds bounds, Vector3[] worldVertices, int[] triangles)
            {
                this.name = name;
                this.bounds = bounds;
                this.worldVertices = worldVertices;
                this.triangles = triangles;
            }

            public bool IsExact => worldVertices != null && triangles != null;
        }

        private readonly List<Blocker> blockers = new List<Blocker>();

        public int Count => blockers.Count;

        /// <summary>
        /// Snapshots every active renderer under <paramref name="root"/> in world space. Inactive
        /// objects are skipped, which is what makes the headland A/B capture meaningful: disabling
        /// the group and re-collecting genuinely removes it from the probe.
        /// </summary>
        /// <param name="excludedGroups">
        /// Names of direct children of <paramref name="root"/> to leave out. The corridor gate excludes
        /// "Sea": the water sheet spans 2.4 km across the escape path by design - the sub surfaces
        /// THROUGH it - so counting it as an obstacle makes the gate unpassable. It is also the one
        /// mesh here that is not readable, so it only ever reports a coarse bounds hit.
        /// </param>
        public static ExteriorOccluders Collect(Transform root, params string[] excludedGroups)
        {
            var set = new ExteriorOccluders();
            if (root == null)
                return set;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (excludedGroups != null && excludedGroups.Length > 0 &&
                    IsUnderAnyGroup(filter.transform, root, excludedGroups))
                    continue;

                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled)
                    continue;

                string name = Path(filter.transform, root);

                if (!mesh.isReadable)
                {
                    set.blockers.Add(new Blocker(name + " (bounds only)", renderer.bounds, null, null));
                    continue;
                }

                Vector3[] local = mesh.vertices;
                var world = new Vector3[local.Length];
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < local.Length; i++)
                    world[i] = matrix.MultiplyPoint3x4(local[i]);

                set.blockers.Add(new Blocker(name, renderer.bounds, world, mesh.triangles));
            }

            return set;
        }

        private static bool IsUnderAnyGroup(Transform node, Transform root, string[] groups)
        {
            for (Transform walk = node; walk != null && walk != root; walk = walk.parent)
            {
                foreach (string group in groups)
                {
                    if (walk.name == group)
                        return true;
                }
            }
            return false;
        }

        private static string Path(Transform node, Transform root)
        {
            string name = node.name;
            for (Transform parent = node.parent; parent != null && parent != root; parent = parent.parent)
                name = parent.name + "/" + name;
            return name;
        }

        /// <summary>
        /// Nearest renderer hit along the segment, or false if the line is clear. Triangles are tested
        /// double-sided on purpose: the cave shell and several pack meshes are single-sided, and a
        /// back-face miss is precisely the failure mode this class was written to remove.
        /// </summary>
        public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance,
            out string name, out float distance)
        {
            name = null;
            distance = float.PositiveInfinity;

            var ray = new Ray(origin, direction);
            foreach (Blocker blocker in blockers)
            {
                if (!blocker.bounds.IntersectRay(ray, out float boundsDistance))
                    continue;
                if (boundsDistance > maxDistance || boundsDistance > distance)
                    continue;

                if (!blocker.IsExact)
                {
                    // Bounds-only fallback: a hit here means "somewhere in this box", so it is
                    // reported but never trusted as an exact distance.
                    distance = Mathf.Max(0f, boundsDistance);
                    name = blocker.name;
                    continue;
                }

                for (int i = 0; i < blocker.triangles.Length; i += 3)
                {
                    if (!RayTriangle(origin, direction,
                            blocker.worldVertices[blocker.triangles[i]],
                            blocker.worldVertices[blocker.triangles[i + 1]],
                            blocker.worldVertices[blocker.triangles[i + 2]],
                            out float hit))
                        continue;
                    if (hit >= distance || hit > maxDistance)
                        continue;

                    distance = hit;
                    name = blocker.name;
                }
            }

            return name != null;
        }

        /// <summary>Moller-Trumbore, double-sided (no winding cull).</summary>
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
    }
}
