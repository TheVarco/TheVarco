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
