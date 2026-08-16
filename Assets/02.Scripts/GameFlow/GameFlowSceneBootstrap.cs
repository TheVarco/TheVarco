using System.Collections.Generic;
using CaveBlockout;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Varco.GameFlow
{
    // MainScene_young 전용 게임 흐름 런타임 설치기
    public static class GameFlowSceneBootstrap
    {
        // 다른 팀원 씬에 영향을 주지 않는 대상 씬 이름
        private const string TargetSceneName = "MainScene_final";

        // 시작 화면 이후 대상 씬 로드도 감지하도록 이벤트 등록
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadedHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        // 대상 씬이 첫 실행 씬인 경우 즉시 설치
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForCurrentScene()
        {
            // 대상 씬이 아니면 아무 오브젝트도 생성하지 않음
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != TargetSceneName)
                return;
            // 팀 네트워크 계층이 이미 설치한 조정자 보호
            if (Object.FindFirstObjectByType<GameFlowCoordinator>() != null)
                return;

            // 성공 체크포인트 판정의 기준 잠수함 탐색
            SubmarineController submarine = Object.FindFirstObjectByType<SubmarineController>();
            if (submarine == null)
            {
                Debug.LogError($"[GameFlow] {TargetSceneName} has no SubmarineController.");
                return;
            }

            // 기존 팀 브리지를 우선 사용하고 없으면 로컬 브리지 사용
            GameObject root = new("[GameFlow]");
            IGameFlowNetworkBridge bridge = FindNetworkBridge()
                ?? CreateDefaultBridge(root);
            GameFlowCoordinator coordinator = root.AddComponent<GameFlowCoordinator>();

            // 체크포인트 참가자와 구역 트리거 런타임 설치
            InstallCheckpointParticipants(root, submarine, bridge);
            InstallZoneTriggers(root, coordinator);

            // 씬에 미리 배치하고 인스펙터에서 연결한 결과 UI 탐색
            GameFlowResultUI resultUI = Object.FindFirstObjectByType<GameFlowResultUI>(
                FindObjectsInactive.Include);
            if (resultUI != null)
                resultUI.Initialize(coordinator);
            else
                Debug.LogError($"[GameFlow] {TargetSceneName} has no configured GameFlowResultUI.");

            coordinator.Initialize(submarine, bridge);
        }

        // 씬에 배치된 팀 네트워크 브리지 탐색
        private static IGameFlowNetworkBridge FindNetworkBridge()
        {
            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IGameFlowNetworkBridge bridge)
                    return bridge;
            }

            return null;
        }

        // NetworkTestStarter가 있는 씬은 Fusion 브리지 사용
        private static IGameFlowNetworkBridge CreateDefaultBridge(GameObject root)
        {
            NetworkTestStarter networkStarter =
                Object.FindFirstObjectByType<NetworkTestStarter>(FindObjectsInactive.Include);
            return networkStarter != null
                ? root.AddComponent<FusionGameFlowNetworkBridge>()
                : root.AddComponent<LocalGameFlowBridge>();
        }

        // 추가로 로드된 대상 씬에 게임 흐름 설치
        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == TargetSceneName)
                InstallForCurrentScene();
        }

        // 잠수함 적 장애물 체크포인트 참가자 설치
        private static void InstallCheckpointParticipants(
            GameObject root,
            SubmarineController submarine,
            IGameFlowNetworkBridge bridge)
        {
            // 잠수함은 세션 내 고정 키 사용
            SubmarineCheckpointParticipant submarineParticipant =
                submarine.GetComponent<SubmarineCheckpointParticipant>()
                ?? submarine.gameObject.AddComponent<SubmarineCheckpointParticipant>();
            submarineParticipant.InitializeCheckpointId("submarine:main");

            // 지원하는 적 종류를 중복 없이 수집
            HashSet<GameObject> enemyRoots = new();
            foreach (SharkController shark in Object.FindObjectsByType<SharkController>(FindObjectsSortMode.None))
                enemyRoots.Add(shark.gameObject);
            foreach (OctopusController octopus in Object.FindObjectsByType<OctopusController>(FindObjectsSortMode.None))
                enemyRoots.Add(octopus.gameObject);
            foreach (UrchinController urchin in Object.FindObjectsByType<UrchinController>(FindObjectsSortMode.None))
                enemyRoots.Add(urchin.gameObject);

            // 계층 경로 기반 적 키 설정
            foreach (GameObject enemyRoot in enemyRoots)
            {
                EnemyCheckpointParticipant participant =
                    enemyRoot.GetComponent<EnemyCheckpointParticipant>()
                    ?? enemyRoot.AddComponent<EnemyCheckpointParticipant>();
                participant.InitializeCheckpointId($"enemy:{BuildHierarchyPath(enemyRoot.transform)}");
            }

            // 장애물은 전체 패턴을 하나의 참가자로 관리
            ObstacleCheckpointParticipant obstacleParticipant =
                root.AddComponent<ObstacleCheckpointParticipant>();
            obstacleParticipant.InitializeCheckpointId("obstacles:main-scene");
            obstacleParticipant.InitializeFromScene();

            // Carryable 전체와 핫바/Item Zone 점유 관계는 하나의 트랜잭션으로 관리한다.
            CarryableCheckpointParticipant carryableParticipant =
                root.AddComponent<CarryableCheckpointParticipant>();
            carryableParticipant.InitializeCheckpointId("items:session");
            carryableParticipant.Initialize(bridge);
        }

        // CaveZoneMarker 크기를 이용한 Z1부터 Z7 트리거 설치
        private static void InstallZoneTriggers(GameObject root, GameFlowCoordinator coordinator)
        {
            CaveZoneMarker[] markers = Object.FindObjectsByType<CaveZoneMarker>(FindObjectsSortMode.None);
            CaveZoneMarker finalZoneMarker = null;
            int finalZone = 0;
            foreach (CaveZoneMarker marker in markers)
            {
                if (!TryParseZone(marker.zoneId, out int zone))
                    continue;

                // 마커 전체 영역을 체크포인트 감지 범위로 사용
                GameObject triggerObject = new($"CheckpointTrigger_Z{zone}");
                triggerObject.transform.SetParent(marker.transform, false);
                BoxCollider collider = triggerObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = marker.guideSize;
                ZoneCheckpointTrigger trigger = triggerObject.AddComponent<ZoneCheckpointTrigger>();
                trigger.Initialize(coordinator, zone);

                if (zone > finalZone)
                {
                    finalZone = zone;
                    finalZoneMarker = marker;
                }
            }

            // 씬에 유효한 구역 마커가 하나도 없으면 성공 출구 생성을 중단
            if (finalZoneMarker == null)
            {
                Debug.LogError("[GameFlow] No valid CaveZoneMarker was found.", root);
                return;
            }

            // 씬에 실제로 존재하는 마지막 구역의 진행 방향 끝에 성공 출구 배치
            GameObject exitObject = new($"GoalExitTrigger_Z{finalZone}");
            exitObject.transform.SetParent(finalZoneMarker.transform, false);
            exitObject.transform.localPosition =
                Vector3.forward * (finalZoneMarker.guideSize.z * 0.48f);

            // Z6 전체 체크포인트 볼륨은 그대로 두고, 끝의 얇은 성공 Trigger만
            // 상어의 순찰/추격 쿼리가 감지하는 장애물 레이어로 사용한다.
            if (finalZone == 6)
            {
                int obstacleLayer = LayerMask.NameToLayer("Obstacle");
                if (obstacleLayer >= 0)
                    exitObject.layer = obstacleLayer;
                else
                    Debug.LogError("[GameFlow] Obstacle layer was not found for the Z6 exit boundary.");
            }

            BoxCollider exitCollider = exitObject.AddComponent<BoxCollider>();
            exitCollider.isTrigger = true;
            exitCollider.size = new Vector3(
                finalZoneMarker.guideSize.x * 0.85f,
                finalZoneMarker.guideSize.y * 0.85f,
                4f);
            GoalExitTrigger exitTrigger = exitObject.AddComponent<GoalExitTrigger>();
            exitTrigger.Initialize(coordinator);
        }

        // Z1부터 Z7 형식만 유효 구역으로 변환
        private static bool TryParseZone(string zoneId, out int zone)
        {
            zone = 0;
            return !string.IsNullOrWhiteSpace(zoneId)
                && zoneId.Length == 2
                && zoneId[0] == 'Z'
                && int.TryParse(zoneId[1].ToString(), out zone)
                && zone is >= 1 and <= 7;
        }

        // 적 오브젝트의 세션 내 고유 계층 경로 생성
        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "missing";

            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }
            return path;
        }
    }
}
