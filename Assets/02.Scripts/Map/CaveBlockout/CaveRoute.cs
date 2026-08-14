using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout
{
    [Serializable]
    public sealed class CaveNoiseSettings
    {
        public bool enabled = true;
        public int seed = 1337;
        [Min(0f)] public float amplitudeMeters = 0.8f;
        [Range(0.5f, 2.5f)] public float strengthGain = 1f;
        [Range(0.05f, 0.35f)] public float maximumDisplacementRatio = 0.15f;
        [Min(1f)] public float wavelengthMeters = 12f;
        [Range(1, 4)] public int octaves = 2;
        [Min(1f)] public float lacunarity = 2f;
        [Range(0.1f, 0.9f)] public float persistence = 0.5f;
        [Range(0f, 3f)] public float floorMultiplier = 1f;
        [Range(0f, 3f)] public float wallMultiplier = 1f;
        [Range(0f, 3f)] public float ceilingMultiplier = 1.15f;
        [Min(1f)] public float portalFadeDistance = 4f;
        public bool visualDetailEnabled;
        [Range(0f, 0.75f)] public float visualDetailAmplitude = 0.1f;
        [Min(1f)] public float visualDetailWavelength = 4f;

        /// <summary>
        /// Share of the full noise weight the very LAST ring of the main route keeps, i.e. the Z6 exit
        /// mouth. 0 reproduces the original behaviour: a mathematically perfect ellipse.
        ///
        /// This exists as its own knob because <see cref="portalFadeDistance"/> cannot be reused. That
        /// one field also smooths the three side-portal welds and the branch ends, and the smoothing
        /// there is load-bearing - it stops structural noise reopening a hairline crack along a weld.
        /// The route's END is different in kind: it is an open mouth, welded to nothing, so it is free
        /// to be as rough as the rock around it.
        ///
        /// Bumps go both inward and outward (noise displaces along the radial in both directions), so
        /// raising this narrows the opening. At the 24 x 16 m exit roughly 2.4-3.6 m of displacement is
        /// available before ApplyNoise's clamps bind, which still leaves far more than the 3 x 3 x 6 m
        /// submarine needs - but re-run the blockout clearance validator after changing it.
        /// </summary>
        [Range(0f, 1f)] public float exitRimNoiseWeight;

        /// <summary>
        /// Metres over which noise ramps from <see cref="exitRimNoiseWeight"/> at the mouth up to full
        /// strength further inside. Keeps the roughening confined to the rim instead of restyling the
        /// whole Z6 throat, which already carries full noise.
        /// </summary>
        [Min(0.1f)] public float exitRimFadeDistance = 5f;

        public void ApplySmoothPreset()
        {
            enabled = false;
            amplitudeMeters = 0f;
            strengthGain = 1f;
            maximumDisplacementRatio = 0.15f;
            visualDetailEnabled = false;
            exitRimNoiseWeight = 0f;
        }

        public void ApplyRockyPreset()
        {
            enabled = true;
            amplitudeMeters = 0.8f;
            strengthGain = 1f;
            maximumDisplacementRatio = 0.15f;
            wavelengthMeters = 12f;
            octaves = 2;
            lacunarity = 2f;
            persistence = 0.5f;
            floorMultiplier = 1f;
            wallMultiplier = 1f;
            ceilingMultiplier = 1.15f;
            portalFadeDistance = 4f;
            visualDetailEnabled = false;
            exitRimNoiseWeight = 0f;
        }

        public void ApplyRoughPreset()
        {
            enabled = true;
            amplitudeMeters = 1.8f;
            strengthGain = 1.2f;
            maximumDisplacementRatio = 0.22f;
            wavelengthMeters = 8f;
            octaves = 2;
            lacunarity = 2f;
            persistence = 0.5f;
            floorMultiplier = 1.1f;
            wallMultiplier = 1f;
            ceilingMultiplier = 1.25f;
            portalFadeDistance = 5f;
            visualDetailEnabled = true;
            visualDetailAmplitude = 0.2f;
            visualDetailWavelength = 8f;
            exitRimNoiseWeight = 0.6f;
            exitRimFadeDistance = 5f;
        }

        public void ApplyStrongPreset()
        {
            enabled = true;
            amplitudeMeters = 3.2f;
            strengthGain = 1.5f;
            maximumDisplacementRatio = 0.3f;
            wavelengthMeters = 8f;
            octaves = 2;
            lacunarity = 2f;
            persistence = 0.6f;
            floorMultiplier = 1.2f;
            wallMultiplier = 1.3f;
            ceilingMultiplier = 1.5f;
            portalFadeDistance = 5f;
            visualDetailEnabled = true;
            visualDetailAmplitude = 0.4f;
            visualDetailWavelength = 8f;
            // The Z6 exit reads as a bored hole with a clean elliptical rim otherwise. 0.85 keeps
            // roughly 2-3 m of displacement at the mouth, both inward and outward.
            exitRimNoiseWeight = 0.85f;
            exitRimFadeDistance = 5f;
        }
    }

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
        [SerializeField] private CaveNoiseSettings noiseSettings = new CaveNoiseSettings();

        public SplineContainer Container => GetComponent<SplineContainer>();
        public IReadOnlyList<CaveRouteSplineDefinition> Definitions => definitions;
        public IReadOnlyList<CavePortalDefinition> Portals => portals;
        public CaveNoiseSettings NoiseSettings => noiseSettings;

        public void SetDefinitions(List<CaveRouteSplineDefinition> routeDefinitions, List<CavePortalDefinition> portalDefinitions)
        {
            definitions = routeDefinitions ?? new List<CaveRouteSplineDefinition>();
            portals = portalDefinitions ?? new List<CavePortalDefinition>();
        }

        public float EvaluateWidth(int splineIndex, float normalizedT)
        {
            return EvaluateProfileFloat(splineIndex, WidthDataKey, normalizedT, 12f);
        }

        public float EvaluateHeight(int splineIndex, float normalizedT)
        {
            return EvaluateProfileFloat(splineIndex, HeightDataKey, normalizedT, 10f);
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

        private float EvaluateProfileFloat(int splineIndex, string key, float normalizedT, float fallback)
        {
            if (Container == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
                return fallback;

            Spline spline = Container[splineIndex];
            if (!spline.TryGetFloatData(key, out SplineData<float> data) || data.Count == 0)
                return fallback;

            // Width and height are shape profiles, not generic metadata. SmoothStep is non-overshooting and
            // reaches zero slope at each authored knot, removing the hard chamber/throat crease rings that
            // remain visible even when procedural surface noise is disabled.
            return data.Evaluate(spline, Mathf.Clamp01(normalizedT), PathIndexUnit.Normalized,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
        }
    }
}
