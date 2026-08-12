using System.Collections;
using UnityEngine;

/// <summary>
/// Shows the shark's target detection and target loss indicators.
/// </summary>
[RequireComponent(typeof(EnemyTargeting))]
public class SharkDetectionIndicator : MonoBehaviour
{
    [SerializeField] private GameObject detectObject;
    [SerializeField] private GameObject questionObject;
    [SerializeField, Min(0f)] private float visibleDuration = 3f;

    private EnemyTargeting targeting;
    private EnemyHealthNetworkSync networkSync;
    private Coroutine hideCoroutine;

    private void Awake()
    {
        targeting = GetComponent<EnemyTargeting>();
        networkSync = GetComponent<EnemyHealthNetworkSync>();

        if (detectObject != null)
            detectObject.SetActive(false);

        if (questionObject != null)
            questionObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (targeting == null)
            targeting = GetComponent<EnemyTargeting>();

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

        if (questionObject != null)
            questionObject.SetActive(false);
    }

    public void SetQuestionVisible(bool visible)
    {
        if (visible)
            HideDetectionImmediately();

        if (questionObject != null)
            questionObject.SetActive(visible);
    }

    public void ShowReplicatedDetection()
    {
        ShowDetection();
    }

    private void Show(Transform detectedTarget)
    {
        ShowDetection();
        networkSync?.PublishSharkDetection();
    }

    private void ShowDetection()
    {
        if (detectObject == null)
            return;

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        detectObject.SetActive(true);
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private void HideDetectionImmediately()
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        if (detectObject != null)
            detectObject.SetActive(false);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(visibleDuration);

        detectObject.SetActive(false);
        hideCoroutine = null;
    }
}
