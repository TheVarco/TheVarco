using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Varco.Ending.EditorTools
{
    /// <summary>
    /// 최신 MainScene_Intro_Cinemachine 맵을 복사해, 게임 로직과 완전히 분리된 15초 엔딩
    /// Cinemachine/Timeline 씬을 저작한다. 원본 인트로 씬과 Timeline은 읽기만 하며 엔딩용
    /// 산출물은 모두 Assets/08.Cinemachine/Ending 아래에 둔다.
    ///
    /// Build는 씬을 저장하지만 Timeline을 Evaluate하지 않는다. Verify는 주요 프레임을 스크럽하지만
    /// 절대로 씬을 저장하지 않는다. Timeline Evaluate 결과가 씬 Transform으로 직렬화되는 것을 막기
    /// 위한 의도적인 분리다.
    /// </summary>
    public static class EndingCinematicBuilder
    {
        const string SourceScenePath = "Assets/01.Scenes/MainScene_Intro_Cinemachine.unity";
        const string EndingScenePath = "Assets/01.Scenes/MainScene_Ending_Cinemachine.unity";
        const string IntroTimelinePath = "Assets/08.Cinemachine/Intro/CutsecenDirectorTimeline.playable";

        const string EndingFolder = "Assets/08.Cinemachine/Ending";
        const string AnimationFolder = EndingFolder + "/Animations";
        const string MaterialFolder = EndingFolder + "/Materials";
        const string TimelinePath = EndingFolder + "/MainScene_Ending_Timeline.playable";
        const string SubmarineClipPath = AnimationFolder + "/Ending_Submarine_Path.anim";
        const string ChildMoveClipPath = AnimationFolder + "/Ending_Child_Run_Path.anim";
        const string CameraShakeClipPath = AnimationFolder + "/Ending_Camera2_Impact_Shake.anim";
        const string FadeClipPath = AnimationFolder + "/Ending_Screen_Fade.anim";
        const string WaterMaterialPath = MaterialFolder + "/Ending_WaterSplash.mat";
        const string SandMaterialPath = MaterialFolder + "/Ending_SandDust.mat";

        const string RawOtterPath = "Assets/99.Resources/Modeling/rawOtter/3D Otter.fbx";
        const string SubmarineVisualPath = "Assets/99.Resources/Modeling/Submarine/Submarine_final.glb";
        const string SmokeTexturePath = "Assets/99.Resources/msVFX_Free Smoke Effects Pack/Textures/msVFX_Stylized Smoke 1_Texture.png";
        const string MixamoMappingSourcePath = "Assets/99.Resources/PlayerAnim/X Bot@Waving.fbx";

        const string OldYellPath = "Assets/99.Resources/PlayerAnim/3D Otter.fbx";
        const string YellPath = "Assets/99.Resources/PlayerAnim/yell.fbx";
        const string SeatPath = "Assets/99.Resources/PlayerAnim/sit.fbx";
        const string RunPath = "Assets/99.Resources/PlayerAnim/X Bot@Run Forward.fbx";
        const string PrayingPath = "Assets/99.Resources/PlayerAnim/X Bot@Praying.fbx";
        const string CheeringPath = "Assets/99.Resources/PlayerAnim/X Bot@Cheering.fbx";

        const string ExitSfxPath = "Assets/99.Resources/Audio/SFX/Varco/Submarine_exit_1.wav";
        const string SplashSfxPath = "Assets/99.Resources/Audio/SFX/Varco/mixkit-jump-into-the-water-1180.wav";
        const string ImpactSfxPath = "Assets/99.Resources/Audio/SFX/Varco/Submarine_impact_1.wav";

        const string CinematicRootName = "[Ending Cinematic]";
        const string DirectorName = "Ending_CutsceneDirector";
        const string FadeRootName = "CutsceneFade";
        const string FadeImageName = "FadeImage";
        const string Cam1Name = "Ending_Camera_1_Z6_Breach";
        const string Cam2Name = "Ending_Camera_2_Beach_Landing";
        const string Cam3Name = "Ending_Camera_3_Child_Reunion";

        const double Duration = 15.0;
        const double ChildRunStart = 9.60;
        const double ChildRunEnd = 14.10;
        const double ReactionEnd = 14.10;
        const float ChildGroundClearance = 0.18f;

        static readonly float[] VerifyTimes =
        {
            0f, 1.15f, 1.25f, 3.10f, 3.25f, 3.95f, 4.05f, 5.20f, 6.80f,
            7.70f, 8.49f, 8.55f, 8.90f, 9.40f, 9.60f, 9.90f, 12.0f, 14.08f, 14.55f, 14.99f
        };

        sealed class MotionSpec
        {
            public string Path;
            public string ClipName;
            public string TakeName;
            public float FirstFrame;
            public float LastFrame;
            public bool Loop;
            public bool UseOtterMapping;
        }

        sealed class SceneAnchors
        {
            public Vector3 ExitPosition;
            public Vector3 ExitDirection;
            public float SeaLevel;
            public Transform Tent;
            public Vector3 TravelDirection;
            public Vector3 Right;
            public Vector3 LandingXZ;
            public Vector3 StopXZ;
            public Terrain Terrain;
        }

        sealed class ActorSet
        {
            public GameObject SubmarineRoot;
            public Animator SubmarineMover;
            public Transform SubmarineAim;
            public float SubmarinePivotToBottom;

            public GameObject FamilyRoot;
            public GameObject Adult01;
            public GameObject Adult02;
            public GameObject Adult03;
            public GameObject Adult04;
            public GameObject Child;
            public GameObject Adult01Visual;
            public GameObject Adult02Visual;
            public GameObject Adult03Visual;
            public GameObject Adult04Visual;
            public GameObject ChildVisual;
            public Animator Adult01Animator;
            public Animator Adult02Animator;
            public Animator Adult03Animator;
            public Animator Adult04Animator;
            public Animator ChildVisualAnimator;
            public Animator ChildMover;
            public Transform ChildCameraAim;

            public Vector3 ChildStart;
            public Vector3 ChildEnd;
            public Vector3 FamilyCentre;
        }

        sealed class CameraSet
        {
            public CinemachineBrain Brain;
            public Camera OutputCamera;
            public CinemachineCamera Cam1;
            public CinemachineCamera Cam2;
            public CinemachineCamera Cam3;
            public Animator Camera2RigAnimator;
        }

        sealed class VfxSet
        {
            public GameObject WaterBreach;
            public GameObject SandImpact;
            public GameObject DustTrail;
            public AudioSource ExitAudio;
            public AudioSource SplashAudio;
            public AudioSource ImpactAudio;
        }

        sealed class MotionClips
        {
            public AnimationClip Seat;
            public AnimationClip Yell;
            public AnimationClip Run;
            public AnimationClip Praying;
            public AnimationClip Cheering;
        }

        [MenuItem("Tools/Varco/Ending/엔딩 컷씬 생성 (Build)")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(BuildInternal, false);
        }

        [MenuItem("Tools/Varco/Ending/엔딩 컷씬 검증 (Verify)")]
        public static void VerifyMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run(VerifyInternal, false);
        }

        // Unity batchmode 진입점.
        public static void Build() => Run(BuildInternal, true);
        public static void Verify() => Run(VerifyInternal, true);

        static void Run(Action action, bool exitWhenDone)
        {
            try
            {
                action();
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (exitWhenDone) EditorApplication.Exit(1);
            }
        }

        // --------------------------------------------------------------------- Build

        static void BuildInternal()
        {
            string sourceSceneHash = HashAsset(SourceScenePath);
            string introTimelineHash = HashAsset(IntroTimelinePath);

            EnsureAssetFolder(EndingFolder);
            EnsureAssetFolder(AnimationFolder);
            EnsureAssetFolder(MaterialFolder);

            EnsureYellAssetName();
            MotionClips motion = ConfigureMotionAssets();

            Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, EndingScenePath, false))
                throw new Exception($"엔딩 씬 Save As 실패: {EndingScenePath}");
            scene = SceneManager.GetActiveScene();
            if (scene.path != EndingScenePath)
                throw new Exception($"Save As 후 열린 씬이 예상과 다릅니다: {scene.path}");

            RemoveIntroAuthoring();
            DisableGameplayObjects();

            SceneAnchors anchors = ResolveAnchors();
            Transform cinematicRoot = CreateRoot(CinematicRootName).transform;
            cinematicRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            CameraSet cameras = EnsureSingleOutputCamera(cinematicRoot);
            Animator fadeAnimator = EnsureFadeOverlay(out Image fadeImage);

            ActorSet actors = CreateActors(scene, cinematicRoot, anchors);
            ConfigureCameras(cameras, cinematicRoot, actors, anchors);
            VfxSet vfx = CreateVfxAndAudio(cinematicRoot, actors, anchors);

            AnimationClip submarinePath = BuildSubmarinePath(actors, anchors);
            AnimationClip childPath = BuildChildPath(actors, anchors.Terrain);
            AnimationClip cameraShake = BuildCameraShake();
            AnimationClip fade = BuildFadeClip();

            TimelineAsset timeline = ResetEndingTimeline();
            PlayableDirector director = CreateDirector(cinematicRoot, timeline);
            BuildTimeline(timeline, director, cameras, actors, vfx, motion,
                submarinePath, childPath, cameraShake, fade, fadeAnimator);

            timeline.editorSettings.frameRate = 60.0;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            director.initialTime = 0;
            director.playOnAwake = true;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;

            // UnderwaterZoneDirector가 Main Camera를 추적하도록 복사된 최신 설정은 보존한다.
            // 단, 원본 씬에 다른 카메라를 가리키던 경우만 출력 카메라로 보정한다.
            RebindCameraReferences(cameras.OutputCamera);

            director.RebuildGraph(); // Evaluate는 하지 않는다.
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(fadeImage);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new Exception("엔딩 씬 저장에 실패했습니다.");

            AssertProtectedAssets(sourceSceneHash, introTimelineHash);
            if (director.playableAsset == AssetDatabase.LoadAssetAtPath<TimelineAsset>(IntroTimelinePath))
                throw new Exception("엔딩 Director가 인트로 Timeline을 참조합니다.");

            Log($"Build 완료: {EndingScenePath}");
            Log($"Timeline {timeline.duration:F3}s / {timeline.editorSettings.frameRate:F0}fps, " +
                $"Z6={V(anchors.ExitPosition)}, Tent={V(anchors.Tent.position)}, 착지={V(anchors.LandingXZ)}, 정지={V(anchors.StopXZ)}");
            Log("중요: sit.fbx의 팔 벌린 최종 포즈는 사용자가 수정한 원본 모션을 그대로 반영합니다.");
        }

        static void RemoveIntroAuthoring()
        {
            foreach (var director in Object.FindObjectsByType<PlayableDirector>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Log($"인트로 Director 제거: {HierarchyPath(director.transform)}");
                Object.DestroyImmediate(director.gameObject);
            }

            foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Log($"인트로 CinemachineCamera 제거: {HierarchyPath(cam.transform)}");
                Object.DestroyImmediate(cam.gameObject);
            }

            GameObject previous = FindGameObject(CinematicRootName);
            if (previous != null) Object.DestroyImmediate(previous);
        }

        static void DisableGameplayObjects()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == "Submarine_final" || go.name.StartsWith("SubMarine_", StringComparison.OrdinalIgnoreCase))
                {
                    go.SetActive(false);
                    Log($"게임플레이 잠수함 비활성화: {go.name}");
                }
            }

            foreach (var canvas in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.transform.root.name == FadeRootName) continue;
                canvas.gameObject.SetActive(false);
            }

            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;
                string typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                if (typeName == "Fusion.NetworkRunner" || typeName.EndsWith(".NetworkRunner", StringComparison.Ordinal))
                {
                    behaviour.gameObject.SetActive(false);
                    Log($"네트워크 Runner 비활성화: {HierarchyPath(behaviour.transform)}");
                }
            }
        }

        static SceneAnchors ResolveAnchors()
        {
            var routes = Object.FindObjectsByType<SplineContainer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var route = routes.FirstOrDefault(x => x.name == "MainRoute")
                        ?? routes.FirstOrDefault(x => HierarchyPath(x.transform).Contains("MainRoute"));
            if (route == null || route.Spline == null)
                throw new Exception("최신 씬에서 MainRoute SplineContainer를 찾지 못했습니다.");

            if (!route.Evaluate(1f, out float3 exit, out float3 tangent, out _))
                throw new Exception("MainRoute 끝점을 평가하지 못했습니다.");

            Transform tent = Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == "Tent_01");
            if (tent == null) throw new Exception("최신 씬에서 Tent_01을 찾지 못했습니다.");

            Vector3 exitPosition = exit;
            Vector3 exitDirection = ((Vector3)tangent).normalized;
            Vector3 toTent = Vector3.ProjectOnPlane(tent.position - exitPosition, Vector3.up);
            if (toTent.sqrMagnitude < 1f) throw new Exception("Z6 출구와 Tent_01 배치가 비정상입니다.");
            Vector3 travel = toTent.normalized;
            if (Vector3.Dot(Vector3.ProjectOnPlane(exitDirection, Vector3.up), travel) < 0f)
                exitDirection = -exitDirection;
            if (exitDirection.y < 0.05f)
                exitDirection = (travel + Vector3.up * 0.48f).normalized;

            float seaLevel = ResolveSeaLevel(exitPosition.y);
            Vector3 right = Vector3.Cross(Vector3.up, travel).normalized;

            // Tent과 부두 소품을 피하도록 진행방향 기준 왼쪽으로 선체를 빼고, 섬 남쪽 가장자리에서
            // 접지한 뒤 가족 뒤쪽에 멈춘다. 모든 값은 최신 Tent/Route 좌표 기준 상대 배치다.
            Vector3 landing = tent.position - travel * 26f - right * 5.5f;
            Vector3 stop = tent.position - travel * 13.0f - right * 5.5f;
            Terrain terrain = FindTerrainAt(stop) ?? FindTerrainAt(landing)
                              ?? Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                  .OrderBy(x => Vector3.Distance(x.transform.position, tent.position)).FirstOrDefault();
            if (terrain == null) throw new Exception("해변 Terrain을 찾지 못했습니다.");

            landing.y = SampleGround(landing, terrain);
            stop.y = SampleGround(stop, terrain);

            var anchors = new SceneAnchors
            {
                ExitPosition = exitPosition,
                ExitDirection = exitDirection,
                SeaLevel = seaLevel,
                Tent = tent,
                TravelDirection = travel,
                Right = right,
                LandingXZ = landing,
                StopXZ = stop,
                Terrain = terrain
            };

            float exitTentDistance = Vector3.Distance(
                Vector3.ProjectOnPlane(exitPosition, Vector3.up), Vector3.ProjectOnPlane(tent.position, Vector3.up));
            if (exitTentDistance < 35f || exitTentDistance > 200f)
                throw new Exception($"Z6 출구–Tent 거리({exitTentDistance:F1}m)가 예상 범위를 벗어났습니다.");
            Log($"최신 씬 Anchor 해석: route={HierarchyPath(route.transform)}, exit={V(exitPosition)}, " +
                $"sea={seaLevel:F2}, tent={V(tent.position)}, terrain={terrain.name}");
            return anchors;
        }

        static float ResolveSeaLevel(float fallback)
        {
            Transform sea = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == "Sea" && HierarchyPath(x).Contains("Exterior"));
            if (sea == null) sea = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == "Sea");
            if (sea == null) return fallback + 13f;

            var renderers = sea.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
                return renderers.OrderByDescending(x => x.bounds.size.x * x.bounds.size.z).First().bounds.center.y;
            return sea.position.y;
        }

        // --------------------------------------------------------------------- Import

        static void EnsureYellAssetName()
        {
            // FBX를 Explorer 등 Unity 외부에서 이름 변경한 직후에도 현재 파일명을 인식한다.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Object oldAsset = AssetDatabase.LoadMainAssetAtPath(OldYellPath);
            Object newAsset = AssetDatabase.LoadMainAssetAtPath(YellPath);
            if (oldAsset == null && newAsset == null)
                throw new FileNotFoundException(
                    $"Yell FBX를 찾지 못했습니다. 현재 기대 경로: {YellPath}", YellPath);
            if (oldAsset != null && newAsset != null)
                throw new Exception($"{OldYellPath}와 {YellPath}가 동시에 존재해 안전하게 이름을 바꿀 수 없습니다.");
            if (oldAsset == null) return;

            string guidBefore = AssetDatabase.AssetPathToGUID(OldYellPath);
            string error = AssetDatabase.MoveAsset(OldYellPath, YellPath);
            if (!string.IsNullOrEmpty(error)) throw new Exception("Yell FBX 이름 변경 실패: " + error);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string guidAfter = AssetDatabase.AssetPathToGUID(YellPath);
            if (guidBefore != guidAfter) throw new Exception("Yell FBX 이동 중 GUID가 바뀌었습니다.");
            Log($"3D Otter.fbx → yell.fbx (GUID 유지 {guidAfter})");
        }

        static MotionClips ConfigureMotionAssets()
        {
            var specs = new[]
            {
                new MotionSpec { Path = SeatPath, ClipName = "Seat_OpenArms", TakeName = "Armature|Armature|Take 001|BaseLayer", FirstFrame = 0, LastFrame = 249, Loop = false, UseOtterMapping = true },
                new MotionSpec { Path = YellPath, ClipName = "Yell", TakeName = "Armature|Armature|Take 001|BaseLayer", FirstFrame = 0, LastFrame = 141, Loop = false, UseOtterMapping = true },
                new MotionSpec { Path = RunPath, ClipName = "Run Forward", TakeName = "mixamo.com", FirstFrame = 0, LastFrame = 27, Loop = true, UseOtterMapping = false },
                new MotionSpec { Path = PrayingPath, ClipName = "Praying", TakeName = "mixamo.com", FirstFrame = 0, LastFrame = 200, Loop = false, UseOtterMapping = false },
                new MotionSpec { Path = CheeringPath, ClipName = "Cheering", TakeName = "mixamo.com", FirstFrame = 0, LastFrame = 87, Loop = false, UseOtterMapping = false }
            };

            ModelImporter otterSource = AssetImporter.GetAtPath(RawOtterPath) as ModelImporter;
            ModelImporter mixamoSource = AssetImporter.GetAtPath(MixamoMappingSourcePath) as ModelImporter;
            if (otterSource == null || mixamoSource == null)
                throw new Exception("Humanoid 매핑 기준 FBX(raw otter 또는 X Bot@Waving)를 찾지 못했습니다.");

            // 1차: 각 FBX의 자체 스켈레톤으로 Humanoid/Avatar를 먼저 생성한다.
            // rawOtter는 최상위가 metarig, sit/yell은 Armature이므로 HumanDescription 전체를
            // 복사하면 대상 스켈레톤 경로가 깨진다. 기존의 잘못된 설정도 Generic으로 한 번
            // 초기화한 뒤, 필요한 경우 사람 본 매핑(human)만 복사한다.
            foreach (MotionSpec spec in specs)
            {
                var importer = AssetImporter.GetAtPath(spec.Path) as ModelImporter;
                if (importer == null) throw new FileNotFoundException("모션 FBX를 찾지 못했습니다.", spec.Path);

                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                importer.humanDescription = new HumanDescription();
                importer.SaveAndReimport();

                importer = AssetImporter.GetAtPath(spec.Path) as ModelImporter;
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();

                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(spec.Path).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isHuman || !avatar.isValid)
                {
                    importer = AssetImporter.GetAtPath(spec.Path) as ModelImporter;
                    HumanDescription targetDescription = importer.humanDescription;
                    HumanDescription mappingSource = spec.UseOtterMapping
                        ? otterSource.humanDescription
                        : mixamoSource.humanDescription;
                    if (mappingSource.human == null || mappingSource.human.Length == 0)
                        throw new Exception($"{spec.Path}에 적용할 Humanoid 본 매핑이 없습니다.");
                    targetDescription.human = mappingSource.human;
                    importer.humanDescription = targetDescription;
                    importer.SaveAndReimport();
                }
            }

            // 2차: 실제 take/range를 명시하고 더미 Take 001을 배제한다.
            foreach (MotionSpec spec in specs)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(spec.Path);
                var defaults = importer.defaultClipAnimations ?? Array.Empty<ModelImporterClipAnimation>();
                ModelImporterClipAnimation source = defaults.FirstOrDefault(x => x.takeName == spec.TakeName)
                                                    ?? defaults.FirstOrDefault(x => x.name == spec.TakeName)
                                                    ?? new ModelImporterClipAnimation { takeName = spec.TakeName };
                source.name = spec.ClipName;
                source.takeName = spec.TakeName;
                source.firstFrame = spec.FirstFrame;
                source.lastFrame = spec.LastFrame;
                source.loopTime = spec.Loop;
                source.loopPose = spec.Loop;
                source.lockRootRotation = true;
                source.lockRootHeightY = true;
                source.lockRootPositionXZ = true;
                source.keepOriginalOrientation = false;
                source.keepOriginalPositionY = true;
                source.keepOriginalPositionXZ = false;
                importer.clipAnimations = new[] { source };
                importer.SaveAndReimport();

                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(spec.Path).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isHuman || !avatar.isValid)
                    throw new Exception($"{spec.Path}의 Humanoid Avatar가 유효하지 않습니다. Avatar Configure를 확인하세요.");
                AnimationClip clip = LoadModelClip(spec.Path, spec.ClipName);
                if (clip == null) throw new Exception($"{spec.Path}에서 '{spec.ClipName}' 클립을 불러오지 못했습니다.");
                Log($"모션 임포트: {spec.ClipName} {spec.FirstFrame:F0}–{spec.LastFrame:F0}f / " +
                    $"{clip.length:F3}s / Humanoid valid / loop={spec.Loop}");
            }

            Avatar destination = AssetDatabase.LoadAllAssetsAtPath(RawOtterPath).OfType<Avatar>().FirstOrDefault();
            if (destination == null || !destination.isHuman || !destination.isValid)
                throw new Exception("목적지 raw otter Avatar가 유효한 Humanoid가 아닙니다.");

            return new MotionClips
            {
                Seat = LoadModelClip(SeatPath, "Seat_OpenArms"),
                Yell = LoadModelClip(YellPath, "Yell"),
                Run = LoadModelClip(RunPath, "Run Forward"),
                Praying = LoadModelClip(PrayingPath, "Praying"),
                Cheering = LoadModelClip(CheeringPath, "Cheering")
            };
        }

        static AnimationClip LoadModelClip(string path, string preferred)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                       .FirstOrDefault(x => x.name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                   ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                       .FirstOrDefault(x => !x.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
        }

        // --------------------------------------------------------------------- Actors

        static ActorSet CreateActors(Scene scene, Transform root, SceneAnchors a)
        {
            var actorsRoot = CreateChild(root, "Actors");
            var set = new ActorSet();

            set.SubmarineRoot = CreateChild(actorsRoot, "Ending_Submarine_Visual").gameObject;
            set.SubmarineMover = set.SubmarineRoot.AddComponent<Animator>();
            ConfigureTimelineAnimator(set.SubmarineMover);
            GameObject subAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SubmarineVisualPath);
            if (subAsset == null) throw new FileNotFoundException("잠수함 시각 모델을 찾지 못했습니다.", SubmarineVisualPath);
            var subVisual = (GameObject)PrefabUtility.InstantiatePrefab(subAsset, scene);
            subVisual.name = "Submarine_final_VisualOnly";
            subVisual.transform.SetParent(set.SubmarineRoot.transform, false);
            subVisual.transform.localPosition = Vector3.zero;
            subVisual.transform.localRotation = Quaternion.identity;
            subVisual.transform.localScale = Vector3.one * 2f;
            foreach (var collider in subVisual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in subVisual.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            set.SubmarineRoot.transform.rotation = Quaternion.LookRotation(a.TravelDirection, Vector3.up);
            set.SubmarinePivotToBottom = PivotToBottom(set.SubmarineRoot);
            if (set.SubmarinePivotToBottom < -1f || set.SubmarinePivotToBottom > 30f)
                throw new Exception($"잠수함 모델 바닥 오프셋({set.SubmarinePivotToBottom:F2})이 비정상입니다.");

            var subAim = CreateChild(set.SubmarineRoot.transform, "Submarine_CameraAim");
            subAim.localPosition = new Vector3(0f, 2.2f, 2.0f);
            set.SubmarineAim = subAim;

            Vector3 approach = Vector3.ProjectOnPlane(a.StopXZ - a.Tent.position, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, approach).normalized;
            set.FamilyRoot = CreateChild(actorsRoot, "Otter_Family").gameObject;
            set.FamilyCentre = a.Tent.position + approach * 8.0f;
            set.FamilyCentre.y = SampleGround(set.FamilyCentre, a.Terrain);
            set.ChildStart = a.Tent.position + approach * 0.8f - side * 0.3f;
            set.ChildStart.y = SampleGround(set.ChildStart, a.Terrain);
            set.ChildEnd = set.FamilyCentre - approach * 0.72f;
            set.ChildEnd.y = SampleGround(set.ChildEnd, a.Terrain);

            Quaternion adultsFacing = Quaternion.LookRotation(-approach, Vector3.up);
            Quaternion childFacing = Quaternion.LookRotation(approach, Vector3.up);

            set.Adult01Animator = CreateOtter(scene, set.FamilyRoot.transform, "Otter_Adult_01_Seat_OpenArms",
                set.FamilyCentre, adultsFacing, 1f, out set.Adult01, out set.Adult01Visual);
            set.Adult02Animator = CreateOtter(scene, set.FamilyRoot.transform, "Otter_Adult_02_Yell",
                Grounded(set.FamilyCentre + side * 2.1f + approach * 0.5f, a.Terrain), adultsFacing, 1f,
                out set.Adult02, out set.Adult02Visual);
            set.Adult03Animator = CreateOtter(scene, set.FamilyRoot.transform, "Otter_Adult_03_Praying",
                Grounded(set.FamilyCentre - side * 2.0f + approach * 0.8f, a.Terrain), adultsFacing, 1f,
                out set.Adult03, out set.Adult03Visual);
            set.Adult04Animator = CreateOtter(scene, set.FamilyRoot.transform, "Otter_Adult_04_Cheering",
                Grounded(set.FamilyCentre + side * 0.2f + approach * 2.0f, a.Terrain), adultsFacing, 1f,
                out set.Adult04, out set.Adult04Visual);
            set.ChildVisualAnimator = CreateOtter(scene, set.FamilyRoot.transform, "Otter_Child",
                set.ChildStart, childFacing, 0.7f, out set.Child, out set.ChildVisual);
            set.ChildMover = set.Child.AddComponent<Animator>();
            ConfigureTimelineAnimator(set.ChildMover);

            var childAim = CreateChild(set.Child.transform, "Child_CameraAim");
            childAim.localPosition = new Vector3(0f, 1.15f, 4.0f);
            set.ChildCameraAim = childAim;
            // 이동/카메라 타깃 루트는 계속 평가하고, 렌더링 모델만 Camera 3에서 활성화한다.
            // 부모 전체를 끄면 Child 이동 트랙이 재활성화 순간 원점으로 초기화될 수 있다.
            set.Adult01Visual.SetActive(false);
            set.Adult02Visual.SetActive(false);
            set.Adult03Visual.SetActive(false);
            set.Adult04Visual.SetActive(false);
            set.ChildVisual.SetActive(false);

            Log($"가족 배치: child {V(set.ChildStart)} → {V(set.ChildEnd)}, family {V(set.FamilyCentre)}");
            return set;
        }

        static Animator CreateOtter(Scene scene, Transform parent, string name, Vector3 position,
            Quaternion rotation, float scale, out GameObject actor, out GameObject visualRoot)
        {
            actor = CreateChild(parent, name).gameObject;
            actor.transform.SetPositionAndRotation(position, rotation);
            actor.transform.localScale = Vector3.one * scale;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(RawOtterPath);
            if (model == null) throw new FileNotFoundException("raw Otter 모델을 찾지 못했습니다.", RawOtterPath);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            visual.name = name + "_Visual";
            visualRoot = visual;
            visual.transform.SetParent(actor.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new Exception($"{name} visual에 Animator가 없습니다.");
            ConfigureTimelineAnimator(animator);
            return animator;
        }

        static void ConfigureTimelineAnimator(Animator animator)
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // --------------------------------------------------------------------- Cameras

        static CameraSet EnsureSingleOutputCamera(Transform cinematicRoot)
        {
            var brains = Object.FindObjectsByType<CinemachineBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            CinemachineBrain brain = brains.FirstOrDefault(x => x.name == "Main Camera")
                                     ?? brains.FirstOrDefault(x => x.GetComponent<Camera>() != null);
            if (brain == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CinemachineBrain));
                brain = go.GetComponent<CinemachineBrain>();
            }

            brain.gameObject.SetActive(true);
            brain.enabled = true;
            Camera output = brain.GetComponent<Camera>();
            if (output == null) output = brain.gameObject.AddComponent<Camera>();
            output.enabled = true;
            output.tag = "MainCamera";
            AudioListener selectedListener = brain.GetComponent<AudioListener>();
            if (selectedListener == null) selectedListener = brain.gameObject.AddComponent<AudioListener>();
            selectedListener.enabled = true;

            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other != output) other.enabled = false;
            foreach (var other in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other != selectedListener) other.enabled = false;
            foreach (var other in brains)
                if (other != brain) other.enabled = false;

            var set = new CameraSet { Brain = brain, OutputCamera = output };
            var camerasRoot = CreateChild(cinematicRoot, "Cinemachine Cameras");
            set.Cam1 = NewCamera(Cam1Name, camerasRoot, 58f);

            var camera2Rig = CreateChild(camerasRoot, "Camera_2_ImpactShake_Rig");
            set.Camera2RigAnimator = camera2Rig.gameObject.AddComponent<Animator>();
            ConfigureTimelineAnimator(set.Camera2RigAnimator);
            set.Cam2 = NewCamera(Cam2Name, camera2Rig, 52f);
            set.Cam3 = NewCamera(Cam3Name, camerasRoot, 55f);
            return set;
        }

        static CinemachineCamera NewCamera(string name, Transform parent, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<CinemachineCamera>();
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 2500f;
            cam.Lens = lens;
            cam.Priority = -100;
            return cam;
        }

        static void ConfigureCameras(CameraSet c, Transform root, ActorSet actors, SceneAnchors a)
        {
            // 카메라는 수동 저작을 우선한다. Cinemachine Follow/Rotation Composer를 붙이면
            // Scene View에서 Ctrl+Shift+F로 맞춘 Transform이 Timeline 평가 때 덮어써진다.
            // Shot 트랙은 카메라 전환만 담당하고, 위치/회전은 각 카메라 Transform을 그대로 사용한다.
            c.Cam1.transform.SetPositionAndRotation(a.ExitPosition - a.TravelDirection * 10f + Vector3.up * 3f,
                Quaternion.LookRotation(a.ExitDirection, Vector3.up));

            // Camera 2는 흔들림용 부모 Rig를 유지하지만, 자식 카메라 Transform은 직접 편집한다.
            Vector3 cam2Pos = a.LandingXZ + a.Right * 24f - a.TravelDirection * 8f + Vector3.up * 11f;
            c.Camera2RigAnimator.transform.SetPositionAndRotation(
                cam2Pos, Quaternion.LookRotation((a.LandingXZ + Vector3.up * 2f) - cam2Pos, Vector3.up));
            c.Cam2.transform.localPosition = Vector3.zero;
            c.Cam2.transform.localRotation = Quaternion.identity;

            // Camera 3 역시 고정 Transform으로 시작하며 Scene View에서 자유롭게 재배치한다.
            c.Cam3.transform.SetPositionAndRotation(actors.ChildStart - a.TravelDirection * 6.5f + Vector3.up * 2.2f,
                Quaternion.LookRotation(a.TravelDirection, Vector3.up));
        }

        // --------------------------------------------------------------------- VFX / Audio

        static VfxSet CreateVfxAndAudio(Transform root, ActorSet actors, SceneAnchors a)
        {
            var vfxRoot = CreateChild(root, "VFX & Audio");
            Material water = BuildParticleMaterial(WaterMaterialPath, "Ending Water Splash",
                new Color(0.60f, 1.0f, 0.94f, 0.82f));
            Material sand = BuildParticleMaterial(SandMaterialPath, "Ending Sand Dust",
                new Color(0.78f, 0.60f, 0.33f, 0.78f));

            float denominator = Mathf.Max(0.08f, a.ExitDirection.y);
            float breachDistance = (a.SeaLevel - a.ExitPosition.y) / denominator;
            Vector3 breach = a.ExitPosition + a.ExitDirection * breachDistance;
            breach.y = a.SeaLevel + 0.1f;

            var set = new VfxSet();
            set.WaterBreach = CreateBurstParticle(vfxRoot, "VFX_Water_Breach", breach,
                water, new ParticleSystem.MinMaxGradient(Color.white, new Color(0.16f, 0.95f, 0.88f, 0.92f)),
                52, 1.55f, 0.34f, 7.5f, 1.15f, true);
            set.SandImpact = CreateBurstParticle(vfxRoot, "VFX_Sand_Impact", a.LandingXZ + Vector3.up * 0.25f,
                sand, new ParticleSystem.MinMaxGradient(new Color(0.88f, 0.71f, 0.45f, 0.9f), new Color(0.47f, 0.30f, 0.16f, 0.72f)),
                68, 1.8f, 0.5f, 6.0f, 1.25f, false);
            set.DustTrail = CreateDustTrail(actors.SubmarineRoot.transform, sand);

            set.ExitAudio = CreateAudioSource(vfxRoot, "SFX_Submarine_Exit", breach);
            set.SplashAudio = CreateAudioSource(vfxRoot, "SFX_Water_Splash", breach);
            set.ImpactAudio = CreateAudioSource(vfxRoot, "SFX_Beach_Impact", a.LandingXZ);
            return set;
        }

        static GameObject CreateBurstParticle(Transform parent, string name, Vector3 position, Material material,
            ParticleSystem.MinMaxGradient colors, short count, float lifetime, float size, float speed,
            float gravity, bool water)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.45f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.55f, size);
            main.startColor = colors;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            main.stopAction = ParticleSystemStopAction.None;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = water ? 33f : 22f;
            shape.radius = water ? 3.2f : 4.5f;
            shape.radiusThickness = 0.72f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = water ? 0.75f : 1.25f;
            noise.frequency = 0.42f;
            noise.scrollSpeed = 0.2f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(water ? new Color(0.20f, 0.78f, 0.72f) : new Color(0.55f, 0.34f, 0.18f), 1) },
                new[] { new GradientAlphaKey(0.85f, 0), new GradientAlphaKey(0f, 1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = material;
            go.SetActive(false);
            return go;
        }

        static GameObject CreateDustTrail(Transform submarine, Material material)
        {
            var go = new GameObject("VFX_Sand_Skid_Trail", typeof(ParticleSystem));
            go.transform.SetParent(submarine, false);
            go.transform.localPosition = new Vector3(0f, -0.6f, -5.5f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 2.8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.75f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.86f, 0.68f, 0.42f, 0.82f), new Color(0.48f, 0.31f, 0.18f, 0.58f));
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 320;
            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(28f, 42f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(4.2f, 0.4f, 1.2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.9f;
            noise.frequency = 0.35f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.82f, 0.62f, 0.34f), 0), new GradientColorKey(new Color(0.40f, 0.27f, 0.18f), 1) },
                new[] { new GradientAlphaKey(0.72f, 0), new GradientAlphaKey(0f, 1) });
            color.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().material = material;
            go.SetActive(false);
            return go;
        }

        static Material BuildParticleMaterial(string path, string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null) throw new Exception("파티클용 Shader를 찾지 못했습니다.");
            var fresh = new Material(shader) { name = name, color = color };
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath);
            if (texture != null)
            {
                if (fresh.HasProperty("_BaseMap")) fresh.SetTexture("_BaseMap", texture);
                if (fresh.HasProperty("_MainTex")) fresh.SetTexture("_MainTex", texture);
            }
            if (fresh.HasProperty("_BaseColor")) fresh.SetColor("_BaseColor", color);
            if (fresh.HasProperty("_Surface")) fresh.SetFloat("_Surface", 1f);
            if (fresh.HasProperty("_Blend")) fresh.SetFloat("_Blend", 0f);
            if (fresh.HasProperty("_ZWrite")) fresh.SetFloat("_ZWrite", 0f);

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(fresh, existing);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }

        static AudioSource CreateAudioSource(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name, typeof(AudioSource));
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            var source = go.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0.55f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 8f;
            source.maxDistance = 180f;
            return source;
        }

        // --------------------------------------------------------------------- Animation assets

        static AnimationClip BuildSubmarinePath(ActorSet actors, SceneAnchors a)
        {
            float breachDistance = (a.SeaLevel - a.ExitPosition.y) / Mathf.Max(0.08f, a.ExitDirection.y);
            Vector3 breach = a.ExitPosition + a.ExitDirection * breachDistance;
            Vector3 start = a.ExitPosition - a.ExitDirection * 13f;
            Vector3 landing = a.LandingXZ + Vector3.up * (actors.SubmarinePivotToBottom + 0.10f);
            Vector3 stop = a.StopXZ + Vector3.up * (actors.SubmarinePivotToBottom + 0.10f);
            Vector3 apex = Vector3.Lerp(breach, landing, 0.43f);
            apex.y = Mathf.Max(a.SeaLevel + 10.5f, landing.y + 13f);
            Vector3 preLand = Vector3.Lerp(breach, landing, 0.84f);
            preLand.y = landing.y + 5.5f;

            Quaternion baseRot = Quaternion.LookRotation(a.ExitDirection, Vector3.up);
            Quaternion travelRot = Quaternion.LookRotation(a.TravelDirection, Vector3.up);
            var times = new[] { 0f, 0.55f, 1.18f, 2.05f, 3.25f, 3.94f, 4.08f, 4.32f, 5.10f, 6.00f, 6.55f, 6.80f, 8.50f };
            var worldPositions = new[]
            {
                start,
                Vector3.Lerp(start, a.ExitPosition, 0.68f),
                breach + Vector3.up * 0.35f,
                apex,
                preLand,
                landing + Vector3.up * 0.35f,
                landing + a.TravelDirection * 0.45f + Vector3.up * 0.85f,
                landing + a.TravelDirection * 1.25f,
                Vector3.Lerp(landing, stop, 0.43f) + Vector3.up * 0.05f,
                Vector3.Lerp(landing, stop, 0.78f),
                Vector3.Lerp(landing, stop, 0.96f),
                stop,
                stop
            };
            var worldRotations = new[]
            {
                baseRot,
                baseRot,
                Quaternion.LookRotation((a.ExitDirection + Vector3.up * 0.18f).normalized, Vector3.up),
                Quaternion.LookRotation((a.TravelDirection + Vector3.up * 0.12f).normalized, Vector3.up) * Quaternion.Euler(0, 0, -4f),
                travelRot * Quaternion.Euler(12f, 0, 3f),
                travelRot * Quaternion.Euler(7f, 0, -2f),
                travelRot * Quaternion.Euler(-5f, 0, 5f),
                travelRot * Quaternion.Euler(3f, 0, -3f),
                travelRot * Quaternion.Euler(1.5f, 0, 2.2f),
                travelRot * Quaternion.Euler(-0.7f, 0, -1.0f),
                travelRot * Quaternion.Euler(0.3f, 0, 0.4f),
                travelRot,
                travelRot
            };

            Vector3[] deltaPositions = worldPositions.Select(x => Quaternion.Inverse(baseRot) * (x - start)).ToArray();
            Quaternion[] deltaRotations = worldRotations.Select(x => Quaternion.Inverse(baseRot) * x).ToArray();
            actors.SubmarineRoot.transform.SetPositionAndRotation(start, baseRot);
            AnimationClip clip = BuildTransformClip(SubmarineClipPath, "Ending_Submarine_Path", times, deltaPositions, deltaRotations);
            return clip;
        }

        static AnimationClip BuildChildPath(ActorSet actors, Terrain terrain)
        {
            Quaternion baseRot = actors.Child.transform.rotation;
            Vector3 start = actors.ChildStart;
            var times = new[] { 0f, 0.25f, 0.65f, 1.4f, 2.4f, 3.4f, 4.15f, 4.5f };
            var fractions = new[] { 0f, 0.035f, 0.13f, 0.31f, 0.57f, 0.79f, 0.94f, 1f };
            var positions = fractions.Select(fraction =>
            {
                Vector3 world = Vector3.Lerp(start, actors.ChildEnd, fraction);
                // 시작/끝 높이의 단순 보간 대신 각 키의 실제 Terrain 높이를 사용한다.
                // Run Forward 리타게팅 중 발과 몸이 지면 아래로 파고드는 것을 막기 위해
                // 작은 여유 높이도 이동 루트에 포함한다.
                world.y = SampleGround(world, terrain) + ChildGroundClearance;
                return Quaternion.Inverse(baseRot) * (world - start);
            }).ToArray();
            var rotations = Enumerable.Repeat(Quaternion.identity, times.Length).ToArray();
            actors.Child.transform.SetPositionAndRotation(start, baseRot);
            return BuildTransformClip(ChildMoveClipPath, "Ending_Child_Run_Path", times, positions, rotations);
        }

        static AnimationClip BuildCameraShake()
        {
            var times = new[] { 0f, 0.08f, 0.16f, 0.24f, 0.34f, 0.46f, 0.62f };
            var positions = new[]
            {
                Vector3.zero, new Vector3(0.18f, -0.12f, 0.05f), new Vector3(-0.14f, 0.09f, -0.04f),
                new Vector3(0.09f, -0.06f, 0.03f), new Vector3(-0.05f, 0.035f, 0f), new Vector3(0.02f, -0.015f, 0f), Vector3.zero
            };
            var rotations = new[]
            {
                Quaternion.identity, Quaternion.Euler(0.5f, -0.7f, 1.2f), Quaternion.Euler(-0.4f, 0.5f, -0.9f),
                Quaternion.Euler(0.25f, -0.3f, 0.55f), Quaternion.Euler(-0.12f, 0.16f, -0.3f),
                Quaternion.Euler(0.05f, -0.06f, 0.12f), Quaternion.identity
            };
            return BuildTransformClip(CameraShakeClipPath, "Ending_Camera2_Impact_Shake", times, positions, rotations);
        }

        static AnimationClip BuildTransformClip(string path, string name, float[] times,
            Vector3[] positions, Quaternion[] rotations)
        {
            if (times.Length != positions.Length || times.Length != rotations.Length)
                throw new ArgumentException("Transform clip key 배열 길이가 다릅니다.");
            var clip = new AnimationClip { name = name, frameRate = 60f };
            SetCurve(clip, typeof(Transform), "m_LocalPosition.x", times, positions.Select(x => x.x).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalPosition.y", times, positions.Select(x => x.y).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalPosition.z", times, positions.Select(x => x.z).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalRotation.x", times, rotations.Select(x => x.x).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalRotation.y", times, rotations.Select(x => x.y).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalRotation.z", times, rotations.Select(x => x.z).ToArray());
            SetCurve(clip, typeof(Transform), "m_LocalRotation.w", times, rotations.Select(x => x.w).ToArray());
            clip.EnsureQuaternionContinuity();
            SetLoop(clip, false);
            return SaveAnimationClip(clip, path);
        }

        static AnimationClip BuildFadeClip()
        {
            var times = new[] { 0f, 7.70f, 8.50f, 8.90f, 9.70f, 14.10f, 15.00f };
            var alpha = new[] { 0f, 0f, 1f, 1f, 0f, 0f, 1f };
            var clip = new AnimationClip { name = "Ending_Screen_Fade", frameRate = 60f };
            SetCurve(clip, typeof(Image), "m_Color.a", times, alpha);
            SetLoop(clip, false);
            return SaveAnimationClip(clip, FadeClipPath);
        }

        static void SetCurve(AnimationClip clip, Type type, string property, float[] times, float[] values)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i < times.Length; ++i) curve.AddKey(new Keyframe(times[i], values[i]));
            for (int i = 0; i < curve.length; ++i)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, type, property), curve);
        }

        static void SetLoop(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        static AnimationClip SaveAnimationClip(AnimationClip fresh, string path)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(fresh, existing);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }

        // --------------------------------------------------------------------- Timeline

        static TimelineAsset ResetEndingTimeline()
        {
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = "MainScene_Ending_Timeline";
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }
            else
            {
                foreach (TrackAsset track in timeline.GetRootTracks().ToList()) timeline.DeleteTrack(track);
            }
            timeline.editorSettings.frameRate = 60.0;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        static PlayableDirector CreateDirector(Transform root, TimelineAsset timeline)
        {
            GameObject go = CreateChild(root, DirectorName).gameObject;
            var director = go.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;
            return director;
        }

        static void BuildTimeline(TimelineAsset timeline, PlayableDirector director, CameraSet cameras,
            ActorSet actors, VfxSet vfx, MotionClips motion, AnimationClip submarinePath,
            AnimationClip childPath, AnimationClip cameraShake, AnimationClip fade, Animator fadeAnimator)
        {
            var shots = timeline.CreateTrack<CinemachineTrack>("Ending Cinemachine Shots");
            director.SetGenericBinding(shots, cameras.Brain);
            AddShot(shots, director, cameras.Cam1, "Camera 1 - Z6 Breach", 0.00, 3.25);
            AddShot(shots, director, cameras.Cam2, "Camera 2 - Beach Landing", 3.10, 5.40);
            AddShot(shots, director, cameras.Cam3, "Camera 3 - Child Reunion", 8.50, 6.50);

            AddActivationTrack(timeline, director, "Reveal - Adult 01", actors.Adult01Visual,
                8.50, Duration - 8.50);
            AddActivationTrack(timeline, director, "Reveal - Adult 02", actors.Adult02Visual,
                8.50, Duration - 8.50);
            AddActivationTrack(timeline, director, "Reveal - Adult 03", actors.Adult03Visual,
                8.50, Duration - 8.50);
            AddActivationTrack(timeline, director, "Reveal - Adult 04", actors.Adult04Visual,
                8.50, Duration - 8.50);
            AddActivationTrack(timeline, director, "Reveal - Child", actors.ChildVisual,
                8.50, Duration - 8.50);

            AddTransformTrack(timeline, director, "Submarine - Breach, Landing & Skid",
                actors.SubmarineMover, submarinePath, 0, 8.50,
                actors.SubmarineRoot.transform.localPosition, actors.SubmarineRoot.transform.localRotation);
            AddTransformTrack(timeline, director, "Camera 2 - Impact Shake",
                cameras.Camera2RigAnimator, cameraShake, 3.86, 0.62,
                cameras.Camera2RigAnimator.transform.localPosition, cameras.Camera2RigAnimator.transform.localRotation);
            AddTransformTrack(timeline, director, "Otter Child - Run Path",
                actors.ChildMover, childPath, ChildRunStart, ChildRunEnd - ChildRunStart,
                actors.Child.transform.localPosition, actors.Child.transform.localRotation);

            AddHumanoidTrack(timeline, director, "Otter Adult 01 - Seat_OpenArms", actors.Adult01Animator,
                motion.Seat, 8.50, ReactionEnd - 8.50, false, true);
            AddHumanoidTrack(timeline, director, "Otter Adult 02 - Yell", actors.Adult02Animator,
                motion.Yell, 9.40, Math.Min(motion.Yell.length, ReactionEnd - 9.40), false, true);
            AddHumanoidTrack(timeline, director, "Otter Adult 03 - Praying", actors.Adult03Animator,
                motion.Praying, 9.65, ReactionEnd - 9.65, false, true);
            AddHumanoidTrack(timeline, director, "Otter Adult 04 - Cheering", actors.Adult04Animator,
                motion.Cheering, 9.90, Math.Min(motion.Cheering.length, ReactionEnd - 9.90), false, true);
            AddHumanoidTrack(timeline, director, "Otter Child - Run Forward", actors.ChildVisualAnimator,
                motion.Run, ChildRunStart, ChildRunEnd - ChildRunStart, true, false);

            var fadeTrack = timeline.CreateTrack<AnimationTrack>("Screen Fade");
            director.SetGenericBinding(fadeTrack, fadeAnimator);
            var fadeClip = fadeTrack.CreateClip(fade);
            fadeClip.start = 0;
            fadeClip.duration = Duration;
            fadeClip.displayName = "Landing fade-out / Reunion fade-in / Final fade-out";
            fadeClip.easeInDuration = fadeClip.easeOutDuration = 0;

            AddControlTrack(timeline, director, "VFX - Water Breach", vfx.WaterBreach, 1.18, 2.0, 1101u);
            AddControlTrack(timeline, director, "VFX - Sand Impact", vfx.SandImpact, 3.94, 2.2, 2201u);
            AddControlTrack(timeline, director, "VFX - Sand Skid Trail", vfx.DustTrail, 4.00, 2.80, 3301u);

            AddAudioTrack(timeline, director, "SFX - Submarine Exit", vfx.ExitAudio, ExitSfxPath, 0.72);
            AddAudioTrack(timeline, director, "SFX - Water Splash", vfx.SplashAudio, SplashSfxPath, 1.18);
            AddAudioTrack(timeline, director, "SFX - Beach Impact", vfx.ImpactAudio, ImpactSfxPath, 3.94);

            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Duration;
            timeline.editorSettings.frameRate = 60.0;
            EditorUtility.SetDirty(timeline);
        }

        static void AddShot(CinemachineTrack track, PlayableDirector director,
            CinemachineVirtualCameraBase cam, string label, double start, double duration)
        {
            TimelineClip clip = track.CreateClip<CinemachineShot>();
            var shot = (CinemachineShot)clip.asset;
            shot.name = label;
            shot.DisplayName = label;
            clip.displayName = label;
            shot.VirtualCamera.exposedName = Guid.NewGuid().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, cam);
            clip.start = start;
            clip.duration = duration;
            clip.easeInDuration = clip.easeOutDuration = 0;
            EditorUtility.SetDirty(shot);
        }

        static void AddTransformTrack(TimelineAsset timeline, PlayableDirector director, string name,
            Animator animator, AnimationClip source, double start, double duration,
            Vector3 positionOffset, Quaternion rotationOffset)
        {
            var track = timeline.CreateTrack<AnimationTrack>(name);
            track.trackOffset = TrackOffset.ApplyTransformOffsets;
            track.position = positionOffset;
            track.rotation = rotationOffset;
            director.SetGenericBinding(track, animator);
            TimelineClip clip = track.CreateClip(source);
            clip.start = start;
            clip.duration = duration;
            clip.displayName = name;
            clip.easeInDuration = clip.easeOutDuration = 0;
            var playable = (AnimationPlayableAsset)clip.asset;
            playable.loop = AnimationPlayableAsset.LoopMode.Off;
            playable.removeStartOffset = false;
            EditorUtility.SetDirty(playable);
        }

        static void AddHumanoidTrack(TimelineAsset timeline, PlayableDirector director, string name,
            Animator animator, AnimationClip source, double start, double duration, bool loop, bool fitOnce)
        {
            if (source == null) throw new Exception($"{name}에 사용할 AnimationClip이 없습니다.");
            var track = timeline.CreateTrack<AnimationTrack>(name);
            track.trackOffset = TrackOffset.ApplySceneOffsets;
            director.SetGenericBinding(track, animator);
            TimelineClip clip = track.CreateClip(source);
            clip.start = start;
            clip.duration = duration;
            clip.displayName = source.name;
            clip.easeInDuration = clip.easeOutDuration = 0;
            if (fitOnce && duration > 0.01) clip.timeScale = source.length / duration;
            var playable = (AnimationPlayableAsset)clip.asset;
            playable.loop = loop ? AnimationPlayableAsset.LoopMode.On : AnimationPlayableAsset.LoopMode.Off;
            playable.applyFootIK = true;
            playable.removeStartOffset = true;
            EditorUtility.SetDirty(playable);
        }

        static void AddControlTrack(TimelineAsset timeline, PlayableDirector director, string name,
            GameObject target, double start, double duration, uint seed)
        {
            var track = timeline.CreateTrack<ControlTrack>(name);
            TimelineClip clip = track.CreateClip<ControlPlayableAsset>();
            clip.start = start;
            clip.duration = duration;
            clip.displayName = name;
            clip.easeInDuration = clip.easeOutDuration = 0;
            var control = (ControlPlayableAsset)clip.asset;
            control.sourceGameObject.exposedName = Guid.NewGuid().ToString();
            control.sourceGameObject.defaultValue = null;
            director.SetReferenceValue(control.sourceGameObject.exposedName, target);
            control.prefabGameObject = null;
            control.active = true;
            control.postPlayback = ActivationControlPlayable.PostPlaybackState.Revert;
            control.updateParticle = true;
            control.searchHierarchy = true;
            control.updateDirector = false;
            control.updateITimeControl = false;
            control.particleRandomSeed = seed;
            EditorUtility.SetDirty(control);
        }

        static void AddActivationTrack(TimelineAsset timeline, PlayableDirector director, string name,
            GameObject target, double start, double duration)
        {
            var track = timeline.CreateTrack<ActivationTrack>(name);
            track.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(track, target);
            TimelineClip clip = track.CreateDefaultClip();
            clip.start = start;
            clip.duration = duration;
            clip.displayName = "Camera 3부터 해달 가족 활성화";
            clip.easeInDuration = clip.easeOutDuration = 0;
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(clip.asset);
        }

        static void AddAudioTrack(TimelineAsset timeline, PlayableDirector director, string name,
            AudioSource source, string audioPath, double start)
        {
            AudioClip audio = AssetDatabase.LoadAssetAtPath<AudioClip>(audioPath);
            if (audio == null) throw new FileNotFoundException("효과음을 찾지 못했습니다.", audioPath);
            var track = timeline.CreateTrack<AudioTrack>(name);
            director.SetGenericBinding(track, source);
            TimelineClip clip = track.CreateClip(audio);
            clip.start = start;
            clip.duration = (double)audio.samples / audio.frequency;
            clip.displayName = audio.name;
            clip.easeInDuration = clip.easeOutDuration = 0;
            ((AudioPlayableAsset)clip.asset).loop = false;
        }

        // --------------------------------------------------------------------- Fade / camera references

        static Animator EnsureFadeOverlay(out Image image)
        {
            GameObject root = FindGameObject(FadeRootName);
            if (root == null)
            {
                root = new GameObject(FadeRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }
            root.SetActive(true);
            Canvas rootCanvas = root.GetComponent<Canvas>();
            if (rootCanvas != null) rootCanvas.sortingOrder = 32000;

            image = root.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.name == FadeImageName);
            if (image == null)
            {
                var go = new GameObject(FadeImageName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                image = go.GetComponent<Image>();
            }
            image.color = new Color(0, 0, 0, 0);
            image.raycastTarget = false;
            Animator animator = image.GetComponent<Animator>();
            if (animator == null) animator = image.gameObject.AddComponent<Animator>();
            ConfigureTimelineAnimator(animator);
            return animator;
        }

        static void RebindCameraReferences(Camera output)
        {
            // UnderwaterZoneDirector의 구체 타입을 직접 참조하지 않아도 최신 씬의 직렬화 필드
            // trackedCamera/trackedTransform을 보존·보정할 수 있게 SerializedObject를 사용한다.
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour.GetType().Name != "UnderwaterZoneDirector") continue;
                var so = new SerializedObject(behaviour);
                var cam = so.FindProperty("trackedCamera");
                if (cam != null && cam.propertyType == SerializedPropertyType.ObjectReference)
                    cam.objectReferenceValue = output;
                var transform = so.FindProperty("trackedTransform");
                if (transform != null && transform.propertyType == SerializedPropertyType.ObjectReference)
                    transform.objectReferenceValue = output.transform;
                var preview = so.FindProperty("previewInEditMode");
                if (preview != null && preview.propertyType == SerializedPropertyType.Boolean)
                    preview.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(behaviour);
                Log($"UnderwaterZoneDirector 출력 카메라 보정: {HierarchyPath(behaviour.transform)}");
            }
        }

        // --------------------------------------------------------------------- Verify

        static void VerifyInternal()
        {
            string sourceSceneHash = HashAsset(SourceScenePath);
            string introTimelineHash = HashAsset(IntroTimelinePath);
            Scene scene = EditorSceneManager.OpenScene(EndingScenePath, OpenSceneMode.Single);
            var director = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == DirectorName);
            if (director == null) throw new Exception("Ending Director가 없습니다. Build를 먼저 실행하세요.");
            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) throw new Exception("Ending Director의 playableAsset이 TimelineAsset이 아닙니다.");
            string timelinePath = AssetDatabase.GetAssetPath(timeline);
            if (timelinePath == IntroTimelinePath || !timelinePath.StartsWith(EndingFolder, StringComparison.Ordinal))
                throw new Exception($"Ending Director가 엔딩 전용 Timeline을 참조하지 않습니다: {timelinePath}");
            if (Math.Abs(timeline.duration - Duration) > 0.001)
                throw new Exception($"Timeline duration이 15초가 아닙니다: {timeline.duration:F4}");
            if (Math.Abs(timeline.editorSettings.frameRate - 60.0) > 0.001)
                throw new Exception($"Timeline fps가 60이 아닙니다: {timeline.editorSettings.frameRate}");
            if (!director.playOnAwake || director.extrapolationMode != DirectorWrapMode.Hold)
                throw new Exception("Director Play On Awake/Hold 설정이 잘못되었습니다.");

            CinemachineBrain brain = Object.FindObjectsByType<CinemachineBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.enabled);
            if (brain == null) throw new Exception("활성 CinemachineBrain이 없습니다.");
            int activeCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Count(x => x.enabled);
            int activeListeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Count(x => x.enabled);
            if (activeCameras != 1 || activeListeners != 1)
                throw new Exception($"활성 Camera/AudioListener 수가 1/1이 아닙니다: {activeCameras}/{activeListeners}");

            var shots = timeline.GetOutputTracks().OfType<CinemachineTrack>().SingleOrDefault();
            if (shots == null || shots.GetClips().Count() != 3)
                throw new Exception("Cinemachine shot이 정확히 3개가 아닙니다.");
            int resolvedShots = 0;
            foreach (TimelineClip clip in shots.GetClips().OrderBy(x => x.start))
            {
                var shot = (CinemachineShot)clip.asset;
                Object value = director.GetReferenceValue(shot.VirtualCamera.exposedName, out bool valid) as Object;
                Log($"샷 참조: {clip.displayName} {clip.start:F2}–{clip.end:F2} valid={valid} → {(value != null ? value.name : "null")}");
                if (valid && value != null) resolvedShots++;
            }
            if (resolvedShots != 3) throw new Exception($"Cinemachine shot 참조가 {resolvedShots}/3만 해석됩니다.");

            VerifyMotionImports();
            VerifyAnimationBindings(timeline, director);
            VerifyControlBindings(timeline, director);

            Image fade = FindGameObject(FadeRootName)?.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(x => x.name == FadeImageName);
            GameObject sub = FindGameObject("Ending_Submarine_Visual");
            GameObject child = FindGameObject("Otter_Child");
            string[] familyVisualNames =
            {
                "Otter_Adult_01_Seat_OpenArms_Visual",
                "Otter_Adult_02_Yell_Visual",
                "Otter_Adult_03_Praying_Visual",
                "Otter_Adult_04_Cheering_Visual",
                "Otter_Child_Visual"
            };
            GameObject[] familyVisuals = familyVisualNames.Select(FindGameObject).ToArray();
            if (fade == null || sub == null || child == null || familyVisuals.Any(x => x == null))
                throw new Exception("검증할 Fade/Submarine/Child/Family Visual 오브젝트가 없습니다.");
            if (familyVisuals.Any(x => x.activeSelf))
                throw new Exception("저장된 씬에서 해달 Visual이 활성 상태입니다. Camera 3 이전에는 비활성이어야 합니다.");

            Vector3 childStartPosition = child.transform.position;
            CinemachineBrain.UpdateMethods originalBrainUpdateMethod = brain.UpdateMethod;
            double originalDirectorTime = director.time;
            try
            {
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                brain.ResetState();
                director.RebuildGraph();
                Log("--- 주요 시간 스크럽 (이 검증은 씬을 저장하지 않음) ---");
                int frame = 0;
                float previous = -1f;
                foreach (float t in VerifyTimes)
                {
                    director.time = t;
                    director.Evaluate();
                    float dt = previous < 0 ? -1f : t - previous;
                    brain.ManualUpdate(++frame, dt);
                    previous = t;
                    bool familyVisible = familyVisuals.All(x => x.activeSelf);
                    Log($"t={t,5:F2} cam={NameOf(brain.ActiveVirtualCamera),-38} fade={fade.color.a:F3} " +
                        $"sub={V(sub.transform.position)} child={V(child.transform.position)} familyVisible={familyVisible}");

                    bool shouldShowFamily = t >= 8.50f;
                    if (familyVisuals.Any(x => x.activeSelf != shouldShowFamily))
                        throw new Exception($"t={t:F2} 해달 Visual 활성 상태가 잘못됐습니다: expected={shouldShowFamily}");

                    // Camera 3가 따라가는 이동 루트는 reveal 시점에도 월드 좌표를 유지해야 한다.
                    // 부모 전체를 ActivationTrack으로 껐다 켜면 이 위치가 (0,0,0)으로 초기화되어
                    // 카메라가 맵 밖으로 점프하는 회귀가 생긴다.
                    if (t >= 8.50f && t < ChildRunStart &&
                        Vector3.Distance(child.transform.position, childStartPosition) > 0.05f)
                        throw new Exception($"t={t:F2} 꼬마 추적 루트가 reveal 중 이동했습니다: " +
                                            $"start={V(childStartPosition)}, actual={V(child.transform.position)}");

                    if (Mathf.Abs(t - 4.05f) < 0.01f || Mathf.Abs(t - 6.80f) < 0.01f)
                    {
                        Bounds bounds = CombinedBounds(sub);
                        float ground = SampleGround(bounds.center, FindTerrainAt(bounds.center));
                        Log($"         접지 검사: hull bottom={bounds.min.y:F2}, terrain={ground:F2}, delta={bounds.min.y - ground:F2}m");
                    }
                }

                // 계획의 중요한 페이드 구간을 수치로 단언한다.
                AssertFadeAt(director, fade, 7.70, 0f, 0.03f);
                AssertFadeAt(director, fade, 8.50, 1f, 0.03f);
                AssertFadeAt(director, fade, 8.90, 1f, 0.03f);
                AssertFadeAt(director, fade, 9.70, 0f, 0.03f);
                AssertFadeAt(director, fade, 14.10, 0f, 0.03f);
                AssertFadeAt(director, fade, 14.99, 1f, 0.04f);

                AssertProtectedAssets(sourceSceneHash, introTimelineHash);
                Log($"Verify 완료: {scene.path}, 15초/60fps, Camera·AudioListener 1개, 5개 Humanoid 바인딩 정상.");
                Log("검증 스크럽 결과는 저장하지 않았습니다.");
            }
            finally
            {
                // ManualUpdate와 Evaluate 결과를 현재 씬에 남기지 않는다. 실패해도 항상 복원한다.
                if (director != null) director.Stop();
                if (brain != null)
                {
                    brain.UpdateMethod = originalBrainUpdateMethod;
                    brain.ResetState();
                }
                if (director != null) director.time = originalDirectorTime;
                EditorSceneManager.OpenScene(EndingScenePath, OpenSceneMode.Single);
            }
        }

        static void VerifyMotionImports()
        {
            var expected = new Dictionary<string, string>
            {
                [SeatPath] = "Seat_OpenArms", [YellPath] = "Yell", [RunPath] = "Run Forward",
                [PrayingPath] = "Praying", [CheeringPath] = "Cheering"
            };
            foreach (var pair in expected)
            {
                var importer = AssetImporter.GetAtPath(pair.Key) as ModelImporter;
                if (importer == null || importer.animationType != ModelImporterAnimationType.Human ||
                    importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    throw new Exception($"{pair.Key}가 Humanoid/Create From This Model이 아닙니다.");
                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(pair.Key).OfType<Avatar>().FirstOrDefault();
                AnimationClip clip = LoadModelClip(pair.Key, pair.Value);
                if (avatar == null || !avatar.isHuman || !avatar.isValid || clip == null)
                    throw new Exception($"{pair.Key} Avatar/clip 검증 실패");
            }
            var runImporter = (ModelImporter)AssetImporter.GetAtPath(RunPath);
            var run = runImporter.clipAnimations.SingleOrDefault(x => x.name == "Run Forward");
            if (run == null || !run.loopTime || !run.loopPose || !run.lockRootRotation ||
                !run.lockRootHeightY || !run.lockRootPositionXZ ||
                Math.Abs(run.firstFrame) > 0.01 || Math.Abs(run.lastFrame - 27f) > 0.01)
                throw new Exception("Run Forward의 0–27f/Loop/Root Bake 설정이 잘못되었습니다.");
            if (AssetDatabase.LoadMainAssetAtPath(OldYellPath) != null ||
                string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(YellPath)))
                throw new Exception("Yell 파일명 또는 에셋 참조 검증에 실패했습니다.");
        }

        static void VerifyAnimationBindings(TimelineAsset timeline, PlayableDirector director)
        {
            string[] expected =
            {
                "Otter Adult 01 - Seat_OpenArms", "Otter Adult 02 - Yell", "Otter Adult 03 - Praying",
                "Otter Adult 04 - Cheering", "Otter Child - Run Forward"
            };
            foreach (string name in expected)
            {
                var track = timeline.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(x => x.name == name);
                if (track == null || !(director.GetGenericBinding(track) is Animator animator) || animator.avatar == null || !animator.avatar.isHuman)
                    throw new Exception($"'{name}' Humanoid Animator 바인딩이 없습니다.");
                TimelineClip clip = track.GetClips().SingleOrDefault();
                if (clip == null || !(clip.asset is AnimationPlayableAsset apa) || apa.clip == null)
                    throw new Exception($"'{name}' AnimationClip이 없습니다.");
                Log($"애니 바인딩: {name} → {HierarchyPath(animator.transform)} / {apa.clip.name} / {clip.start:F2}–{clip.end:F2}");
            }
        }

        static void VerifyControlBindings(TimelineAsset timeline, PlayableDirector director)
        {
            var controls = timeline.GetOutputTracks().OfType<ControlTrack>().ToArray();
            if (controls.Length != 3) throw new Exception($"VFX ControlTrack이 3개가 아닙니다: {controls.Length}");
            foreach (var track in controls)
            {
                var asset = track.GetClips().Single().asset as ControlPlayableAsset;
                GameObject value = asset != null ? asset.sourceGameObject.Resolve(director) : null;
                if (value == null || value.GetComponentInChildren<ParticleSystem>(true) == null)
                    throw new Exception($"'{track.name}' 파티클 ExposedReference가 해석되지 않습니다.");
            }

            var expectedVisualNames = new HashSet<string>
            {
                "Otter_Adult_01_Seat_OpenArms_Visual",
                "Otter_Adult_02_Yell_Visual",
                "Otter_Adult_03_Praying_Visual",
                "Otter_Adult_04_Cheering_Visual",
                "Otter_Child_Visual"
            };
            ActivationTrack[] reveals = timeline.GetOutputTracks().OfType<ActivationTrack>().ToArray();
            if (reveals.Length != expectedVisualNames.Count)
                throw new Exception($"해달 Visual ActivationTrack이 5개가 아닙니다: {reveals.Length}");

            var resolvedVisualNames = new HashSet<string>();
            foreach (ActivationTrack reveal in reveals)
            {
                GameObject visual = director.GetGenericBinding(reveal) as GameObject;
                TimelineClip revealClip = reveal.GetClips().SingleOrDefault();
                if (visual == null || !expectedVisualNames.Contains(visual.name) || revealClip == null ||
                    Math.Abs(revealClip.start - 8.50) > 0.001 || Math.Abs(revealClip.end - Duration) > 0.001)
                    throw new Exception($"'{reveal.name}'의 Camera 3 전용 Visual 활성 바인딩/시간이 잘못됐습니다.");
                resolvedVisualNames.Add(visual.name);
            }
            if (!resolvedVisualNames.SetEquals(expectedVisualNames))
                throw new Exception("Camera 3 reveal 트랙이 해달 Visual 5개를 정확히 한 번씩 바인딩하지 않았습니다.");
        }

        static void AssertFadeAt(PlayableDirector director, Image fade, double time, float expected, float tolerance)
        {
            director.time = time;
            director.Evaluate();
            if (Mathf.Abs(fade.color.a - expected) > tolerance)
                throw new Exception($"t={time:F2} 페이드 알파 {fade.color.a:F3}, 예상 {expected:F2}");
        }

        // --------------------------------------------------------------------- Utilities

        static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject CreateRoot(string name)
        {
            var existing = FindGameObject(name);
            if (existing != null) Object.DestroyImmediate(existing);
            return new GameObject(name);
        }

        static GameObject FindGameObject(string name)
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == name)?.gameObject;
        }

        static Terrain FindTerrainAt(Vector3 point)
        {
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (terrain.terrainData == null) continue;
                Vector3 min = terrain.GetPosition();
                Vector3 max = min + terrain.terrainData.size;
                if (point.x >= min.x && point.x <= max.x && point.z >= min.z && point.z <= max.z)
                    return terrain;
            }
            return null;
        }

        static float SampleGround(Vector3 point, Terrain terrain)
        {
            if (terrain != null && terrain.terrainData != null)
                return terrain.SampleHeight(point) + terrain.GetPosition().y;
            if (Physics.Raycast(new Vector3(point.x, 1000f, point.z), Vector3.down, out RaycastHit hit, 2000f,
                    ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return point.y;
        }

        static Vector3 Grounded(Vector3 point, Terrain terrain)
        {
            point.y = SampleGround(point, terrain);
            return point;
        }

        static float PivotToBottom(GameObject root)
        {
            Bounds b = CombinedBounds(root);
            return root.transform.position.y - b.min.y;
        }

        static Bounds CombinedBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; ++i)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string HashAsset(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                 ?? throw new Exception("Unity 프로젝트 루트를 찾지 못했습니다.");
            string full = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) throw new FileNotFoundException("보호 대상 파일을 찾지 못했습니다.", full);
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(File.ReadAllBytes(full)).Select(x => x.ToString("x2")));
        }

        static void AssertProtectedAssets(string sceneHash, string timelineHash)
        {
            string sceneAfter = HashAsset(SourceScenePath);
            string timelineAfter = HashAsset(IntroTimelinePath);
            if (sceneAfter != sceneHash) throw new Exception("원본 MainScene_Intro_Cinemachine 씬이 변경되었습니다.");
            if (timelineAfter != timelineHash) throw new Exception("원본 Intro Timeline이 변경되었습니다.");
            Log($"원본 보호 확인: Intro scene SHA256 {sceneAfter[..12]}…, Intro Timeline {timelineAfter[..12]}…");
        }

        static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }
            return string.Join("/", names);
        }

        static string NameOf(ICinemachineCamera cam) => cam != null ? cam.Name : "(none)";
        static string V(Vector3 value) => $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        static void Log(string message) => Debug.Log("[Ending] " + message);
    }
}
