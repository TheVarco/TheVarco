using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SharkTargeting))]
public class SharkDetectionIndicator : MonoBehaviour
{
    [SerializeField] private GameObject detectObject;
    [SerializeField, Min(0f)] private float visibleDuration = 3f;

    private SharkTargeting targeting;
    private Coroutine hideCoroutine;

    private void Awake()
    {
        targeting = GetComponent<SharkTargeting>();

        if (detectObject != null)
            detectObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (targeting == null)
            targeting = GetComponent<SharkTargeting>();

        targeting.OnTargetDetected += Show;
    }

    private void OnDisable()
    {
        targeting.OnTargetDetected -= Show;

        HideImmediately();
    }

    public void HideImmediately()
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        if (detectObject != null)
            detectObject.SetActive(false);
    }

    private void Show(Transform detectedTarget)
    {
        if (detectObject == null)
            return;

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        detectObject.SetActive(true);
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(visibleDuration);

        detectObject.SetActive(false);
        hideCoroutine = null;
    }
}
