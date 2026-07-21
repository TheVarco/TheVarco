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
        public bool startWithSubmarine = true;

        private void Start()
        {
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
        }
    }
}
