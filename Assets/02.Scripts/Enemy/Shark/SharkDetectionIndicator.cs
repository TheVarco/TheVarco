using System.Collections;
using UnityEngine;

/// <summary>
/// 상어의 최초 타깃 탐지 표시.
/// </summary>
[RequireComponent(typeof(EnemyTargeting))]
public class SharkDetectionIndicator : MonoBehaviour
{
    [SerializeField] private GameObject detectObject;                 // 감지 표시 오브젝트.
    [SerializeField, Min(0f)] private float visibleDuration = 3f;    // 표시 유지 시간.

    private EnemyTargeting targeting; // 공통 타겟팅 컴포넌트.
    private Coroutine hideCoroutine;  // 표시 종료 코루틴.

    private void Awake()
    {
        targeting = GetComponent<EnemyTargeting>();

        if (detectObject != null)
            detectObject.SetActive(false);
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

    /// <summary>
    /// 감지 표시 즉시 숨김.
    /// </summary>
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

    /// <summary>
    /// 감지 표시 활성화 및 종료 타이머 시작.
    /// </summary>
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
