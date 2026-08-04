using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SubmarineSonarTests
{
    [Test]
    public void WorldPositionToSonar_MapsSubmarineForwardToScreenUp()
    {
        Vector2 front = SubmarineSonarController.WorldPositionToSonar(
            Vector3.zero,
            Quaternion.identity,
            new Vector3(0f, 0f, 25f),
            50f);
        Vector2 right = SubmarineSonarController.WorldPositionToSonar(
            Vector3.zero,
            Quaternion.identity,
            new Vector3(25f, 0f, 0f),
            50f);
        Vector2 behind = SubmarineSonarController.WorldPositionToSonar(
            Vector3.zero,
            Quaternion.identity,
            new Vector3(0f, 0f, -25f),
            50f);

        Assert.That(front.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(front.y, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(right.x, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(right.y, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(behind.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(behind.y, Is.EqualTo(-0.5f).Within(0.0001f));
    }

    [Test]
    public void WorldPositionToSonar_AccountsForSubmarineYaw()
    {
        Vector2 result = SubmarineSonarController.WorldPositionToSonar(
            Vector3.zero,
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(25f, 0f, 0f),
            50f);

        Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(result.y, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [TestCase(2.01f, SonarVerticalDirection.Above)]
    [TestCase(2f, SonarVerticalDirection.Level)]
    [TestCase(-2f, SonarVerticalDirection.Level)]
    [TestCase(-2.01f, SonarVerticalDirection.Below)]
    public void GetVerticalDirection_UsesConfiguredThreshold(
        float localHeight,
        SonarVerticalDirection expected)
    {
        Assert.That(SubmarineSonarController.GetVerticalDirection(localHeight, 2f), Is.EqualTo(expected));
    }

    [Test]
    public void EvaluateEchoAlpha_RevealsThenFades()
    {
        Assert.That(SubmarineSonarController.EvaluateEchoAlpha(0.9f, 1f, 3f), Is.Zero);
        Assert.That(SubmarineSonarController.EvaluateEchoAlpha(1f, 1f, 3f), Is.EqualTo(1f));
        Assert.That(SubmarineSonarController.EvaluateEchoAlpha(2f, 1f, 3f), Is.EqualTo(0.5f));
        Assert.That(SubmarineSonarController.EvaluateEchoAlpha(3f, 1f, 3f), Is.Zero);
    }

    [Test]
    public void SonarTarget_RegistersOnlyWhileActive()
    {
        GameObject targetObject = new GameObject("Sonar Target Test");

        try
        {
            SonarTarget target = targetObject.AddComponent<SonarTarget>();
            Assert.That(SonarTarget.ActiveTargets.Contains(target), Is.True);
            Assert.That(target.IsDetectable, Is.True);

            targetObject.SetActive(false);
            Assert.That(SonarTarget.ActiveTargets.Contains(target), Is.False);
            Assert.That(target.IsDetectable, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void SonarTarget_WithDeadHealthIsNotDetectable()
    {
        GameObject targetObject = new GameObject("Dead Sonar Target Test");

        try
        {
            Health health = targetObject.AddComponent<Health>();
            SonarTarget target = targetObject.AddComponent<SonarTarget>();
            health.ApplyDamage(DamageInfo.WithoutImpact(1000f, null));

            Assert.That(target.IsDetectable, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void Controller_DoesNotCreateCanvasWhenDisplayIsMissing()
    {
        GameObject submarine = new GameObject("Submarine Sonar Test");
        GameObject monitor = new GameObject("Object_12.001");
        monitor.transform.SetParent(submarine.transform);

        try
        {
            LogAssert.Expect(
                LogType.Error,
                "SubmarineSonarController: Assign a prebuilt SubmarineSonarGraphic to Display.");
            SubmarineSonarController controller = submarine.AddComponent<SubmarineSonarController>();

            Assert.That(controller.Display, Is.Null);
            Assert.That(controller.enabled, Is.False);
            Assert.That(monitor.transform.childCount, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(submarine);
        }
    }
}
