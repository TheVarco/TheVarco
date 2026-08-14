using UnityEngine;

public static class VarcoAudio
{
    public static void PlayOneShotAt(
        Transform anchor,
        AudioClip clip,
        float volume = 1f,
        float minDistance = 1.5f,
        float maxDistance = 35f,
        Transform emitterParent = null)
    {
        if (anchor == null || clip == null)
            return;

        GameObject emitter = new GameObject("Audio - " + clip.name);
        // 충돌체나 소모 아이템이 직후 비활성화/Despawn돼도 OneShot이 잘리지 않게
        // 월드 위치에 독립 방출기를 만든다.
        emitter.transform.position = anchor.position;
        if (emitterParent != null)
            emitter.transform.SetParent(emitterParent, true);

        AudioSource source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = Mathf.Clamp01(volume);
        source.PlayOneShot(clip);
        Object.Destroy(emitter, Mathf.Max(0.1f, clip.length + 0.2f));
    }

    public static AudioSource EnsureLoop(
        Transform anchor,
        string sourceName,
        AudioClip clip,
        bool spatial,
        float volume,
        float minDistance = 2f,
        float maxDistance = 45f)
    {
        if (anchor == null || clip == null)
            return null;

        Transform child = anchor.Find(sourceName);
        GameObject emitter = child != null ? child.gameObject : new GameObject(sourceName);
        if (child == null)
            emitter.transform.SetParent(anchor, false);

        AudioSource source = emitter.GetComponent<AudioSource>();
        if (source == null)
            source = emitter.AddComponent<AudioSource>();

        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = spatial ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = Mathf.Clamp01(volume);
        if (!source.isPlaying)
            source.Play();
        return source;
    }
}

// MainScene_final의 수중 배경음을 한 번만 유지한다.
public static class VarcoAmbientAudioBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartAmbience()
    {
        VarcoAudioLibrary library = VarcoAudioLibrary.Instance;
        if (library == null || library.underwaterAmbience == null)
            return;
        if (Object.FindFirstObjectByType<PlayerCameraRig>() == null)
            return;

        GameObject root = GameObject.Find("Varco Global Audio");
        if (root == null)
            root = new GameObject("Varco Global Audio");
        VarcoAudio.EnsureLoop(root.transform, "Underwater Ambience", library.underwaterAmbience, false, 0.28f);
    }
}
