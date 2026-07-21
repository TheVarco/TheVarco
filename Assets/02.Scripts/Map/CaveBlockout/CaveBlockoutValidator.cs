using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout
{
    public sealed class CaveValidationResult
    {
        public float routeLength;
        public float totalRise;
        public float minimumWidth = float.PositiveInfinity;
        public float minimumHeight = float.PositiveInfinity;
        public float maximumSlope;
        public float minimumTurnRadius = float.PositiveInfinity;
        public float maximumReverseDrop;
        public int branchCount;
        public readonly List<string> issues = new List<string>();
        public bool Passed => issues.Count == 0;
    }

    public static class CaveBlockoutValidator
    {
        public static CaveValidationResult Validate(CaveRoute mainRoute, CaveRoute branches)
        {
            CaveValidationResult result = new CaveValidationResult();
            if (mainRoute == null || mainRoute.Container.Splines.Count == 0)
            {
                result.issues.Add("Main route is missing.");
                return result;
            }

            SplineContainer container = mainRoute.Container;
            result.routeLength = container.CalculateLength(0);
            int samples = Mathf.Max(32, Mathf.CeilToInt(result.routeLength / 3f));

            Vector3 start = container.EvaluatePosition(0, 0f);
            Vector3 end = container.EvaluatePosition(0, 1f);
            result.totalRise = end.y - start.y;

            Vector3 previousPosition = start;
            Vector3 previousTangent = ((Vector3)container.EvaluateTangent(0, 0f)).normalized;
            float peakHeight = start.y;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 position = container.EvaluatePosition(0, t);
                Vector3 tangent = ((Vector3)container.EvaluateTangent(0, t)).normalized;
                result.minimumWidth = Mathf.Min(result.minimumWidth, mainRoute.EvaluateWidth(0, t));
                result.minimumHeight = Mathf.Min(result.minimumHeight, mainRoute.EvaluateHeight(0, t));

                peakHeight = Mathf.Max(peakHeight, position.y);
                result.maximumReverseDrop = Mathf.Max(result.maximumReverseDrop, peakHeight - position.y);

                if (i > 0)
                {
                    Vector3 delta = position - previousPosition;
                    float horizontal = new Vector2(delta.x, delta.z).magnitude;
                    float slope = Mathf.Atan2(Mathf.Abs(delta.y), Mathf.Max(horizontal, 0.0001f)) * Mathf.Rad2Deg;
                    result.maximumSlope = Mathf.Max(result.maximumSlope, slope);

                    float angle = Vector3.Angle(previousTangent, tangent) * Mathf.Deg2Rad;
                    if (angle > 0.002f)
                        result.minimumTurnRadius = Mathf.Min(result.minimumTurnRadius, delta.magnitude / angle);
                }

                previousPosition = position;
                previousTangent = tangent;
            }

            if (float.IsPositiveInfinity(result.minimumTurnRadius))
                result.minimumTurnRadius = 9999f;

            result.branchCount = branches != null ? branches.Definitions.Count : 0;

            if (result.routeLength < 650f || result.routeLength > 700f)
                result.issues.Add($"Route length {result.routeLength:F1}m is outside 650-700m.");
            if (result.totalRise < 259f || result.totalRise > 261f)
                result.issues.Add($"Total rise {result.totalRise:F1}m is outside 259-261m.");
            if (result.minimumWidth < 10f)
                result.issues.Add($"Minimum width {result.minimumWidth:F1}m is below 10m.");
            if (result.minimumHeight < 8f)
                result.issues.Add($"Minimum height {result.minimumHeight:F1}m is below 8m.");
            if (result.maximumSlope > 30.5f)
                result.issues.Add($"Maximum slope {result.maximumSlope:F1} degrees exceeds 30 degrees.");
            if (result.minimumTurnRadius < 14f)
                result.issues.Add($"Minimum turn radius {result.minimumTurnRadius:F1}m is below 14m.");
            if (result.maximumReverseDrop > 3f)
                result.issues.Add($"Reverse drop {result.maximumReverseDrop:F1}m exceeds 3m.");
            if (result.branchCount != 3)
                result.issues.Add($"Expected 3 resource branches but found {result.branchCount}.");

            return result;
        }
    }
}
