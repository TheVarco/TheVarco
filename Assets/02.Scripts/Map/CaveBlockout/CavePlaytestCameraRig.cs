using UnityEngine;

namespace CaveBlockout
{
    public sealed class CavePlaytestCameraRig : MonoBehaviour
    {
        public Transform target;
        public Camera controlledCamera;
        public Vector3 offset = new Vector3(0f, 2.5f, -9f);
        public float lookAtHeight = 0.5f;
        public float mouseSensitivity = 2.5f;
        public float followSmoothing = 12f;
        public float minPitch = -80f;
        public float maxPitch = 80f;

        private float yaw;
        private float pitch;
        private Quaternion orbitRotation = Quaternion.identity;

        private void OnEnable()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 desiredPosition = target.position + orbitRotation * offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Mathf.Clamp01(followSmoothing * Time.deltaTime));
            Vector3 lookDirection = target.position + Vector3.up * lookAtHeight - transform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }
    }
}
