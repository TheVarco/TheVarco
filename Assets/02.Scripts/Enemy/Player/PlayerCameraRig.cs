using UnityEngine;

// 마우스로 상하좌우를 자유롭게 둘러보는 카메라 리그.
// PlayerController의 lookReference 자리에 이 오브젝트를 연결하면 된다.
// 1인칭/3인칭 전환은 이 스크립트 하나에서 처리하고, PlayerController는 건드릴 필요 없음.
public class PlayerCameraRig : MonoBehaviour
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("모드")]
    public ViewMode viewMode = ViewMode.ThirdPerson;

    [Header("마우스 감도")]
    public float mouseSensitivity = 2.5f;
    [Tooltip("수영이므로 위아래 각도 제한을 걸지 않으면 완전 자유 시점이 됨")]
    public bool clampPitch = false;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("3인칭 설정")]
    public Transform target;              // 따라다닐 플레이어(otter 캐릭터) Transform
    public Vector3 thirdPersonOffset = new Vector3(0f, 1.2f, -4f);
    public float followSmoothing = 10f;
    [Tooltip("target 피벗 기준 얼마나 위쪽을 바라볼지. 0이면 발밑을 보게 되어 화면 중앙에서 벗어나 보임")]
    public float lookAtHeight = 1f;

    [Header("1인칭 설정")]
    public Vector3 firstPersonOffset = new Vector3(0f, 0.5f, 0.2f);

    [Header("조준 줌 (RangedAttack이 SetAiming으로 켜고 끔)")]
    [Tooltip("실제 화각을 조절할 Camera 컴포넌트 연결 (Main Camera)")]
    public Camera cam;
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    public float zoomSpeed = 8f;

    // 지금 조준 중인지. RangedAttack이 우클릭 누르고 있는 동안 true로 설정해줌.
    // PlayerController도 이 값을 봐서 조준 중엔 몸을 카메라 방향으로 돌린다.
    public bool IsAiming { get; private set; }

    public void SetAiming(bool aiming)
    {
        IsAiming = aiming;
    }

    private float yaw;
    private float pitch;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        if (clampPitch)
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // 테스트용 임시 전환 키 (나중에 설정 메뉴 UI로 교체)
        if (Input.GetKeyDown(KeyCode.V))
            viewMode = viewMode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson;

        // 조준 중이면 화각을 좁혀서(줌인) 확대된 느낌을 줌
        if (cam != null)
        {
            float targetFOV = IsAiming ? aimFOV : normalFOV;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, zoomSpeed * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (viewMode == ViewMode.FirstPerson)
        {
            transform.position = target.position + firstPersonOffset;
        }
        else
        {
            // 마우스로 정한 yaw/pitch는 "카메라가 target 주위 어디에 위치할지"만 정하고,
            // 실제로 보는 방향은 아래 LookAt이 항상 target을 정확히 조준하도록 덮어씀
            Vector3 desiredPosition = target.position + transform.rotation * thirdPersonOffset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSmoothing * Time.deltaTime);
            transform.LookAt(target.position + Vector3.up * lookAtHeight);
        }
    }
}