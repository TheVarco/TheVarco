using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Varco.Ending.EditorTools
{
    /// <summary>
    /// Captures the currently visible humanoid arm pose and layers it over Seat_OpenArms.
    /// The imported FBX clip is never modified.
    /// </summary>
    static class EndingSeatHugPoseCapture
    {
        const string EndingScenePath = "Assets/01.Scenes/MainScene_Ending_Cinemachine.unity";
        const string DirectorName = "Ending_CutsceneDirector";
        const string SeatRootName = "Otter_Adult_01_Seat_OpenArms";
        const string BaseTrackName = "Otter Adult 01 - Seat_OpenArms";
        const string OverrideTrackName = "Seat Hug Pose - Scene Override";
        const string ClipPath = "Assets/08.Cinemachine/Ending/Animations/Ending_Seat_HugPose_Override.anim";
        const string MaskPath = "Assets/08.Cinemachine/Ending/Animations/Ending_Seat_HugPose.mask";
        const double PoseStart = 8.5;
        const double PoseDuration = 6.5;

        static bool s_Capturing;

        [MenuItem("Tools/Varco/Ending/현재 Seat 팔 자세를 Timeline에 적용")]
        static void CaptureFromMenu()
        {
            Scene scene = FindLoadedEndingScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[Seat Hug Pose] MainScene_Ending_Cinemachine 씬을 먼저 열어 주세요.");
                return;
            }

            CaptureAndApply(scene);
        }

        static void CaptureAndApply(Scene scene)
        {
            if (s_Capturing || EditorApplication.isPlayingOrWillChangePlaymode) return;
            s_Capturing = true;
            try
            {
                GameObject seatRoot = FindGameObject(scene, SeatRootName);
                if (seatRoot == null)
                    throw new InvalidOperationException("Seat 해달 루트를 찾지 못했습니다.");

                Animator animator = seatRoot.GetComponentsInChildren<Animator>(true).FirstOrDefault();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException("Seat 해달의 유효한 Humanoid Animator를 찾지 못했습니다.");

                Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
                Transform leftShoulder = bones.FirstOrDefault(x => x.name == "shoulder.L");
                Transform rightShoulder = bones.FirstOrDefault(x => x.name == "shoulder.R");
                if (leftShoulder == null || rightShoulder == null)
                    throw new InvalidOperationException("Seat 해달에서 shoulder.L / shoulder.R 본을 찾지 못했습니다.");

                PlayableDirector director = FindComponent<PlayableDirector>(scene, DirectorName);
                TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
                if (director == null || timeline == null)
                    throw new InvalidOperationException("Ending Director/Timeline을 찾지 못했습니다.");

                AnimationTrack baseTrack = timeline.GetOutputTracks().OfType<AnimationTrack>()
                    .FirstOrDefault(x => x.name == BaseTrackName);
                if (baseTrack == null)
                    throw new InvalidOperationException("Seat_OpenArms 기본 Animation Track을 찾지 못했습니다.");

                AnimationClip poseClip = GetOrCreatePoseClip();
                poseClip.ClearCurves();
                poseClip.frameRate = 60f;
                poseClip.wrapMode = WrapMode.ClampForever;

                AddHumanoidArmMuscles(animator, poseClip);
                AddBoneSubtreeCurves(animator.transform, leftShoulder, poseClip);
                AddBoneSubtreeCurves(animator.transform, rightShoulder, poseClip);
                poseClip.EnsureQuaternionContinuity();
                EditorUtility.SetDirty(poseClip);

                AvatarMask mask = GetOrCreateArmMask();
                ConfigureArmMask(mask);
                EditorUtility.SetDirty(mask);

                AnimationTrack oldOverride = baseTrack.GetChildTracks().OfType<AnimationTrack>()
                    .FirstOrDefault(x => x.name == OverrideTrackName);
                if (oldOverride != null) timeline.DeleteTrack(oldOverride);

                AnimationTrack overrideTrack = timeline.CreateTrack<AnimationTrack>(baseTrack, OverrideTrackName);
                overrideTrack.applyAvatarMask = true;
                overrideTrack.avatarMask = mask;
                overrideTrack.trackOffset = TrackOffset.ApplySceneOffsets;

                TimelineClip timelineClip = overrideTrack.CreateClip(poseClip);
                timelineClip.start = PoseStart;
                timelineClip.duration = PoseDuration;
                timelineClip.displayName = "Current Scene Hug Pose";
                timelineClip.easeInDuration = 0d;
                timelineClip.easeOutDuration = 0d;
                var playableAsset = (AnimationPlayableAsset)timelineClip.asset;
                playableAsset.loop = AnimationPlayableAsset.LoopMode.Off;
                playableAsset.applyFootIK = false;
                playableAsset.removeStartOffset = true;

                EditorUtility.SetDirty(playableAsset);
                EditorUtility.SetDirty(overrideTrack);
                EditorUtility.SetDirty(baseTrack);
                EditorUtility.SetDirty(timeline);
                AssetDatabase.SaveAssets();

                director.RebuildGraph();
                Debug.Log("[Seat Hug Pose] shoulder.L/R 및 모든 하위 본의 현재 Scene View 자세를 " +
                          "Seat Hug Pose - Scene Override 트랙에 적용했습니다. Build/Verify는 실행하지 않았습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                s_Capturing = false;
            }
        }

        static AnimationClip GetOrCreatePoseClip()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip != null) return clip;

            clip = new AnimationClip { name = "Ending Seat Hug Pose Override" };
            AssetDatabase.CreateAsset(clip, ClipPath);
            return clip;
        }

        static AvatarMask GetOrCreateArmMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask != null) return mask;

            mask = new AvatarMask { name = "Ending Seat Hug Pose" };
            AssetDatabase.CreateAsset(mask, MaskPath);
            return mask;
        }

        static void ConfigureArmMask(AvatarMask mask)
        {
            for (AvatarMaskBodyPart part = AvatarMaskBodyPart.Root;
                 part < AvatarMaskBodyPart.LastBodyPart; ++part)
                mask.SetHumanoidBodyPartActive(part, false);

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
        }

        static void AddHumanoidArmMuscles(Animator animator, AnimationClip clip)
        {
            var pose = new HumanPose();
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                handler.GetHumanPose(ref pose);
            }
            finally
            {
                handler.Dispose();
            }

            for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; ++i)
            {
                string muscle = HumanTrait.MuscleName[i];
                if (!IsArmMuscle(muscle)) continue;
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), pose.muscles[i]);
            }
        }

        static bool IsArmMuscle(string muscle)
        {
            bool side = muscle.StartsWith("Left ", StringComparison.Ordinal) ||
                        muscle.StartsWith("Right ", StringComparison.Ordinal);
            if (!side) return false;

            return muscle.IndexOf("Shoulder", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Arm", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Forearm", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Hand", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Thumb", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Index", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Middle", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Ring", StringComparison.Ordinal) >= 0 ||
                   muscle.IndexOf("Little", StringComparison.Ordinal) >= 0;
        }

        static void AddBoneSubtreeCurves(Transform animatorRoot, Transform shoulder, AnimationClip clip)
        {
            foreach (Transform bone in shoulder.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(bone, animatorRoot);
                Vector3 position = bone.localPosition;
                Quaternion rotation = bone.localRotation;
                Vector3 scale = bone.localScale;

                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), position.x);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"), position.y);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z"), position.z);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.x"), rotation.x);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.y"), rotation.y);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.z"), rotation.z);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.w"), rotation.w);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale.x"), scale.x);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale.y"), scale.y);
                SetConstantCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale.z"), scale.z);
            }
        }

        static void SetConstantCurve(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            var curve = AnimationCurve.Constant(0f, (float)PoseDuration, value);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        static Scene FindLoadedEndingScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.path == EndingScenePath)
                    return scene;
            }
            return default;
        }

        static GameObject FindGameObject(Scene scene, string name)
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == name)?.gameObject;
        }

        static T FindComponent<T>(Scene scene, string name) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == name);
        }
    }
}
