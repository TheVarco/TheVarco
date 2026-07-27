using UnityEngine;

namespace CaveBlockout
{
    public sealed class CavePlaytestSwitcher : MonoBehaviour
    {
        public GameObject submarineRoot;
        public GameObject otterRoot;
        public MonoBehaviour submarineController;
        public MonoBehaviour otterController;
        public CavePlaytestCameraRig cameraRig;
        public Camera targetCamera;
        public float submarineFOV = 60f;
        public float otterPlayerFOV = 30f;
        public bool startWithSubmarine = true;

        private void Start()
        {
            if (targetCamera == null)
            {
                if (cameraRig != null && cameraRig.controlledCamera != null)
                    targetCamera = cameraRig.controlledCamera;
                else
                    targetCamera = Camera.main;
            }

            ActivateSubmarine(startWithSubmarine);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) ActivateSubmarine(true);
            if (Input.GetKeyDown(KeyCode.F2)) ActivateSubmarine(false);
        }

        public void ActivateSubmarine(bool useSubmarine)
        {
            if (submarineRoot != null) submarineRoot.SetActive(useSubmarine);
            if (otterRoot != null) otterRoot.SetActive(!useSubmarine);
            if (submarineController != null) submarineController.enabled = useSubmarine;
            if (otterController != null) otterController.enabled = !useSubmarine;

            if (cameraRig != null)
                cameraRig.target = useSubmarine ? submarineRoot?.transform : otterRoot?.transform;

            // 플레이어 테스트 상태(F2, useSubmarine == false)일 때만 카메라 화각(FOV)을 30으로 설정
            float fov = useSubmarine ? submarineFOV : otterPlayerFOV;

            if (targetCamera != null)
            {
                targetCamera.fieldOfView = fov;
            }

            UpdatePlayerCameraRigFOV(transform, fov);
            if (otterRoot != null)
            {
                UpdatePlayerCameraRigFOV(otterRoot.transform, fov);
            }
        }

        private static void UpdatePlayerCameraRigFOV(Transform root, float fov)
        {
            if (root == null) return;
            MonoBehaviour[] scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour script in scripts)
            {
                if (script != null && script.GetType().Name == "PlayerCameraRig")
                {
                    var field = script.GetType().GetField("normalFOV");
                    if (field != null)
                    {
                        field.SetValue(script, fov);
                    }
                }
            }
        }
    }
}
