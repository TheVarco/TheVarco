using System.Linq;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Ending.EditorTools
{
    /// <summary>
    /// Adds Scene View capture controls directly to Ending Camera 1's inspector header.
    /// The captured position is converted to a WorldSpace FollowOffset, so Cinemachine
    /// does not overwrite the edit on the next Timeline evaluation.
    /// </summary>
    [InitializeOnLoad]
    static class EndingCamera1SceneViewCapture
    {
        const string CameraName = "Ending_Camera_1_Z6_Breach";

        static EndingCamera1SceneViewCapture()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= DrawCameraControls;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawCameraControls;
        }

        static void DrawCameraControls(UnityEditor.Editor inspector)
        {
            if (inspector == null || inspector.target is not CinemachineCamera camera ||
                camera.name != CameraName)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ending Camera 1 - Scene View Capture", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Scene View를 원하는 위치로 맞춘 뒤 아래 버튼을 누르세요. Timeline을 다시 스크럽해도 위치가 유지됩니다.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Scene View 위치를 Follow Offset에 적용", GUILayout.Height(26f)))
                    Capture(camera, false);

                if (GUILayout.Button("Scene View 전체 구도 적용 (위치 + 시선 + FOV)"))
                    Capture(camera, true);
            }
            EditorGUILayout.EndVertical();
        }

        static void Capture(CinemachineCamera camera, bool includeFraming)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            Camera sceneCamera = sceneView != null ? sceneView.camera : null;
            if (sceneCamera == null)
            {
                Debug.LogError("[Ending Camera 1] 활성 Scene View를 찾을 수 없습니다.", camera);
                return;
            }
            if (sceneCamera.orthographic)
            {
                Debug.LogError("[Ending Camera 1] Scene View를 Perspective 모드로 바꾼 뒤 다시 적용해 주세요.", camera);
                sceneView.ShowNotification(new GUIContent("Perspective 모드에서 적용해 주세요."));
                return;
            }

            CinemachineFollow follow = camera.GetComponent<CinemachineFollow>();
            Transform followTarget = camera.Target.TrackingTarget;
            if (follow == null || followTarget == null)
            {
                Debug.LogError("[Ending Camera 1] Cinemachine Follow 또는 Tracking Target이 없습니다.", camera);
                return;
            }

            Vector3 capturedPosition = sceneCamera.transform.position;
            Quaternion capturedRotation = sceneCamera.transform.rotation;
            Object[] undoTargets = includeFraming
                ? new Object[] { camera, follow, camera.transform, camera.GetComponent<CinemachineRotationComposer>() }
                    .Where(x => x != null).ToArray()
                : new Object[] { camera, follow, camera.transform };
            Undo.RecordObjects(undoTargets, includeFraming
                ? "Capture Ending Camera 1 Scene View Framing"
                : "Capture Ending Camera 1 Scene View Position");

            TrackerSettings tracker = follow.TrackerSettings;
            tracker.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings = tracker;
            follow.FollowOffset = capturedPosition - followTarget.position;

            // Keep the serialized transform readable in Edit Mode. Cinemachine will produce
            // this same position from FollowOffset when the Timeline evaluates.
            camera.transform.position = capturedPosition;

            if (includeFraming)
                CaptureFraming(camera, sceneCamera, capturedPosition, capturedRotation);

            camera.ForceCameraPosition(capturedPosition, capturedRotation);
            EditorUtility.SetDirty(follow);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            SceneView.RepaintAll();

            string message = includeFraming
                ? "Scene View 위치/시선/FOV를 Camera 1에 적용했습니다."
                : $"Scene View 위치를 적용했습니다. Follow Offset = {follow.FollowOffset:F2}";
            Debug.Log("[Ending Camera 1] " + message + " Build/Verify는 실행하지 않았습니다.", camera);
            sceneView.ShowNotification(new GUIContent(message));
        }

        static void CaptureFraming(CinemachineCamera camera, Camera sceneCamera,
            Vector3 capturedPosition, Quaternion capturedRotation)
        {
            LensSettings lens = camera.Lens;
            lens.FieldOfView = sceneCamera.fieldOfView;
            camera.Lens = lens;

            CinemachineRotationComposer composer = camera.GetComponent<CinemachineRotationComposer>();
            Transform lookAt = camera.Target.LookAtTarget;
            if (composer == null || lookAt == null)
            {
                camera.transform.rotation = capturedRotation;
                return;
            }

            Vector3 aimPoint = lookAt.position + lookAt.rotation * composer.TargetOffset;
            Vector3 localAim = Quaternion.Inverse(capturedRotation) * (aimPoint - capturedPosition);
            if (localAim.z <= 0.001f)
            {
                Debug.LogWarning("[Ending Camera 1] LookAt Target이 Scene View 뒤쪽에 있어 시선 구도는 변경하지 않았습니다.", camera);
                return;
            }

            Camera outputCamera = Object.FindObjectsByType<CinemachineBrain>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.gameObject.scene == camera.gameObject.scene)
                .Select(x => x.OutputCamera)
                .FirstOrDefault(x => x != null);
            float aspect = outputCamera != null ? outputCamera.aspect : sceneCamera.aspect;
            float tanHalfFov = Mathf.Tan(lens.FieldOfView * Mathf.Deg2Rad * 0.5f);
            Vector2 screenPosition = new(
                0.5f * localAim.x / (localAim.z * tanHalfFov * Mathf.Max(0.01f, aspect)),
                0.5f * localAim.y / (localAim.z * tanHalfFov));

            ScreenComposerSettings composition = composer.Composition;
            composition.ScreenPosition = new Vector2(
                Mathf.Clamp(screenPosition.x, -1.5f, 1.5f),
                Mathf.Clamp(screenPosition.y, -1.5f, 1.5f));
            composer.Composition = composition;
            camera.transform.rotation = capturedRotation;
            EditorUtility.SetDirty(composer);
        }
    }
}
