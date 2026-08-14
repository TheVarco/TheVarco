using System.Collections.Generic;
using System.IO;
using System.Linq;
using CaveBlockout.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Tests
{
    public sealed class CaveBlockoutTests
    {
        [SetUp]
        public void OpenGeneratedMap()
        {
            EditorSceneManager.OpenScene(CaveBlockoutBuilder.MainMapPath, OpenSceneMode.Single);
        }

        [Test]
        public void MainRoute_SatisfiesGuideMetrics()
        {
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            CaveValidationResult result = CaveBlockoutValidator.Validate(mainRoute, branches);

            Assert.That(result.routeLength, Is.InRange(550f, 600f));
            Assert.That(result.totalRise, Is.InRange(259f, 261f));
            Assert.That(result.minimumWidth, Is.GreaterThanOrEqualTo(10f));
            Assert.That(result.minimumHeight, Is.GreaterThanOrEqualTo(8f));
            Assert.That(result.maximumSlope, Is.LessThanOrEqualTo(30.5f));
            Assert.That(result.minimumTurnRadius, Is.GreaterThanOrEqualTo(14f));
            Assert.That(result.maximumReverseDrop, Is.LessThanOrEqualTo(3f));
            Assert.That(result.branchCount, Is.EqualTo(3));
            Assert.That(result.issues, Is.Empty, string.Join("\n", result.issues));
        }

        [Test]
        public void Routes_StoreEditableWidthHeightZoneRollAndPortalData()
        {
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            Spline mainSpline = mainRoute.Container[0];
            AssertEmbeddedData(mainSpline, true);
            Assert.That(mainRoute.Portals.Select(portal => portal.zoneId), Is.EquivalentTo(new[] { "Z2", "Z4", "Z5" }));
            Assert.That(mainRoute.NoiseSettings.enabled, Is.True);
            Assert.That(mainRoute.NoiseSettings.amplitudeMeters, Is.EqualTo(3.2f).Within(0.001f));
            Assert.That(mainRoute.NoiseSettings.strengthGain, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(mainRoute.NoiseSettings.maximumDisplacementRatio, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(mainRoute.NoiseSettings.visualDetailEnabled, Is.True);
            Assert.That(mainRoute.NoiseSettings.visualDetailAmplitude, Is.EqualTo(0.4f).Within(0.001f));

            Assert.That(branches.Container.Splines.Count, Is.EqualTo(3));
            for (int i = 0; i < branches.Container.Splines.Count; i++)
                AssertEmbeddedData(branches.Container[i], false);
            Assert.That(branches.Definitions.All(definition => definition.sections.Single().capEnd), Is.True);
            Assert.That(branches.NoiseSettings.enabled, Is.False);
            Assert.That(branches.NoiseSettings.amplitudeMeters, Is.Zero.Within(0.001f));
            Assert.That(branches.NoiseSettings.visualDetailEnabled, Is.False);
        }

        [Test]
        public void MainRoute_IsOneContinuousZ1ToZ6Centerline()
        {
            FindRoutes(out CaveRoute mainRoute, out _);
            CaveRouteSplineDefinition definition = mainRoute.Definitions.Single(route => route.isMainRoute);

            Assert.That(definition.sections.Count, Is.EqualTo(6));
            for (int i = 0; i < definition.sections.Count; i++)
            {
                CaveRouteSection section = definition.sections[i];
                Assert.That(section.zoneId, Is.EqualTo("Z" + (i + 1)));
                Assert.That(section.startKnot, Is.EqualTo(i == 0 ? 0 : definition.sections[i - 1].endKnot));
                Assert.That(section.endDistanceMeters, Is.GreaterThan(section.startDistanceMeters));
                if (i > 0)
                    Assert.That(section.startDistanceMeters, Is.EqualTo(definition.sections[i - 1].endDistanceMeters).Within(0.05f));
            }
            Assert.That(definition.sections.Last().endKnot, Is.EqualTo(mainRoute.Container[0].Count - 1));
        }

        [Test]
        public void ResourceBranches_HaveNoTrimmedCentrelineGapAtTheirPortals()
        {
            FindRoutes(out _, out CaveRoute branches);
            foreach (CaveRouteSplineDefinition definition in branches.Definitions)
            {
                Assert.That(definition.startTrimMeters, Is.Zero.Within(0.001f), definition.routeId);
                Assert.That(definition.sections.Single().startKnot, Is.Zero, definition.routeId);
                Assert.That(definition.sections.Single().endKnot, Is.EqualTo(branches.Container[definition.splineIndex].Count - 1), definition.routeId);
            }
        }

        [Test]
        public void GeneratedMeshes_AreSeparateValidAndWithinBudget()
        {
            GameObject root = GameObject.Find(CaveBlockoutBuilder.RootName);
            Transform generated = root.transform.Find("Generated");
            MeshFilter[] filters = generated.GetComponentsInChildren<MeshFilter>(true);
            MeshCollider[] colliders = generated.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(filters.Length, Is.EqualTo(1));
            Assert.That(colliders.Length, Is.EqualTo(1));

            int triangles = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh visual = filters[i].sharedMesh;
                Mesh collision = filters[i].GetComponent<MeshCollider>().sharedMesh;
                Assert.That(visual, Is.Not.Null);
                Assert.That(collision, Is.Not.Null);
                Assert.That(visual, Is.Not.SameAs(collision));
                Assert.That(visual.vertexCount, Is.GreaterThan(0));
                Assert.That(collision.vertexCount, Is.GreaterThan(0));
                Assert.That(collision.vertexCount, Is.EqualTo(visual.vertexCount));
                CollectionAssert.AreEqual(visual.triangles, collision.triangles);
                Assert.That(visual.normals.All(normal => normal.sqrMagnitude > 0.9f), Is.True);
                float maximumTriangleEdge = 0f;
                Vector3[] vertices = visual.vertices;
                int[] meshTriangles = visual.triangles;
                for (int triangle = 0; triangle < meshTriangles.Length; triangle += 3)
                {
                    Vector3 a = vertices[meshTriangles[triangle]];
                    Vector3 b = vertices[meshTriangles[triangle + 1]];
                    Vector3 c = vertices[meshTriangles[triangle + 2]];
                    maximumTriangleEdge = Mathf.Max(maximumTriangleEdge,
                        Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a));
                }
                Assert.That(maximumTriangleEdge, Is.LessThan(18f),
                    "An oversized triangle usually means a portal correspondence crossed the junction.");
                triangles += visual.triangles.Length / 3;

                CaveMeshTopologyReport topology = CaveMeshTopologyAnalyzer.Analyze(visual);
                Assert.That(topology.boundaryLoopCount, Is.EqualTo(1), "Only the Z6 exit may remain open.");
                Assert.That(topology.boundaryEdgeCount, Is.EqualTo(CaveMeshGenerator.Sides));
                Assert.That(topology.nonManifoldEdgeCount, Is.Zero);
                Assert.That(topology.windingMismatchCount, Is.Zero);
                Assert.That(topology.degenerateTriangleCount, Is.Zero);
            }

            Assert.That(triangles, Is.LessThan(50000));
            CaveValidationSummary summary = Object.FindFirstObjectByType<CaveValidationSummary>(FindObjectsInactive.Include);
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.triangleCount, Is.EqualTo(triangles));
            Assert.That(summary.boundaryLoopCount, Is.EqualTo(1));
        }

        [Test]
        public void SubmarineProxy_ClearsMainAndBranchRoutesInBothDirections()
        {
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            bool passed = CaveClearanceValidator.ValidateAll(mainRoute, branches, out string details);
            Assert.That(passed, Is.True, details);
        }

        [Test]
        public void BranchDeadEnds_UseRoundedCapsBeyondSplineEndpoints()
        {
            FindRoutes(out _, out CaveRoute branches);
            MeshFilter filter = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated")
                .GetComponentInChildren<MeshFilter>(true);
            Vector3[] vertices = filter.sharedMesh.vertices
                .Select(filter.transform.TransformPoint).ToArray();

            foreach (CaveRouteSplineDefinition definition in branches.Definitions)
            {
                int splineIndex = definition.splineIndex;
                Vector3 endCenter = branches.Container.EvaluatePosition(splineIndex, 1f);
                Vector3 endTangent = ((Vector3)branches.Container.EvaluateTangent(splineIndex, 1f)).normalized;
                float maximumRadius = Mathf.Max(branches.EvaluateWidth(splineIndex, 1f),
                    branches.EvaluateHeight(splineIndex, 1f)) * 0.5f + 1f;

                List<float> capOffsets = vertices.Select(vertex => vertex - endCenter)
                    .Where(relative => Vector3.Dot(relative, endTangent) > 0.2f)
                    .Where(relative => Vector3.ProjectOnPlane(relative, endTangent).magnitude <= maximumRadius)
                    .Select(relative => Vector3.Dot(relative, endTangent))
                    .Where(offset => offset < 15f)
                    .OrderBy(offset => offset)
                    .ToList();

                Assert.That(capOffsets, Is.Not.Empty, definition.routeId + " has no geometry beyond its endpoint.");
                Assert.That(capOffsets.Max(), Is.GreaterThanOrEqualTo(3f),
                    definition.routeId + " still looks like a flat end cap.");
                int distinctLatitudes = capOffsets.Select(offset => Mathf.RoundToInt(offset * 4f)).Distinct().Count();
                Assert.That(distinctLatitudes, Is.GreaterThanOrEqualTo(CaveMeshGenerator.RoundedCapLatitudeCount),
                    definition.routeId + " does not contain enough rounded cap bands.");
            }
        }

        [Test]
        public void Regeneration_IsDeterministicAndPreservesSceneObjects()
        {
            Camera camera = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            Light light = Object.FindFirstObjectByType<Light>(FindObjectsInactive.Include);
            MeshFilter beforeFilter = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated")
                .GetComponentInChildren<MeshFilter>(true);
            Vector3[] beforeVertices = beforeFilter.sharedMesh.vertices;
            int[] beforeTriangles = beforeFilter.sharedMesh.triangles;

            CaveBlockoutBuilder.RegenerateCurrentScene(false);

            MeshFilter afterFilter = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated")
                .GetComponentInChildren<MeshFilter>(true);
            CollectionAssert.AreEqual(beforeVertices, afterFilter.sharedMesh.vertices);
            CollectionAssert.AreEqual(beforeTriangles, afterFilter.sharedMesh.triangles);
            Assert.That(Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include), Is.SameAs(camera));
            Assert.That(Object.FindFirstObjectByType<Light>(FindObjectsInactive.Include), Is.SameAs(light));
        }

        [Test]
        public void NoiseToggle_RefreshesTheLiveVisualMeshAndRestoresExactSmoothGeometry()
        {
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            Transform generated = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated");
            MeshFilter filter = generated.GetComponentInChildren<MeshFilter>(true);
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            Mesh liveVisualAsset = filter.sharedMesh;
            Vector3[] serializedStrongVertices = filter.sharedMesh.vertices;

            mainRoute.NoiseSettings.ApplySmoothPreset();
            branches.NoiseSettings.ApplySmoothPreset();
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            filter = generated.GetComponentInChildren<MeshFilter>(true);
            collider = filter.GetComponent<MeshCollider>();
            Assert.That(filter.sharedMesh, Is.SameAs(liveVisualAsset),
                "Regeneration must refresh the existing asset so its GUID and live renderer reference remain stable.");
            Vector3[] smoothVertices = filter.sharedMesh.vertices;
            CollectionAssert.AreEqual(smoothVertices, collider.sharedMesh.vertices,
                "Disabling all noise must produce identical visual and collision geometry.");

            float totalLength = mainRoute.Container.CalculateLength(0);
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, 0f), Is.Zero.Within(0.0001f));
            // Smooth leaves exitRimNoiseWeight at 0, so the exit is a clean ellipse under this preset.
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, totalLength), Is.Zero.Within(0.0001f));

            mainRoute.NoiseSettings.ApplyStrongPreset();

            // The two route ends are NOT symmetric, and this is the guard for that.
            //
            // The start is welded to a rounded cap: displacing its first ring tears the cap off, so its
            // weight must stay exactly 0 no matter which preset is loaded.
            //
            // The end is the Z6 exit - an open mouth welded to nothing. It used to be forced to 0 by the
            // same shared portalFadeDistance term, which is what made the opening a mathematically
            // perfect ellipse. Strong now gives it a floor so the rim is rough. If someone "tidies up"
            // by collapsing these back into one symmetric term, one of these two assertions fails.
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, 0f), Is.Zero.Within(0.0001f),
                "The route start is welded to a rounded cap and must never take noise.");
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, totalLength),
                Is.EqualTo(mainRoute.NoiseSettings.exitRimNoiseWeight).Within(0.0001f),
                "The exit rim must carry exactly the configured exitRimNoiseWeight.");
            Assert.That(mainRoute.NoiseSettings.exitRimNoiseWeight, Is.GreaterThan(0f),
                "Strong is the shipping main-route preset; a zero rim weight means the exit is a clean ellipse again.");
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            filter = generated.GetComponentInChildren<MeshFilter>(true);
            collider = filter.GetComponent<MeshCollider>();
            Vector3[] strongVisualVertices = filter.sharedMesh.vertices;
            Vector3[] strongColliderVertices = collider.sharedMesh.vertices;
            Assert.That(filter.sharedMesh, Is.SameAs(liveVisualAsset));
            Assert.That(strongVisualVertices.SequenceEqual(smoothVertices), Is.False,
                "Enabling strong noise must immediately change the live visual mesh.");
            Assert.That(strongVisualVertices.SequenceEqual(strongColliderVertices), Is.False,
                "Visual-only detail must not leak into collision geometry.");
            float maximumVisualDetail = strongVisualVertices.Zip(strongColliderVertices, Vector3.Distance).Max();
            Assert.That(maximumVisualDetail, Is.GreaterThan(0.01f));
            Assert.That(maximumVisualDetail, Is.LessThanOrEqualTo(mainRoute.NoiseSettings.visualDetailAmplitude + 0.001f),
                "Visual and collider meshes may differ only by the configured visual-detail displacement.");
            AssertBranchesContainStructuralNoise(branches, filter.transform, smoothVertices, strongColliderVertices,
                mainRoute.NoiseSettings);

            mainRoute.NoiseSettings.ApplySmoothPreset();
            branches.NoiseSettings.ApplySmoothPreset();
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            filter = generated.GetComponentInChildren<MeshFilter>(true);
            collider = filter.GetComponent<MeshCollider>();
            Assert.That(filter.sharedMesh, Is.SameAs(liveVisualAsset));
            CollectionAssert.AreEqual(smoothVertices, filter.sharedMesh.vertices,
                "Disabling noise again must restore the exact smooth visual mesh without an asset reload.");
            CollectionAssert.AreEqual(filter.sharedMesh.vertices, collider.sharedMesh.vertices);

            // Generated mesh assets are shared across tests. Restore the scene's serialized Strong default so
            // test ordering cannot leave a smooth asset behind for the deterministic-regeneration check.
            mainRoute.NoiseSettings.ApplyStrongPreset();
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            filter = generated.GetComponentInChildren<MeshFilter>(true);
            CollectionAssert.AreEqual(serializedStrongVertices, filter.sharedMesh.vertices,
                "The test must restore the exact Strong geometry serialized by MainMap.");
        }

        [Test]
        public void PortalNoiseMask_ProtectsOnlyTheLocalEllipticalAperture()
        {
            const float portalDistance = 100f;
            const float longitudinalHalfLength = 12f;
            const float centerAngle = 0.35f;
            const float angularHalfWidth = 0.6f;
            const float angularPhysicalHalfLength = 8f;
            const float fadeDistance = 5f;

            Assert.That(CaveMeshGenerator.EvaluatePortalNoiseWeight(
                portalDistance, centerAngle, portalDistance, longitudinalHalfLength, centerAngle,
                angularHalfWidth, angularPhysicalHalfLength, fadeDistance), Is.Zero.Within(0.0001f));
            Assert.That(CaveMeshGenerator.EvaluatePortalNoiseWeight(
                portalDistance + longitudinalHalfLength, centerAngle, portalDistance, longitudinalHalfLength,
                centerAngle, angularHalfWidth, angularPhysicalHalfLength, fadeDistance), Is.Zero.Within(0.0001f),
                "The aperture boundary must remain welded and noise-free.");
            Assert.That(CaveMeshGenerator.EvaluatePortalNoiseWeight(
                portalDistance, centerAngle + Mathf.PI, portalDistance, longitudinalHalfLength, centerAngle,
                angularHalfWidth, angularPhysicalHalfLength, fadeDistance), Is.GreaterThan(0.99f),
                "The opposite cave wall must keep Strong noise at the same longitudinal distance.");
            Assert.That(CaveMeshGenerator.EvaluatePortalNoiseWeight(
                portalDistance + longitudinalHalfLength + fadeDistance, centerAngle, portalDistance,
                longitudinalHalfLength, centerAngle, angularHalfWidth, angularPhysicalHalfLength, fadeDistance),
                Is.GreaterThan(0.99f), "Noise must be fully restored five metres beyond the aperture boundary.");
        }

        [Test]
        public void RoughNoisePreset_StaysWithinMeshSamplingBandwidth()
        {
            CaveNoiseSettings settings = new CaveNoiseSettings();
            settings.ApplyRoughPreset();
            float shortestStructuralWavelength = settings.wavelengthMeters /
                                                 Mathf.Pow(settings.lacunarity, settings.octaves - 1);
            Assert.That(shortestStructuralWavelength, Is.GreaterThanOrEqualTo(CaveMeshGenerator.SampleSpacing * 2f));
            Assert.That(settings.visualDetailWavelength, Is.GreaterThanOrEqualTo(CaveMeshGenerator.SampleSpacing * 2f));
        }

        [Test]
        public void RotationMinimizingFrame_DoesNotCorkscrewAlongHorizontalTurns()
        {
            Vector3 previousTangent = Vector3.forward;
            Vector3 up = Vector3.up;
            for (int step = 1; step <= 72; step++)
            {
                float heading = step * 5f;
                Vector3 tangent = Quaternion.AngleAxis(heading, Vector3.up) * Vector3.forward;
                up = CaveMeshGenerator.TransportRotationMinimizingUp(previousTangent, up, tangent);

                Assert.That(Vector3.Dot(up, Vector3.up), Is.GreaterThan(0.9999f),
                    $"The frame accumulated unintended roll at heading {heading:F0} degrees.");
                Assert.That(Mathf.Abs(Vector3.Dot(up, tangent)), Is.LessThan(0.0001f));
                previousTangent = tangent;
            }
        }

        [Test]
        public void WidthAndHeightProfiles_AreSmoothAtKnotsAndNeverOvershoot()
        {
            FindRoutes(out CaveRoute mainRoute, out _);
            Spline spline = mainRoute.Container[0];

            for (int segment = 0; segment < spline.Count - 1; segment++)
            {
                float startT = spline.ConvertIndexUnit(segment, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                float endT = spline.ConvertIndexUnit(segment + 1, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                float startWidth = mainRoute.EvaluateWidth(0, startT);
                float endWidth = mainRoute.EvaluateWidth(0, endT);
                float startHeight = mainRoute.EvaluateHeight(0, startT);
                float endHeight = mainRoute.EvaluateHeight(0, endT);
                for (int sample = 0; sample <= 16; sample++)
                {
                    float t = Mathf.Lerp(startT, endT, sample / 16f);
                    Assert.That(mainRoute.EvaluateWidth(0, t),
                        Is.InRange(Mathf.Min(startWidth, endWidth) - 0.001f,
                            Mathf.Max(startWidth, endWidth) + 0.001f));
                    Assert.That(mainRoute.EvaluateHeight(0, t),
                        Is.InRange(Mathf.Min(startHeight, endHeight) - 0.001f,
                            Mathf.Max(startHeight, endHeight) + 0.001f));
                }
            }

            for (int knot = 1; knot < spline.Count - 1; knot++)
            {
                float previousT = spline.ConvertIndexUnit(knot - 1, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                float knotT = spline.ConvertIndexUnit(knot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                float nextT = spline.ConvertIndexUnit(knot + 1, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                AssertProfileSlopeSettlesAtKnot(t => mainRoute.EvaluateWidth(0, t), previousT, knotT, nextT,
                    $"width at knot {knot}");
                AssertProfileSlopeSettlesAtKnot(t => mainRoute.EvaluateHeight(0, t), previousT, knotT, nextT,
                    $"height at knot {knot}");
            }
        }

        [Test]
        public void StrongNoisePreset_IsMateriallyStrongerAndKeepsTheMinimumSafeRadius()
        {
            CaveNoiseSettings rough = new CaveNoiseSettings();
            rough.ApplyRoughPreset();
            CaveNoiseSettings strong = new CaveNoiseSettings();
            strong.ApplyStrongPreset();

            Assert.That(strong.amplitudeMeters, Is.GreaterThan(rough.amplitudeMeters));
            Assert.That(strong.strengthGain, Is.GreaterThan(rough.strengthGain));
            Assert.That(strong.maximumDisplacementRatio, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(strong.visualDetailAmplitude, Is.GreaterThan(rough.visualDetailAmplitude));
            float shortestStructuralWavelength = strong.wavelengthMeters /
                                                 Mathf.Pow(strong.lacunarity, strong.octaves - 1);
            Assert.That(shortestStructuralWavelength, Is.GreaterThanOrEqualTo(CaveMeshGenerator.SampleSpacing * 2f));
            float minimumBranchRadius = 5f;
            float maximumInwardDisplacement = Mathf.Min(strong.amplitudeMeters,
                minimumBranchRadius * strong.maximumDisplacementRatio,
                minimumBranchRadius - CaveMeshGenerator.MinimumNoiseClearanceRadius);
            Assert.That(minimumBranchRadius - maximumInwardDisplacement,
                Is.GreaterThanOrEqualTo(CaveMeshGenerator.MinimumNoiseClearanceRadius));
        }

        [Test]
        public void MidpointInsertion_PreservesCurveMetadataUndoAndGeneratedTopology()
        {
            FindRoutes(out CaveRoute mainRoute, out _);
            const int splineIndex = 0;
            const int segmentIndex = 1;
            Spline spline = mainRoute.Container[splineIndex];
            int originalKnotCount = spline.Count;
            float originalLength = spline.GetLength();
            BezierCurve originalCurve = spline.GetCurve(segmentIndex);
            float expectedCurveT = spline.GetCurveInterpolation(segmentIndex, spline.GetCurveLength(segmentIndex) * 0.5f);
            Assert.That(spline.TryGetFloatData(CaveRoute.WidthDataKey, out SplineData<float> widths), Is.True);
            Assert.That(spline.TryGetFloatData(CaveRoute.HeightDataKey, out SplineData<float> heights), Is.True);
            float expectedWidth = widths.Evaluate(spline, segmentIndex + expectedCurveT, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
            float expectedHeight = heights.Evaluate(spline, segmentIndex + expectedCurveT, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
            float[] sectionDistances = mainRoute.Definitions.SelectMany(definition => definition.sections)
                .SelectMany(section => new[] { section.startDistanceMeters, section.endDistanceMeters }).ToArray();
            float[] portalDistances = mainRoute.Portals.Select(portal => portal.mainDistanceMeters).ToArray();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Test cave midpoint insertion");
            try
            {
                CaveRouteKnotInsertionResult result = CaveRouteEditingUtility.InsertKnotAtSegmentMidpoint(
                    mainRoute, splineIndex, segmentIndex, false);

                Assert.That(result.knotIndex, Is.EqualTo(segmentIndex + 1));
                Assert.That(result.curveT, Is.EqualTo(expectedCurveT).Within(0.0001f));
                Assert.That(spline.Count, Is.EqualTo(originalKnotCount + 1));
                Assert.That(spline.GetLength(), Is.EqualTo(originalLength).Within(0.002f),
                    "Adding an editing point must not move or resize the authored route.");

                BezierCurve left = spline.GetCurve(segmentIndex);
                BezierCurve right = spline.GetCurve(segmentIndex + 1);
                for (int i = 0; i <= 32; i++)
                {
                    float sourceT = i / 32f;
                    Vector3 expected = CurveUtility.EvaluatePosition(originalCurve, sourceT);
                    Vector3 actual = sourceT <= result.curveT
                        ? CurveUtility.EvaluatePosition(left, sourceT / result.curveT)
                        : CurveUtility.EvaluatePosition(right, (sourceT - result.curveT) / (1f - result.curveT));
                    Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.002f));
                }

                AssertEmbeddedData(spline, true);
                Assert.That(GetDataValueAtKnot(widths, result.knotIndex), Is.EqualTo(expectedWidth).Within(0.001f));
                Assert.That(GetDataValueAtKnot(heights, result.knotIndex), Is.EqualTo(expectedHeight).Within(0.001f));
                Assert.That(spline.TryGetIntData(CaveRoute.PortalDataKey, out SplineData<int> portals), Is.True);
                Assert.That(GetDataValueAtKnot(portals, result.knotIndex), Is.Zero,
                    "A midpoint must not accidentally become another branch portal.");
                CollectionAssert.AreEqual(sectionDistances, mainRoute.Definitions.SelectMany(definition => definition.sections)
                    .SelectMany(section => new[] { section.startDistanceMeters, section.endDistanceMeters }).ToArray());
                CollectionAssert.AreEqual(portalDistances,
                    mainRoute.Portals.Select(portal => portal.mainDistanceMeters).ToArray());

                CaveBlockoutBuilder.RegenerateCurrentScene(false);
                Mesh generated = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated")
                    .GetComponentInChildren<MeshFilter>(true).sharedMesh;
                CaveMeshTopologyReport topology = CaveMeshTopologyAnalyzer.Analyze(generated);
                Assert.That(topology.IsValidOpenCave, Is.True);
                Assert.That(topology.boundaryLoopCount, Is.EqualTo(1));
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
                Assert.That(mainRoute.Container[splineIndex].Count, Is.EqualTo(originalKnotCount),
                    "Ctrl+Z must restore the original spline and metadata.");
                // Reload the serialized source before restoring generated assets. Undo can retain harmless
                // sub-float tangent differences that should not become the baseline for the next test.
                EditorSceneManager.OpenScene(CaveBlockoutBuilder.MainMapPath, OpenSceneMode.Single);
                CaveBlockoutBuilder.RegenerateCurrentScene(false);
            }
        }

        [Test]
        public void ReviewCapture_WritesExpectedEvidenceWithoutDirtyingScene()
        {
            FindRoutes(out CaveRoute mainRoute, out CaveRoute branches);
            List<CaveReviewViewpoint> viewpoints = CaveReviewCapture.BuildViewpoints(mainRoute, branches);
            Assert.That(viewpoints.Count, Is.EqualTo(36));
            Assert.That(viewpoints.Select(view => view.name), Is.Unique);

            bool wasDirty = EditorSceneManager.GetActiveScene().isDirty;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(projectRoot, CaveReviewCapture.ArtifactFolder, "EditMode-" + System.Guid.NewGuid().ToString("N"));
            CaveReviewCaptureResult result = CaveReviewCapture.CaptureCurrentScene(output);

            Assert.That(result.shotCount, Is.EqualTo(viewpoints.Count));
            Assert.That(File.Exists(result.manifestPath), Is.True);
            Assert.That(new FileInfo(result.manifestPath).Length, Is.GreaterThan(100));
            Assert.That(File.Exists(result.contactSheetPath), Is.True);
            Assert.That(new FileInfo(result.contactSheetPath).Length, Is.GreaterThan(1000));
            CaveReviewManifest manifest = JsonUtility.FromJson<CaveReviewManifest>(File.ReadAllText(result.manifestPath));
            Assert.That(manifest.shots.Count, Is.EqualTo(viewpoints.Count));
            Assert.That(manifest.shots.All(shot => shot.sha256.Length == 64), Is.True);
            Assert.That(manifest.shots.Select(shot => shot.sha256).Distinct().Count(), Is.GreaterThan(viewpoints.Count / 2),
                "Review renders are near-identical; output may be blank or headless.");
            Assert.That(manifest.shots.All(shot => File.Exists(Path.Combine(output, shot.file))), Is.True);
            Assert.That(manifest.shots.All(shot => new FileInfo(Path.Combine(output, shot.file)).Length > 1000), Is.True);
            Assert.That(manifest.noise.seed, Is.EqualTo(mainRoute.NoiseSettings.seed));
            Assert.That(manifest.noise.amplitudeMeters, Is.EqualTo(mainRoute.NoiseSettings.amplitudeMeters).Within(0.001f));
            Assert.That(manifest.noise.strengthGain, Is.EqualTo(mainRoute.NoiseSettings.strengthGain).Within(0.001f));
            Assert.That(manifest.noise.maximumDisplacementRatio,
                Is.EqualTo(mainRoute.NoiseSettings.maximumDisplacementRatio).Within(0.001f));
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.EqualTo(wasDirty));
        }

        [Test]
        public void PlaytestHierarchy_ContainsBothMoversAndSharedCamera()
        {
            GameObject root = GameObject.Find(CaveBlockoutBuilder.RootName);
            Transform playtest = root.transform.Find("Playtest");
            Assert.That(playtest.Find("SubmarineProxy"), Is.Not.Null);
            Assert.That(playtest.Find("OtterPlayer"), Is.Not.Null);
            Assert.That(playtest.Find("CameraRig"), Is.Not.Null);
            Assert.That(playtest.GetComponent<CavePlaytestSwitcher>(), Is.Not.Null);
            Assert.That(playtest.Find("SubmarineProxy").localScale, Is.EqualTo(new Vector3(3f, 3f, 6f)));
            Assert.That(playtest.Find("OtterPlayer").GetComponents<MonoBehaviour>().Any(component => component.GetType().Name == "PlayerController"), Is.True);
        }

        private static void AssertEmbeddedData(Spline spline, bool expectPortals)
        {
            Assert.That(spline.TryGetFloatData(CaveRoute.WidthDataKey, out SplineData<float> widths), Is.True);
            Assert.That(spline.TryGetFloatData(CaveRoute.HeightDataKey, out SplineData<float> heights), Is.True);
            Assert.That(spline.TryGetFloatData(CaveRoute.RollDataKey, out SplineData<float> rolls), Is.True);
            Assert.That(spline.TryGetIntData(CaveRoute.ZoneDataKey, out SplineData<int> zones), Is.True);
            Assert.That(spline.TryGetIntData(CaveRoute.PortalDataKey, out SplineData<int> portals), Is.True);
            Assert.That(widths.Count, Is.EqualTo(spline.Count));
            Assert.That(heights.Count, Is.EqualTo(spline.Count));
            Assert.That(rolls.Count, Is.EqualTo(spline.Count));
            Assert.That(zones.Count, Is.EqualTo(spline.Count));
            Assert.That(portals.Any(point => point.Value > 0), Is.EqualTo(expectPortals));
        }

        private static T GetDataValueAtKnot<T>(SplineData<T> data, int knotIndex)
        {
            for (int i = 0; i < data.Count; i++)
            {
                if (Mathf.Approximately(data[i].Index, knotIndex))
                    return data[i].Value;
            }
            Assert.Fail($"No spline data point exists at knot {knotIndex}.");
            return default;
        }

        private static void AssertBranchesContainStructuralNoise(CaveRoute branches, Transform meshTransform,
            IReadOnlyList<Vector3> smoothVertices, IReadOnlyList<Vector3> colliderVertices,
            CaveNoiseSettings settings)
        {
            foreach (CaveRouteSplineDefinition definition in branches.Definitions)
            {
                float branchLength = branches.Container.CalculateLength(definition.splineIndex);
                float start = Mathf.Min(branchLength - 3f,
                    definition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth);
                float midpointDistance = (start + branchLength) * 0.5f;
                float midpointT = branches.EvaluateTAtDistance(definition.splineIndex, midpointDistance);
                Vector3 center = branches.Container.EvaluatePosition(definition.splineIndex, midpointT);
                Vector3 tangent = ((Vector3)branches.Container.EvaluateTangent(definition.splineIndex, midpointT)).normalized;
                float searchRadius = Mathf.Max(branches.EvaluateWidth(definition.splineIndex, midpointT),
                    branches.EvaluateHeight(definition.splineIndex, midpointT)) * 0.5f + settings.amplitudeMeters + 2f;
                float maximumDisplacement = 0f;
                int sampleCount = 0;
                for (int vertex = 0; vertex < smoothVertices.Count; vertex++)
                {
                    Vector3 smoothWorld = meshTransform.TransformPoint(smoothVertices[vertex]);
                    Vector3 relative = smoothWorld - center;
                    if (Mathf.Abs(Vector3.Dot(relative, tangent)) > CaveMeshGenerator.SampleSpacing * 1.1f ||
                        Vector3.ProjectOnPlane(relative, tangent).magnitude > searchRadius)
                        continue;

                    sampleCount++;
                    Vector3 colliderWorld = meshTransform.TransformPoint(colliderVertices[vertex]);
                    maximumDisplacement = Mathf.Max(maximumDisplacement,
                        Vector3.Distance(smoothWorld, colliderWorld));
                }

                Assert.That(CaveMeshGenerator.EvaluateBranchNoiseWeight(
                    settings, midpointDistance, start, branchLength), Is.GreaterThan(0.99f));
                Assert.That(sampleCount, Is.GreaterThan(0), definition.routeId + " has no sampled midpoint vertices.");
                Assert.That(maximumDisplacement, Is.GreaterThan(0.05f),
                    definition.routeId + " remains smooth in the middle despite Strong structural noise.");
            }
        }

        private static void AssertProfileSlopeSettlesAtKnot(System.Func<float, float> evaluate,
            float previousT, float knotT, float nextT, string label)
        {
            float h = Mathf.Min(knotT - previousT, nextT - knotT) * 0.02f;
            float center = evaluate(knotT);
            AssertQuadraticConvergence(Mathf.Abs(center - evaluate(knotT - h)),
                Mathf.Abs(center - evaluate(knotT - h * 0.5f)), label + " from the left");
            AssertQuadraticConvergence(Mathf.Abs(evaluate(knotT + h) - center),
                Mathf.Abs(evaluate(knotT + h * 0.5f) - center), label + " from the right");
        }

        private static void AssertQuadraticConvergence(float fullStepDelta, float halfStepDelta, string label)
        {
            if (fullStepDelta <= 0.00001f)
                return;
            Assert.That(halfStepDelta / fullStepDelta, Is.LessThan(0.35f),
                label + " retains a linear crease instead of settling to zero slope.");
        }

        private static void FindRoutes(out CaveRoute mainRoute, out CaveRoute branches)
        {
            CaveRoute[] routes = Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            mainRoute = routes.First(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute capturedMain = mainRoute;
            branches = routes.First(route => route != capturedMain);
        }
    }
}
