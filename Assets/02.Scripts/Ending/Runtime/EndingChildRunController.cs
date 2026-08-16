using UnityEngine;

namespace Varco.Ending
{
    /// <summary>
    /// Scene-authored endpoints for the ending child's run.  Move the endpoint transforms in
    /// Scene View; the Timeline track reads them every frame, so no generated animation clip is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EndingChildRunController : MonoBehaviour
    {
        public Transform StartTarget;
        public Transform EndTarget;
        public Terrain GroundTerrain;
        [Min(0f)] public float GroundClearance = 0.18f;

        public bool IsConfigured => StartTarget != null && EndTarget != null;

        public void Apply(float normalizedProgress)
        {
            if (!IsConfigured) return;

            float t = Mathf.Clamp01(normalizedProgress);
            Vector3 start = StartTarget.position;
            Vector3 end = EndTarget.position;
            Vector3 position = Vector3.Lerp(start, end, t);
            position.y = SampleGround(position) + GroundClearance;
            transform.position = position;

            Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up);
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public float SampleGround(Vector3 worldPosition)
        {
            if (GroundTerrain != null && GroundTerrain.terrainData != null)
                return GroundTerrain.SampleHeight(worldPosition) + GroundTerrain.GetPosition().y;
            return worldPosition.y - GroundClearance;
        }

        void OnDrawGizmos()
        {
            if (!IsConfigured) return;

            const int segments = 24;
            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
            Vector3 previous = GroundedPoint(0f);
            Gizmos.DrawWireSphere(previous, 0.16f);
            for (int i = 1; i <= segments; ++i)
            {
                Vector3 current = GroundedPoint(i / (float)segments);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }

            Gizmos.color = new Color(1f, 0.65f, 0.05f, 1f);
            Gizmos.DrawSphere(GroundedPoint(1f), 0.18f);
        }

        Vector3 GroundedPoint(float progress)
        {
            Vector3 point = Vector3.Lerp(StartTarget.position, EndTarget.position, progress);
            point.y = SampleGround(point) + GroundClearance;
            return point;
        }
    }
}
