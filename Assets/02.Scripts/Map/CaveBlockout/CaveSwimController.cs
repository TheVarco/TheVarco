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
        public bool snapToLookDirection = true;

        public Transform visualTransform;
        public KeyCode quickRotateKey = KeyCode.Q;
        public float spinDuration = 0.4f;

        private Rigidbody body;
        private Vector3 inputDirection;
        private Coroutine spinCoroutine;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            if (visualTransform == null)
            {
                visualTransform = transform.Find("OtterVisual");
                if (visualTransform == null && transform.childCount > 0)
                {
                    visualTransform = transform.GetChild(0);
                }
            }
        }

        public Animator animator;

        private static readonly int DefaultStateHash = Animator.StringToHash("Default");
        private static readonly int Swim1StateHash = Animator.StringToHash("Swim1");
        private static readonly int Swim2StateHash = Animator.StringToHash("Swim2");

        public float dashSpeed = 16f;
        public bool IsDashing { get; private set; }

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

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            IsDashing = shiftHeld && inputDirection.sqrMagnitude > 0.01f;

            if (Input.GetKeyDown(quickRotateKey))
            {
                if (visualTransform != null)
                {
                    if (animator == null) animator = GetComponent<Animator>();
                    if (animator != null)
                    {
                        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                        bool isDefaultOrSwim = stateInfo.shortNameHash == DefaultStateHash ||
                                               stateInfo.shortNameHash == Swim1StateHash ||
                                               stateInfo.shortNameHash == Swim2StateHash;
                        if (!isDefaultOrSwim) return;
                    }

                    bool isSwimming = inputDirection.sqrMagnitude > 0.01f || (body != null && body.linearVelocity.magnitude > 0.1f);
                    Vector3 spinAxis = isSwimming ? new Vector3(0f, 0f, 360f) : new Vector3(0f, 360f, 0f);

                    if (spinCoroutine != null) StopCoroutine(spinCoroutine);
                    spinCoroutine = StartCoroutine(AnimateVisualSpin(spinAxis));
                }
            }
            if (snapToLookDirection && lookReference != null)
            {
                Vector3 euler = lookReference.rotation.eulerAngles;
                transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
            }
        }

        private void LateUpdate()
        {
            if (visualTransform == null) return;

            // 어떠한 애니메이션/동작 상태라도 OtterVisual의 좌표 및 로테이션은 0으로 고정
            visualTransform.localPosition = Vector3.zero;
            if (spinCoroutine == null)
            {
                visualTransform.localRotation = Quaternion.identity;
            }
        }

        private System.Collections.IEnumerator AnimateVisualSpin(Vector3 spinAxis)
        {
            float elapsed = 0f;
            while (elapsed < spinDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / spinDuration);
                visualTransform.localPosition = Vector3.zero;
                visualTransform.localRotation = Quaternion.Euler(spinAxis * progress);
                yield return null;
            }
            visualTransform.localPosition = Vector3.zero;
            visualTransform.localRotation = Quaternion.identity;
            spinCoroutine = null;
        }

        private void FixedUpdate()
        {
            float activeSpeed = IsDashing ? dashSpeed : moveSpeed;
            Vector3 targetVelocity = inputDirection * activeSpeed;
            body.AddForce((targetVelocity - body.linearVelocity) * acceleration, ForceMode.Acceleration);

            if (inputDirection.sqrMagnitude < 0.01f)
                body.linearVelocity = Vector3.Lerp(body.linearVelocity, Vector3.zero, braking * Time.fixedDeltaTime);

            if (!snapToLookDirection && inputDirection.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(inputDirection, Vector3.up);
                Vector3 euler = target.eulerAngles;
                Quaternion fixedTarget = Quaternion.Euler(euler.x, euler.y, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, fixedTarget, rotationSpeed * Time.fixedDeltaTime);
            }
        }
    }
}
