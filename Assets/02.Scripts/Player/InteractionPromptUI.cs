using UnityEngine;
using UnityEngine.UI;

// PlayerInteractor의 OnPromptChanged 신호를 받아서, 화면에 "E : ..." 문구를 보여주거나 숨기는 UI.
public class InteractionPromptUI : MonoBehaviour
{
    [Tooltip("신호를 받아올 대상 (Player에 붙어있는 PlayerInteractor)")]
    public PlayerInteractor interactor;
    [Tooltip("문구가 표시될 UI Text (Canvas 안에 만들어서 연결)")]
    public Text promptText;
    [Tooltip("화면 우측 중앙에 배치한 공용 조작 안내 Image. 대상별 Sprite가 이 Image에 표시됩니다.")]
    public Image interactionGuideImage;

    void Start()
    {
        GameUIFont.Apply(promptText);
        SubscribeToInteractor();

        HandlePromptChanged(null); // 시작할 땐 아무것도 안 보이게
    }

    // 로컬 플레이어가 스폰될 때 자기 자신을 등록하기 위해 호출 (씬 UI는 동적 스폰된 플레이어를 미리 참조할 수 없음)
    public void SetInteractor(PlayerInteractor newInteractor)
    {
        UnsubscribeFromInteractor();

        interactor = newInteractor;
        SubscribeToInteractor();

        HandlePromptChanged(null);
    }

    void OnDestroy()
    {
        // 구독 해제 안 하면, 이 오브젝트가 사라진 뒤에도 PlayerInteractor가
        // 없는 UI를 계속 부르려다 에러가 날 수 있음
        UnsubscribeFromInteractor();
    }

    private void SubscribeToInteractor()
    {
        if (interactor == null) return;

        // 비활성 Canvas를 깨우면서 SetInteractor와 Start가 연달아 호출되어도 한 번만 구독한다.
        interactor.OnPromptChanged -= HandlePromptChanged;
        interactor.OnPromptChanged += HandlePromptChanged;
    }

    private void UnsubscribeFromInteractor()
    {
        if (interactor != null)
            interactor.OnPromptChanged -= HandlePromptChanged;
    }

    private void HandlePromptChanged(string prompt)
    {
        bool hasPrompt = !string.IsNullOrEmpty(prompt);
        if (promptText != null)
        {
            promptText.gameObject.SetActive(hasPrompt);

            if (hasPrompt)
                promptText.text = prompt;
        }

        UpdateInteractionGuide(hasPrompt);
    }

    private void UpdateInteractionGuide(bool hasPrompt)
    {
        if (interactionGuideImage == null) return;

        Sprite guideSprite = null;
        if (hasPrompt && interactor != null && interactor.CurrentTarget is Component targetComponent)
        {
            InteractionGuideProvider provider = targetComponent.GetComponent<InteractionGuideProvider>();
            if (provider != null)
                guideSprite = provider.GuideSprite;
        }

        interactionGuideImage.sprite = guideSprite;
        interactionGuideImage.gameObject.SetActive(guideSprite != null);
    }
}
