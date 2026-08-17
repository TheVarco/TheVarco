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
        GameUIFont.Apply(promptText);

        if (interactor != null)
            interactor.OnPromptChanged += HandlePromptChanged;

        HandlePromptChanged(null); // 시작할 땐 아무것도 안 보이게
    }

    // 로컬 플레이어가 스폰될 때 자기 자신을 등록하기 위해 호출 (씬 UI는 동적 스폰된 플레이어를 미리 참조할 수 없음)
    public void SetInteractor(PlayerInteractor newInteractor)
    {
        if (interactor != null)
            interactor.OnPromptChanged -= HandlePromptChanged;

        interactor = newInteractor;

        if (interactor != null)
            interactor.OnPromptChanged += HandlePromptChanged;

        HandlePromptChanged(null);
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
