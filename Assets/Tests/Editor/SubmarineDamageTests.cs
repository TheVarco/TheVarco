using System.Reflection;
using NUnit.Framework;
using UnityEngine;

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
