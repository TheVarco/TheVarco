using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// 외부 이동 수신 검증체
public sealed class HydrothermalVentExternalMotionProbe : MonoBehaviour, IExternalMotionReceiver
{
    public int ExternalMotionReceiverId => GetInstanceID(); // 검증 대상 식별값
    public int ImpulseCount { get; private set; } // 순간 충격 호출 횟수
    public int AccelerationCount { get; private set; } // 지속 가속 호출 횟수
    public Vector3 TotalVelocityChange { get; private set; } // 전달받은 전체 속도 변화량

    // 순간 충격 호출 기록
    public void ApplyExternalImpulse(Vector3 velocityChange)
    {
        ImpulseCount++;
        TotalVelocityChange += velocityChange;
    }

    // 지속 가속 호출 기록
    public void ApplyExternalAcceleration(Vector3 acceleration, float deltaTime)
    {
        AccelerationCount++;
        TotalVelocityChange += acceleration * deltaTime;
    }
}

// 분출구 상태와 패턴 검증
public class VentControllerTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic; // 비공개 메서드와 필드 접근 옵션

    // 상태별 콜라이더 활성 여부 검증
    [Test]
    public void SetState_ControlsEffectColliderAndResetClearsTheHazard()
    {
        GameObject ventObject = new GameObject("Vent State Test");

        try
        {
            BoxCollider effectCollider = ventObject.AddComponent<BoxCollider>();
            VentController ventController = ventObject.AddComponent<VentController>();

            ventController.ResetVent();
            Assert.That(effectCollider.enabled, Is.False);
            Assert.That(ventController.CurrentState, Is.EqualTo(VentState.Inactive));

            ventController.SetState(VentState.Warning);
            Assert.That(effectCollider.enabled, Is.False);
            Assert.That(ventController.CurrentState, Is.EqualTo(VentState.Warning));

            ventController.SetState(VentState.Active);
            Assert.That(effectCollider.enabled, Is.True);
            Assert.That(ventController.IsHazardActive, Is.True);

            ventController.ResetVent();
            Assert.That(effectCollider.enabled, Is.False);
            Assert.That(ventController.CurrentState, Is.EqualTo(VentState.Inactive));
        }
        finally
        {
            Object.DestroyImmediate(ventObject);
        }
    }

    // 다중 콜라이더 대상 중복 충격과 피해 방지 검증
    [Test]
    public void MultipleColliders_ReceiveOneImpulseAndOneDamagePerEruption()
    {
        GameObject ventObject = new GameObject("Vent Contact Test");
        GameObject target = new GameObject("Multi Collider Target");

        try
        {
            ventObject.AddComponent<BoxCollider>();
            VentController ventController = ventObject.AddComponent<VentController>();

            Health health = target.AddComponent<Health>();
            InvokePrivate(health, "Awake");
            HydrothermalVentExternalMotionProbe probe = target.AddComponent<HydrothermalVentExternalMotionProbe>();
            BoxCollider firstCollider = target.AddComponent<BoxCollider>();
            SphereCollider secondCollider = target.AddComponent<SphereCollider>();

            ventController.SetState(VentState.Active);
            InvokePrivate(ventController, "TryAffect", firstCollider);
            InvokePrivate(ventController, "TryAffect", secondCollider);

            Assert.That(probe.ImpulseCount, Is.EqualTo(1));
            Assert.That(probe.AccelerationCount, Is.EqualTo(1));
            Assert.That(health.CurrentHealth, Is.EqualTo(90f));

            ventController.SetState(VentState.Inactive);
            ventController.SetState(VentState.Active);
            InvokePrivate(ventController, "TryAffect", firstCollider);

            Assert.That(probe.ImpulseCount, Is.EqualTo(2));
            Assert.That(health.CurrentHealth, Is.EqualTo(80f));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(ventObject);
        }
    }

    // 같은 그룹 분출구 동시 상태 변경 검증
    [Test]
    public void GroupStateChange_UpdatesEveryRegisteredVentTogether()
    {
        GameObject runnerObject = new GameObject("Pattern Runner Test");
        GameObject firstObject = new GameObject("Vent A");
        GameObject secondObject = new GameObject("Vent B");

        try
        {
            firstObject.AddComponent<BoxCollider>();
            secondObject.AddComponent<BoxCollider>();
            VentController first = firstObject.AddComponent<VentController>();
            VentController second = secondObject.AddComponent<VentController>();
            VentPattern runner = runnerObject.AddComponent<VentPattern>();

            List<VentController> group = new List<VentController> { first, second };
            SetPrivateField(runner, "groupA", group);
            InvokePrivate(runner, "SetGroupState", group, VentState.Active);

            Assert.That(first.CurrentState, Is.EqualTo(VentState.Active));
            Assert.That(second.CurrentState, Is.EqualTo(VentState.Active));
        }
        finally
        {
            Object.DestroyImmediate(secondObject);
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(runnerObject);
        }
    }

    // 패턴 소유권 확보와 반환 검증
    [Test]
    public void PatternControl_DisablesAndReleasesAutomaticAloneOwnership()
    {
        GameObject ventObject = new GameObject("Vent Ownership Test");
        GameObject patternObject = new GameObject("Pattern Ownership Test");
        patternObject.SetActive(false);

        try
        {
            ventObject.AddComponent<BoxCollider>();
            VentController ventController = ventObject.AddComponent<VentController>();
            VentPattern pattern = patternObject.AddComponent<VentPattern>();
            SetPrivateField(pattern, "groupA", new List<VentController> { ventController });

            InvokePrivate(pattern, "ClaimPatternControl");
            Assert.That(ventController.IsPatternControlled, Is.True);

            InvokePrivate(pattern, "ReleasePatternControl");
            Assert.That(ventController.IsPatternControlled, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(patternObject);
            Object.DestroyImmediate(ventObject);
        }
    }

    // 패턴 드롭다운 항목 검증
    [Test]
    public void PatternDropdown_ContainsOnlyAloneAndCross()
    {
        Assert.That(
            System.Enum.GetNames(typeof(VentPatternType)),
            Is.EqualTo(new[] { "Alone", "Cross" }));
    }

    // Cross 예고 시간이 분출 시간을 넘지 않는지 검증
    [Test]
    public void OnValidate_ClampsNextWarningLeadTimeToActiveDuration()
    {
        GameObject runnerObject = new GameObject("Pattern Timing Test");

        try
        {
            VentPattern runner = runnerObject.AddComponent<VentPattern>();
            SetPrivateField(runner, "activeDuration", 0.5f);
            SetPrivateField(runner, "crossWarningLeadTime", 2f);

            InvokePrivate(runner, "OnValidate");

            Assert.That(GetPrivateField<float>(runner, "crossWarningLeadTime"), Is.EqualTo(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(runnerObject);
        }
    }

    // 비공개 메서드 실행 보조
    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance);
        Assert.That(method, Is.Not.Null, $"Expected private method {methodName}.");
        method.Invoke(target, arguments);
    }

    // 비공개 필드 값 설정 보조
    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Expected private field {fieldName}.");
        field.SetValue(target, value);
    }

    // 비공개 필드 값 읽기 보조
    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Expected private field {fieldName}.");
        return (T)field.GetValue(target);
    }
}
