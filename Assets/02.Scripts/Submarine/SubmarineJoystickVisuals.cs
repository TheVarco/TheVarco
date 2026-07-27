using UnityEngine;

// SubmarineController가 계산한 입력값을 이용해 좌우 조이스틱 모델을 움직이기
public class SubmarineJoystickVisuals : MonoBehaviour
{
    private SubmarineController controller;
    [SerializeField] private Transform leftPivot;
    [SerializeField] private Transform rightPivot;

    [Header("Tilt")]
    // 최대 기울기, 목표 회전 추종 속도, 각 피벗의 회전축을 설정
    [SerializeField] private float maxTiltAngle = 15f;
    [SerializeField] private float responseSpeed = 8f;
    [SerializeField] private Vector3 leftTiltAxis = Vector3.right;
    [SerializeField] private Vector3 rightTiltAxis = Vector3.forward;
    [SerializeField] private bool invertLeft = true;
    [SerializeField] private bool invertRight = true;

    // 조이스틱 회전값
    // 입력 회전을 이 값 뒤에 곱해 모델의 초기 기울기 보존
    private Quaternion leftNeutralRotation;
    private Quaternion rightNeutralRotation;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<SubmarineController>();
        
        if (leftPivot != null)
            leftNeutralRotation = leftPivot.localRotation;
        if (rightPivot != null)
            rightNeutralRotation = rightPivot.localRotation;
    }

    private void LateUpdate()
    {
        // 운전자가 없거나 컨트롤러가 없으면 입력 0 처리
        float throttle = controller != null ? controller.ThrottleInput : 0f;
        float steering = controller != null ? controller.SteeringInput : 0f;

        // 프레임 속도에 영향받지 않도록 처리
        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, responseSpeed) * Time.deltaTime);

        if (leftPivot != null)
        {
            // 왼쪽 조이스틱은 W/S 전진·후진 표현
            float sign = invertLeft ? -1f : 1f;
            Quaternion target = leftNeutralRotation
                * Quaternion.AngleAxis(throttle * maxTiltAngle * sign, NormalizedAxis(leftTiltAxis));
            leftPivot.localRotation = Quaternion.Slerp(leftPivot.localRotation, target, blend);
        }

        if (rightPivot != null)
        {
            // 오른쪽 조이스틱은 A/D 좌우 회전 표현
            float sign = invertRight ? -1f : 1f;
            Quaternion target = rightNeutralRotation
                * Quaternion.AngleAxis(-steering * maxTiltAngle * sign, NormalizedAxis(rightTiltAxis));
            rightPivot.localRotation = Quaternion.Slerp(rightPivot.localRotation, target, blend);
        }
    }

    // 조이스틱 자식의 회전시킬 Transform을 찾는다.
    private Transform FindRigPivot(string rigName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child.name != rigName)
                continue;

            foreach (Transform descendant in child.GetComponentsInChildren<Transform>(true))
            {
                if (descendant.name == "Boot_High")
                    return descendant;
            }
        }

        return null;
    }

    // 회전축 정규화
    // 잘못된 영벡터가 들어오면 X축을 기본값으로 사용
    private static Vector3 NormalizedAxis(Vector3 axis)
    {
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
    }
}
