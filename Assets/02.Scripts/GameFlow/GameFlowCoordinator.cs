using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Varco.GameFlow
{
    // 게임 흐름 권한 관리
    // 호스트가 체크포인트 캡처와 복원을 실행
    // 참가자 순서를 고정해 내부 스폰 위치 배정
    // 실패와 성공 상태를 모든 피어에 전파
    // 성공 실패 체크포인트 상태를 총괄하는 권한 기반 조정자
    [DisallowMultipleComponent]
    public sealed class GameFlowCoordinator : MonoBehaviour
    {
        // 세션 메모리 스냅샷 관리자
        private readonly CheckpointSnapshotService checkpointService = new();
        // 내부 스폰 배정에 사용할 정렬된 플레이어 목록
        private readonly List<IPlayerCheckpointParticipant> orderedPlayers = new();
        // 권한과 플레이어 목록을 제공하는 네트워크 경계
        private IGameFlowNetworkBridge bridge;
        // 잠수함 파괴 이벤트 구독 대상
        private Health submarineHealth;
        // 체크포인트와 출구 판정 대상 잠수함
        private SubmarineController submarine;
        // 잠수함 내부 플레이어 스폰 위치 제공자
        private SubmarinePlayerSpawnPoints submarinePlayerSpawnPoints;
        // 현재 프레임에 접수된 최고 우선순위 결과
        private GameFlowOutcomeCandidate pendingOutcome;
        // 로컬 상태 변경 버전
        private int revision;
        // 중복 수신 방지용 마지막 적용 버전
        private int lastAppliedRevision = -1;
        // 중복 초기화 방지 상태
        private bool initialized;
        // 불완전 플레이어 스냅샷 경고 중복 방지
        private bool warnedIncompletePlayerSnapshot;
        // 내부 스폰 위치 누락 경고 중복 방지
        private bool warnedMissingPlayerSpawnPoints;
        // 내부 스폰 위치 부족 경고 중복 방지
        private bool warnedInsufficientPlayerSpawnPoints;

        // 현재 게임 흐름 상태
        public GameFlowState State { get; private set; } = GameFlowState.Playing;
        // 현재 실패 원인
        public GameFailureReason FailureReason { get; private set; } = GameFailureReason.None;
        // 최초 Z1을 포함한 현재 체크포인트 구역
        public int CurrentCheckpointZone { get; private set; } = 1;
        // 판정과 버튼 명령을 실행할 권한 여부
        public bool IsAuthority => bridge != null && bridge.IsReady && bridge.IsAuthority;
        // 판정 대상 잠수함
        public SubmarineController Submarine => submarine;

        // UI와 외부 시스템에 전달하는 상태 변경 이벤트
        public event Action<GameFlowReplicatedState> FlowStateChanged;
        // 체크포인트 전진 알림 이벤트
        public event Action<int> CheckpointChanged;

        // 잠수함과 네트워크 경계를 연결하고 최초 상태 시작
    public void Initialize(SubmarineController targetSubmarine, IGameFlowNetworkBridge networkBridge)
    {
        // 잠수함 참조와 네트워크 브리지를 저장하고 초기 체크포인트 준비
            if (initialized)
                return;

            submarine = targetSubmarine;
            submarineHealth = submarine != null ? submarine.GetComponent<Health>() : null;
            if (submarine != null)
            {
                submarinePlayerSpawnPoints =
                    submarine.GetComponent<SubmarinePlayerSpawnPoints>()
                    ?? submarine.gameObject.AddComponent<SubmarinePlayerSpawnPoints>();
                submarinePlayerSpawnPoints.RefreshFromHierarchyIfNeeded();
            }
            bridge = networkBridge;
            initialized = true;

            // 잠수함 사망 이벤트로 체력 소진 실패 감지
            if (submarineHealth != null)
                submarineHealth.OnDeath.AddListener(ReportSubmarineDestroyed);
            else
                Debug.LogError("[GameFlow] Submarine Health was not found.", this);

            // 팀 브리지 또는 로컬 브리지의 상태 수신 연결
            if (bridge != null)
                bridge.ReplicatedStateReceived += ApplyReplicatedState;
            else
                Debug.LogError("[GameFlow] No network bridge was supplied.", this);

            // 플레이어 등록이 끝난 다음 Z1 기본 스냅샷 캡처
            StartCoroutine(CaptureInitialCheckpointAfterStartup());
            PublishCurrentState();
        }

        // 시작 참가자 내부 배치 갱신
    public void RefreshStartupPlayersAfterJoin()
    {
        // 시작 구역 권한 상태인지 확인한 뒤 참가자 전체를 다시 배치
            if (!initialized
                || !IsAuthority
                || State != GameFlowState.Playing
                || CurrentCheckpointZone != 1)
            {
                return;
            }

            IReadOnlyList<IPlayerCheckpointParticipant> players = bridge.Players;
            if (players == null || players.Count == 0)
                return;

            PlacePlayersAtSubmarineSpawnPoints(players);
            checkpointService.Capture(1, players);
            WarnIfPlayerSnapshotIsIncomplete();
        }

        // 이벤트 구독 정리
    private void OnDestroy()
    {
        // 연결했던 잠수함과 브리지 이벤트 구독 해제
            if (submarineHealth != null)
                submarineHealth.OnDeath.RemoveListener(ReportSubmarineDestroyed);
            if (bridge != null)
                bridge.ReplicatedStateReceived -= ApplyReplicatedState;
        }

        // 권한 인스턴스에서 파티 전멸 상태 감시
    private void Update()
    {
        // 호스트에서 모든 참가자의 생존 여부를 매 프레임 검사
            if (!initialized || !IsAuthority || State != GameFlowState.Playing)
                return;

            IReadOnlyList<IPlayerCheckpointParticipant> players = bridge.Players;
            // 참가자가 아직 등록되지 않은 시작 구간은 판정 보류
            if (players.Count == 0)
                return;

            // 한 명이라도 생존 상태면 전멸 아님
            bool allDowned = true;
            foreach (IPlayerCheckpointParticipant player in players)
            {
                if (player == null || player.IsDowned)
                    continue;

                allDowned = false;
                break;
            }

            if (allDowned)
                QueueOutcome(GameFlowOutcomeCandidate.PartyWiped);
        }

        // 같은 프레임의 모든 결과를 모은 뒤 최종 상태 확정
    private void LateUpdate()
    {
        // 같은 프레임에 모인 성공 실패 후보 중 최종 결과 하나 적용
            if (!IsAuthority || State != GameFlowState.Playing || pendingOutcome == GameFlowOutcomeCandidate.None)
                return;

            GameFlowOutcomeCandidate outcome = pendingOutcome;
            pendingOutcome = GameFlowOutcomeCandidate.None;

            if (outcome == GameFlowOutcomeCandidate.Success)
                TransitionTo(GameFlowState.Succeeded, GameFailureReason.None);
            else
                TransitionTo(GameFlowState.Failed, GameFlowOutcomeResolver.ToFailureReason(outcome));
        }

        // 잠수함 체력 소진 결과 접수
    public void ReportSubmarineDestroyed()
    {
        // 잠수함 파괴를 실패 후보로 등록
            QueueOutcome(GameFlowOutcomeCandidate.SubmarineDestroyed);
        }

        // 등록된 잠수함의 Z7 출구 도달 결과 접수
    public void ReportExitReached(SubmarineController reachedSubmarine)
    {
        // 현재 잠수함이 출구에 도달한 경우 성공 후보 등록
            if (reachedSubmarine == submarine)
                QueueOutcome(GameFlowOutcomeCandidate.Success);
        }

        // 더 앞선 구역에서만 새 스냅샷 저장
    public bool TryActivateCheckpoint(int zone, SubmarineController reachedSubmarine)
    {
        // 호스트와 진행 방향과 잠수함 일치 조건을 순서대로 검증
            // 판정 권한과 진행 상태와 잠수함 일치 여부 확인
            if (!IsAuthority
                || State != GameFlowState.Playing
                || reachedSubmarine != submarine
                || !CheckpointZoneProgression.CanAdvance(CurrentCheckpointZone, zone)
                || !CanCaptureCheckpoint())
            {
                return false;
            }

            // 모든 참가자 캡처 성공 후 체크포인트 확정
            if (!checkpointService.Capture(zone, bridge.Players))
                return false;

            CurrentCheckpointZone = zone;
            WarnIfPlayerSnapshotIsIncomplete();
            CheckpointChanged?.Invoke(zone);
            PublishCurrentState();
            return true;
        }

        // 실패 화면의 체크포인트 재시작 명령 처리
    public void RequestRestartFromCheckpoint()
    {
        // 실패 상태와 저장 스냅샷이 준비된 경우 복원 코루틴 시작
            if (!IsAuthority || State != GameFlowState.Failed || checkpointService.CurrentSnapshot == null)
                return;

            StartCoroutine(RestoreCheckpointRoutine());
        }

        // 성공 실패 화면의 시작 화면 명령 처리
    public void RequestReturnToStart()
    {
        // 종료 상태에서만 브리지에 시작 화면 복귀 요청 전달
            if (!IsAuthority || State is not (GameFlowState.Failed or GameFlowState.Succeeded))
                return;

            bridge.ReturnToStartScene();
        }

        // 현재 프레임의 최고 우선순위 결과만 보관
    private void QueueOutcome(GameFlowOutcomeCandidate candidate)
    {
        // 이미 예약된 결과와 우선순위를 비교해 더 강한 후보 보관
            if (!IsAuthority || State != GameFlowState.Playing)
                return;

            pendingOutcome = GameFlowOutcomeResolver.Choose(pendingOutcome, candidate);
        }

        // 사망 결과와 겹치지 않는 안전한 캡처 시점 확인
    private bool CanCaptureCheckpoint()
    {
        // 잠수함과 플레이어가 저장 가능한 상태인지 확인
            if (pendingOutcome is GameFlowOutcomeCandidate.SubmarineDestroyed or GameFlowOutcomeCandidate.PartyWiped)
                return false;
            // 잠수함 파괴 직전 캡처 차단
            if (submarineHealth == null || submarineHealth.IsDead)
                return false;

            IReadOnlyList<IPlayerCheckpointParticipant> players = bridge.Players;
            if (players.Count == 0)
                return false;

            // 최소 한 명의 생존 플레이어 요구
            foreach (IPlayerCheckpointParticipant player in players)
            {
                if (player != null && !player.IsDowned)
                    return true;
            }

            return false;
        }

        // 첫 구역 트리거 진입 전 실패에 사용할 Z1 캡처
    private IEnumerator CaptureInitialCheckpointAfterStartup()
    {
        // 호스트와 첫 플레이어가 준비될 때까지 프레임 단위 대기
            while (initialized
                && State == GameFlowState.Playing
                && checkpointService.CurrentSnapshot == null)
            {
                if (bridge != null && bridge.IsReady && !bridge.IsAuthority)
                    yield break;

                if (IsAuthority && CanCaptureCheckpoint())
                    break;

                yield return null;
            }

            if (!initialized
                || State != GameFlowState.Playing
                || checkpointService.CurrentSnapshot != null)
            {
                yield break;
            }

            IReadOnlyList<IPlayerCheckpointParticipant> initialPlayers = bridge.Players;
            PlacePlayersAtSubmarineSpawnPoints(initialPlayers);
            checkpointService.Capture(1, initialPlayers);
            WarnIfPlayerSnapshotIsIncomplete();
        }

        // 정지 해제 제거 복원 재연결 재개 순서의 복원 트랜잭션
    private IEnumerator RestoreCheckpointRoutine()
    {
        // 입력 차단과 상태 복원과 물리 반영과 입력 재개 순서로 실행
            // 모든 참가자 입력과 시뮬레이션 차단
            TransitionTo(GameFlowState.Restoring, GameFailureReason.None);
            IReadOnlyList<IPlayerCheckpointParticipant> players = bridge.Players;
            checkpointService.SetGameplayEnabled(false, players);
            yield return null;

            // 저장하지 않는 일시 오브젝트 제거
            GameFlowTransientCleanup.Clear(bridge);
            // 저장된 참가자 상태 적용
            checkpointService.Restore(players);
            // 좌석에 앉지 않은 플레이어를 잠수함 내부에 순서대로 배치
            PlacePlayersAtSubmarineSpawnPoints(players);
            yield return new WaitForFixedUpdate();

            // 물리 반영 이후 입력과 시뮬레이션 재개
            checkpointService.SetGameplayEnabled(true, bridge.Players);
            pendingOutcome = GameFlowOutcomeCandidate.None;
            TransitionTo(GameFlowState.Playing, GameFailureReason.None);
        }

        // 플레이어 키 순서대로 잠수함 내부 스폰 위치 배정
    private void PlacePlayersAtSubmarineSpawnPoints(
        IReadOnlyList<IPlayerCheckpointParticipant> players)
    {
        // 참가자를 고정 키로 정렬해 서로 다른 내부 스폰 지점에 배치
            if (players == null || players.Count == 0)
                return;
            if (submarinePlayerSpawnPoints == null || submarinePlayerSpawnPoints.Count == 0)
            {
                if (!warnedMissingPlayerSpawnPoints)
                {
                    warnedMissingPlayerSpawnPoints = true;
                    Debug.LogError(
                        "[GameFlow] No submarine PlayerSpawnPoint transforms were found",
                        this);
                }
                return;
            }

            // 브리지의 수집 순서와 관계없이 항상 같은 플레이어 순서 유지
            orderedPlayers.Clear();
            foreach (IPlayerCheckpointParticipant player in players)
            {
                if (player != null)
                    orderedPlayers.Add(player);
            }
            orderedPlayers.Sort(ComparePlayersByKey);

            int spawnIndex = 0;
            foreach (IPlayerCheckpointParticipant player in orderedPlayers)
            {
                if (spawnIndex >= submarinePlayerSpawnPoints.Count)
                {
                    if (!warnedInsufficientPlayerSpawnPoints)
                    {
                        warnedInsufficientPlayerSpawnPoints = true;
                        Debug.LogError(
                            "[GameFlow] Submarine PlayerSpawnPoint count is smaller than the active player count",
                            this);
                    }
                    break;
                }

                if (!submarinePlayerSpawnPoints.TryGetSpawnPose(
                        spawnIndex,
                        out Vector3 position,
                        out Quaternion rotation))
                {
                    continue;
                }

                // 모든 플레이어를 서로 다른 내부 위치에 순서대로 배치
                if (player.TrySetCheckpointSpawnPose(position, rotation))
                    spawnIndex++;
            }
        }

        // 숫자형 PlayerKey를 우선 사용하고 문자열 키를 보조 기준으로 사용
    private static int ComparePlayersByKey(
        IPlayerCheckpointParticipant left,
        IPlayerCheckpointParticipant right)
    {
        // 숫자 키를 우선 비교하고 실패하면 문자열 키로 순서 결정
            string leftKey = left?.PlayerKey ?? string.Empty;
            string rightKey = right?.PlayerKey ?? string.Empty;
            if (long.TryParse(leftKey, out long leftNumber)
                && long.TryParse(rightKey, out long rightNumber))
            {
                int numberComparison = leftNumber.CompareTo(rightNumber);
                if (numberComparison != 0)
                    return numberComparison;
            }

            return string.CompareOrdinal(leftKey, rightKey);
        }

        // 상태와 실패 원인을 함께 전환
    private void TransitionTo(GameFlowState nextState, GameFailureReason reason)
    {
        // 상태와 실패 원인과 버전을 갱신한 뒤 변경 이벤트 전파
            State = nextState;
            FailureReason = reason;

            // 터미널 상태와 복원 상태에서 게임플레이 차단
            bool gameplayEnabled = nextState == GameFlowState.Playing;
            if (!gameplayEnabled && bridge != null)
                checkpointService.SetGameplayEnabled(false, bridge.Players);

            PublishCurrentState();
        }

        // 새 버전을 생성하고 로컬 적용 후 네트워크 전파
    private void PublishCurrentState()
    {
        // 현재 게임 흐름 값을 복제 구조체로 만들어 브리지에 전달
            GameFlowReplicatedState state = new(State, FailureReason, CurrentCheckpointZone, ++revision);
            ApplyReplicatedState(state);
            bridge?.PublishState(state);
        }

        // 더 최신인 수신 상태만 적용
    private void ApplyReplicatedState(GameFlowReplicatedState replicatedState)
    {
        // 더 최신 버전만 받아 로컬 상태와 결과 화면 갱신
            if (replicatedState.Revision <= lastAppliedRevision)
                return;

            lastAppliedRevision = replicatedState.Revision;
            revision = Mathf.Max(revision, replicatedState.Revision);
            State = replicatedState.State;
            FailureReason = replicatedState.FailureReason;
            CurrentCheckpointZone = Mathf.Max(1, replicatedState.CheckpointZone);

            // 비권한 인스턴스도 수신 상태에 맞춰 입력 게이트 적용
            if (bridge != null && bridge.IsReady && !bridge.IsAuthority)
            {
                checkpointService.SetGameplayEnabled(
                    replicatedState.State == GameFlowState.Playing,
                    bridge.Players);
            }

            FlowStateChanged?.Invoke(replicatedState);
        }

        // 팀 플레이어 어댑터가 없을 때 제한 사항 경고
    private void WarnIfPlayerSnapshotIsIncomplete()
    {
        // 참가자 스냅샷 지원 범위를 검사해 경고를 한 번만 출력
            if (warnedIncompletePlayerSnapshot)
                return;

            foreach (IPlayerCheckpointParticipant player in bridge.Players)
            {
                if (player == null || player.SupportsCompleteSnapshot)
                    continue;

                warnedIncompletePlayerSnapshot = true;
                Debug.LogWarning(
                    "[GameFlow] The local legacy player adapter restores position/vitals/seat only. " +
                    "Inventory and hotbar restoration requires the team's IPlayerCheckpointParticipant implementation.",
                    this);
                return;
            }
        }
    }

    // 체크포인트에서 복원하지 않는 일시 오브젝트 정리기
    internal static class GameFlowTransientCleanup
    {
        // 투사체 로프 투사체 활성 낙석 제거
        public static void Clear(IGameFlowNetworkBridge bridge)
        {
            // 체크포인트 임시 엔티티와 월드 임시 오브젝트를 차례로 제거
            foreach (Projectile projectile in UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (projectile == null)
                    continue;

                if (bridge == null
                    || !bridge.TryDespawnCheckpointEntity(
                        $"projectile:{projectile.GetInstanceID()}",
                        projectile.gameObject))
                {
                    UnityEngine.Object.Destroy(projectile.gameObject);
                }
            }

            foreach (RopeProjectile projectile in UnityEngine.Object.FindObjectsByType<RopeProjectile>(FindObjectsSortMode.None))
            {
                if (projectile == null)
                    continue;

                if (bridge == null
                    || !bridge.TryDespawnCheckpointEntity(
                        $"rope-projectile:{projectile.GetInstanceID()}",
                        projectile.gameObject))
                {
                    UnityEngine.Object.Destroy(projectile.gameObject);
                }
            }

            foreach (FallingRock rock in UnityEngine.Object.FindObjectsByType<FallingRock>(FindObjectsSortMode.None))
                if (rock != null && rock.IsLaunched) rock.gameObject.SetActive(false);
        }
    }
}
