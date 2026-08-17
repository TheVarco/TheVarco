using UnityEngine;

namespace Varco.Ending
{
    /// <summary>
    /// Follows a source on the horizontal plane while staying at a fixed waterline height.
    /// This lets Camera 1 show the submarine's vertical breach instead of rising with it.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class EndingCamera1SurfaceTarget : MonoBehaviour
    {
        public Transform Source;
        public float FixedWorldY = 273.3f;
        public Vector2 HorizontalOffset = Vector2.zero;

        void OnEnable() => ApplyNow();
        void Update() => ApplyNow();
        void LateUpdate() => ApplyNow();

        public void ApplyNow()
        {
            if (Source == null) return;
            Vector3 source = Source.position;
            transform.position = new Vector3(
                source.x + HorizontalOffset.x,
                FixedWorldY,
                source.z + HorizontalOffset.y);
        }

        void OnDrawGizmos()
        {
            if (Source == null) return;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.95f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, Source.position);
        }
    }
}
