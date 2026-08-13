using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, so the environment can be applied and reviewed from the command line:
    ///
    ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; \
    ///     -executeMethod Varco.Underwater.EditorTools.UnderwaterBatch.ApplyToMainMap -logFile -
    ///
    /// Do not pass -nographics to the capture entry point; it needs a real graphics device.
    /// </summary>
    public static class UnderwaterBatch
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";

        /// <summary>
        /// Turns the underwater volume back on in the play scene.
        ///
        /// MainScene_final's camera shipped with renderPostProcessing = 0, which silently disabled the
        /// ENTIRE volume stack at runtime: no ACES tonemapping, no colour grading, no bloom. Without a
        /// tonemapper every value above 1 hard-clips, and the zone set authors ambient up to 9.96 linear
        /// in green and blue - so near surfaces flattened to white-cyan while the screen-space extinction
        /// pass, which is a renderer feature rather than a volume component, kept running and kept sight
        /// lines short. Bright, flat, and murky at the same time.
        ///
        /// MainMap does not have this problem because UnderwaterEnvironmentBuilder.Apply() has only ever
        /// been run there - <see cref="ApplyToMainMap"/> hardcodes that path. This method applies the one
        /// step the play scene is missing rather than the whole builder, whose other effects on a scene
        /// it has never touched are not predictable.
        ///
        /// Also wires the director's trackedCamera, which is null in the play scene. It currently works
        /// by falling back to Camera.main, but an explicit reference does not depend on tag hygiene.
        /// </summary>
        public static void FixPlaySceneCamera()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);

                Camera camera = UnderwaterEnvironmentBuilder.FindMainCamera();
                if (camera == null)
                    throw new InvalidOperationException($"no game camera in {PlayScenePath}");

                var before = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                bool postWasOn = before != null && before.renderPostProcessing;

                UnderwaterEnvironmentBuilder.ConfigureCamera();

                var director = UnityEngine.Object.FindFirstObjectByType<UnderwaterZoneDirector>();
                bool wiredTracked = false;
                if (director != null)
                {
                    var serialized = new SerializedObject(director);
                    SerializedProperty tracked = serialized.FindProperty("trackedCamera");
                    if (tracked != null && tracked.objectReferenceValue == null)
                    {
                        tracked.objectReferenceValue = camera;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(director);
                        wiredTracked = true;
                    }
                }

                EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("the scene refused to save");

                Debug.Log($"UNDERWATER_BATCH_FIX_PLAY_CAMERA PASS camera='{camera.name}' " +
                          $"renderPostProcessing {(postWasOn ? "was already on" : "OFF -> ON")}, " +
                          $"trackedCamera {(wiredTracked ? "wired" : "already set or no director")}, " +
                          $"clearFlags={camera.clearFlags}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"UNDERWATER_BATCH_FIX_PLAY_CAMERA FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void ApplyToMainMap()
        {
            try
            {
                EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
                UnderwaterEnvironmentBuilder.Apply(true);
                Debug.Log("UNDERWATER_BATCH_APPLY PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"UNDERWATER_BATCH_APPLY FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void CaptureMainMap()
        {
            try
            {
                EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
                string contactSheet = UnderwaterReviewCapture.Capture();
                Debug.Log($"UNDERWATER_BATCH_CAPTURE PASS contactSheet={contactSheet}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"UNDERWATER_BATCH_CAPTURE FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        public static void ApplyAndCaptureMainMap()
        {
            try
            {
                EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
                UnderwaterEnvironmentBuilder.Apply(true);
                string contactSheet = UnderwaterReviewCapture.Capture();
                Debug.Log($"UNDERWATER_BATCH_APPLY_AND_CAPTURE PASS contactSheet={contactSheet}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"UNDERWATER_BATCH_APPLY_AND_CAPTURE FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Compile-only pass, used to surface script and shader errors without touching the scene.</summary>
        public static void ValidateCompile()
        {
            Debug.Log("UNDERWATER_BATCH_COMPILE PASS");
        }
    }
}
