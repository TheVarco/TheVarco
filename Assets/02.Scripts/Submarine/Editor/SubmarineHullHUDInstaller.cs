using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Submarine.EditorTools
{
    /// <summary>
    /// Puts <c>SubmarineHullHUDBootstrap</c> back on MainScene_final's UI canvas.
    ///
    /// The bootstrap is one component on one canvas, and it is the only part of the hull-damage HUD that
    /// lives in the scene rather than in a script or a sprite - <c>SubmarineHullHUD.Create</c> builds the
    /// rest at runtime from <c>Awake</c>. That made it the one piece lost when MainScene_final was
    /// resolved in favour of this branch during the merge with origin/main: this branch had rewritten the
    /// whole scene (cross-sections, shell, 387 decor props, 31 hazards, 52 items), so taking the other
    /// side would have meant regenerating all of that, while taking this side costs exactly this one
    /// component plus a re-run of CaveItemBatch.
    ///
    /// Written as a batch rather than done by hand because hand-editing scene YAML to add a component
    /// means inventing a file id and splicing it into m_Component - the editor does both correctly, and
    /// this way the step is repeatable if the merge ever has to be redone.
    /// </summary>
    public static class SubmarineHullHUDInstaller
    {
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";

        [MenuItem("Tools/Underwater Cave/Install Submarine Hull HUD Bootstrap")]
        public static void InstallInteractive() => InstallBatch();

        public static void InstallBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);

                // The submarine carries its own canvas for the sonar screen, so "the only canvas in the
                // scene" is not a safe identification. The HUD belongs on the scene's screen-space UI,
                // which is the canvas that is not part of the submarine.
                Canvas[] canvases = UnityEngine.Object
                    .FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(canvas => canvas.transform is RectTransform)
                    .Where(canvas => canvas.GetComponentInParent<SubmarineController>() == null)
                    .ToArray();
                if (canvases.Length != 1)
                    throw new InvalidOperationException(
                        $"expected exactly one screen-space canvas in {PlayScenePath} but found " +
                        $"{canvases.Length} ({string.Join(", ", canvases.Select(c => c.name))}); refusing " +
                        "to guess which one the HUD belongs on");

                Canvas target = canvases[0];
                if (target.GetComponent<SubmarineHullHUDBootstrap>() != null)
                {
                    Debug.Log($"HULL_HUD_BOOTSTRAP SKIP already on '{target.name}'");
                    return;
                }

                Undo.AddComponent<SubmarineHullHUDBootstrap>(target.gameObject);
                EditorUtility.SetDirty(target.gameObject);
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("save refused");

                // MainScene_final's Fusion NetworkObjects bake their SortKeys from GlobalObjectId on
                // sceneSaving, and a first save can write provisional keys. Adding a plain UI component
                // should not disturb them, but the second save is what makes that true rather than hoped.
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("second save refused");

                Debug.Log($"HULL_HUD_BOOTSTRAP PASS added to '{target.name}'");
            }
            catch (Exception exception)
            {
                Debug.LogError($"HULL_HUD_BOOTSTRAP FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }
    }
}
