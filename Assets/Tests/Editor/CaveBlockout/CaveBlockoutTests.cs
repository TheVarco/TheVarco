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

            Assert.That(result.routeLength, Is.InRange(650f, 700f));
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
            Assert.That(mainRoute.Portals.Select(portal => portal.zoneId), Is.EquivalentTo(new[] { "Z2", "Z4", "Z6" }));
            Assert.That(mainRoute.NoiseSettings.enabled, Is.True);
            Assert.That(mainRoute.NoiseSettings.amplitudeMeters, Is.EqualTo(0.8f).Within(0.001f));

            Assert.That(branches.Container.Splines.Count, Is.EqualTo(3));
            for (int i = 0; i < branches.Container.Splines.Count; i++)
                AssertEmbeddedData(branches.Container[i], false);
            Assert.That(branches.Definitions.All(definition => definition.sections.Single().capEnd), Is.True);
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
                triangles += visual.triangles.Length / 3;

                CaveMeshTopologyReport topology = CaveMeshTopologyAnalyzer.Analyze(visual);
                Assert.That(topology.boundaryLoopCount, Is.EqualTo(1), "Only the Z7 exit may remain open.");
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
        public void Noise_IsDeterministicContinuousAndFadesAtPortals()
        {
            FindRoutes(out CaveRoute mainRoute, out _);
            Transform generated = GameObject.Find(CaveBlockoutBuilder.RootName).transform.Find("Generated");
            MeshFilter filter = generated.GetComponentInChildren<MeshFilter>(true);
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            CollectionAssert.AreEqual(filter.sharedMesh.vertices, collider.sharedMesh.vertices,
                "Structural noise must match the collider when visual detail is disabled.");

            float totalLength = mainRoute.Container.CalculateLength(0);
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, 0f), Is.Zero.Within(0.0001f));
            Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, totalLength), Is.Zero.Within(0.0001f));
            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute, portal.mainDistanceMeters), Is.Zero.Within(0.0001f));
                Assert.That(CaveMeshGenerator.EvaluateMainNoiseWeight(mainRoute,
                    portal.mainDistanceMeters + 16f + mainRoute.NoiseSettings.portalFadeDistance + 0.5f), Is.GreaterThan(0.99f));
            }

            Vector3[] original = filter.sharedMesh.vertices;
            int originalSeed = mainRoute.NoiseSettings.seed;
            mainRoute.NoiseSettings.seed = originalSeed + 1;
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            Vector3[] changed = generated.GetComponentInChildren<MeshFilter>(true).sharedMesh.vertices;
            Assert.That(changed.SequenceEqual(original), Is.False, "Changing the seed must change generated geometry.");

            mainRoute.NoiseSettings.seed = originalSeed;
            CaveBlockoutBuilder.RegenerateCurrentScene(false);
            Vector3[] restored = generated.GetComponentInChildren<MeshFilter>(true).sharedMesh.vertices;
            CollectionAssert.AreEqual(original, restored, "Restoring the seed must restore the exact mesh.");
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
                new UnityEngine.Splines.Interpolators.LerpFloat());
            float expectedHeight = heights.Evaluate(spline, segmentIndex + expectedCurveT, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.LerpFloat());
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
            Assert.That(viewpoints.Count, Is.EqualTo(33));
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

        private static void FindRoutes(out CaveRoute mainRoute, out CaveRoute branches)
        {
            CaveRoute[] routes = Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            mainRoute = routes.First(route => route.Definitions.Any(definition => definition.isMainRoute));
            CaveRoute capturedMain = mainRoute;
            branches = routes.First(route => route != capturedMain);
        }
    }
}
