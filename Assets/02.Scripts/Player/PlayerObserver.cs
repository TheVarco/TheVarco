using Fusion;
using UnityEngine;
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

        if (Input.GetKeyDown(nextTargetKey)) CycleTarget(1);
        if (Input.GetKeyDown(prevTargetKey)) CycleTarget(-1);
    }

    private void EnterSpectate()
    {
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<PlayerCameraRig>();
        if (cameraRig == null) return;

        viewModeBeforeSpectate = cameraRig.viewMode;
        cameraRig.viewMode = PlayerCameraRig.ViewMode.ThirdPerson;

        // 처음엔 본인을 보여줌 (타겟은 이미 나 자신으로 되어있으니 그대로 둠)
        List<Transform> candidates = GetSpectatableTargets();
        spectateIndex = candidates.IndexOf(transform);
    }

    private void ExitSpectate()
    {
        if (cameraRig == null) return;
        cameraRig.viewMode = viewModeBeforeSpectate;
        cameraRig.SetTarget(transform); // 부활했으니 다시 나 자신으로
    }

    private void CycleTarget(int direction)
    {
        List<Transform> candidates = GetSpectatableTargets();
        if (candidates.Count == 0) return;

        spectateIndex = (spectateIndex + direction + candidates.Count) % candidates.Count;
        cameraRig.SetTarget(candidates[spectateIndex]);
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
