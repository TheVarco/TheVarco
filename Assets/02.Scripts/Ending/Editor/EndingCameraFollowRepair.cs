using System;
using System.Linq;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Varco.Ending;
using Object = UnityEngine.Object;

namespace Varco.Ending.EditorTools
{
    /// <summary>
    /// Restores position-only following without adding an Aim stage. This keeps the rotations
    /// authored with Align With View while allowing the cameras to travel with their subjects.
    /// </summary>
    static class EndingCameraFollowRepair
    {
        const string EndingScenePath = "Assets/01.Scenes/MainScene_Ending_Cinemachine.unity";
        const string DirectorName = "Ending_CutsceneDirector";
        const string Cam1Name = "Ending_Camera_1_Z6_Breach";
        const string Cam2Name = "Ending_Camera_2_Beach_Landing";
        const string Cam3Name = "Ending_Camera_3_Child_Reunion";
        const string SubmarineName = "Ending_Submarine_Visual";
        const string SubmarineAimName = "Submarine_CameraAim";
        const string ChildName = "Otter_Child";
        const string SurfaceTargetName = "Camera1_SurfaceFollowTarget";

        static bool s_Applying;

        [MenuItem("Tools/Varco/Ending/현재 카메라 Follow 복구")]
        static void ApplyFromMenu()
        {
            Scene scene = FindLoadedEndingScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[Ending Camera Follow] MainScene_Ending_Cinemachine 씬을 먼저 열어 주세요.");
                return;
            }
            Apply(scene);
        }

        [MenuItem("Tools/Varco/Ending/현재 Camera 1 수면선 로우앵글 적용")]
        static void ApplyCamera1FromMenu()
        {
            Scene scene = FindLoadedEndingScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[Ending Camera 1] MainScene_Ending_Cinemachine 씬을 먼저 열어 주세요.");
                return;
            }
            ApplyCamera1Only(scene);
        }

        static void Apply(Scene scene)
        {
            if (s_Applying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            s_Applying = true;
            try
            {
                CinemachineCamera cam1 = FindCamera(scene, Cam1Name);
                CinemachineCamera cam2 = FindCamera(scene, Cam2Name);
                CinemachineCamera cam3 = FindCamera(scene, Cam3Name);
                Transform submarine = FindGameObject(scene, SubmarineName)?.transform;
                Transform submarineAim = FindGameObject(scene, SubmarineAimName)?.transform;
                Transform child = FindGameObject(scene, ChildName)?.transform;
                PlayableDirector director = FindComponent<PlayableDirector>(scene, DirectorName);
                if (cam1 == null || cam2 == null || cam3 == null || submarine == null || submarineAim == null ||
                    child == null || director == null)
                    throw new InvalidOperationException("Ending 카메라/잠수함/꼬마/Director 중 하나를 찾지 못했습니다.");

                double originalTime = director.time;
                director.time = 1.18;
                director.Evaluate();
                ConfigureCamera1Surface(scene, cam1, submarine, submarineAim);
                FollowSnapshot second = CaptureSnapshot(director, cam2, submarine, 4.00);
                FollowSnapshot third = CaptureSnapshot(director, cam3, child, 9.60);

                ConfigureFollow(second, new Vector3(0.10f, 0.10f, 0.16f));
                ConfigureFollow(third, new Vector3(0.18f, 0.22f, 0.30f));

                director.time = originalTime;
                director.Evaluate();
                director.RebuildGraph();

                EditorUtility.SetDirty(cam1);
                EditorUtility.SetDirty(cam2);
                EditorUtility.SetDirty(cam3);
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                Debug.Log("[Ending Camera Follow] Camera 1/2는 잠수함, Camera 3은 꼬마를 따라가도록 복구했습니다. " +
                          "Rotation Composer는 추가하지 않아 Scene View에서 맞춘 회전은 유지됩니다. Build/Verify는 실행하지 않았습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                s_Applying = false;
            }
        }

        static void ApplyCamera1Only(Scene scene)
        {
            if (s_Applying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            s_Applying = true;
            try
            {
                CinemachineCamera camera = FindCamera(scene, Cam1Name);
                Transform submarine = FindGameObject(scene, SubmarineName)?.transform;
                Transform submarineAim = FindGameObject(scene, SubmarineAimName)?.transform;
                PlayableDirector director = FindComponent<PlayableDirector>(scene, DirectorName);
                if (camera == null || submarine == null || submarineAim == null || director == null)
                    throw new InvalidOperationException("Camera 1/잠수함/CameraAim/Director 중 하나를 찾지 못했습니다.");

                double originalTime = director.time;
                director.time = 1.18;
                director.Evaluate();
                ConfigureCamera1Surface(scene, camera, submarine, submarineAim);

                director.time = originalTime;
                director.Evaluate();
                director.RebuildGraph();
                EditorUtility.SetDirty(camera);
                EditorUtility.SetDirty(director);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                Debug.Log("[Ending Camera 1] 수면선 3/4 로우앵글을 적용했습니다. Camera 2·3과 Timeline은 변경하지 않았고 Build/Verify도 실행하지 않았습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                s_Applying = false;
            }
        }

        static void ConfigureCamera1Surface(Scene scene, CinemachineCamera camera,
            Transform submarine, Transform submarineAim)
        {
            GameObject targetObject = FindGameObject(scene, SurfaceTargetName);
            if (targetObject == null)
            {
                targetObject = new GameObject(SurfaceTargetName);
                targetObject.transform.SetParent(camera.transform.parent, true);
            }

            EndingCamera1SurfaceTarget surface = targetObject.GetComponent<EndingCamera1SurfaceTarget>();
            if (surface == null) surface = targetObject.AddComponent<EndingCamera1SurfaceTarget>();
            surface.Source = submarine;
            surface.FixedWorldY = 273.3f;
            surface.HorizontalOffset = Vector2.zero;
            surface.ApplyNow();

            Vector3 forward = Vector3.ProjectOnPlane(submarine.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            var cameraTarget = camera.Target;
            cameraTarget.TrackingTarget = surface.transform;
            cameraTarget.LookAtTarget = submarineAim;
            cameraTarget.CustomLookAtTarget = true;
            camera.Target = cameraTarget;

            CinemachineFollow follow = camera.GetComponent<CinemachineFollow>();
            if (follow == null) follow = camera.gameObject.AddComponent<CinemachineFollow>();
            follow.enabled = true;
            follow.FollowOffset = -forward * 14f + right * 4.5f + Vector3.up * 1.2f;
            TrackerSettings tracker = TrackerSettings.Default;
            tracker.BindingMode = BindingMode.WorldSpace;
            tracker.PositionDamping = new Vector3(0.18f, 0f, 0.20f);
            tracker.RotationDamping = Vector3.zero;
            follow.TrackerSettings = tracker;

            CinemachineRotationComposer composer = camera.GetComponent<CinemachineRotationComposer>();
            if (composer == null) composer = camera.gameObject.AddComponent<CinemachineRotationComposer>();
            composer.enabled = true;
            composer.TargetOffset = Vector3.zero;
            composer.Damping = new Vector2(0.12f, 0.28f);
            composer.CenterOnActivate = true;
            ScreenComposerSettings composition = ScreenComposerSettings.Default;
            composition.ScreenPosition = new Vector2(0f, -0.10f);
            composition.DeadZone.Enabled = false;
            composition.HardLimits.Enabled = false;
            composer.Composition = composition;

            LensSettings lens = camera.Lens;
            lens.FieldOfView = 50f;
            camera.Lens = lens;

            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(targetObject);
            EditorUtility.SetDirty(follow);
            EditorUtility.SetDirty(composer);
            EditorUtility.SetDirty(camera);
        }

        static FollowSnapshot CaptureSnapshot(PlayableDirector director, CinemachineCamera camera,
            Transform target, double referenceTime)
        {
            director.time = referenceTime;
            director.Evaluate();
            return new FollowSnapshot
            {
                Camera = camera,
                Target = target,
                Offset = camera.transform.position - target.position
            };
        }

        static void ConfigureFollow(FollowSnapshot snapshot, Vector3 damping)
        {
            CinemachineCamera camera = snapshot.Camera;
            var target = camera.Target;
            target.TrackingTarget = snapshot.Target;
            target.LookAtTarget = null;
            target.CustomLookAtTarget = false;
            camera.Target = target;

            CinemachineFollow follow = camera.GetComponent<CinemachineFollow>();
            if (follow == null) follow = camera.gameObject.AddComponent<CinemachineFollow>();
            follow.enabled = true;
            follow.FollowOffset = snapshot.Offset;

            TrackerSettings tracker = TrackerSettings.Default;
            tracker.BindingMode = BindingMode.WorldSpace;
            tracker.PositionDamping = damping;
            tracker.RotationDamping = Vector3.zero;
            follow.TrackerSettings = tracker;
            EditorUtility.SetDirty(follow);
        }

        static CinemachineCamera FindCamera(Scene scene, string name)
        {
            return Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == name);
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

        static Scene FindLoadedEndingScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.path == EndingScenePath) return scene;
            }
            return default;
        }

        struct FollowSnapshot
        {
            public CinemachineCamera Camera;
            public Transform Target;
            public Vector3 Offset;
        }
    }
}
