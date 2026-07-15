using UnityEngine;

// 파밍 아이템, 잠수함 탑승 지점, 스위치 등 "플레이어가 E키로 뭔가 할 수 있는 모든 것"이
// 공통으로 구현하는 규격. 새 상호작용 오브젝트를 만들 때마다 이 인터페이스만 구현하면
// PlayerInteractor가 자동으로 인식한다.
public interface Interactable
{
    // E키를 눌렀을 때 실제로 일어나는 동작
    void Interact(GameObject interactor);

    // 화면에 띄울 안내 문구 (예: "F : 채집하기", "F : 잠수함 탑승")
    string GetInteractionPrompt();

    // 지금 상호작용이 가능한 상태인지 (예: 이미 채집된 아이템이면 false)
    bool CanInteract(GameObject interactor);
}