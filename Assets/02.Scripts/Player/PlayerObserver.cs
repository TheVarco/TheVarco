using Fusion;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// 로컬 플레이어가 기절하면 자동으로 관전 모드로 전환해주는 컴포넌트.
// 기절 시작 시 본인을 3인칭으로 보여주다가, 클릭하면 본인 포함 전체 플레이어를 순환하며 관전할 수 있다.
// 순수 로컬 연출이라 네트워크로 동기화할 상태는 없음.
[RequireComponent(typeof(PlayerDownedState))]
public class PlayerObserver : NetworkBehaviour
{
    [Header("관전 대상 전환 키")]
    public KeyCode nextTargetKey = KeyCode.Mouse0;
    public KeyCode prevTargetKey = KeyCode.Mouse1;

    private PlayerDownedState downedState;
    private PlayerCameraRig cameraRig;
    private PlayerCameraRig.ViewMode viewModeBeforeSpectate;
    private int spectateIndex;
    private GameObject hudRoot;
    private Text hudLabel;

    public override void Spawned()
    {
        if (!Object.HasInputAuthority) { enabled = false; return; }

        downedState = GetComponent<PlayerDownedState>();
        downedState.OnDowned += EnterSpectate;
        downedState.OnRevived += ExitSpectate;
    }

    void Update()
    {
        if (cameraRig == null || downedState == null || !downedState.IsDowned) return;

        // 보던 사람이 죽거나 접속을 끊으면 카메라가 파괴된 Transform을 계속 따라간다
        if (IsCurrentTargetGone()) CycleTarget(1);

        if (Input.GetKeyDown(nextTargetKey)) CycleTarget(1);
        if (Input.GetKeyDown(prevTargetKey)) CycleTarget(-1);
    }

    private bool IsCurrentTargetGone()
    {
        Transform t = cameraRig.target;
        if (t == null) return true;
        if (t == transform) return false; // 나 자신은 기절 상태여도 계속 볼 수 있음

        PlayerDownedState state = t.GetComponent<PlayerDownedState>();
        return state == null || state.IsDowned;
    }

    private void EnterSpectate()
    {
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<PlayerCameraRig>();
        if (cameraRig == null) return;

        viewModeBeforeSpectate = cameraRig.viewMode;
        cameraRig.viewMode = PlayerCameraRig.ViewMode.ThirdPerson;

        cameraRig.SetTarget(transform); // 처음엔 본인을 보여줌

        if (hudLabel == null) BuildHud();
        hudLabel.enabled = true;
        RefreshHud();
    }

    private void ExitSpectate()
    {
        if (hudLabel != null) hudLabel.enabled = false;

        if (cameraRig == null) return;
        cameraRig.viewMode = viewModeBeforeSpectate;
        cameraRig.SetTarget(transform); // 부활했으니 다시 나 자신으로
    }

    // 지금 누구를 보고 있는지 + 전환 방법. 관전 중에만 화면 하단에 뜬다
    private void RefreshHud()
    {
        if (hudLabel == null || cameraRig == null) return;

        Transform t = cameraRig.target;
        string who = "?";
        if (t == transform)
        {
            who = "나";
        }
        else if (t != null)
        {
            NetworkObject targetObject = t.GetComponent<NetworkObject>();
            if (targetObject != null && targetObject.InputAuthority.IsRealPlayer)
                who = $"플레이어 {targetObject.InputAuthority.PlayerId}";
        }

        hudLabel.text = $"관전 중 · {who}\n좌클릭 다음 / 우클릭 이전";
    }

    private void BuildHud()
    {
        // 프리팹은 씬 UI를 참조할 수 없어서 런타임에 만든다 (PlayerTrappedIndicator와 같은 방식)
        hudRoot = new GameObject("Spectate HUD");

        Canvas canvas = hudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(hudRoot.transform, false);

        hudLabel = textObject.AddComponent<Text>();
        GameUIFont.Apply(hudLabel);
        hudLabel.fontSize = 22;
        hudLabel.alignment = TextAnchor.LowerCenter;
        hudLabel.color = new Color(0.9f, 0.95f, 1f, 0.9f);
        hudLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        hudLabel.verticalOverflow = VerticalWrapMode.Overflow;
        hudLabel.raycastTarget = false;

        RectTransform rect = hudLabel.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 90f);
        rect.sizeDelta = new Vector2(520f, 60f);
    }

    void OnDestroy()
    {
        if (hudRoot != null) Destroy(hudRoot);
    }

    private void CycleTarget(int direction)
    {
        List<Transform> candidates = GetSpectatableTargets();
        if (candidates.Count == 0) return;

        // 목록이 매번 달라지므로(죽거나 나가면 빠짐) 저장된 인덱스 대신 지금 보는 대상에서 다시 센다
        int current = candidates.IndexOf(cameraRig.target);
        spectateIndex = current >= 0
            ? (current + direction + candidates.Count) % candidates.Count
            : 0;

        cameraRig.SetTarget(candidates[spectateIndex]);
        RefreshHud();
    }

    // 본인 포함, 기절 안 한(살아있는) 플레이어 전체 목록
    private List<Transform> GetSpectatableTargets()
    {
        List<Transform> result = new List<Transform>();
        PlayerDownedState[] all = FindObjectsByType<PlayerDownedState>(FindObjectsSortMode.None);
        foreach (var p in all)
        {
            if (p != downedState && p.IsDowned) continue; // 남이 기절 상태면 제외, 나 자신은 기절 상태여도 포함
            result.Add(p.transform);
        }
        return result;
    }
}
