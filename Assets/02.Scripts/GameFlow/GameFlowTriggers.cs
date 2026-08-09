using UnityEngine;

namespace Varco.GameFlow
{
    // 잠수함 진입 시 해당 구역 체크포인트를 요청하는 트리거
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ZoneCheckpointTrigger : MonoBehaviour
    {
        // 게임 흐름 전달 대상
        private GameFlowCoordinator coordinator;
        // Z1부터 Z7까지의 구역 번호
        private int zone;

        // 런타임 생성 직후 조정자와 구역 연결
        public void Initialize(GameFlowCoordinator targetCoordinator, int zoneNumber)
        {
            coordinator = targetCoordinator;
            zone = Mathf.Clamp(zoneNumber, 1, 7);
            GetComponent<Collider>().isTrigger = true;
        }

        // 잠수함 또는 잠수함 자식 콜라이더만 처리
        private void OnTriggerEnter(Collider other)
        {
            SubmarineController submarine = other != null
                ? other.GetComponentInParent<SubmarineController>()
                : null;
            coordinator?.TryActivateCheckpoint(zone, submarine);
        }
    }

    // 잠수함 진입 시 게임 성공을 요청하는 Z7 출구 트리거
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class GoalExitTrigger : MonoBehaviour
    {
        // 성공 결과 전달 대상
        private GameFlowCoordinator coordinator;

        // 런타임 생성 직후 조정자 연결
        public void Initialize(GameFlowCoordinator targetCoordinator)
        {
            coordinator = targetCoordinator;
            GetComponent<Collider>().isTrigger = true;
        }

        // 등록된 잠수함 여부는 조정자에서 최종 확인
        private void OnTriggerEnter(Collider other)
        {
            SubmarineController submarine = other != null
                ? other.GetComponentInParent<SubmarineController>()
                : null;
            coordinator?.ReportExitReached(submarine);
        }
    }
}
