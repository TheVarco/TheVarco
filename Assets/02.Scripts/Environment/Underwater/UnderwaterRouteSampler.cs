using CaveBlockout;
using UnityEngine;
using UnityEngine.Splines;

namespace Varco.Underwater
{
    /// <summary>
    /// Pre-samples a cave route spline into a polyline with cumulative arc length, then answers
    /// "how far along the route is this world position" with a local search seeded from the previous
    /// query.
    ///
    /// SplineUtility.GetNearestPoint re-subdivides the whole 650 m spline on every call, which is
    /// wasted work when the camera moves a few centimetres per frame. Metre-level accuracy is ample
    /// here because zone boundaries are blended over an 18 m window.
    /// </summary>
    public sealed class UnderwaterRouteSampler
    {
        private const float SampleSpacingMeters = 2f;
        private const int MinSamples = 16;
        private const int MaxSamples = 2048;
        private const int LocalSearchWindow = 12;

        private Vector3[] positions;
        private float[] distances;
        private int lastIndex;

        public bool IsValid => positions != null && positions.Length >= 2;
        public float TotalLength => IsValid ? distances[distances.Length - 1] : 0f;

        /// <summary>
        /// World position and outward direction of the route's far end - the cave exit mouth. The
        /// direction comes from the last polyline segment (2 m spacing), which the noise system keeps
        /// clean: displacement fades to zero within 5 m of either route end.
        /// </summary>
        public bool TryGetEndPose(out Vector3 position, out Vector3 tangent)
        {
            if (!IsValid)
            {
                position = Vector3.zero;
                tangent = Vector3.forward;
                return false;
            }

            position = positions[positions.Length - 1];
            tangent = (position - positions[positions.Length - 2]).normalized;
            return true;
        }

        public bool Build(CaveRoute route, int splineIndex = 0)
        {
            positions = null;
            distances = null;
            lastIndex = 0;

            SplineContainer container = route != null ? route.Container : null;
            if (container == null || splineIndex < 0 || splineIndex >= container.Splines.Count)
                return false;

            Spline spline = container[splineIndex];
            float length = spline.GetLength();
            if (length <= 0.01f)
                return false;

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / SampleSpacingMeters) + 1, MinSamples, MaxSamples);
            positions = new Vector3[sampleCount];
            distances = new float[sampleCount];

            Transform containerTransform = container.transform;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                positions[i] = containerTransform.TransformPoint(spline.EvaluatePosition(t));
                distances[i] = i == 0 ? 0f : distances[i - 1] + Vector3.Distance(positions[i - 1], positions[i]);
            }

            return true;
        }

        /// <summary>
        /// Distance in metres along the route of the point closest to <paramref name="worldPosition"/>.
        /// <paramref name="lateralDistance"/> is how far off the route centre line the query point sits,
        /// which the director uses to tell "inside the main tunnel" from "off in a branch".
        /// </summary>
        public float Project(Vector3 worldPosition, out float lateralDistance, bool forceFullScan = false)
        {
            lateralDistance = float.PositiveInfinity;
            if (!IsValid)
                return 0f;

            int bestSegment = -1;
            float bestSqr = float.PositiveInfinity;
            float bestT = 0f;

            if (!forceFullScan)
            {
                int from = Mathf.Max(0, lastIndex - LocalSearchWindow);
                int to = Mathf.Min(positions.Length - 2, lastIndex + LocalSearchWindow);
                ScanSegments(worldPosition, from, to, ref bestSegment, ref bestSqr, ref bestT);

                // A local miss means the tracked object jumped (respawn, zone warp, editor scrub).
                // Anything beyond the searched window is not trustworthy, so fall through to a full scan.
                bool windowIsWholeRoute = from == 0 && to == positions.Length - 2;
                float windowRadius = LocalSearchWindow * SampleSpacingMeters;
                if (!windowIsWholeRoute && bestSqr > windowRadius * windowRadius)
                    bestSegment = -1;
            }

            if (bestSegment < 0)
            {
                bestSqr = float.PositiveInfinity;
                ScanSegments(worldPosition, 0, positions.Length - 2, ref bestSegment, ref bestSqr, ref bestT);
            }

            if (bestSegment < 0)
                return 0f;

            lastIndex = bestSegment;
            lateralDistance = Mathf.Sqrt(bestSqr);
            return Mathf.Lerp(distances[bestSegment], distances[bestSegment + 1], bestT);
        }

        private void ScanSegments(Vector3 point, int from, int to, ref int bestSegment, ref float bestSqr, ref float bestT)
        {
            for (int i = from; i <= to; i++)
            {
                Vector3 a = positions[i];
                Vector3 segment = positions[i + 1] - a;
                float segmentSqr = segment.sqrMagnitude;
                float t = segmentSqr > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / segmentSqr) : 0f;
                float sqr = (point - (a + segment * t)).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestSegment = i;
                    bestT = t;
                }
            }
        }
    }
}
