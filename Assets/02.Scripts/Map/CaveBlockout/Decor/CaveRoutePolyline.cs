using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Decor
{
    /// <summary>
    /// One cave route spline pre-sampled into a world-space polyline that carries cumulative arc
    /// length and the spline parameter at every sample.
    ///
    /// Two reasons this exists rather than calling the spline directly. First, spline <c>t</c> is not
    /// arc length — Unity gives each curve segment an equal slice of [0,1] regardless of how long it
    /// is — so "35 metres along the route" cannot be turned into a <c>t</c> without walking the curve.
    /// <see cref="CaveRoute.EvaluateTAtDistance"/> does exactly that walk with 512 samples on every
    /// single call, which is fine for a once-per-frame camera query but not for a brush stroke that
    /// resolves dozens of candidates per mouse move. Second, placement needs the inverse direction
    /// too (world point to route distance), which the spline API does not offer cheaply.
    /// </summary>
    public sealed class CaveRoutePolyline
    {
        private const float SampleSpacingMeters = 1f;
        private const int MinSamples = 16;
        private const int MaxSamples = 4096;

        private Vector3[] positions;
        private Vector3[] tangents;
        private float[] distances;
        private float[] parameters;

        public int SplineIndex { get; private set; }
        public bool IsValid => positions != null && positions.Length >= 2;
        public float Length => IsValid ? distances[distances.Length - 1] : 0f;

        public static CaveRoutePolyline Build(CaveRoute route, int splineIndex)
        {
            SplineContainer container = route != null ? route.Container : null;
            if (container == null || splineIndex < 0 || splineIndex >= container.Splines.Count)
                return null;

            float length = container[splineIndex].GetLength();
            if (length <= 0.01f)
                return null;

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / SampleSpacingMeters) + 1, MinSamples, MaxSamples);
            var polyline = new CaveRoutePolyline
            {
                SplineIndex = splineIndex,
                positions = new Vector3[sampleCount],
                tangents = new Vector3[sampleCount],
                distances = new float[sampleCount],
                parameters = new float[sampleCount]
            };

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                // Container overloads return world space; Spline's own return local. Staying on the
                // container keeps every consumer in one space.
                Vector3 position = container.EvaluatePosition(splineIndex, t);
                Vector3 tangent = ((Vector3)container.EvaluateTangent(splineIndex, t)).normalized;
                if (tangent.sqrMagnitude < 1e-6f)
                    tangent = i > 0 ? polyline.tangents[i - 1] : Vector3.forward;

                polyline.parameters[i] = t;
                polyline.positions[i] = position;
                polyline.tangents[i] = tangent;
                polyline.distances[i] = i == 0
                    ? 0f
                    : polyline.distances[i - 1] + Vector3.Distance(polyline.positions[i - 1], position);
            }

            return polyline.Length > 0.01f ? polyline : null;
        }

        /// <summary>World position, unit tangent and spline parameter at <paramref name="distance"/> metres along the route.</summary>
        public void Sample(float distance, out Vector3 position, out Vector3 tangent, out float parameter)
        {
            if (!IsValid)
            {
                position = Vector3.zero;
                tangent = Vector3.forward;
                parameter = 0f;
                return;
            }

            float clamped = Mathf.Clamp(distance, 0f, Length);
            int index = FindSegment(clamped);
            float span = distances[index + 1] - distances[index];
            float alpha = span > 1e-5f ? (clamped - distances[index]) / span : 0f;

            position = Vector3.Lerp(positions[index], positions[index + 1], alpha);
            tangent = Vector3.Slerp(tangents[index], tangents[index + 1], alpha).normalized;
            parameter = Mathf.Lerp(parameters[index], parameters[index + 1], alpha);
        }

        /// <summary>
        /// Distance along the route of the point closest to <paramref name="worldPosition"/>.
        /// <paramref name="lateralDistance"/> is how far off the centre line the query point sits,
        /// which is what the corridor clearance guard measures against.
        /// </summary>
        public float Project(Vector3 worldPosition, out float lateralDistance)
        {
            lateralDistance = float.PositiveInfinity;
            if (!IsValid)
                return 0f;

            int bestSegment = 0;
            float bestSqr = float.PositiveInfinity;
            float bestAlpha = 0f;

            for (int i = 0; i < positions.Length - 1; i++)
            {
                Vector3 a = positions[i];
                Vector3 segment = positions[i + 1] - a;
                float segmentSqr = segment.sqrMagnitude;
                float alpha = segmentSqr > 1e-6f
                    ? Mathf.Clamp01(Vector3.Dot(worldPosition - a, segment) / segmentSqr)
                    : 0f;
                float sqr = (worldPosition - (a + segment * alpha)).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;

                bestSqr = sqr;
                bestSegment = i;
                bestAlpha = alpha;
            }

            lateralDistance = Mathf.Sqrt(bestSqr);
            return Mathf.Lerp(distances[bestSegment], distances[bestSegment + 1], bestAlpha);
        }

        private int FindSegment(float distance)
        {
            int low = 0;
            int high = distances.Length - 1;
            while (low < high - 1)
            {
                int mid = (low + high) / 2;
                if (distances[mid] <= distance)
                    low = mid;
                else
                    high = mid;
            }
            return Mathf.Clamp(low, 0, distances.Length - 2);
        }
    }
}
