using UnityEngine;

namespace CaveBlockout
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CaveSwimController : MonoBehaviour
    {
        public Transform lookReference;
        public float moveSpeed = 10f;
        public float acceleration = 12f;
        public float braking = 4f;
        public float rotationSpeed = 5f;

        private Rigidbody body;
        private Vector3 inputDirection;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void Update()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float forward = Input.GetAxisRaw("Vertical");
            float vertical = 0f;
            if (Input.GetKey(KeyCode.Space)) vertical += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) vertical -= 1f;

            Transform reference = lookReference != null ? lookReference : transform;
            inputDirection = reference.forward * forward + reference.right * horizontal + Vector3.up * vertical;
            if (inputDirection.sqrMagnitude > 1f)
                inputDirection.Normalize();
        }

        private void FixedUpdate()
        {
            Vector3 targetVelocity = inputDirection * moveSpeed;
            body.AddForce((targetVelocity - body.linearVelocity) * acceleration, ForceMode.Acceleration);

            if (inputDirection.sqrMagnitude < 0.01f)
                body.linearVelocity = Vector3.Lerp(body.linearVelocity, Vector3.zero, braking * Time.fixedDeltaTime);

            if (inputDirection.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(inputDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.fixedDeltaTime);
            }
        }
    }
}
