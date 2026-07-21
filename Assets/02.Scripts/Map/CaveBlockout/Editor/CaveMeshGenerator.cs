using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public readonly struct CaveMeshGenerationResult
    {
        public readonly int triangleCount;
        public readonly CaveMeshTopologyReport topology;

        public CaveMeshGenerationResult(int triangleCount, CaveMeshTopologyReport topology)
        {
            this.triangleCount = triangleCount;
            this.topology = topology;
        }
    }

    public sealed class CaveMeshTopologyReport
    {
        public int boundaryEdgeCount;
        public int boundaryLoopCount;
        public int nonManifoldEdgeCount;
        public int windingMismatchCount;
        public int degenerateTriangleCount;
        public bool IsValidOpenCave => boundaryLoopCount == 1 && nonManifoldEdgeCount == 0 &&
                                       windingMismatchCount == 0 && degenerateTriangleCount == 0;
    }

    public static class CaveMeshGenerator
    {
        public const float SampleSpacing = 2f;
        public const float PortalSampleSpacing = 1f;
        public const int Sides = 24;
        public const string GeneratedAssetFolder = "Assets/Generated/CaveBlockout";
        private const string VisualMeshPath = GeneratedAssetFolder + "/CaveShell.asset";
        private const string ColliderMeshPath = GeneratedAssetFolder + "/CaveShell_Collider.asset";
        public const float BranchCollarDepth = 10f;
        private const float StraightSleeveDepth = 3f;

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();

            public int AddVertex(Vector3 vertex, Vector3 normal, Vector2 uv)
            {
                int index = vertices.Count;
                vertices.Add(vertex);
                normals.Add(normal);
                uvs.Add(uv);
                return index;
            }

            public void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                CompactUnusedVertices();
                Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private void CompactUnusedVertices()
            {
                int[] remap = Enumerable.Repeat(-1, vertices.Count).ToArray();
                List<Vector3> compactVertices = new List<Vector3>();
                List<Vector3> compactNormals = new List<Vector3>();
                List<Vector2> compactUvs = new List<Vector2>();
                for (int i = 0; i < triangles.Count; i++)
                {
                    int source = triangles[i];
                    if (remap[source] < 0)
                    {
                        remap[source] = compactVertices.Count;
                        compactVertices.Add(vertices[source]);
                        compactNormals.Add(normals[source]);
                        compactUvs.Add(uvs[source]);
                    }
                    triangles[i] = remap[source];
                }
                vertices.Clear();
                vertices.AddRange(compactVertices);
                normals.Clear();
                normals.AddRange(compactNormals);
                uvs.Clear();
                uvs.AddRange(compactUvs);
            }
        }

        private sealed class TubeRings
        {
            public readonly List<float> distances = new List<float>();
            public readonly List<float> normalizedTs = new List<float>();
            public readonly List<Vector3> centers = new List<Vector3>();
            public readonly List<Vector3> tangents = new List<Vector3>();
            public readonly List<Vector3> rights = new List<Vector3>();
            public readonly List<Vector3> ups = new List<Vector3>();
            public readonly List<int[]> indices = new List<int[]>();
            public int SideCount => indices.Count == 0 ? 0 : indices[0].Length;
        }

        private sealed class PortalCarve
        {
            public CavePortalDefinition portal;
            public CaveRouteSplineDefinition branchDefinition;
            public int ringStart;
            public int ringEnd;
            public int sideStart;
            public int sideQuads;
            public float branchStartDistance;
            public int[] boundaryLoop;
        }

        public static CaveMeshGenerationResult GenerateAll(CaveRoute mainRoute, CaveRoute branches, Transform generatedRoot, Material material)
        {
            if (mainRoute == null || branches == null)
                throw new ArgumentNullException(nameof(mainRoute), "Both main and branch routes are required.");

            EnsureAssetFolder();
            ClearChildren(generatedRoot);
            PruneLegacyMeshAssets();

            Mesh visualGenerated = BuildCombinedShell(mainRoute, branches, generatedRoot, "CaveShell", true);
            Mesh colliderGenerated = BuildCombinedShell(mainRoute, branches, generatedRoot, "CaveShell_Collider", false);
            Mesh visualAsset = SaveMeshAsset(visualGenerated, VisualMeshPath);
            Mesh colliderAsset = SaveMeshAsset(colliderGenerated, ColliderMeshPath);

            GameObject shellObject = new GameObject("CaveShell");
            shellObject.transform.SetParent(generatedRoot, false);
            MeshFilter filter = shellObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = shellObject.AddComponent<MeshRenderer>();
            MeshCollider collider = shellObject.AddComponent<MeshCollider>();
            filter.sharedMesh = visualAsset;
            renderer.sharedMaterial = material;
            collider.sharedMesh = colliderAsset;
            collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                      MeshColliderCookingOptions.EnableMeshCleaning |
                                      MeshColliderCookingOptions.WeldColocatedVertices;
            shellObject.isStatic = true;

            CaveMeshTopologyReport topology = CaveMeshTopologyAnalyzer.Analyze(visualAsset);
            AssetDatabase.SaveAssets();
            return new CaveMeshGenerationResult(visualAsset.triangles.Length / 3, topology);
        }

        private static Mesh BuildCombinedShell(CaveRoute mainRoute, CaveRoute branches, Transform outputSpace, string meshName, bool allowVisualDetail)
        {
            CaveRouteSplineDefinition mainDefinition = mainRoute.Definitions.First(definition => definition.isMainRoute);
            float mainLength = mainRoute.Container.CalculateLength(mainDefinition.splineIndex);
            List<float> mainDistances = BuildMainDistances(mainRoute, mainLength);
            MeshBuilder builder = new MeshBuilder();
            TubeRings main = BuildTubeVertices(builder, mainRoute, mainDefinition.splineIndex, mainDistances, Sides, outputSpace,
                null, mainRoute.NoiseSettings, allowVisualDetail,
                distance => EvaluateMainNoiseWeight(mainRoute, distance, mainLength));
            List<PortalCarve> carves = BuildPortalCarves(mainRoute, branches, main, outputSpace);

            for (int ring = 0; ring < main.indices.Count - 1; ring++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    if (IsPortalQuad(ring, side, carves))
                        continue;
                    AddTubeQuad(builder, main.indices[ring], main.indices[ring + 1], side);
                }
            }

            bool capMainStart = mainDefinition.sections.Count == 0 || mainDefinition.sections[0].capStart;
            if (capMainStart)
                AddCap(builder, main.indices[0], main.centers[0], main.tangents[0], true);

            foreach (PortalCarve carve in carves)
            {
                carve.boundaryLoop = ExtractBoundaryLoop(main, carve);
                float branchLength = branches.Container.CalculateLength(carve.portal.branchSplineIndex);
                List<float> branchDistances = BuildDistanceRange(carve.branchStartDistance, branchLength, SampleSpacing);
                int branchSides = carve.boundaryLoop.Length;
                int[] sleeveLoop = CreateStraightSleeve(builder, carve.boundaryLoop, branches,
                    carve.portal.branchSplineIndex, carve.branchStartDistance, outputSpace);
                AddCollar(builder, carve.boundaryLoop, sleeveLoop);
                float[] branchAngles = BuildBoundaryAngles(builder.vertices, sleeveLoop, branches,
                    carve.portal.branchSplineIndex, carve.branchStartDistance, outputSpace);
                TubeRings branch = BuildTubeVertices(builder, branches, carve.portal.branchSplineIndex, branchDistances,
                    branchSides, outputSpace, branchAngles, mainRoute.NoiseSettings, allowVisualDetail,
                    distance => EvaluateBranchNoiseWeight(mainRoute.NoiseSettings, distance, carve.branchStartDistance, branchLength));
                for (int ring = 0; ring < branch.indices.Count - 1; ring++)
                {
                    for (int side = 0; side < branchSides; side++)
                        AddTubeQuad(builder, branch.indices[ring], branch.indices[ring + 1], side);
                }

                AddCollar(builder, sleeveLoop, branch.indices[0]);
                bool capBranchEnd = carve.branchDefinition.sections.Count == 0 || carve.branchDefinition.sections[0].capEnd;
                if (capBranchEnd)
                {
                    int last = branch.indices.Count - 1;
                    AddCap(builder, branch.indices[last], branch.centers[last], -branch.tangents[last], false);
                }
            }

            return builder.ToMesh(meshName);
        }

        private static List<float> BuildMainDistances(CaveRoute mainRoute, float totalLength)
        {
            List<float> distances = BuildDistanceRange(0f, totalLength, SampleSpacing);
            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                float center = ResolvePortalDistance(mainRoute, portal);
                const float halfLength = 18f;
                for (float offset = -halfLength - 1f; offset <= halfLength + 1.001f; offset += PortalSampleSpacing)
                    distances.Add(Mathf.Clamp(center + offset, 0f, totalLength));
            }
            return SortAndUnique(distances);
        }

        private static List<float> BuildDistanceRange(float start, float end, float spacing)
        {
            List<float> distances = new List<float> { start };
            for (float distance = start + spacing; distance < end - 0.01f; distance += spacing)
                distances.Add(distance);
            distances.Add(end);
            return SortAndUnique(distances);
        }

        private static List<float> SortAndUnique(List<float> values)
        {
            values.Sort();
            List<float> result = new List<float>(values.Count);
            foreach (float value in values)
            {
                if (result.Count == 0 || Mathf.Abs(result[result.Count - 1] - value) > 0.02f)
                    result.Add(value);
            }
            return result;
        }

        private static TubeRings BuildTubeVertices(MeshBuilder builder, CaveRoute route, int splineIndex, IReadOnlyList<float> distances,
            int sideCount, Transform outputSpace, IReadOnlyList<float> sideAngles = null, CaveNoiseSettings noiseSettings = null,
            bool allowVisualDetail = false, Func<float, float> noiseWeightResolver = null)
        {
            TubeRings tube = new TubeRings();
            Vector3 previousWorldTangent = Vector3.forward;
            Vector3 previousWorldUp = Vector3.up;
            for (int ring = 0; ring < distances.Count; ring++)
            {
                float distance = distances[ring];
                float t = route.EvaluateTAtDistance(splineIndex, distance);
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
                    Vector3 transported = Quaternion.FromToRotation(previousWorldTangent, tangentWorld) * previousWorldUp;
                    Vector3 desired = Vector3.ProjectOnPlane(packageUp, tangentWorld).normalized;
                    if (desired.sqrMagnitude < 0.01f) desired = transported;
                    if (Vector3.Dot(desired, transported) < 0f) desired = -desired;
                    upWorld = Vector3.Slerp(transported, desired, 0.2f).normalized;
                }

                upWorld = Quaternion.AngleAxis(route.EvaluateRoll(splineIndex, t), tangentWorld) * upWorld;
                Vector3 rightWorld = Vector3.Cross(upWorld, tangentWorld).normalized;
                upWorld = Vector3.Cross(tangentWorld, rightWorld).normalized;
                Vector3 center = outputSpace.InverseTransformPoint(centerWorld);
                Vector3 tangent = outputSpace.InverseTransformDirection(tangentWorld).normalized;
                Vector3 right = outputSpace.InverseTransformDirection(rightWorld).normalized;
                Vector3 up = outputSpace.InverseTransformDirection(upWorld).normalized;
                float widthRadius = route.EvaluateWidth(splineIndex, t) * 0.5f;
                float heightRadius = route.EvaluateHeight(splineIndex, t) * 0.5f;
                int[] ringIndices = new int[sideCount];
                for (int side = 0; side < sideCount; side++)
                {
                    float angle = sideAngles != null ? sideAngles[side] : side / (float)sideCount * Mathf.PI * 2f;
                    Vector3 radial = right * (Mathf.Cos(angle) * widthRadius) + up * (Mathf.Sin(angle) * heightRadius);
                    float noiseWeight = noiseWeightResolver != null ? Mathf.Clamp01(noiseWeightResolver(distance)) : 1f;
                    Vector3 vertex = ApplyNoise(center + radial, radial, angle, noiseSettings, allowVisualDetail, noiseWeight);
                    ringIndices[side] = builder.AddVertex(vertex, -radial.normalized,
                        new Vector2(side / (float)sideCount, distance / 5f));
                }

                tube.distances.Add(distance);
                tube.normalizedTs.Add(t);
                tube.centers.Add(center);
                tube.tangents.Add(tangent);
                tube.rights.Add(right);
                tube.ups.Add(up);
                tube.indices.Add(ringIndices);
                previousWorldTangent = tangentWorld;
                previousWorldUp = upWorld;
            }
            return tube;
        }

        public static float EvaluateMainNoiseWeight(CaveRoute mainRoute, float distanceMeters)
        {
            float length = mainRoute != null && mainRoute.Container.Splines.Count > 0
                ? mainRoute.Container.CalculateLength(0)
                : 0f;
            return EvaluateMainNoiseWeight(mainRoute, distanceMeters, length);
        }

        private static float EvaluateMainNoiseWeight(CaveRoute mainRoute, float distanceMeters, float totalLength)
        {
            if (mainRoute == null) return 1f;
            CaveNoiseSettings settings = mainRoute.NoiseSettings;
            float fade = Mathf.Max(0.1f, settings.portalFadeDistance);
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceMeters / fade));
            weight = Mathf.Min(weight, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((totalLength - distanceMeters) / fade)));
            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                float delta = Mathf.Abs(distanceMeters - ResolvePortalDistance(mainRoute, portal));
                float portalWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((delta - 16f) / fade));
                weight = Mathf.Min(weight, portalWeight);
            }
            return weight;
        }

        private static float EvaluateBranchNoiseWeight(CaveNoiseSettings settings, float distance, float start, float end)
        {
            float fade = Mathf.Max(0.1f, settings.portalFadeDistance);
            float fromStart = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - start) / fade));
            float fromEnd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((end - distance) / fade));
            return Mathf.Min(fromStart, fromEnd);
        }

        private static Vector3 ApplyNoise(Vector3 position, Vector3 radial, float angle, CaveNoiseSettings settings,
            bool allowVisualDetail, float weight)
        {
            if (settings == null || !settings.enabled || settings.amplitudeMeters <= 0f || weight <= 0f)
                return position;

            float vertical = Mathf.Sin(angle);
            float surfaceMultiplier = vertical >= 0f
                ? Mathf.Lerp(settings.wallMultiplier, settings.ceilingMultiplier, vertical)
                : Mathf.Lerp(settings.wallMultiplier, settings.floorMultiplier, -vertical);
            float radius = radial.magnitude;
            float requestedAmplitude = settings.amplitudeMeters * Mathf.Max(0f, surfaceMultiplier);
            float clearanceSurplus = Mathf.Max(0f, radius - 2.5f);
            float structuralAmplitude = Mathf.Min(requestedAmplitude, radius * 0.15f, clearanceSurplus) * weight;
            float structural = FractalNoise(position, settings.seed, settings.wavelengthMeters, settings.octaves,
                settings.lacunarity, settings.persistence) * structuralAmplitude;

            float visualDetail = 0f;
            if (allowVisualDetail && settings.visualDetailEnabled && settings.visualDetailAmplitude > 0f)
            {
                float signed = FractalNoise(position, settings.seed + 7919, settings.visualDetailWavelength, 2, 2f, 0.5f);
                visualDetail = -(signed * 0.5f + 0.5f) * Mathf.Min(0.15f, settings.visualDetailAmplitude) * weight;
            }
            return position + radial.normalized * (structural + visualDetail);
        }

        private static float FractalNoise(Vector3 position, int seed, float wavelength, int octaves, float lacunarity, float persistence)
        {
            float3 sample = new float3(position.x, position.y, position.z) / Mathf.Max(0.1f, wavelength);
            sample += new float3(seed * 0.017f, seed * 0.031f, seed * 0.047f);
            float amplitude = 1f;
            float total = 0f;
            float normalization = 0f;
            int octaveCount = Mathf.Clamp(octaves, 1, 4);
            for (int octave = 0; octave < octaveCount; octave++)
            {
                total += noise.snoise(sample) * amplitude;
                normalization += amplitude;
                sample *= Mathf.Max(1f, lacunarity);
                amplitude *= Mathf.Clamp(persistence, 0.1f, 0.9f);
            }
            return normalization > 0f ? total / normalization : 0f;
        }

        private static List<PortalCarve> BuildPortalCarves(CaveRoute mainRoute, CaveRoute branches, TubeRings main, Transform outputSpace)
        {
            List<PortalCarve> result = new List<PortalCarve>();
            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                CaveRouteSplineDefinition branchDefinition = branches.Definitions.First(definition => definition.splineIndex == portal.branchSplineIndex);
                float portalDistance = ResolvePortalDistance(mainRoute, portal);
                float branchStartDistance = Mathf.Min(branches.Container.CalculateLength(portal.branchSplineIndex) - 3f,
                    branchDefinition.startTrimMeters + BranchCollarDepth);
                float branchStartT = branches.EvaluateTAtDistance(portal.branchSplineIndex, branchStartDistance);
                float branchWidth = branches.EvaluateWidth(portal.branchSplineIndex, branchStartT);
                float branchHeight = branches.EvaluateHeight(portal.branchSplineIndex, branchStartT);
                float halfLength = Mathf.Max(16f, Mathf.Max(portal.longitudinalHalfSize, branchWidth * 0.5f + 4f));
                int centerRing = FindClosest(main.distances, portalDistance);
                int ringStart = FindClosest(main.distances, portalDistance - halfLength);
                int ringEnd = FindClosest(main.distances, portalDistance + halfLength);
                ringStart = Mathf.Clamp(ringStart, 1, main.indices.Count - 3);
                ringEnd = Mathf.Clamp(ringEnd, ringStart + 2, main.indices.Count - 2);

                Vector3 direction = outputSpace.InverseTransformDirection(mainRoute.transform.TransformDirection(portal.direction)).normalized;
                float x = Vector3.Dot(direction, main.rights[centerRing]);
                float y = Vector3.Dot(direction, main.ups[centerRing]);
                float centerAngle = Mathf.Atan2(y, x);
                if (centerAngle < 0f) centerAngle += Mathf.PI * 2f;
                float t = main.normalizedTs[centerRing];
                float widthRadius = mainRoute.EvaluateWidth(0, t) * 0.5f;
                float heightRadius = mainRoute.EvaluateHeight(0, t) * 0.5f;
                float cos = Mathf.Cos(centerAngle);
                float sin = Mathf.Sin(centerAngle);
                float ellipseRadius = 1f / Mathf.Sqrt(cos * cos / (widthRadius * widthRadius) + sin * sin / (heightRadius * heightRadius));
                float desiredHalfAngle = Mathf.Asin(Mathf.Clamp((branchHeight * 0.5f + 3f) / Mathf.Max(1f, ellipseRadius), 0.1f, 0.95f));
                int sideQuads = Mathf.Clamp(Mathf.CeilToInt(desiredHalfAngle * 2f / (Mathf.PI * 2f / Sides)), 8, Sides / 2);
                int centerSide = Mathf.RoundToInt(centerAngle / (Mathf.PI * 2f) * Sides);
                int sideStart = centerSide - sideQuads / 2;
                result.Add(new PortalCarve
                {
                    portal = portal,
                    branchDefinition = branchDefinition,
                    ringStart = ringStart,
                    ringEnd = ringEnd,
                    sideStart = sideStart,
                    sideQuads = sideQuads,
                    branchStartDistance = branchStartDistance
                });
            }
            return result;
        }

        private static bool IsPortalQuad(int ring, int side, IReadOnlyList<PortalCarve> carves)
        {
            foreach (PortalCarve carve in carves)
            {
                if (ring < carve.ringStart || ring >= carve.ringEnd)
                    continue;
                int relative = Mod(side - carve.sideStart, Sides);
                if (relative < carve.sideQuads)
                    return true;
            }
            return false;
        }

        private static int[] ExtractBoundaryLoop(TubeRings main, PortalCarve carve)
        {
            List<int> loop = new List<int>();
            for (int sideOffset = 0; sideOffset <= carve.sideQuads; sideOffset++)
                loop.Add(main.indices[carve.ringStart][Mod(carve.sideStart + sideOffset, Sides)]);
            int sideEnd = Mod(carve.sideStart + carve.sideQuads, Sides);
            for (int ring = carve.ringStart + 1; ring <= carve.ringEnd; ring++)
                loop.Add(main.indices[ring][sideEnd]);
            for (int sideOffset = carve.sideQuads - 1; sideOffset >= 0; sideOffset--)
                loop.Add(main.indices[carve.ringEnd][Mod(carve.sideStart + sideOffset, Sides)]);
            int sideStart = Mod(carve.sideStart, Sides);
            for (int ring = carve.ringEnd - 1; ring > carve.ringStart; ring--)
                loop.Add(main.indices[ring][sideStart]);
            return loop.ToArray();
        }

        private static float[] BuildBoundaryAngles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> boundaryLoop,
            CaveRoute branchRoute, int splineIndex, float startDistance, Transform outputSpace)
        {
            float t = branchRoute.EvaluateTAtDistance(splineIndex, startDistance);
            Vector3 tangentWorld = ((Vector3)branchRoute.Container.EvaluateTangent(splineIndex, t)).normalized;
            Vector3 upWorld = Vector3.ProjectOnPlane(branchRoute.Container.EvaluateUpVector(splineIndex, t), tangentWorld).normalized;
            if (upWorld.sqrMagnitude < 0.01f) upWorld = Vector3.ProjectOnPlane(Vector3.up, tangentWorld).normalized;
            Vector3 rightWorld = Vector3.Cross(upWorld, tangentWorld).normalized;
            upWorld = Vector3.Cross(tangentWorld, rightWorld).normalized;
            Vector3 right = outputSpace.InverseTransformDirection(rightWorld).normalized;
            Vector3 up = outputSpace.InverseTransformDirection(upWorld).normalized;
            Vector3 centroid = Vector3.zero;
            foreach (int index in boundaryLoop) centroid += vertices[index];
            centroid /= boundaryLoop.Count;

            Vector2[] projected = new Vector2[boundaryLoop.Count];
            float signedArea = 0f;
            for (int i = 0; i < boundaryLoop.Count; i++)
            {
                Vector3 relative = vertices[boundaryLoop[i]] - centroid;
                projected[i] = new Vector2(Vector3.Dot(relative, right), Vector3.Dot(relative, up));
            }
            for (int i = 0; i < projected.Length; i++)
            {
                Vector2 a = projected[i];
                Vector2 b = projected[(i + 1) % projected.Length];
                signedArea += a.x * b.y - b.x * a.y;
            }

            float direction = signedArea >= 0f ? 1f : -1f;
            float[] angles = new float[boundaryLoop.Count];
            angles[0] = Mathf.Atan2(projected[0].y, projected[0].x);
            for (int i = 1; i < projected.Length; i++)
            {
                float angle = Mathf.Atan2(projected[i].y, projected[i].x);
                if (direction > 0f)
                    while (angle <= angles[i - 1]) angle += Mathf.PI * 2f;
                else
                    while (angle >= angles[i - 1]) angle -= Mathf.PI * 2f;
                angles[i] = angle;
            }

            return angles;
        }

        private static int[] CreateStraightSleeve(MeshBuilder builder, IReadOnlyList<int> boundaryLoop,
            CaveRoute branchRoute, int splineIndex, float branchStartDistance, Transform outputSpace)
        {
            Vector3 centroid = Vector3.zero;
            foreach (int index in boundaryLoop) centroid += builder.vertices[index];
            centroid /= boundaryLoop.Count;
            float t = branchRoute.EvaluateTAtDistance(splineIndex, branchStartDistance);
            Vector3 branchCenter = outputSpace.InverseTransformPoint(branchRoute.Container.EvaluatePosition(splineIndex, t));
            Vector3 direction = (branchCenter - centroid).normalized;
            int[] sleeve = new int[boundaryLoop.Count];
            for (int i = 0; i < boundaryLoop.Count; i++)
            {
                int source = boundaryLoop[i];
                sleeve[i] = builder.AddVertex(builder.vertices[source] + direction * StraightSleeveDepth,
                    builder.normals[source], builder.uvs[source]);
            }
            return sleeve;
        }

        private static void AddCollar(MeshBuilder builder, IReadOnlyList<int> mainLoop, IReadOnlyList<int> branchLoop)
        {
            for (int i = 0; i < mainLoop.Count; i++)
            {
                int next = (i + 1) % mainLoop.Count;
                int mainA = mainLoop[i];
                int mainB = mainLoop[next];
                int branchA = branchLoop[i];
                int branchB = branchLoop[next];
                builder.AddTriangle(mainA, branchA, mainB);
                builder.AddTriangle(mainB, branchA, branchB);
            }
        }

        private static void AddTubeQuad(MeshBuilder builder, IReadOnlyList<int> current, IReadOnlyList<int> next, int side)
        {
            int nextSide = (side + 1) % current.Count;
            int a = current[side];
            int b = current[nextSide];
            int c = next[side];
            int d = next[nextSide];
            builder.AddTriangle(a, c, b);
            builder.AddTriangle(b, c, d);
        }

        private static void AddCap(MeshBuilder builder, IReadOnlyList<int> ring, Vector3 center, Vector3 normal, bool startCap)
        {
            int centerIndex = builder.AddVertex(center, normal.normalized, new Vector2(0.5f, 0.5f));
            for (int side = 0; side < ring.Count; side++)
            {
                int a = ring[side];
                int b = ring[(side + 1) % ring.Count];
                if (startCap) builder.AddTriangle(centerIndex, a, b);
                else builder.AddTriangle(centerIndex, b, a);
            }
        }

        private static int FindClosest(IReadOnlyList<float> values, float target)
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < values.Count; i++)
            {
                float distance = Mathf.Abs(values[i] - target);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        private static float ResolvePortalDistance(CaveRoute mainRoute, CavePortalDefinition portal)
        {
            if (portal.mainDistanceMeters >= 0f)
                return portal.mainDistanceMeters;
            float t = mainRoute.ResolvePortalT(portal);
            return ApproximateLength(mainRoute.Container, 0, 0f, t, 256);
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

        private static int Mod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
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

        private static void PruneLegacyMeshAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { GeneratedAssetFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == VisualMeshPath || path == ColliderMeshPath) continue;
                AssetDatabase.DeleteAsset(path);
            }
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

    public static class CaveMeshTopologyAnalyzer
    {
        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int min;
            public readonly int max;

            public EdgeKey(int a, int b)
            {
                min = Mathf.Min(a, b);
                max = Mathf.Max(a, b);
            }

            public bool Equals(EdgeKey other) => min == other.min && max == other.max;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(min * 397 ^ max);
        }

        private sealed class EdgeUse
        {
            public int count;
            public int directionBalance;
        }

        public static CaveMeshTopologyReport Analyze(Mesh mesh)
        {
            CaveMeshTopologyReport report = new CaveMeshTopologyReport();
            if (mesh == null) return report;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Dictionary<EdgeKey, EdgeUse> edges = new Dictionary<EdgeKey, EdgeUse>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a == b || b == c || c == a || Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 0.000001f)
                    report.degenerateTriangleCount++;
                AddEdge(edges, a, b);
                AddEdge(edges, b, c);
                AddEdge(edges, c, a);
            }

            Dictionary<int, List<int>> boundaryGraph = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<EdgeKey, EdgeUse> pair in edges)
            {
                if (pair.Value.count == 1)
                {
                    report.boundaryEdgeCount++;
                    AddNeighbor(boundaryGraph, pair.Key.min, pair.Key.max);
                    AddNeighbor(boundaryGraph, pair.Key.max, pair.Key.min);
                }
                else if (pair.Value.count != 2)
                {
                    report.nonManifoldEdgeCount++;
                }
                else if (pair.Value.directionBalance != 0)
                {
                    report.windingMismatchCount++;
                }
            }

            HashSet<int> visited = new HashSet<int>();
            foreach (int vertex in boundaryGraph.Keys)
            {
                if (!visited.Add(vertex)) continue;
                report.boundaryLoopCount++;
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(vertex);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    foreach (int neighbor in boundaryGraph[current])
                    {
                        if (visited.Add(neighbor)) queue.Enqueue(neighbor);
                    }
                }
            }
            return report;
        }

        private static void AddEdge(Dictionary<EdgeKey, EdgeUse> edges, int from, int to)
        {
            EdgeKey key = new EdgeKey(from, to);
            if (!edges.TryGetValue(key, out EdgeUse use))
            {
                use = new EdgeUse();
                edges.Add(key, use);
            }
            use.count++;
            use.directionBalance += from == key.min ? 1 : -1;
        }

        private static void AddNeighbor(Dictionary<int, List<int>> graph, int from, int to)
        {
            if (!graph.TryGetValue(from, out List<int> neighbors))
            {
                neighbors = new List<int>();
                graph.Add(from, neighbors);
            }
            neighbors.Add(to);
        }
    }
}
