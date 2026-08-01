using UnityEngine;

namespace Varco.Underwater
{
    /// <summary>
    /// Keeps one world-space suspended-particle emitter around the tracked camera and scales its
    /// density and volume to the zone the camera is currently in.
    ///
    /// The route is ~650 m long, so a single follower beats seven static emitters: it costs one
    /// particle system instead of seven and never leaves a zone unpopulated. Simulation space is
    /// World, so moving the emitter transform relocates the spawn box while already-spawned motes stay
    /// put and drift on their own.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class UnderwaterParticleFollower : MonoBehaviour
    {
        [SerializeField] private UnderwaterZoneDirector director;
        [SerializeField] private Transform followTarget;

        [Header("Placement")]
        [Tooltip("Emitter box is pushed this far along the target's forward axis so most motes spawn " +
                 "in front of the camera rather than behind it.")]
        [Range(0f, 40f)] [SerializeField] private float forwardOffset = 6f;
        [Tooltip("Emitter box edge length as a multiple of the current zone's visibility distance.")]
        [Range(0.5f, 4f)] [SerializeField] private float boxSizePerVisibility = 2.2f;
        [SerializeField] private Vector2 boxSizeRange = new Vector2(14f, 70f);

        [Header("Density")]
        [Tooltip("Live particle count at particleDensityScale = 1.")]
        [Range(0f, 2000f)] [SerializeField] private float baseParticleCount = 420f;
        [Range(0.1f, 30f)] [SerializeField] private float averageLifetimeSeconds = 7f;

        private ParticleSystem particles;
        private ParticleSystem.ShapeModule shape;
        private ParticleSystem.EmissionModule emission;
        private bool modulesResolved;

        private void OnEnable()
        {
            particles = GetComponent<ParticleSystem>();
            if (particles != null)
            {
                shape = particles.shape;
                emission = particles.emission;
                modulesResolved = true;
            }

            if (director == null)
                director = GetComponentInParent<UnderwaterZoneDirector>();
        }

        private void LateUpdate()
        {
            if (!modulesResolved)
                return;

            if (followTarget == null)
            {
                Camera main = Camera.main;
                if (main == null)
                    return;
                followTarget = main.transform;
            }

            if (director == null)
                director = GetComponentInParent<UnderwaterZoneDirector>();

            float visibility = 14f;
            float densityScale = 1f;
            if (director != null && director.CurrentProfile != null)
            {
                visibility = Mathf.Max(1f, director.CurrentProfile.visibilityMeters * director.visibilityMultiplier);
                densityScale = director.CurrentProfile.particleDensityScale;
            }

            float edge = Mathf.Clamp(visibility * boxSizePerVisibility, boxSizeRange.x, boxSizeRange.y);
            transform.position = followTarget.position + followTarget.forward * forwardOffset;
            shape.scale = new Vector3(edge, edge * 0.65f, edge);

            float liveTarget = baseParticleCount * Mathf.Max(0f, densityScale);
            emission.rateOverTime = liveTarget / Mathf.Max(0.1f, averageLifetimeSeconds);
        }
    }
}
