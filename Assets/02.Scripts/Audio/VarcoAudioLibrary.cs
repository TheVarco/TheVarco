using UnityEngine;

public enum PlayerAudioCue
{
    BubbleGunShot,
    OxygenTankUse,
    Eat
}

// 모든 게임 SFX 참조를 한 Asset에 모아 오디오 폴더 구조가 바뀌어도
// 각 프리팹의 참조가 연쇄적으로 끊기지 않게 한다.
public sealed class VarcoAudioLibrary : ScriptableObject
{
    private const string ResourceName = "VarcoAudioLibrary";
    private static VarcoAudioLibrary instance;

    public AudioClip bubbleGunShot;
    public AudioClip caveRockImpact;
    public AudioClip sharkBite;
    public AudioClip sonarPing;
    public AudioClip submarineExit;
    public AudioClip submarineHum;
    public AudioClip oxygenTankUse;
    public AudioClip underwaterAmbience;
    public AudioClip underwaterSwim;
    public AudioClip underwaterTornado;
    public AudioClip ventBubbles;
    public AudioClip itemEat;
    public AudioClip submarineHatch;
    public AudioClip hammerMotion;
    public AudioClip hammerMetalImpact;
    public AudioClip[] submarineImpacts;

    public static VarcoAudioLibrary Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<VarcoAudioLibrary>(ResourceName);
            return instance;
        }
    }

    public AudioClip GetPlayerCue(PlayerAudioCue cue)
    {
        switch (cue)
        {
            case PlayerAudioCue.BubbleGunShot: return bubbleGunShot;
            case PlayerAudioCue.OxygenTankUse: return oxygenTankUse;
            case PlayerAudioCue.Eat: return itemEat;
            default: return null;
        }
    }

    public AudioClip GetSubmarineImpact(int seed)
    {
        if (submarineImpacts == null || submarineImpacts.Length == 0)
            return null;
        return submarineImpacts[(seed & int.MaxValue) % submarineImpacts.Length];
    }
}
