using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout
{
    [Serializable]
    public sealed class CaveRouteSection
    {
        public string zoneId;
        public int startKnot;
        public int endKnot;
        public float nominalLength;
        public Vector2 guideSize;
        public bool capStart;
        public bool capEnd;
    }

    [Serializable]
    public sealed class CaveRouteSplineDefinition
    {
        public string routeId;
        public int splineIndex;
        public bool isMainRoute;
        public float startTrimMeters;
        public List<CaveRouteSection> sections = new List<CaveRouteSection>();
    }

    [Serializable]
    public sealed class CavePortalDefinition
    {
        public string zoneId;
        public float mainKnot;
        public int branchSplineIndex;
        public Vector3 direction;
        public float longitudinalHalfSize = 7f;
        public float angularHalfSize = 30f;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class CaveRoute : MonoBehaviour
    {
        public const string WidthDataKey = "cave.width";
        public const string HeightDataKey = "cave.height";
        public const string ZoneDataKey = "cave.zone";
        public const string RollDataKey = "cave.roll";
        public const string PortalDataKey = "cave.portal";

        [SerializeField] private List<CaveRouteSplineDefinition> definitions = new List<CaveRouteSplineDefinition>();
        [SerializeField] private List<CavePortalDefinition> portals = new List<CavePortalDefinition>();

        public SplineContainer Container => GetComponent<SplineContainer>();
        public IReadOnlyList<CaveRouteSplineDefinition> Definitions => definitions;
        public IReadOnlyList<CavePortalDefinition> Portals => portals;

        public void SetDefinitions(List<CaveRouteSplineDefinition> routeDefinitions, List<CavePortalDefinition> portalDefinitions)
        {
            definitions = routeDefinitions ?? new List<CaveRouteSplineDefinition>();
            portals = portalDefinitions ?? new List<CavePortalDefinition>();
        }

        public float EvaluateWidth(int splineIndex, float normalizedT)
        {
            return EvaluateFloat(splineIndex, WidthDataKey, normalizedT, 12f);
        }

        public float EvaluateHeight(int splineIndex, float normalizedT)
        {
            return EvaluateFloat(splineIndex, HeightDataKey, normalizedT, 10f);
        }

        public float EvaluateRoll(int splineIndex, float normalizedT)
        {
            return EvaluateFloat(splineIndex, RollDataKey, normalizedT, 0f);
        }

        private float EvaluateFloat(int splineIndex, string key, float normalizedT, float fallback)
        {
            if (Container == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
                return fallback;

            Spline spline = Container[splineIndex];
            if (!spline.TryGetFloatData(key, out SplineData<float> data) || data.Count == 0)
                return fallback;

            return data.Evaluate(spline, Mathf.Clamp01(normalizedT), PathIndexUnit.Normalized, new UnityEngine.Splines.Interpolators.LerpFloat());
        }
    }
}
