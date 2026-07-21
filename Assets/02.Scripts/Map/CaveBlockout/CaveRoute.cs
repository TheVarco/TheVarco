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
        public float startDistanceMeters = -1f;
        public float endDistanceMeters = -1f;
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
        public float mainDistanceMeters = -1f;
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

        public float ResolveSectionStartT(CaveRouteSplineDefinition definition, CaveRouteSection section)
        {
            return section.startDistanceMeters >= 0f
                ? EvaluateTAtDistance(definition.splineIndex, section.startDistanceMeters)
                : Container[definition.splineIndex].ConvertIndexUnit(section.startKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
        }

        public float ResolveSectionEndT(CaveRouteSplineDefinition definition, CaveRouteSection section)
        {
            return section.endDistanceMeters >= 0f
                ? EvaluateTAtDistance(definition.splineIndex, section.endDistanceMeters)
                : Container[definition.splineIndex].ConvertIndexUnit(section.endKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
        }

        public float ResolvePortalT(CavePortalDefinition portal)
        {
            return portal.mainDistanceMeters >= 0f
                ? EvaluateTAtDistance(0, portal.mainDistanceMeters)
                : Container[0].ConvertIndexUnit(portal.mainKnot, PathIndexUnit.Knot, PathIndexUnit.Normalized);
        }

        public float EvaluateTAtDistance(int splineIndex, float distanceMeters)
        {
            if (Container == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
                return 0f;

            const int samples = 512;
            float target = Mathf.Max(0f, distanceMeters);
            float accumulated = 0f;
            float previousT = 0f;
            Vector3 previous = Container.EvaluatePosition(splineIndex, 0f);
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 current = Container.EvaluatePosition(splineIndex, t);
                float segment = Vector3.Distance(previous, current);
                if (accumulated + segment >= target)
                {
                    float alpha = segment > 0.0001f ? (target - accumulated) / segment : 0f;
                    return Mathf.Lerp(previousT, t, alpha);
                }
                accumulated += segment;
                previous = current;
                previousT = t;
            }
            return 1f;
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
