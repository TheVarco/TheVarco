using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Varco.Intro.EditorTools
{
    // MainScene_Intro_Cinemachine 의 인트로 컷씬을 저작한다.
    //
    // 씬에는 CinemachineCamera_1/2/3, CutsceenDirector(PlayableDirector), 그리고 Object_6 발광
    // 깜빡임 트랙 하나짜리 Timeline 이 이미 있다. 여기에 카메라 전환(CinemachineTrack)과 잠수함
    // 이탈 애니메이션을 얹는다.
    //
    // 진입점을 Build 와 Verify 로 나눈 이유: director.Evaluate() 는 씬의 실제 트랜스폼에 값을 쓴다.
    // 검증 스크럽을 한 뒤 같은 메서드에서 씬을 저장하면 잠수함이 마지막 스크럽 위치로 직렬화되어
    // 원본 배치가 조용히 파괴된다. 그래서 Verify 는 절대 저장하지 않는다.
    public static class IntroCutsceneBuilder
    {
        const string ScenePath = "Assets/01.Scenes/MainScene_Intro_Cinemachine.unity";
        const string DislodgeClipPath = "Assets/08.Cinemachine/Intro/Animations/Submarine_Dislodge.anim";
        const string FlickerClipPath = "Assets/08.Cinemachine/Intro/Animations/Submarine_Object6_Flicker.anim";

        const string FadeClipPath = "Assets/08.Cinemachine/Intro/Animations/Intro_FadeIn.anim";

        const string ShotTrackName = "Cinemachine Shots";
        const string DislodgeTrackName = "Submarine Dislodge";
        const string FadeTrackName = "Screen Fade";
        const string FlickerTrackName = "Object_6 Emission Flicker";

        const string FadeRootName = "CutsceneFade";
        const string FadeImageName = "FadeImage";

        const string Cam1Name = "CinemachineCamera_1";
        const string Cam2Name = "CinemachineCamera_2";
        const string Cam3Name = "CinemachineCamera_3";
        const string SubmarineName = "Submarine_final";

        // --- 샷 배치 (초). 겹치는 1초가 곧 블렌드 시간이다 ---
        const double ShotAStart = 0.0, ShotADuration = 5.0;   // CinemachineCamera_1 (돌 동굴 길)
        const double ShotBStart = 4.0, ShotBDuration = 5.5;   // CinemachineCamera_3 (불빛 클로즈업)
        const double ShotCStart = 8.5, ShotCDuration = 6.5;   // CinemachineCamera_2 (잠수함 와이드)
        const double FlickerStart = 5.6;                      // 3번이 완전히 잡힌 뒤 깜빡임
        const double DislodgeStart = 9.5, DislodgeDuration = 5.5;

        // 검은 화면에서 Shot A 로 밝아지는 페이드인.
        // FadeHold 동안 완전한 검정을 유지한 뒤 FadeIn 동안 걷힌다.
        // Shot A 는 0~4초 단독 구간이므로, 이 둘의 합이 4초를 너무 잡아먹지 않게 둘 것.
        const float FadeHold = 0.3f;
        const float FadeIn = 1.2f;

        // --- 잠수함 이탈 연출 ---
        const float DislodgeDistance = 10.0f;  // 로컬 forward 방향 총 이동 거리 (m)
        const float ShudderPos = 0.15f;        // 떨림 위치 진폭 (m)
        const float ShudderRot = 2.0f;         // 떨림 회전 진폭 (deg)
        const float HullProbeRadius = 3.3f;    // 종점 장애물 프로브 반경 (선체 반폭)

        // 이탈 애니메이션 키프레임.
        // fwd 는 DislodgeDistance 의 비율, lat/vert 는 ShudderPos 의 비율,
        // rx/ry/rz 는 ShudderRot 의 비율(1.0 = 2도). 충격 구간은 1.0 을 넘는다.
        //                                    t      fwd    lat    vert    rx     ry     rz
        static readonly float[,] DislodgeKeys = {
            // 떨림 — 바위에 걸려 진동한다
            {                                 0.00f, 0.000f,  0.00f,  0.00f,  0.0f,  0.0f,  0.0f },
            {                                 0.18f, 0.003f,  0.67f,  0.27f,  0.5f,  0.15f, -0.40f },
            {                                 0.30f, 0.000f, -0.47f, -0.20f, -0.35f, -0.10f,  0.30f },
            {                                 0.55f, 0.002f,  0.13f,  0.07f,  0.10f,  0.00f, -0.05f },
            {                                 0.72f, 0.005f, -0.87f,  0.40f, -0.70f,  0.20f,  0.55f },
            {                                 0.88f, 0.002f,  0.60f, -0.33f,  0.45f, -0.15f, -0.35f },
            {                                 1.05f, 0.003f,  0.00f,  0.00f,  0.05f,  0.0f,   0.0f },
            {                                 1.30f, 0.008f,  1.00f,  0.53f,  0.90f,  0.25f, -0.65f },
            {                                 1.45f, 0.005f, -0.73f, -0.40f, -0.60f, -0.20f,  0.45f },
            {                                 1.62f, 0.010f,  0.47f,  0.20f,  0.30f,  0.10f, -0.25f },
            {                                 1.80f, 0.007f, -0.33f, -0.13f, -0.20f, -0.05f,  0.15f },
            {                                 2.00f, 0.013f,  0.13f,  0.07f,  0.10f,  0.0f,  -0.05f },
            // 이탈 충격 — 한 번 크게 튄다
            {                                 2.10f, 0.050f,  1.47f,  0.80f,  2.00f,  0.50f, -1.30f },
            {                                 2.25f, 0.063f, -0.93f, -0.47f, -1.10f, -0.30f,  0.75f },
            {                                 2.40f, 0.075f,  0.40f,  0.20f,  0.40f,  0.10f, -0.25f },
            // 이탈 — 부드럽게 가속했다가 감속하며 빠져나온다 (smoothstep)
            {                                 3.00f, 0.165f,  0.20f,  0.13f,  0.15f,  0.05f, -0.10f },
            {                                 3.60f, 0.405f,  0.00f,  0.00f, -0.30f,  0.0f,   0.05f },
            {                                 4.20f, 0.668f, -0.13f,  0.07f, -0.70f, -0.05f,  0.15f },
            {                                 4.80f, 0.892f, -0.07f,  0.00f, -1.10f, -0.05f,  0.10f },
            {                                 5.50f, 1.000f,  0.00f,  0.00f, -1.50f,  0.0f,   0.0f },
        };

        // 검증 스크럽 시각. 14.99 가 프레이밍 확인 시점이고, 15.0 은 duration(15.0 - 1틱)을
        // 넘어선 지점이라 WrapMode 동작 확인 전용이다.
        static readonly float[] ScrubTimes =
        {
            0f, 0.3f, 0.9f, 1.5f, 2f, 4.0f, 4.5f, 4.99f, 5.0f, 5.5f, 5.6f, 6.2f, 6.8f,
            8.5f, 9.0f, 9.4f, 9.6f, 11.5f, 14.99f, 15.0f
        };

        [MenuItem("Tools/Varco/Intro/컷씬 트랙 생성 (Build)")]
        public static void BuildMenu() => Run(BuildInternal, false);

        [MenuItem("Tools/Varco/Intro/컷씬 검증 (Verify)")]
        public static void VerifyMenu() => Run(VerifyInternal, false);

        [MenuItem("Tools/Varco/Intro/깜빡임 시작값을 0으로 (1회성)")]
        public static void FixFlickerStartMenu() => Run(FixFlickerStartInternal, false);

        // 배치모드 진입점 (-executeMethod). 끝나면 에디터를 종료한다.
        public static void Build() => Run(BuildInternal, true);
        public static void Verify() => Run(VerifyInternal, true);
        public static void FixFlickerStart() => Run(FixFlickerStartInternal, true);

        // 깜빡임 클립의 첫 키를 0으로 내린다. 원래 0.6 이었는데 정상 점등이 10 이라
        // 6% — "꺼진" 게 아니라 희미하게 켜진 상태로 읽혔다.
        // 사용자가 만든 애셋을 고치는 1회성 작업이라 Build 와 분리했다. Build 는 이 클립의
        // 커브를 건드리지 않으므로 재실행해도 되돌아가지 않는다.
        static void FixFlickerStartInternal()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FlickerClipPath);
            if (clip == null) throw new Exception($"'{FlickerClipPath}' 를 찾지 못했습니다.");

            int changed = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("material._EmissionColor.")) continue;
                // 알파는 밝기가 아니다. 현재 상수 1 이고 t=0 키도 없으므로 건드리지 않는다.
                if (binding.propertyName.EndsWith(".a")) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;

                var keys = curve.keys;
                if (keys[0].time > 0.0001f)
                {
                    Debug.LogWarning($"[Intro] {binding.propertyName} 의 첫 키가 t={keys[0].time:F4} 라 건너뜁니다.");
                    continue;
                }
                if (Mathf.Approximately(keys[0].value, 0f)) continue;

                float before = keys[0].value;
                keys[0].value = 0f;            // 탄젠트(Infinity = 계단형)는 그대로 유지된다
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                Log($"{binding.propertyName}: 첫 키 {before:F3} → 0");
                changed++;
            }

            if (changed == 0) { Debug.LogWarning("[Intro] 바뀐 커브가 없습니다."); return; }
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Log($"깜빡임 시작값 정리 완료 — 커브 {changed}개 (알파는 그대로).");
        }

        static void Run(Action body, bool exitWhenDone)
        {
            try
            {
                body();
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (exitWhenDone) EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ Build

        static void BuildInternal()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var ctx = Context.Collect();

            var timeline = ctx.Timeline;
            Log($"Timeline '{timeline.name}' 트랙 {timeline.GetRootTracks().Count()}개로 시작");

            // 재실행 대비: 이전에 만든 트랙이 있으면 바인딩/노출참조까지 깨끗이 지운다
            RemoveGeneratedTracks(timeline, ctx.Director);

            // --- 1) 카메라 전환 트랙 ---
            var shotTrack = timeline.CreateTrack<CinemachineTrack>(ShotTrackName);
            ctx.Director.SetGenericBinding(shotTrack, ctx.Brain);
            CreateShot(shotTrack, ctx.Director, ctx.Cam1, "Shot A - " + Cam1Name, ShotAStart, ShotADuration);
            CreateShot(shotTrack, ctx.Director, ctx.Cam3, "Shot B - " + Cam3Name, ShotBStart, ShotBDuration);
            CreateShot(shotTrack, ctx.Director, ctx.Cam2, "Shot C - " + Cam2Name, ShotCStart, ShotCDuration);
            EditorUtility.SetDirty(shotTrack);

            // --- 2) 기존 깜빡임 클립을 3번 카메라 구간으로 이동 ---
            var flickerTrack = timeline.GetOutputTracks().FirstOrDefault(t => t.name == FlickerTrackName);
            if (flickerTrack == null)
                throw new Exception($"'{FlickerTrackName}' 트랙을 찾지 못했습니다. Timeline 이 예상과 다릅니다.");
            var flickerClip = flickerTrack.GetClips().FirstOrDefault();
            if (flickerClip == null)
                throw new Exception($"'{FlickerTrackName}' 트랙에 클립이 없습니다.");
            flickerClip.start = FlickerStart;
            Log($"깜빡임 클립 '{flickerClip.displayName}' → start {flickerClip.start:F2}, " +
                $"duration {flickerClip.duration:F2} (end {flickerClip.end:F2})");
            EditorUtility.SetDirty(flickerTrack);

            // --- 3) 잠수함 이탈 애니메이션 ---
            // 커브는 시작 포즈 기준 상대값이고, 실제 포즈는 트랙 오프셋이 담당한다. (아래 주석 참조)
            Vector3 subPos = ctx.Submarine.transform.localPosition;
            Quaternion subRot = ctx.Submarine.transform.localRotation;
            var animClip = BuildDislodgeClip(subPos, subRot);
            ProbeDislodgeEndpoint(ctx.Submarine.transform, subPos, subRot);

            var animator = ctx.Submarine.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(ctx.Submarine);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var dislodgeTrack = timeline.CreateTrack<AnimationTrack>(DislodgeTrackName);
            // ApplyTransformOffsets 는 AnimationOffsetPlayable 을 끼워 넣고, 그건 클립 가중치가
            // 0일 때도 루트에 트랙 오프셋을 쓴다. 오프셋을 zero 로 두면 컷씬 내내(클립 밖에서도)
            // 잠수함이 원점으로 끌려간다 — 첫 검증에서 실제로 그렇게 나왔다.
            // 오프셋에 원래 포즈를 넣으면 클립 밖에서는 원래 자리에, 클립 안에서는 그 위에 델타가 얹힌다.
            dislodgeTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
            dislodgeTrack.position = subPos;
            dislodgeTrack.rotation = subRot;
            ctx.Director.SetGenericBinding(dislodgeTrack, animator);
            var dislodgeClip = dislodgeTrack.CreateClip(animClip);
            dislodgeClip.start = DislodgeStart;
            dislodgeClip.duration = DislodgeDuration;
            dislodgeClip.displayName = DislodgeTrackName;
            dislodgeClip.easeInDuration = 0;
            dislodgeClip.easeOutDuration = 0;
            EditorUtility.SetDirty(dislodgeTrack);
            EditorUtility.SetDirty(dislodgeClip.asset);

            // --- 3.5) 검은 화면 → Shot A 페이드인 ---
            var fadeAnimator = EnsureFadeOverlay();
            var fadeClip = BuildFadeClip();
            var fadeTrack = timeline.CreateTrack<AnimationTrack>(FadeTrackName);
            // 오버레이 이미지는 트랜스폼을 건드리지 않으므로(색 알파만 애니메이션) 오프셋이 필요 없다.
            fadeTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
            fadeTrack.position = Vector3.zero;
            fadeTrack.rotation = Quaternion.identity;
            ctx.Director.SetGenericBinding(fadeTrack, fadeAnimator);
            var fadeTimelineClip = fadeTrack.CreateClip(fadeClip);
            fadeTimelineClip.start = 0.0;
            fadeTimelineClip.duration = FadeHold + FadeIn;
            fadeTimelineClip.displayName = "Fade In From Black";
            fadeTimelineClip.easeInDuration = 0;
            fadeTimelineClip.easeOutDuration = 0;
            EditorUtility.SetDirty(fadeTrack);
            EditorUtility.SetDirty(fadeTimelineClip.asset);
            Log($"페이드인 {0.0:F2}~{FadeHold + FadeIn:F2} (검정 유지 {FadeHold:F2}s + 페이드 {FadeIn:F2}s)");

            // --- 4) 컷씬이 끝나면 마지막 샷을 유지한다 ---
            ctx.Director.extrapolationMode = DirectorWrapMode.Hold;
            // Hold 만으로는 부족하다. 그래프가 카메라 오버라이드를 놓는 순간 Brain 은 우선순위 큐로
            // 되돌아가는데, vcam 3개가 전부 기본값 동률이라 첫 검증에서 CinemachineCamera_3 이 이겼다.
            // 마지막 샷 카메라가 큐에서도 이기도록 우선순위를 올린다.
            // (1번/3번은 Enabled=false 라 실효 우선순위 0이므로 건드리지 않는다.)
            ctx.Cam2.Priority.Value = 10;
            EditorUtility.SetDirty(ctx.Cam2);
            Log($"{Cam2Name} 우선순위 → {ctx.Cam2.Priority.Value} (컷씬 종료 후 유지용)");

            // --- 5) 블렌드 계산. m_BlendsValid 는 비직렬화 필드라 저장 전에 여기서 계산해 둬야 한다.
            //         Evaluate() 는 부르지 않는다 — 씬 트랜스폼에 값이 써진다.
            ctx.Director.RebuildGraph();
            EditorUtility.SetDirty(ctx.Director);

            PruneOrphanDirectorEntries(ctx.Director, timeline);
            ReportGraphOutputs(ctx.Director);
            ReportBlends(shotTrack);

            // --- 6) 저장. exposedName 문자열은 .playable 에, GUID→오브젝트 맵은 씬에 들어간다.
            //         둘 중 하나만 저장하면 샷이 전부 null 로 resolve 되고 에러도 안 난다.
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Log($"완료. Timeline duration = {timeline.duration:F3}s, 트랙 {timeline.GetRootTracks().Count()}개");
        }

        static void RemoveGeneratedTracks(TimelineAsset timeline, PlayableDirector director)
        {
            foreach (var track in timeline.GetRootTracks().ToList())
            {
                bool isGenerated = track is CinemachineTrack
                    || track.name == DislodgeTrackName
                    || track.name == FadeTrackName;
                if (!isGenerated) continue;

                foreach (var clip in track.GetClips().ToList())
                {
                    if (clip.asset is CinemachineShot shot && !string.IsNullOrEmpty(shot.VirtualCamera.exposedName.ToString()))
                        director.ClearReferenceValue(shot.VirtualCamera.exposedName);
                }
                string removedName = track.name;   // DeleteTrack 이후엔 track 이 파괴돼 접근할 수 없다
                director.ClearGenericBinding(track);
                timeline.DeleteTrack(track);
                Log($"기존 트랙 '{removedName}' 제거 (재실행)");
            }
        }

        // Build 가 중간에 실패하면 트랙은 .playable 에서 사라졌는데 씬의 노출참조/바인딩만 남는다.
        // 그 고아 항목은 런타임에 무시되지만 계속 쌓이므로, 살아있는 트랙/샷 기준으로 정리한다.
        // 키를 하나라도 읽지 못하면 정리를 통째로 포기한다 — 잘못 지우면 컷씬이 조용히 죽는다.
        static void PruneOrphanDirectorEntries(PlayableDirector director, TimelineAsset timeline)
        {
            var liveTracks = timeline.GetOutputTracks().Cast<Object>().ToList();
            var liveExposed = new HashSet<string>(
                liveTracks.OfType<TrackAsset>()
                    .SelectMany(t => t.GetClips())
                    .Select(c => c.asset as CinemachineShot)
                    .Where(s => s != null)
                    .Select(s => ExposedKey(s.VirtualCamera.exposedName)));

            var so = new SerializedObject(director);
            int removed = 0;

            var refs = so.FindProperty("m_ExposedReferences.m_References");
            if (refs != null && refs.isArray)
            {
                var keys = new string[refs.arraySize];
                bool readable = true;
                for (int i = 0; i < refs.arraySize; ++i)
                {
                    var key = refs.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                    if (key == null || key.propertyType != SerializedPropertyType.String) { readable = false; break; }
                    keys[i] = key.stringValue;
                }
                // 안전장치: 살아있는 키가 직렬화 배열에서 전부 발견될 때만 지운다.
                // 키 추출 방식이 틀렸는데도 지우면 살아있는 참조까지 날아가 컷씬이 조용히 죽는다.
                // (실제로 한 번 그렇게 됐다 — 이 검사가 그때 막았어야 하는 것이다.)
                int matched = readable ? keys.Count(k => liveExposed.Contains(k)) : 0;
                if (!readable || matched != liveExposed.Count)
                {
                    Debug.LogWarning($"[Intro] 노출참조 키 매칭 실패 (일치 {matched} / 기대 {liveExposed.Count}). 정리를 건너뜁니다.");
                    if (readable) Log("  직렬화 키: " + string.Join(" | ", keys));
                    Log("  살아있는 키: " + string.Join(" | ", liveExposed));
                }
                else
                {
                    for (int i = refs.arraySize - 1; i >= 0; --i)
                        if (!liveExposed.Contains(keys[i])) { refs.DeleteArrayElementAtIndex(i); removed++; }
                }
            }
            else Debug.LogWarning("[Intro] m_ExposedReferences.m_References 를 찾지 못했습니다.");

            var bindings = so.FindProperty("m_SceneBindings");
            if (bindings != null && bindings.isArray)
            {
                for (int i = bindings.arraySize - 1; i >= 0; --i)
                {
                    var key = bindings.GetArrayElementAtIndex(i).FindPropertyRelative("key");
                    if (key == null || key.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (!liveTracks.Contains(key.objectReferenceValue))
                    {
                        bindings.DeleteArrayElementAtIndex(i);
                        removed++;
                    }
                }
            }
            else Debug.LogWarning("[Intro] m_SceneBindings 를 찾지 못했습니다.");

            if (removed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                Log($"고아 노출참조/바인딩 {removed}건 정리");
            }
        }

        // PropertyName.ToString() 은 "<이름>:<해시>" 를 돌려주는데 직렬화된 키는 이름뿐이다.
        // GUID 에는 ':' 가 없으므로 마지막 ':' 앞부분이 곧 직렬화 키다.
        static string ExposedKey(PropertyName name)
        {
            string s = name.ToString();
            int i = s.LastIndexOf(':');
            return i >= 0 ? s.Substring(0, i) : s;
        }

        static void CreateShot(CinemachineTrack track, PlayableDirector director,
            CinemachineCamera vcam, string label, double start, double duration)
        {
            var clip = track.CreateClip<CinemachineShot>();
            var shot = (CinemachineShot)clip.asset;
            shot.name = label;
            shot.DisplayName = label;
            clip.displayName = label;

            // VirtualCamera 는 프로퍼티가 아니라 필드다. 로컬 복사본에 쓰면 조용히 유실된다.
            shot.VirtualCamera.exposedName = Guid.NewGuid().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, vcam);

            clip.start = start;
            clip.duration = duration;
            // ease 가 0 이 아니면 비겹침 구간에서 단독 클립의 weight 가 1 미만이 되고,
            // 믹서의 "혼자 페이드아웃" 특수 케이스가 발동해 camB=null 로 블렌드되며 화면이 튄다.
            clip.easeInDuration = 0;
            clip.easeOutDuration = 0;

            EditorUtility.SetDirty(shot);
            Log($"샷 '{label}' {start:F2}~{start + duration:F2} → {vcam.name}");
        }

        // ------------------------------------------------------- 이탈 애니메이션 생성

        static AnimationClip BuildDislodgeClip(Vector3 basePos, Quaternion baseRot)
        {
            // Timeline 은 루트 모션을 클립 첫 프레임 기준 델타로 환산한 뒤 트랙 오프셋을 다시 얹는다.
            // (첫 검증에서 실측: 절대 좌표 커브를 넣었더니 결과가 정확히 (0, 0, 6) 이었다 —
            //  시작 방향 기준 로컬 델타였다.)
            // 그래서 커브는 "시작 포즈 기준 상대값"으로 쓴다. 클립 로컬 축은 잠수함 자신의 축이므로
            // +z 가 곧 기수 방향이다. 오일러도 0 근처에 머물러 ±180 wrap 이 생길 여지가 없다.
            Log($"잠수함 시작 포즈: localPos {V(basePos)}, localEuler {V(baseRot.eulerAngles)}");
            Log($"이탈 방향(월드) {V(baseRot * Vector3.forward)}, 거리 {DislodgeDistance:F2} m");

            int n = DislodgeKeys.GetLength(0);
            var kt = new float[n];
            var px = new float[n]; var py = new float[n]; var pz = new float[n];
            var rx = new float[n]; var ry = new float[n]; var rz = new float[n];

            for (int i = 0; i < n; ++i)
            {
                kt[i] = DislodgeKeys[i, 0];
                px[i] = DislodgeKeys[i, 2] * ShudderPos;        // 우현 방향 떨림
                py[i] = DislodgeKeys[i, 3] * ShudderPos;        // 상하 떨림
                pz[i] = DislodgeKeys[i, 1] * DislodgeDistance;  // 기수 방향 이탈
                rx[i] = DislodgeKeys[i, 4] * ShudderRot;
                ry[i] = DislodgeKeys[i, 5] * ShudderRot;
                rz[i] = DislodgeKeys[i, 6] * ShudderRot;
            }

            var clip = new AnimationClip { name = "Submarine_Dislodge", frameRate = 60f };
            SetCurve(clip, "m_LocalPosition.x", kt, px);
            SetCurve(clip, "m_LocalPosition.y", kt, py);
            SetCurve(clip, "m_LocalPosition.z", kt, pz);
            // 쿼터니언 4성분을 독립 float 커브로 넣으면 구면 보간이 아니라 성분별 선형 보간이 되어
            // 회전이 호를 벗어난다. Timeline 자신의 레코더도 오일러로 기록한다.
            SetCurve(clip, "localEulerAnglesRaw.x", kt, rx);
            SetCurve(clip, "localEulerAnglesRaw.y", kt, ry);
            SetCurve(clip, "localEulerAnglesRaw.z", kt, rz);

            // 반드시 커브를 넣은 뒤에 Get → 수정 → Set. new AnimationClipSettings() 를 쓰면
            // stopTime 이 0 이 되고 그게 clip.length → TimelineClip.duration 을 0 으로 만든다.
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DislodgeClipPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else
            {
                AssetDatabase.CreateAsset(clip, DislodgeClipPath);
            }

            Log($"이탈 클립 생성: length {clip.length:F3}s, 키 {n}개 (시작 포즈 기준 상대값)");
            if (Mathf.Abs(clip.length - (float)DislodgeDuration) > 0.01f)
                Debug.LogWarning($"[Intro] 클립 길이 {clip.length:F3}s 가 의도한 {DislodgeDuration:F2}s 와 다릅니다.");
            return clip;
        }

        // -------------------------------------------------------- 페이드 오버레이

        // 화면 전체를 덮는 검은 UI 이미지. 포스트프로세싱(노출/컬러그레이딩)으로 어둡게 하는 방법도
        // 있지만, 그건 카메라의 PostProcessing 설정과 HDR/블룸 조합에 따라 "완전한 검정"이
        // 보장되지 않는다. 오버레이는 렌더 파이프라인과 무관하게 확실히 불투명하고,
        // 알파 값을 그대로 읽을 수 있어 검증도 쉽다.
        static Animator EnsureFadeOverlay()
        {
            var existing = GameObject.Find(FadeRootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);   // 재실행 시 깨끗이 다시 만든다

            var root = new GameObject(FadeRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Create CutsceneFade");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // 무엇보다도 위에

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var imageGo = new GameObject(FadeImageName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageGo, "Create FadeImage");
            imageGo.transform.SetParent(root.transform, false);

            var rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;          // 화면 전체로 늘린다
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = imageGo.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;            // 입력을 먹지 않게

            var animator = Undo.AddComponent<Animator>(imageGo);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Log($"'{FadeRootName}/{FadeImageName}' 오버레이 생성 (Canvas sortingOrder {canvas.sortingOrder})");
            return animator;
        }

        static AnimationClip BuildFadeClip()
        {
            // 알파만 애니메이션한다. RGB 는 검정 그대로 두므로 커브가 필요 없다.
            var times = new[] { 0f, FadeHold, FadeHold + FadeIn };
            var alpha = new[] { 1f, 1f, 0f };

            var clip = new AnimationClip { name = "Intro_FadeIn", frameRate = 60f };
            SetCurve(clip, "m_Color.a", times, alpha, typeof(Image));

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(FadeClipPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else
            {
                AssetDatabase.CreateAsset(clip, FadeClipPath);
            }

            Log($"페이드 클립 생성: length {clip.length:F3}s (알파 1 → 1 → 0)");
            return clip;
        }

        static void SetCurve(AnimationClip clip, string property, float[] times, float[] values)
            => SetCurve(clip, property, times, values, typeof(Transform));

        static void SetCurve(AnimationClip clip, string property, float[] times, float[] values, Type type)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i < times.Length; ++i) curve.AddKey(new Keyframe(times[i], values[i]));
            for (int i = 0; i < curve.length; ++i)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(
                clip, EditorCurveBinding.FloatCurve(string.Empty, type, property), curve);
        }

        // 종점에 무엇이 있는지 무조건 로그로 남긴다. 잠수함은 바위에 "박혀" 있으므로 콜라이더
        // 내부에서 출발하는 SphereCast 는 신뢰할 수 없다. 히트 0 을 "통과"로 읽지 말 것 —
        // 이 씬의 CaveBlockout 장식에는 콜라이더가 꺼진 오버라이드가 여럿 있다.
        static void ProbeDislodgeEndpoint(Transform sub, Vector3 basePos, Quaternion baseRot)
        {
            Physics.SyncTransforms();
            Vector3 endLocalPos = basePos + baseRot * new Vector3(0f, 0f, DislodgeDistance);
            Vector3 endWorld = sub.parent != null ? sub.parent.TransformPoint(endLocalPos) : endLocalPos;
            var hits = Physics.OverlapSphere(endWorld, HullProbeRadius, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(sub))
                .ToArray();

            Log($"이탈 종점 월드좌표 {V(endWorld)}, 반경 {HullProbeRadius:F1} m 프로브 → 히트 {hits.Length}개");
            if (hits.Length == 0)
            {
                Debug.LogWarning("[Intro] 종점에서 장애물이 검출되지 않았습니다. 이 씬에는 콜라이더가 " +
                                 "꺼진 장식 프리팹이 있으므로 '관통 없음'으로 단정할 수 없습니다. 육안 확인 필요.");
            }
            else
            {
                foreach (var h in hits.Take(12)) Log($"  겹침: {h.gameObject.name} ({h.GetType().Name})");
                Debug.LogWarning($"[Intro] 이탈 종점이 {hits.Length}개 콜라이더와 겹칩니다. " +
                                 $"DislodgeDistance({DislodgeDistance:F1} m) 조정을 검토하세요.");
            }
        }

        // ----------------------------------------------------------------- Verify

        static void VerifyInternal()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var ctx = Context.Collect();
            var director = ctx.Director;
            var brain = ctx.Brain;

            var shotTrack = ctx.Timeline.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
            if (shotTrack == null) throw new Exception("CinemachineTrack 이 없습니다. 먼저 Build 를 실행하세요.");

            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.ResetState();
            director.RebuildGraph();

            ReportGraphOutputs(director);
            ReportBlends(shotTrack);

            // 샷 3개가 실제로 resolve 되는지 먼저 단언한다. ExposedReference 가 절반만 저장되면
            // 믹서가 !IsValid 클립을 조용히 스킵해 "카메라가 안 바뀐다"는 증상만 남는다.
            Log("--- 샷 참조 해석 ---");
            int resolved = 0;
            foreach (var clip in shotTrack.GetClips().OrderBy(c => c.start))
            {
                var shot = (CinemachineShot)clip.asset;
                var value = director.GetReferenceValue(shot.VirtualCamera.exposedName, out bool valid);
                string name = (value as Object) != null ? (value as Object).name : "(null)";
                Log($"  {clip.displayName}: valid={valid} → {name}");
                if (valid && (value as Object) != null) resolved++;
            }
            if (resolved != 3)
                throw new Exception($"샷 참조가 3개 중 {resolved}개만 해석됐습니다. " +
                                    "ExposedReference 배선이 .playable 또는 씬 한쪽에만 저장됐습니다.");

            var renderer = ctx.Object6 != null ? ctx.Object6.GetComponent<Renderer>() : null;
            var subBounds = CombinedBounds(ctx.Submarine);

            Log("--- 스크럽 ---");
            int frame = 0;
            float prev = -1f;
            foreach (float t in ScrubTimes)
            {
                director.time = t;
                director.Evaluate();
                float dt = prev < 0f ? -1f : (t - prev);
                brain.ManualUpdate(++frame, dt);
                prev = t;

                var sb = new StringBuilder();
                sb.Append($"t={t,6:F2}  cam={Nm(brain.ActiveVirtualCamera)}  blending={brain.IsBlending}");
                var blend = brain.ActiveBlend;
                if (blend != null)
                    sb.Append($"  [A={Nm(blend.CamA)} B={Nm(blend.CamB)} w={blend.BlendWeight:F3} dur={blend.Duration:F2}]");
                Log(sb.ToString());

                if (renderer != null)
                {
                    string shared = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_EmissionColor")
                        ? renderer.sharedMaterial.GetColor("_EmissionColor").ToString("F3") : "(없음)";
                    string mpb = "(블록 없음)";
                    if (renderer.HasPropertyBlock())
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        mpb = block.GetColor("_EmissionColor").ToString("F3");
                    }
                    Log($"         emission shared={shared} mpb={mpb}");
                }

                if (ctx.FadeImage != null)
                    Log($"         페이드 알파={ctx.FadeImage.color.a:F3} " +
                        $"({(ctx.FadeImage.color.a > 0.995f ? "완전한 검정" : ctx.FadeImage.color.a < 0.005f ? "투명" : "전환 중")})");

                var st = ctx.Submarine.transform;
                Log($"         sub local={V(st.localPosition)} euler={V(st.localEulerAngles)} world={V(st.position)}");

                if (Mathf.Approximately(t, 6.2f) && ctx.Object6 != null)
                    Log($"         프레이밍: Object_6 이 {Cam3Name} 절두체 안? " +
                        $"{InFrustum(ctx.Cam3, ctx.Brain, RendererBounds(ctx.Object6))}");
                // 11.5 는 이동 중, 14.99 는 종점. 이탈 거리를 늘리면 종점이 프레임 밖으로
                // 나가거나 근접 평면을 뚫을 수 있으므로 양쪽 다 본다.
                if (Mathf.Approximately(t, 11.5f) || Mathf.Approximately(t, 14.99f))
                {
                    Log($"         프레이밍: 잠수함이 {Cam2Name} 절두체 안? " +
                        $"{InFrustum(ctx.Cam2, ctx.Brain, ShiftBounds(subBounds, st.position))}");
                    Log($"         {Cam2Name} 까지 거리 {Vector3.Distance(ctx.Cam2.transform.position, st.position):F2} m " +
                        $"(near clip {ctx.Cam2.State.Lens.NearClipPlane:F2} m)");
                }
            }

            Log("검증 종료 — 씬을 저장하지 않았습니다.");
        }

        // -------------------------------------------------------------- 공용 리포트

        static void ReportGraphOutputs(PlayableDirector director)
        {
            var graph = director.playableGraph;
            if (!graph.IsValid()) { Debug.LogWarning("[Intro] PlayableGraph 가 유효하지 않습니다."); return; }

            Log($"--- PlayableGraph 출력 {graph.GetOutputCount()}개 ---");
            var animators = new List<string>();
            for (int i = 0; i < graph.GetOutputCount(); ++i)
            {
                var output = graph.GetOutput(i);
                if (output.IsPlayableOutputOfType<AnimationPlayableOutput>())
                {
                    var target = ((AnimationPlayableOutput)output).GetTarget();
                    string name = target != null ? target.name : "(null)";
                    animators.Add(name);
                    Log($"  [{i}] Animation → {name}");
                }
                else
                {
                    Log($"  [{i}] {output.GetPlayableOutputType().Name}");
                }
            }
            if (animators.Distinct().Count() < 2)
                Debug.LogWarning($"[Intro] Animation 출력이 {animators.Distinct().Count()}개뿐입니다. " +
                                 "중첩 Animator 중 하나가 드롭됐을 수 있습니다.");
        }

        static void ReportBlends(TrackAsset shotTrack)
        {
            Log("--- 샷 블렌드 ---");
            foreach (var clip in shotTrack.GetClips().OrderBy(c => c.start))
                Log($"  {clip.displayName}: {clip.start:F2}~{clip.end:F2} " +
                    $"blendIn={clip.blendInDuration:F2} blendOut={clip.blendOutDuration:F2} " +
                    $"easeIn={clip.easeInDuration:F2} easeOut={clip.easeOutDuration:F2}");
        }

        // ------------------------------------------------------------------ 유틸

        static string Nm(ICinemachineCamera cam) => cam != null ? cam.Name : "(없음)";
        static string V(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
        static void Log(string message) => Debug.Log("[Intro] " + message);

        static Bounds RendererBounds(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
        }

        static Bounds CombinedBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Bounds ShiftBounds(Bounds b, Vector3 newCenter) => new Bounds(newCenter, b.size);

        static bool InFrustum(CinemachineCamera vcam, CinemachineBrain brain, Bounds bounds)
        {
            var state = vcam.State;
            float aspect = brain.OutputCamera != null ? brain.OutputCamera.aspect : 16f / 9f;
            var proj = Matrix4x4.Perspective(state.Lens.FieldOfView, aspect,
                state.Lens.NearClipPlane, state.Lens.FarClipPlane);
            // Unity 의 뷰 행렬은 카메라가 -Z 를 보므로 Z 를 뒤집어야 한다.
            // 이걸 빼먹으면 절두체가 정반대를 향해 "앞에 있는 것"이 전부 False 로 나온다.
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                       * Matrix4x4.TRS(state.GetFinalPosition(), state.GetFinalOrientation(), Vector3.one).inverse;
            var planes = GeometryUtility.CalculateFrustumPlanes(proj * view);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        // ------------------------------------------------------------ 씬 참조 수집

        class Context
        {
            public PlayableDirector Director;
            public TimelineAsset Timeline;
            public CinemachineBrain Brain;
            public CinemachineCamera Cam1, Cam2, Cam3;
            public GameObject Submarine;
            public GameObject Object6;
            public Image FadeImage;

            public static Context Collect()
            {
                var ctx = new Context();

                ctx.Director = Object.FindObjectsByType<PlayableDirector>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
                if (ctx.Director == null) throw new Exception("씬에 PlayableDirector 가 없습니다.");

                ctx.Timeline = ctx.Director.playableAsset as TimelineAsset;
                if (ctx.Timeline == null)
                    throw new Exception($"'{ctx.Director.name}' 의 playableAsset 이 TimelineAsset 이 아닙니다.");

                ctx.Brain = Object.FindObjectsByType<CinemachineBrain>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
                if (ctx.Brain == null) throw new Exception("씬에 CinemachineBrain 이 없습니다.");

                var cams = Object.FindObjectsByType<CinemachineCamera>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                ctx.Cam1 = Find(cams, Cam1Name);
                ctx.Cam2 = Find(cams, Cam2Name);
                ctx.Cam3 = Find(cams, Cam3Name);

                ctx.Submarine = GameObject.Find(SubmarineName);
                if (ctx.Submarine == null) throw new Exception($"씬에서 '{SubmarineName}' 을 찾지 못했습니다.");

                // 깜빡임이 바인딩된 Animator 가 붙은 오브젝트 = Object_6
                foreach (var kvp in ctx.Director.playableAsset.outputs)
                {
                    var track = kvp.sourceObject as TrackAsset;
                    if (track == null || track.name != FlickerTrackName) continue;
                    var bound = ctx.Director.GetGenericBinding(track) as Animator;
                    if (bound != null) ctx.Object6 = bound.gameObject;
                }
                if (ctx.Object6 == null)
                    Debug.LogWarning($"[Intro] '{FlickerTrackName}' 트랙의 Animator 바인딩을 찾지 못했습니다.");

                var fadeRoot = GameObject.Find(FadeRootName);
                if (fadeRoot != null) ctx.FadeImage = fadeRoot.GetComponentInChildren<Image>(true);

                Debug.Log($"[Intro] 참조 수집: director={ctx.Director.name}, brain={ctx.Brain.name}, " +
                          $"sub={ctx.Submarine.name}, object6={(ctx.Object6 != null ? ctx.Object6.name : "(없음)")}");
                return ctx;
            }

            static CinemachineCamera Find(CinemachineCamera[] cams, string name)
            {
                var found = cams.FirstOrDefault(c => c.name == name);
                if (found == null)
                    throw new Exception($"씬에서 '{name}' 을 찾지 못했습니다. " +
                                        $"현재 카메라: {string.Join(", ", cams.Select(c => c.name))}");
                return found;
            }
        }
    }
}
