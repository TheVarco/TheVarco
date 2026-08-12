using UnityEngine;
using UnityEngine.EventSystems;

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

    [Header("3인칭 카메라 충돌")]
    [Tooltip("이 레이어에 막히면 카메라를 앞으로 당겨서 벽/천장을 뚫지 않게 함. 플레이어 레이어는 자동 제외")]
    public LayerMask cameraObstacleMask = ~0;
    [Tooltip("판정 굵기. 0이면 모서리에서 살짝 뚫림")]
    public float cameraCollisionRadius = 0.2f;
    [Tooltip("벽에서 이만큼 떨어뜨림. 카메라 근접 평면에서 계산한 최소치보다 작으면 그쪽이 우선됨")]
    public float cameraCollisionPadding = 0.25f;
    [Tooltip("이 거리보다 가까워지면 아예 눈 위치로 붙인다. 벽 코앞에서 캐릭터 등짝만 보이는 것 방지")]
    public float minThirdPersonDistance = 0.8f;

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

    // 네트워크 입력 전송용 (NetworkTestStarter.OnInput에서 읽어감)
    public float Yaw => yaw;
    public float Pitch => pitch;

    void Start()
    {
        if (target != null)
            Cursor.lockState = CursorLockMode.Locked;
        else
            Cursor.lockState = CursorLockMode.None;
    }

    // PlayerController가 스폰 시(입력 권한이 있을 때만) 직접 호출해서 타겟을 등록함.
    // 여러 명이 접속했을 때 아무 플레이어나 찾아버리는 걸 방지하기 위해
    // 카메라가 찾아 나서지 않고 로컬 플레이어 쪽에서 스스로 등록하는 방식으로 바꿈.
    public void SetTarget(Transform t)
    {
        target = t;
        if (target != null)
            Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // ESC 키로 마우스 커서 잠금/해제 토글 (UI 조작 및 테스트 편의)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
        }

        // 플레이어가 연결된 상태에서 게임 화면을 좌클릭하면 다시 마우스 잠금
        // UI 위의 클릭은 버튼 입력이 끝날 수 있도록 잠금 대상에서 제외
        bool isPointerOverUi = EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject();
        if (target != null
            && Cursor.lockState != CursorLockMode.Locked
            && Input.GetMouseButtonDown(0)
            && !isPointerOverUi)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        // 마우스가 잠겨있을 때만 시점 회전
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            if (clampPitch)
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

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

        Vector3 eyePosition = target.position + lookRotation * eyeOffset;

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

            // 눈에서 카메라 자리까지 훑어서 막히면 앞으로 당긴다.
            // 기절하면 몸이 위로 떠올라 천장에 붙는데, 그때 카메라만 지형 안으로 들어가 화면이 깨짐
            Vector3 toCamera = desiredPosition - eyePosition;
            float distance = toCamera.magnitude;

            // 플레이어 레이어는 뺀다. 안 그러면 자기 몸에 막혀 카메라가 얼굴에 붙음
            int mask = cameraObstacleMask & ~(1 << target.gameObject.layer);

            if (distance > 0.001f
                && Physics.SphereCast(eyePosition, cameraCollisionRadius, toCamera / distance,
                                      out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                // 근접 평면 안쪽에 벽이 들어오면 벽이 잘려서 너머가 비쳐 보인다.
                // 화면 모서리까지 고려한 실제 필요 거리를 카메라 설정에서 직접 뽑는다
                float safePadding = cameraCollisionPadding;
                if (cam != null)
                {
                    float t = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    safePadding = Mathf.Max(safePadding,
                        cam.nearClipPlane * Mathf.Sqrt(1f + t * t * (1f + cam.aspect * cam.aspect)));
                }

                // 막혔을 때 부드럽게 따라가면 그동안 이미 벽 안에 들어가 있으므로 즉시 붙인다.
                // 벗어날 때는 아래 Lerp를 타서 부드럽게 돌아옴
                float allowed = hit.distance - safePadding;
                transform.position = allowed <= minThirdPersonDistance
                    ? eyePosition // 너무 가까우면 등짝만 보이므로 차라리 1인칭처럼
                    : eyePosition + toCamera / distance * allowed;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, desiredPosition, followSmoothing * Time.deltaTime);
            }
        }
    }
}
