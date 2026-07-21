using UnityEngine;
using UnityEngine.UI;

// PlayerInteractor의 OnPromptChanged 신호를 받아서, 화면에 "E : ..." 문구를 보여주거나 숨기는 UI.
public class InteractionPromptUI : MonoBehaviour
{
    [Tooltip("신호를 받아올 대상 (Player에 붙어있는 PlayerInteractor)")]
    public PlayerInteractor interactor;
    [Tooltip("문구가 표시될 UI Text (Canvas 안에 만들어서 연결)")]
    public Text promptText;

    void Start()
    {
        if (interactor != null)
            interactor.OnPromptChanged += HandlePromptChanged;

        HandlePromptChanged(null); // 시작할 땐 아무것도 안 보이게
    }

    void OnDestroy()
    {
        // 구독 해제 안 하면, 이 오브젝트가 사라진 뒤에도 PlayerInteractor가
        // 없는 UI를 계속 부르려다 에러가 날 수 있음
        if (interactor != null)
            interactor.OnPromptChanged -= HandlePromptChanged;
    }

    private void HandlePromptChanged(string prompt)
    {
        if (promptText == null) return;

        bool hasPrompt = !string.IsNullOrEmpty(prompt);
        promptText.gameObject.SetActive(hasPrompt);

        if (hasPrompt)
            promptText.text = prompt;
    }
}