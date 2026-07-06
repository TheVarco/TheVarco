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
    public float lookAtHeight = 1f; //카메라 기준 플레이어 높이 설정

    [Header("1인칭 설정")]
    public Vector3 firstPersonOffset = new Vector3(0f, 0.5f, 0.2f);

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