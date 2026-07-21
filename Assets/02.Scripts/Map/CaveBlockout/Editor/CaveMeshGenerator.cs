using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public readonly struct CaveMeshGenerationResult
    {
        public readonly int triangleCount;

        public CaveMeshGenerationResult(int triangleCount)
        {
            this.triangleCount = triangleCount;
        }
    }

    public static class CaveMeshGenerator
    {
        public const float SampleSpacing = 3f;
        public const int Sides = 12;
        public const float VisualJitter = 0.4f;
        public const string GeneratedAssetFolder = "Assets/Generated/CaveBlockout";

        private readonly struct PortalSample
        {
            public readonly Vector3 center;
            public readonly Vector3 direction;
            public readonly float longitudinalHalfSize;
            public readonly float cosineThreshold;

            public PortalSample(Vector3 center, Vector3 direction, float longitudinalHalfSize, float angularHalfSize)
            {
                this.center = center;
                this.direction = direction.normalized;
                this.longitudinalHalfSize = longitudinalHalfSize;
                cosineThreshold = Mathf.Cos(angularHalfSize * Mathf.Deg2Rad);
            }
        }

        public static CaveMeshGenerationResult GenerateAll(CaveRoute mainRoute, CaveRoute branches, Transform generatedRoot, Material material)
        {
            EnsureAssetFolder();
            ClearChildren(generatedRoot);
            int triangleCount = 0;

            List<PortalSample> portals = BuildPortalSamples(mainRoute);
            triangleCount += GenerateRoute(mainRoute, generatedRoot, material, portals);
            triangleCount += GenerateRoute(branches, generatedRoot, material, null);
            AssetDatabase.SaveAssets();
            return new CaveMeshGenerationResult(triangleCount);
        }

        private static int GenerateRoute(CaveRoute route, Transform generatedRoot, Material material, IReadOnlyList<PortalSample> portals)
        {
            if (route == null)
                return 0;

            int triangleCount = 0;
            foreach (CaveRouteSplineDefinition definition in route.Definitions)
            {
                if (definition.splineIndex < 0 || definition.splineIndex >= route.Container.Splines.Count)
                    continue;

                Spline spline = route.Container[definition.splineIndex];
                foreach (CaveRouteSection section in definition.sections)
                {
                    float startT = spline.ConvertIndexUnit(section.startKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                    float endT = spline.ConvertIndexUnit(section.endKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                    if (definition.startTrimMeters > 0f)
                        startT = FindTAtDistance(route.Container, definition.splineIndex, startT, endT, definition.startTrimMeters);

                    string safeName = Sanitize(definition.routeId + "_" + section.zoneId);
                    Mesh visualMesh = BuildTubeMesh(route, definition.splineIndex, startT, endT, section.capStart, section.capEnd, true, safeName, generatedRoot, portals);
                    Mesh colliderMesh = BuildTubeMesh(route, definition.splineIndex, startT, endT, section.capStart, section.capEnd, false, safeName + "_Collider", generatedRoot, portals);

                    Mesh visualAsset = SaveMeshAsset(visualMesh, $"{GeneratedAssetFolder}/{safeName}.asset");
                    Mesh colliderAsset = SaveMeshAsset(colliderMesh, $"{GeneratedAssetFolder}/{safeName}_Collider.asset");

                    GameObject sectionObject = new GameObject(section.zoneId);
                    sectionObject.transform.SetParent(generatedRoot, false);
                    MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
                    MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
                    MeshCollider collider = sectionObject.AddComponent<MeshCollider>();
                    filter.sharedMesh = visualAsset;
                    renderer.sharedMaterial = material;
                    collider.sharedMesh = colliderAsset;
                    collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                              MeshColliderCookingOptions.EnableMeshCleaning |
                                              MeshColliderCookingOptions.WeldColocatedVertices;
                    sectionObject.isStatic = true;
                    triangleCount += visualAsset.triangles.Length / 3;
                }
            }

            return triangleCount;
        }

        private static Mesh BuildTubeMesh(
            CaveRoute route,
            int splineIndex,
            float startT,
            float endT,
            bool capStart,
            bool capEnd,
            bool addJitter,
            string meshName,
            Transform outputSpace,
            IReadOnlyList<PortalSample> portals)
        {
            float length = ApproximateLength(route.Container, splineIndex, startT, endT, 96);
            int ringCount = Mathf.Max(2, Mathf.CeilToInt(length / SampleSpacing) + 1);
            List<Vector3> vertices = new List<Vector3>(ringCount * Sides + 2);
            List<Vector3> normals = new List<Vector3>(ringCount * Sides + 2);
            List<Vector2> uvs = new List<Vector2>(ringCount * Sides + 2);
            List<int> triangles = new List<int>((ringCount - 1) * Sides * 6);
            Vector3[] centers = new Vector3[ringCount];
            Vector3[] tangents = new Vector3[ringCount];
            Vector3[] rights = new Vector3[ringCount];
            Vector3[] ups = new Vector3[ringCount];

            Vector3 previousTangent = Vector3.forward;
            Vector3 previousUp = Vector3.up;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float alpha = ring / (float)(ringCount - 1);
                float t = Mathf.Lerp(startT, endT, alpha);
                Vector3 centerWorld = route.Container.EvaluatePosition(splineIndex, t);
                Vector3 tangentWorld = ((Vector3)route.Container.EvaluateTangent(splineIndex, t)).normalized;
                Vector3 packageUp = ((Vector3)route.Container.EvaluateUpVector(splineIndex, t)).normalized;
                Vector3 upWorld;

                if (ring == 0)
                {
                    upWorld = Vector3.ProjectOnPlane(packageUp, tangentWorld).normalized;
                    if (upWorld.sqrMagnitude < 0.01f)
                        upWorld = Vector3.ProjectOnPlane(Vector3.up, tangentWorld).normalized;
                    if (upWorld.sqrMagnitude < 0.01f)
                        upWorld = Vector3.right;
                }
                else
                {
                    Vector3 transportedUp = Quaternion.FromToRotation(previousTangent, tangentWorld) * previousUp;
                    Vector3 desiredUp = Vector3.ProjectOnPlane(packageUp, tangentWorld).normalized;
                    if (desiredUp.sqrMagnitude < 0.01f)
                        desiredUp = transportedUp;
                    if (Vector3.Dot(desiredUp, transportedUp) < 0f)
                        desiredUp = -desiredUp;
                    upWorld = Vector3.Slerp(transportedUp, desiredUp, 0.2f).normalized;
                }

                float roll = route.EvaluateRoll(splineIndex, t);
                upWorld = Quaternion.AngleAxis(roll, tangentWorld) * upWorld;
                Vector3 rightWorld = Vector3.Cross(upWorld, tangentWorld).normalized;
                upWorld = Vector3.Cross(tangentWorld, rightWorld).normalized;

                centers[ring] = outputSpace.InverseTransformPoint(centerWorld);
                tangents[ring] = outputSpace.InverseTransformDirection(tangentWorld).normalized;
                rights[ring] = outputSpace.InverseTransformDirection(rightWorld).normalized;
                ups[ring] = outputSpace.InverseTransformDirection(upWorld).normalized;
                previousTangent = tangentWorld;
                previousUp = upWorld;

                float widthRadius = route.EvaluateWidth(splineIndex, t) * 0.5f;
                float heightRadius = route.EvaluateHeight(splineIndex, t) * 0.5f;
                for (int side = 0; side < Sides; side++)
                {
                    float angle = side / (float)Sides * Mathf.PI * 2f;
                    Vector3 radial = rights[ring] * (Mathf.Cos(angle) * widthRadius) + ups[ring] * (Mathf.Sin(angle) * heightRadius);
                    if (addJitter)
                        radial *= 1f + HashSigned(meshName, ring, side) * VisualJitter / Mathf.Max(1f, radial.magnitude);

                    vertices.Add(centers[ring] + radial);
                    normals.Add(-radial.normalized);
                    uvs.Add(new Vector2(side / (float)Sides, alpha * length / 5f));
                }
            }

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    int nextSide = (side + 1) % Sides;
                    float midAngle = (side + 0.5f) / Sides * Mathf.PI * 2f;
                    Vector3 radial = (rights[ring] * Mathf.Cos(midAngle) + ups[ring] * Mathf.Sin(midAngle)).normalized;
                    Vector3 centerWorld = outputSpace.TransformPoint((centers[ring] + centers[ring + 1]) * 0.5f);
                    Vector3 radialWorld = outputSpace.TransformDirection(radial).normalized;
                    if (ShouldSkipForPortal(centerWorld, radialWorld, portals))
                        continue;

                    int a = ring * Sides + side;
                    int b = ring * Sides + nextSide;
                    int c = (ring + 1) * Sides + side;
                    int d = (ring + 1) * Sides + nextSide;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            if (capStart)
                AddCap(vertices, normals, uvs, triangles, centers[0], tangents[0], 0, true);
            if (capEnd)
                AddCap(vertices, normals, uvs, triangles, centers[ringCount - 1], -tangents[ringCount - 1], (ringCount - 1) * Sides, false);

            Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCap(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, Vector3 center, Vector3 desiredNormal, int ringStart, bool startCap)
        {
            int centerIndex = vertices.Count;
            vertices.Add(center);
            normals.Add(desiredNormal.normalized);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int side = 0; side < Sides; side++)
            {
                int a = ringStart + side;
                int b = ringStart + (side + 1) % Sides;
                Vector3 cross = Vector3.Cross(vertices[a] - center, vertices[b] - center);
                bool correctOrder = Vector3.Dot(cross, desiredNormal) > 0f;
                triangles.Add(centerIndex);
                triangles.Add(correctOrder ? a : b);
                triangles.Add(correctOrder ? b : a);
            }
        }

        private static bool ShouldSkipForPortal(Vector3 center, Vector3 radial, IReadOnlyList<PortalSample> portals)
        {
            if (portals == null)
                return false;

            foreach (PortalSample portal in portals)
            {
                if (Vector3.Distance(center, portal.center) <= portal.longitudinalHalfSize &&
                    Vector3.Dot(radial, portal.direction) >= portal.cosineThreshold)
                    return true;
            }
            return false;
        }

        private static List<PortalSample> BuildPortalSamples(CaveRoute mainRoute)
        {
            List<PortalSample> samples = new List<PortalSample>();
            if (mainRoute == null || mainRoute.Container.Splines.Count == 0)
                return samples;

            Spline spline = mainRoute.Container[0];
            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                float t = spline.ConvertIndexUnit(portal.mainKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                Vector3 center = mainRoute.Container.EvaluatePosition(0, t);
                Vector3 direction = mainRoute.transform.TransformDirection(portal.direction).normalized;
                samples.Add(new PortalSample(center, direction, portal.longitudinalHalfSize, portal.angularHalfSize));
            }
            return samples;
        }

        public static float ApproximateLength(SplineContainer container, int splineIndex, float startT, float endT, int samples)
        {
            float length = 0f;
            Vector3 previous = container.EvaluatePosition(splineIndex, startT);
            for (int i = 1; i <= samples; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)samples);
                Vector3 current = container.EvaluatePosition(splineIndex, t);
                length += Vector3.Distance(previous, current);
                previous = current;
            }
            return length;
        }

        public static float FindTAtDistance(SplineContainer container, int splineIndex, float startT, float endT, float distance)
        {
            const int samples = 256;
            float accumulated = 0f;
            float previousT = startT;
            Vector3 previous = container.EvaluatePosition(splineIndex, startT);
            for (int i = 1; i <= samples; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)samples);
                Vector3 current = container.EvaluatePosition(splineIndex, t);
                float segment = Vector3.Distance(previous, current);
                if (accumulated + segment >= distance)
                {
                    float alpha = segment > 0.0001f ? (distance - accumulated) / segment : 0f;
                    return Mathf.Lerp(previousT, t, alpha);
                }
                accumulated += segment;
                previous = current;
                previousT = t;
            }
            return endT;
        }

        private static Mesh SaveMeshAsset(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static float HashSigned(string key, int ring, int side)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < key.Length; i++)
                    hash = (hash ^ key[i]) * 16777619u;
                hash = (hash ^ (uint)ring) * 16777619u;
                hash = (hash ^ (uint)side) * 16777619u;
                hash ^= hash >> 13;
                hash *= 1274126177u;
                return (hash / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        private static void EnsureAssetFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Generated"))
                AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
                AssetDatabase.CreateFolder("Assets/Generated", "CaveBlockout");
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
