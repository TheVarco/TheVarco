using System.Collections.Generic;
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
            Assert.That(filters.Length, Is.EqualTo(10));
            Assert.That(colliders.Length, Is.EqualTo(10));

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
                Assert.That(visual.normals.All(normal => normal.sqrMagnitude > 0.9f), Is.True);
                triangles += visual.triangles.Length / 3;
            }

            Assert.That(triangles, Is.LessThan(50000));
            CaveValidationSummary summary = Object.FindFirstObjectByType<CaveValidationSummary>(FindObjectsInactive.Include);
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.triangleCount, Is.EqualTo(triangles));
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
            Dictionary<string, Hash128> before = GetGeneratedMeshHashes();

            CaveBlockoutBuilder.RegenerateCurrentScene(false);

            Dictionary<string, Hash128> after = GetGeneratedMeshHashes();
            Assert.That(after, Is.EquivalentTo(before));
            Assert.That(Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include), Is.SameAs(camera));
            Assert.That(Object.FindFirstObjectByType<Light>(FindObjectsInactive.Include), Is.SameAs(light));
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

        private static Dictionary<string, Hash128> GetGeneratedMeshHashes()
        {
            return AssetDatabase.FindAssets("t:Mesh", new[] { CaveMeshGenerator.GeneratedAssetFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToDictionary(path => path, AssetDatabase.GetAssetDependencyHash);
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
