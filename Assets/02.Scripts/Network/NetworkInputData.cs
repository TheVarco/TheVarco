using Fusion;

// 클라이언트가 매 틱 서버로 보내는 입력값. PlayerController가 FixedUpdateNetwork에서 이걸 읽어서 이동/회전을 계산함.
public struct NetworkInputData : INetworkInput
{
    public float Horizontal;
    public float Vertical;
    public NetworkBool Up;    // Space (수영 모드 상승 / 걷기 모드 점프)
    public NetworkBool Down;  // Ctrl (수영 모드 하강)
    public NetworkBool Dash;  // Shift
    public float Yaw;
    public float Pitch;
}
