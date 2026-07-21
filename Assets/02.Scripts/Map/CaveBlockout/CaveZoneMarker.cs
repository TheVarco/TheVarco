using UnityEngine;

namespace CaveBlockout
{
    public sealed class CaveZoneMarker : MonoBehaviour
    {
        public string zoneId;
        public Vector3 guideSize = Vector3.one * 10f;
        public Color color = new Color(0.15f, 0.75f, 1f, 0.25f);

        private void OnDrawGizmos()
        {
            Gizmos.color = color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, guideSize);
            Gizmos.matrix = previousMatrix;
#if UNITY_EDITOR
            UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 1f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * guideSize.y * 0.55f, zoneId);
#endif
        }
    }
}
