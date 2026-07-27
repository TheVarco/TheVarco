using UnityEngine;

// 마우스로 상하좌우를 자유롭게 둘러보는 카메라 리그.
// 핵심 아이디어: 1인칭이든 3인칭이든 "같은 시선 방향"을 공유하고,
// 3인칭은 그 시선 방향을 따라 눈 위치에서 그냥 뒤로 빼는 것뿐임.
// (예전엔 3인칭을 캐릭터 주위를 도는 별도의 오빗 시스템으로 만들어서,
//  가파른 각도에서 위치가 뒤집히는 등 복잡한 문제가 계속 생겼음. 이 방식은 그 문제 자체가 안 생김)
public class PlayerCameraRig : MonoBehaviour
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("모드")]
    public ViewMode viewMode = ViewMode.ThirdPerson;

    [Header("마우스 감도")]
    public float mouseSensitivity = 2.5f;
    public bool clampPitch = true;
    public float minPitch = -89f;
    public float maxPitch = 89f;

    [Header("참조")]
    public Transform target; // 따라다닐 플레이어 Transform

    [Header("눈 위치 (1인칭 기준점, 3인칭도 여기서부터 뒤로 뺌)")]
    public Vector3 eyeOffset = new Vector3(0f, 0.5f, 0f);

    [Header("3인칭 설정")]
    [Tooltip("눈 위치에서 보는 방향 반대쪽으로 얼마나 뺄지")]
    public float thirdPersonDistance = 4f;
    [Tooltip("눈 위치보다 얼마나 위에서 볼지")]
    public float thirdPersonHeightOffset = 0.3f;
    public float followSmoothing = 10f;

    [Header("조준 줌 (무기 아이템이 SetAiming으로 켜고 끔)")]
    public Camera cam;
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    public float zoomSpeed = 8f;

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
        if (target == null)
        {
            FindPlayerTarget();
        }
    }

    private void FindPlayerTarget()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("OtterPlayer");
        if (player == null)
        {
            PlayerController controller = FindFirstObjectByType<PlayerController>();
            if (controller != null) player = controller.gameObject;
        }
        if (player != null) target = player.transform;
    }

    void Update()
    {
        if (target == null)
        {
            FindPlayerTarget();
        }

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        if (clampPitch)
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

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

        // 1인칭/3인칭 둘 다 완전히 같은 회전(yaw, pitch)을 공유함 -> 전환할 때 시점이 튈 일 자체가 없음
        Quaternion lookRotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.rotation = lookRotation;

        Vector3 eyePosition = target.position + eyeOffset;

        if (viewMode == ViewMode.FirstPerson)
        {
            transform.position = eyePosition;
        }
        else
        {
            // 눈 위치에서, 지금 보는 방향의 반대쪽(뒤)으로 정해진 거리만큼 빼고, 살짝 위로 올림.
            // 벡터를 회전시키는 게 아니라 "직선으로 빼는" 것뿐이라, pitch가 아무리 가팔라도 뒤집히지 않음
            Vector3 desiredPosition = eyePosition
                - lookRotation * Vector3.forward * thirdPersonDistance
                + Vector3.up * thirdPersonHeightOffset;

            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSmoothing * Time.deltaTime);
        }
    }
}