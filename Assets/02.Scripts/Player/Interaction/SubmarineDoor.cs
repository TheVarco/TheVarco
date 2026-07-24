using System.Collections;
using UnityEngine;

public class SubmarineDoor : MonoBehaviour, Interactable
{
    [SerializeField] private string doorOpen = "IsOpen";
    
    [SerializeField] private float autoCloseDelay = 5f;
    
    private Animator doorAnimator;

    private bool isOpen;
    private Coroutine autoCloseRoutine;

    void Awake()
    {
        if (doorAnimator == null)
            doorAnimator = GetComponent<Animator>();
    }

    public void Interact(GameObject interactor)
    {
        if (isOpen)
            CloseDoor();
        else
            OpenDoor();
    }

    public string GetInteractionPrompt()
    {
        return "E : 잠수함 출입구 열기/닫기";
    }

    public bool CanInteract(GameObject interactor)
    {
        return doorAnimator != null;
    }
    
    private void OpenDoor()
    {
        isOpen = true;
        
        doorAnimator.SetBool(doorOpen, true);
    }
    
    private void CloseDoor()
    {
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }
        
        isOpen = false;
        
        doorAnimator.SetBool(doorOpen, false);
    }
    
    public void OnOpenAnimationFinished()
    {
        if (!isOpen || autoCloseRoutine != null)
            return;

        autoCloseRoutine = StartCoroutine(AutoCloseAfterDelay());
    }
    
    private IEnumerator AutoCloseAfterDelay()
    {
        yield return new WaitForSeconds(autoCloseDelay);

        autoCloseRoutine = null;
        CloseDoor();
    }
}
