using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class SubmarineDamageTests
{
    [TestCase(0f, 0)]
    [TestCase(0.001f, 0)]
    [TestCase(0.01f, 1)]
    [TestCase(10f, 1)]
    [TestCase(10.01f, 2)]
    [TestCase(20f, 2)]
    [TestCase(30f, 3)]
    [TestCase(40f, 4)]
    [TestCase(40.01f, 5)]
    [TestCase(60f, 5)]
    public void DamageStage_UsesTenDamageBucketsAndCapsAtFive(
        float accumulatedDamage,
        int expectedStage)
    {
        int stage = RepairableStructure.CalculateDamageStage(accumulatedDamage, 10f, 5);

        Assert.That(stage, Is.EqualTo(expectedStage));
    }

    [Test]
    public void HiddenOverflow_MustBeRepairedBeforeStageFiveDrops()
    {
        float accumulatedDamage = 60f;

        accumulatedDamage -= 10f;
        Assert.That(
            RepairableStructure.CalculateDamageStage(accumulatedDamage, 10f, 5),
            Is.EqualTo(5),
            "60 damage repaired by 10 still has 50 accumulated damage.");

        accumulatedDamage -= 10f;
        Assert.That(
            RepairableStructure.CalculateDamageStage(accumulatedDamage, 10f, 5),
            Is.EqualTo(4),
            "Only after repairing a total of 20 should 60 damage fall to stage 4.");
    }

    [Test]
    public void GlassDamageOverlay_CreatesRuntimeQuadAndTogglesVisibility()
    {
        GameObject target = new GameObject("Glass Damage Overlay Test");
        Texture2D albedo = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        try
        {
            GlassDamageOverlay overlay = target.AddComponent<GlassDamageOverlay>();

            overlay.Show(albedo, null);

            MeshFilter filter = target.GetComponent<MeshFilter>();
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(4));
            Assert.That(filter.sharedMesh.tangents, Has.Length.EqualTo(4));
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(albedo));
            Assert.That(overlay.IsVisible, Is.True);

            overlay.Hide();
            Assert.That(overlay.IsVisible, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(albedo);
        }
    }

    [Test]
    public void RepairProgressWorldUI_UsesLeftToRightImageFillWithoutPercentageText()
    {
        RepairProgressWorldUI progressUI = RepairProgressWorldUI.CreateRuntime();
        GameObject viewer = new GameObject("Repair UI Viewer");

        try
        {
            viewer.transform.position = new Vector3(0f, 0f, -2f);
            progressUI.Show(Vector3.zero, Vector3.forward, 0.4f, viewer.transform);

            Image fill = progressUI.transform.Find("Gauge Fill").GetComponent<Image>();
            Text prompt = progressUI.transform.Find("Repair Prompt").GetComponent<Text>();

            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            Assert.That(fill.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(prompt.text, Does.Not.Contain("%"));
        }
        finally
        {
            Object.DestroyImmediate(progressUI.gameObject);
            Object.DestroyImmediate(viewer);
        }
    }

    [Test]
    public void ApplyDamage_ReportsOnlyHealthThatWasActuallyRemoved()
    {
        GameObject target = new GameObject("Health Test Target");
        GameObject source = new GameObject("Damage Source");

        try
        {
            Health health = target.AddComponent<Health>();
            typeof(Health)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(health, null);
            DamageAppliedInfo received = default;
            int eventCount = 0;
            health.OnDamageApplied += info =>
            {
                received = info;
                eventCount++;
            };

            float applied = health.ApplyDamage(DamageInfo.WithoutImpact(150f, source));

            Assert.That(applied, Is.EqualTo(100f));
            Assert.That(received.AppliedAmount, Is.EqualTo(100f));
            Assert.That(received.Damage.RequestedAmount, Is.EqualTo(150f));
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(health.IsDead, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(target);
        }
    }
}
