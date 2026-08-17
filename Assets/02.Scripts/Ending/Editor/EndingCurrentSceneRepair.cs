using System;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Varco.Ending;
using Object = UnityEngine.Object;

namespace Varco.Ending.EditorTools
{
    /// <summary>
    /// A narrow, idempotent repair for the already-authored ending scene.  It never invokes the
    /// cinematic builder or verifier and never copies/backups the scene.
    /// </summary>
    static class EndingCurrentSceneRepair
    {
        const string EndingScenePath = "Assets/01.Scenes/MainScene_Ending_Cinemachine.unity";
        const string DirectorName = "Ending_CutsceneDirector";
        const string ChildName = "Otter_Child";
        const string SeatName = "Otter_Adult_01_Seat_OpenArms";
        const string StartName = "Otter_Child_Run_Start";
        const string EndName = "Otter_Child_Run_Target";
        const string OldTrackName = "Otter Child - Run Path";
        const string NewTrackName = "Otter Child - Scene Targets";

        static bool s_Repairing;

        [MenuItem("Tools/Varco/Ending/현재 엔딩 씬 출력·꼬마 경로 복구")]
        static void RepairFromMenu()
        {
            Scene scene = FindLoadedEndingScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[Ending Repair] MainScene_Ending_Cinemachine 씬을 먼저 열어 주세요.");
                return;
            }

            RepairLoadedEndingScene(scene);
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

        static void RepairLoadedEndingScene(Scene scene)
        {
            if (s_Repairing || EditorApplication.isPlayingOrWillChangePlaymode) return;

            s_Repairing = true;
            try
            {
                bool sceneChanged = false;
                bool assetChanged = false;

                CinemachineBrain brain = FindInScene<CinemachineBrain>(scene, "Main Camera");
                if (brain == null) throw new InvalidOperationException("Ending Main Camera의 CinemachineBrain이 없습니다.");
                if (brain.UpdateMethod != CinemachineBrain.UpdateMethods.SmartUpdate)
                {
                    brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;
                    EditorUtility.SetDirty(brain);
                    sceneChanged = true;
                }

                Image fade = FindInScene<Image>(scene, "FadeImage");
                if (fade == null) throw new InvalidOperationException("Ending FadeImage가 없습니다.");
                if (fade.color.a > 0.0001f)
                {
                    Color color = fade.color;
                    color.a = 0f;
                    fade.color = color;
                    EditorUtility.SetDirty(fade);
                    sceneChanged = true;
                }

                GameObject child = FindGameObject(scene, ChildName);
                GameObject seat = FindGameObject(scene, SeatName);
                if (child == null || seat == null)
                    throw new InvalidOperationException("Ending 꼬마/Seat 해달 루트를 찾지 못했습니다.");

                Transform start = FindGameObject(scene, StartName)?.transform;
                if (start == null)
                {
                    var go = new GameObject(StartName);
                    go.transform.SetParent(child.transform.parent, true);
                    go.transform.SetPositionAndRotation(child.transform.position, child.transform.rotation);
                    start = go.transform;
                    sceneChanged = true;
                }

                Transform end = FindGameObject(scene, EndName)?.transform;
                if (end == null)
                {
                    var go = new GameObject(EndName);
                    go.transform.SetParent(seat.transform, false);
                    go.transform.localPosition = new Vector3(0f, 0f, 0.75f);
                    go.transform.localRotation = Quaternion.identity;
                    end = go.transform;
                    sceneChanged = true;
                }

                EndingChildRunController controller = child.GetComponent<EndingChildRunController>();
                if (controller == null)
                {
                    controller = child.AddComponent<EndingChildRunController>();
                    sceneChanged = true;
                }
                Terrain terrain = FindTerrainAt(scene, child.transform.position)
                                  ?? FindTerrainAt(scene, seat.transform.position);
                if (controller.StartTarget != start || controller.EndTarget != end ||
                    controller.GroundTerrain != terrain || !Mathf.Approximately(controller.GroundClearance, 0.18f))
                {
                    controller.StartTarget = start;
                    controller.EndTarget = end;
                    controller.GroundTerrain = terrain;
                    controller.GroundClearance = 0.18f;
                    EditorUtility.SetDirty(controller);
                    sceneChanged = true;
                }

                PlayableDirector director = FindInScene<PlayableDirector>(scene, DirectorName);
                TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
                if (director == null || timeline == null)
                    throw new InvalidOperationException("Ending Director/Timeline을 찾지 못했습니다.");

                AnimationTrack oldTrack = timeline.GetOutputTracks().OfType<AnimationTrack>()
                    .FirstOrDefault(x => x.name == OldTrackName);
                EndingChildRunTrack newTrack = timeline.GetOutputTracks().OfType<EndingChildRunTrack>()
                    .FirstOrDefault(x => x.name == NewTrackName);

                if (oldTrack != null)
                {
                    director.ClearGenericBinding(oldTrack);
                    timeline.DeleteTrack(oldTrack);
                    assetChanged = true;
                }

                if (newTrack == null)
                {
                    newTrack = timeline.CreateTrack<EndingChildRunTrack>(NewTrackName);
                    TimelineClip timelineClip = newTrack.CreateClip<EndingChildRunClip>();
                    timelineClip.start = 0d;
                    timelineClip.duration = 15d;
                    timelineClip.displayName = "Scene Target Run: wait 0–9.6 / arrive 14.1 / hold";
                    timelineClip.easeInDuration = 0d;
                    timelineClip.easeOutDuration = 0d;
                    var clipAsset = (EndingChildRunClip)timelineClip.asset;
                    clipAsset.MovementStart = 9.6f;
                    clipAsset.MovementDuration = 4.5f;
                    clipAsset.Easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                    EditorUtility.SetDirty(clipAsset);
                    assetChanged = true;
                }

                if (!ReferenceEquals(director.GetGenericBinding(newTrack), controller))
                {
                    director.SetGenericBinding(newTrack, controller);
                    EditorUtility.SetDirty(director);
                    sceneChanged = true;
                }

                if (assetChanged)
                {
                    EditorUtility.SetDirty(newTrack);
                    EditorUtility.SetDirty(timeline);
                    AssetDatabase.SaveAssets();
                }
                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                if (sceneChanged || assetChanged)
                    Debug.Log("[Ending Repair] Game View 출력과 Scene Target 기반 꼬마 이동을 현재 씬에 적용했습니다. Build/Verify는 실행하지 않았습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                s_Repairing = false;
            }
        }

        static GameObject FindGameObject(Scene scene, string name)
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == name)?.gameObject;
        }

        static T FindInScene<T>(Scene scene, string gameObjectName) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == gameObjectName);
        }

        static Terrain FindTerrainAt(Scene scene, Vector3 point)
        {
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (terrain.gameObject.scene != scene || terrain.terrainData == null) continue;
                Vector3 min = terrain.GetPosition();
                Vector3 max = min + terrain.terrainData.size;
                if (point.x >= min.x && point.x <= max.x && point.z >= min.z && point.z <= max.z)
                    return terrain;
            }
            return null;
        }
    }
}
