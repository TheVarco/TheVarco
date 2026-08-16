using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;

namespace Varco.GameFlow
{
    // Fusion 플레이어의 기본 상태를 저장하고 복원하는 참가자
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FusionPlayerCheckpointParticipant : NetworkBehaviour,
        IPlayerCheckpointParticipant
    {
        // 모든 피어에 전달되는 게임플레이 입력 허용 상태
        [Networked, OnChangedRender(nameof(HandleGameplayEnabledChanged))]
        private NetworkBool NetworkedGameplayEnabled { get; set; }

        // 플레이어 기본 상태 참조
        private PlayerDownedState downedState;
        private Health health;
        private OxygenStat oxygen;
        private HungerStat hunger;
        private Rigidbody body;
        private NetworkRigidbody3D networkBody;
        private PlayerSeatController seatController;

        // 입력 차단 전 컴포넌트 상태
        private readonly Dictionary<MonoBehaviour, bool> inputStates = new();

        // 생존 수치 정지 전 컴포넌트 상태
        private readonly Dictionary<MonoBehaviour, bool> simulationStates = new();

        // 다음 네트워크 틱에 처리할 권한 위치 복원 요청
        private bool hasPendingNetworkTeleport;
        // 다음 네트워크 틱에 적용할 월드 위치
        private Vector3 pendingTeleportPosition;
        // 다음 네트워크 틱에 적용할 월드 회전
        private Quaternion pendingTeleportRotation;

        // 중복 입력 게이트 적용 방지
        private bool localInputEnabled = true;

        // 중복 생존 수치 게이트 적용 방지
        private bool localSimulationEnabled = true;

        // Fusion PlayerRef 기반 안정적인 플레이어 키
        public string PlayerKey => Object != null && Object.InputAuthority.IsRealPlayer
            ? Object.InputAuthority.PlayerId.ToString()
            : GetInstanceID().ToString();

        // 체크포인트 스냅샷 매칭 키
        public string CheckpointId => $"player:{PlayerKey}";

        // 잠수함 적 장애물 이후 플레이어 복원
        public int RestoreOrder => 100;

        // 파티 전멸 판정용 다운 상태
        public bool IsDowned => downedState != null && downedState.IsDowned;

        // Carryable 핫바는 items:session이 함께 저장하며 수량형 PlayerInventory는 범위에서 제외한다.
        public bool SupportsCompleteSnapshot => true;

        // 플레이어 기본 상태 참조 수집
        private void Awake()
        {
            downedState = GetComponent<PlayerDownedState>();
            health = GetComponent<Health>();
            oxygen = GetComponentInChildren<OxygenStat>(true);
            hunger = GetComponentInChildren<HungerStat>(true);
            body = GetComponent<Rigidbody>();
            networkBody = GetComponent<NetworkRigidbody3D>();
            seatController = GetComponent<PlayerSeatController>();
        }

        // 권한 인스턴스에서 초기 입력 상태 설정
        public override void Spawned()
        {
            if (Object.HasStateAuthority)
                NetworkedGameplayEnabled = true;

            ApplyGameplayGate(NetworkedGameplayEnabled);
        }

        // Fusion 물리 상태에 체크포인트 위치를 권한 위치로 기록
        public override void FixedUpdateNetwork()
        {
            if (!hasPendingNetworkTeleport || networkBody == null)
                return;
            if (Object == null || !Object.HasStateAuthority || !Object.IsInSimulation)
                return;

            // 일반 Rigidbody 이동과 달리 네트워크 위치 데이터까지 함께 갱신
            networkBody.Teleport(pendingTeleportPosition, pendingTeleportRotation);
            ResetBodyVelocity();
            hasPendingNetworkTeleport = false;
        }

        // 위치 회전 체력 산소 허기 다운 상태 캡처
        public object CaptureCheckpointState()
        {
            return new PlayerState
            {
                Position = hasPendingNetworkTeleport ? pendingTeleportPosition : transform.position,
                Rotation = hasPendingNetworkTeleport ? pendingTeleportRotation : transform.rotation,
                Health = GameFlowHealthUtility.Capture(health),
                Oxygen = oxygen != null ? oxygen.CurrentValue : 0f,
                OxygenMax = oxygen != null ? oxygen.maxValue : 0f,
                Hunger = hunger != null ? hunger.CurrentValue : 0f,
                HungerMax = hunger != null ? hunger.maxValue : 0f,
                IsDowned = IsDowned
            };
        }

        // 복원 전 현재 좌석 연결 해제
        public void PrepareForCheckpointRestore()
        {
            if (seatController != null && seatController.CurrentSeat != null)
                seatController.CurrentSeat.ForceExit(seatController);
        }

        // 기본 상태를 State Authority에서 복원
        public void RestoreCheckpointState(object state)
        {
            if (state is not PlayerState playerState)
                return;
            if (Object != null && !Object.HasStateAuthority)
                return;

            if (networkBody != null && Object != null)
            {
                // Teleport는 FixedUpdateNetwork에서만 호출할 수 있어 다음 틱으로 예약
                QueueNetworkTeleport(playerState.Position, playerState.Rotation);
            }
            else if (body != null)
            {
                body.position = playerState.Position;
                body.rotation = playerState.Rotation;
                ResetBodyVelocity();
            }
            else
            {
                transform.SetPositionAndRotation(playerState.Position, playerState.Rotation);
            }

            // 생존 스냅샷이면 체력 복원 전에 다운 상태 해제
            if (!playerState.IsDowned && IsDowned)
                downedState.Revive();

            GameFlowHealthUtility.Restore(health, playerState.Health);
            GameFlowHealthUtility.RestoreStat(oxygen, playerState.Oxygen, playerState.OxygenMax);
            GameFlowHealthUtility.RestoreStat(hunger, playerState.Hunger, playerState.HungerMax);
        }

        // 현재 좌석을 해제하고 플레이어의 내부 스폰 위치 설정
        public bool TrySetCheckpointSpawnPose(Vector3 position, Quaternion rotation)
        {
            if (Object != null && !Object.HasStateAuthority)
                return false;

            if (seatController != null && seatController.CurrentSeat != null)
                seatController.CurrentSeat.ForceExit(seatController);

            if (networkBody != null && Object != null)
            {
                QueueNetworkTeleport(position, rotation);
            }
            else if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                ResetBodyVelocity();
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            return true;
        }

        // Fusion 물리 틱에서 적용할 위치와 회전 보관
        private void QueueNetworkTeleport(Vector3 position, Quaternion rotation)
        {
            pendingTeleportPosition = position;
            pendingTeleportRotation = rotation;
            hasPendingNetworkTeleport = true;
        }

        // 복원 전 움직임이 새 위치에 이어지지 않도록 물리 속도 초기화
        private void ResetBodyVelocity()
        {
            if (body == null)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // 내부 스폰 방식은 기존 좌석을 다시 연결하지 않음
        public void CompleteCheckpointRestore() { }

        // 권한 인스턴스에서 입력과 생존 수치 시뮬레이션 상태 전파
        public void SetGameplayEnabled(bool enabled)
        {
            if (Object == null)
            {
                ApplyGameplayGate(enabled);
                return;
            }

            if (!Object.HasStateAuthority)
                return;

            NetworkedGameplayEnabled = enabled;
            ApplyGameplayGate(enabled);
        }

        // 복제된 입력 허용 상태 적용
        private void HandleGameplayEnabledChanged()
        {
            ApplyGameplayGate(NetworkedGameplayEnabled);
        }

        // 로컬 입력과 생존 수치 시뮬레이션을 함께 제어
        private void ApplyGameplayGate(bool enabled)
        {
            ApplyInputGate(enabled);
            ApplySimulationGate(enabled);
        }

        // Input Authority 플레이어의 입력 생성과 입력 컴포넌트 제어
        private void ApplyInputGate(bool enabled)
        {
            if (Object != null && !Object.HasInputAuthority)
                return;
            if (localInputEnabled == enabled)
                return;

            localInputEnabled = enabled;
            if (Runner != null)
                Runner.ProvideInput = enabled;

            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            if (!enabled)
            {
                inputStates.Clear();
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (!IsInputBehaviour(behaviour))
                        continue;

                    inputStates[behaviour] = behaviour.enabled;
                    behaviour.enabled = false;
                }
                return;
            }

            foreach (KeyValuePair<MonoBehaviour, bool> entry in inputStates)
            {
                if (entry.Key != null && entry.Value && !IsDowned)
                    entry.Key.enabled = true;
            }
            inputStates.Clear();
        }

        // 모든 피어의 생존 수치 관련 Update 제어
        private void ApplySimulationGate(bool enabled)
        {
            if (localSimulationEnabled == enabled)
                return;

            localSimulationEnabled = enabled;
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            if (!enabled)
            {
                simulationStates.Clear();
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (!IsSimulationBehaviour(behaviour))
                        continue;

                    simulationStates[behaviour] = behaviour.enabled;
                    behaviour.enabled = false;
                }
                return;
            }

            foreach (KeyValuePair<MonoBehaviour, bool> entry in simulationStates)
            {
                if (entry.Key != null)
                    entry.Key.enabled = entry.Value;
            }
            simulationStates.Clear();
        }

        // 플레이어 조작에 직접 관여하는 컴포넌트 판정
        private static bool IsInputBehaviour(MonoBehaviour behaviour)
        {
            return behaviour is PlayerController
                || behaviour is PlayerGrabber
                || behaviour is PlayerHotbar
                || behaviour is MeleeAttack
                || behaviour is PlayerItemGiver
                || behaviour is PlayerReviver
                || behaviour is PlayerInteractor
                || behaviour is PlayerSeatController
                || behaviour is PlayerObserver
                || behaviour != null && behaviour.GetType().Name == "CaveSwimController";
        }

        // 체크포인트 결과 화면에서 정지할 생존 수치 컴포넌트 판정
        private static bool IsSimulationBehaviour(MonoBehaviour behaviour)
        {
            return behaviour is DepletingStat
                || behaviour is OxygenDrowning
                || behaviour is HungerAffectsOxygen;
        }

        // 플레이어 기본 상태 세션 메모리 데이터
        private sealed class PlayerState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public HealthCheckpointState Health;
            public float Oxygen;
            public float OxygenMax;
            public float Hunger;
            public float HungerMax;
            public bool IsDowned;
        }
    }
}
