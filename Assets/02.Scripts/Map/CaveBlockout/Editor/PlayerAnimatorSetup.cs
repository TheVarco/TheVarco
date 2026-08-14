using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CaveBlockout.Editor
{
    public static class PlayerAnimatorSetup
    {
        private const string ControllerPath = "Assets/05.Animations/Player/Controller.controller";
        private const string AnimFolderPath = "Assets/05.Animations/Player/";
        private const string PlayerAnimFolderPath = "Assets/99.Resources/PlayerAnim/";
        private const string EmoteStateTag = "Emote";
        private const float MinimumEmoteDurationSeconds = 2f;

        private sealed class EmoteDefinition
        {
            public readonly string StateName;
            public readonly string TriggerName;
            public readonly string FbxPath;
            public readonly string ExpectedClipName;
            public readonly Vector3 Position;

            public EmoteDefinition(string stateName, string triggerName, string fileName, string expectedClipName, Vector3 position)
            {
                StateName = stateName;
                TriggerName = triggerName;
                FbxPath = PlayerAnimFolderPath + fileName;
                ExpectedClipName = expectedClipName;
                Position = position;
            }
        }

        private static readonly EmoteDefinition[] EmoteDefinitions =
        {
            new EmoteDefinition("FemaleStanding", "EmoteFemaleStanding", "X Bot@Female Standing Pose.fbx", "Female Standing Pose", new Vector3(800, 0, 0)),
            new EmoteDefinition("FemaleLaying", "EmoteFemaleLaying", "X Bot@Female Laying Pose.fbx", "Female Laying Pose", new Vector3(800, 100, 0)),
            new EmoteDefinition("Waving", "EmoteWaving", "X Bot@Waving.fbx", "Waving", new Vector3(800, 200, 0)),
            new EmoteDefinition("No", "EmoteNo", "X Bot@No.fbx", "No", new Vector3(800, 300, 0)),
            new EmoteDefinition("Salute", "EmoteSalute", "X Bot@Salute.fbx", "Salute", new Vector3(800, 400, 0))
        };

        [MenuItem("Tools/Player/Setup Punching and Emotes")]
        public static void SetupPlayerEmotesAndPunching()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || controller.layers.Length == 0)
            {
                Debug.LogError($"Cannot set up player emotes because the Animator Controller is missing or has no layers: {ControllerPath}");
                return;
            }

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
            AnimatorState defaultState = FindState(rootStateMachine, "Default");
            AnimatorState noWeaponState = FindState(rootStateMachine, "NoWeapon");
            if (defaultState == null || noWeaponState == null)
            {
                Debug.LogError("Cannot set up player emotes because the existing Default and NoWeapon states are required.");
                return;
            }

            if (!HasParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool))
            {
                Debug.LogError("Cannot set up player emotes because the existing IsMoving Bool parameter is required.");
                return;
            }

            foreach (EmoteDefinition definition in EmoteDefinitions)
            {
                if (HasParameterWithDifferentType(controller, definition.TriggerName, AnimatorControllerParameterType.Trigger))
                {
                    Debug.LogError($"Cannot set up player emotes because parameter '{definition.TriggerName}' already exists with a non-Trigger type.");
                    return;
                }
            }

            RemoveLegacyEmote(controller, rootStateMachine, "MaleStanding", "EmoteMaleStanding");
            RemoveLegacyEmote(controller, rootStateMachine, "Thinking", "EmoteThinking");

            AnimationClip punchingClip = ConfigureAndLoadFbxClip(PlayerAnimFolderPath + "X Bot@Punching.fbx", "Punching");
            if (punchingClip == null)
            {
                return;
            }

            AnimationClip[] emoteClips = new AnimationClip[EmoteDefinitions.Length];
            for (int i = 0; i < EmoteDefinitions.Length; i++)
            {
                EmoteDefinition definition = EmoteDefinitions[i];
                emoteClips[i] = ConfigureAndLoadFbxClip(definition.FbxPath, definition.ExpectedClipName);
                if (emoteClips[i] == null)
                {
                    return;
                }
            }

            noWeaponState.motion = punchingClip;
            EditorUtility.SetDirty(noWeaponState);

            for (int i = 0; i < EmoteDefinitions.Length; i++)
            {
                EmoteDefinition definition = EmoteDefinitions[i];
                AddParameterIfNotExists(controller, definition.TriggerName, AnimatorControllerParameterType.Trigger);

                AnimatorState emoteState = GetOrAddEmoteState(rootStateMachine, definition, emoteClips[i]);
                ConfigureEmoteAnyStateTransition(rootStateMachine, emoteState, definition.TriggerName);
                ConfigureEmoteReturnTransitions(emoteState, defaultState);
                EditorUtility.SetDirty(emoteState);
            }

            EditorUtility.SetDirty(rootStateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Punching and player emotes were set up without rebuilding the Animator Controller.");
        }

        [MenuItem("Tools/Player/Setup Player Animator Controller")]
        public static void SetupPlayerAnimator()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // 기존 파라미터를 삭제하지 않고 필요한 파라미터만 안전하게 추가 (수동 설정 보존)
            AddParameterIfNotExists(controller, "Speed", AnimatorControllerParameterType.Float);
            AddParameterIfNotExists(controller, "IsMoving", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "IsSwimming", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "IsFixing", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "IsSitting", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "IsPushPull", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "HasWeapon", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "Attack", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Melee", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Ranged", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Throw", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Hit", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Eat", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Get", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "HP", AnimatorControllerParameterType.Float, 100f);

            // IK Pass 보장
            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length > 0)
            {
                if (!layers[0].iKPass)
                {
                    layers[0].iKPass = true;
                    controller.layers = layers;
                }
            }

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

            // 애니메이션 클립 로드
            AnimationClip defaultClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Default.anim");
            AnimationClip submarineClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "SubmarineState.anim");
            if (submarineClip == null) submarineClip = defaultClip;
            AnimationClip swim1Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Swim1.anim");
            AnimationClip swim2Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Swim2.anim");
            AnimationClip walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Walk.anim");
            AnimationClip noWeaponClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "NoWeapon.anim");
            AnimationClip meleeWeaponClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "MeleeWeapon.anim");
            AnimationClip fixingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Fixing.anim");
            AnimationClip gettingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Getting.anim");
            AnimationClip hitClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Hit.anim");
            AnimationClip pushPullClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "PushPull.anim");
            AnimationClip sittingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Sitting.anim");
            AnimationClip upperClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Upper.anim");
            AnimationClip throwClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Throw.anim");
            AnimationClip eatClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "eat.anim");
            AnimationClip deadClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Dead.anim");

            // 기존 스테이트 및 트랜지션을 지우지 않고 없는 스테이트만 안전하게 보충
            AnimatorState defaultState = GetOrAddState(rootStateMachine, "Default", defaultClip, new Vector3(300, 0, 0));
            if (rootStateMachine.defaultState == null) rootStateMachine.defaultState = defaultState;

            AnimatorState submarineState = GetOrAddState(rootStateMachine, "SubmarineState", submarineClip, new Vector3(300, -100, 0));
            AnimatorState swim1State = GetOrAddState(rootStateMachine, "Swim1", swim1Clip, new Vector3(300, 100, 0));
            AnimatorState swim2State = GetOrAddState(rootStateMachine, "Swim2", swim2Clip, new Vector3(300, 200, 0));
            AnimatorState walkState = GetOrAddState(rootStateMachine, "Walk", walkClip, new Vector3(300, 300, 0));
            AnimatorState noWeaponState = GetOrAddState(rootStateMachine, "NoWeapon", noWeaponClip, new Vector3(550, 0, 0));
            AnimatorState meleeWeaponState = GetOrAddState(rootStateMachine, "MeleeWeapon", meleeWeaponClip, new Vector3(550, 100, 0));
            AnimatorState fixingState = GetOrAddState(rootStateMachine, "Fixing", fixingClip, new Vector3(550, 200, 0));
            AnimatorState gettingState = GetOrAddState(rootStateMachine, "Getting", gettingClip, new Vector3(550, 300, 0));
            AnimatorState hitState = GetOrAddState(rootStateMachine, "Hit", hitClip, new Vector3(550, 400, 0));
            AnimatorState pushPullState = GetOrAddState(rootStateMachine, "PushPull", pushPullClip, new Vector3(550, 500, 0));
            AnimatorState sittingState = GetOrAddState(rootStateMachine, "Sitting", sittingClip, new Vector3(550, 600, 0));
            AnimatorState upperState = GetOrAddState(rootStateMachine, "Upper", upperClip, new Vector3(550, 700, 0));
            AnimatorState throwState = GetOrAddState(rootStateMachine, "Throw", throwClip, new Vector3(550, 750, 0));
            AnimatorState eatState = GetOrAddState(rootStateMachine, "Eat", eatClip, new Vector3(550, 800, 0));
            
            AnimatorState deadState = GetOrAddState(rootStateMachine, "Dead", deadClip, new Vector3(550, 900, 0));
            deadState.speed = -1f;

            // Movement transitions (수영 & 걷기 & 대기)
            // Default <-> SubmarineState (수중 대기 <-> 잠수함 내부 대기)
            AnimatorStateTransition defaultToSubmarine = AddTransitionIfNotExists(defaultState, submarineState);
            SetTransitionConditions(defaultToSubmarine, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            AnimatorStateTransition submarineToDefault = AddTransitionIfNotExists(submarineState, defaultState);
            SetTransitionConditions(submarineToDefault, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // SubmarineState <-> Walk (잠수함 대기 <-> 잠수함 걷기)
            AnimatorStateTransition submarineToWalk = AddTransitionIfNotExists(submarineState, walkState);
            SetTransitionConditions(submarineToWalk, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            AnimatorStateTransition walkToSubmarine = AddTransitionIfNotExists(walkState, submarineState);
            SetTransitionConditions(walkToSubmarine, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // SubmarineState -> Swim1 (잠수함 대기 중 잠수함 밖으로 이동)
            AnimatorStateTransition submarineToSwim = AddTransitionIfNotExists(submarineState, swim1State);
            SetTransitionConditions(submarineToSwim, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Default -> Swim1 (수영 이동 시작)
            AnimatorStateTransition idleToSwim = AddTransitionIfNotExists(defaultState, swim1State);
            SetTransitionConditions(idleToSwim, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Swim1 -> Default (수중 이동 정지)
            AnimatorStateTransition swimToIdle = AddTransitionIfNotExists(swim1State, defaultState);
            SetTransitionConditions(swimToIdle, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Swim1 -> SubmarineState (수영 중 잠수함 안에서 정지)
            AnimatorStateTransition swimToSubmarine = AddTransitionIfNotExists(swim1State, submarineState);
            SetTransitionConditions(swimToSubmarine, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // Default -> Walk (수중 대기 중 잠수함 안으로 걷기 시작)
            AnimatorStateTransition idleToWalk = AddTransitionIfNotExists(defaultState, walkState);
            SetTransitionConditions(idleToWalk, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // Walk -> Default (걷다가 잠수함 밖으로 나가 정지)
            AnimatorStateTransition walkToIdle = AddTransitionIfNotExists(walkState, defaultState);
            SetTransitionConditions(walkToIdle, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Walk -> Swim1 (걷다가 잠수함 외부로 진입하여 수영)
            AnimatorStateTransition walkToSwim = AddTransitionIfNotExists(walkState, swim1State);
            SetTransitionConditions(walkToSwim, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Swim1 -> Walk (수영하다가 잠수함 내부로 진입하여 걷기)
            AnimatorStateTransition swim1ToWalk = AddTransitionIfNotExists(swim1State, walkState);
            SetTransitionConditions(swim1ToWalk, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // Swim1 -> Swim2 (대시 가속)
            AnimatorStateTransition swim1ToSwim2 = AddTransitionIfNotExists(swim1State, swim2State);
            SetTransitionConditions(swim1ToSwim2, 0.2f, false,
                (AnimatorConditionMode.Greater, 3.0f, "Speed"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Swim2 -> Swim1 (감속)
            AnimatorStateTransition swim2ToSwim1 = AddTransitionIfNotExists(swim2State, swim1State);
            SetTransitionConditions(swim2ToSwim1, 0.2f, false,
                (AnimatorConditionMode.Less, 3.0f, "Speed"));

            // Swim2 -> Default (수중 고속 이동 정지)
            AnimatorStateTransition swim2ToIdle = AddTransitionIfNotExists(swim2State, defaultState);
            SetTransitionConditions(swim2ToIdle, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.If, 0, "IsSwimming"));

            // Swim2 -> SubmarineState (고속 수영 중 잠수함 내부 정지)
            AnimatorStateTransition swim2ToSubmarine = AddTransitionIfNotExists(swim2State, submarineState);
            SetTransitionConditions(swim2ToSubmarine, 0.2f, false,
                (AnimatorConditionMode.IfNot, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // Swim2 -> Walk (고속 수영 중 잠수함 진입 걷기)
            AnimatorStateTransition swim2ToWalk = AddTransitionIfNotExists(swim2State, walkState);
            SetTransitionConditions(swim2ToWalk, 0.2f, false,
                (AnimatorConditionMode.If, 0, "IsMoving"),
                (AnimatorConditionMode.IfNot, 0, "IsSwimming"));

            // AnyState transitions
            AnimatorStateTransition anyToDead = AddAnyStateTransitionIfNotExists(rootStateMachine, deadState);
            EnsureSingleCondition(anyToDead, AnimatorConditionMode.Less, 0.01f, "HP", 0.1f, false);

            AnimatorStateTransition deadToDefault = AddTransitionIfNotExists(deadState, defaultState);
            EnsureSingleCondition(deadToDefault, AnimatorConditionMode.Greater, 0.01f, "HP", 0.2f, false);

            AnimatorStateTransition anyToHit = AddAnyStateTransitionIfNotExists(rootStateMachine, hitState);
            EnsureSingleCondition(anyToHit, AnimatorConditionMode.If, 0, "Hit", 0.1f, false);

            AnimatorStateTransition anyToMeleeAttack = AddAnyStateTransitionIfNotExists(rootStateMachine, meleeWeaponState);
            EnsureSingleCondition(anyToMeleeAttack, AnimatorConditionMode.If, 0, "Melee", 0.1f, false);

            AnimatorStateTransition anyToRangedAttack = AddAnyStateTransitionIfNotExists(rootStateMachine, upperState);
            EnsureSingleCondition(anyToRangedAttack, AnimatorConditionMode.If, 0, "Ranged", 0.1f, false);

            AnimatorStateTransition anyToThrow = AddAnyStateTransitionIfNotExists(rootStateMachine, throwState);
            EnsureSingleCondition(anyToThrow, AnimatorConditionMode.If, 0, "Throw", 0.1f, false);

            AnimatorStateTransition anyToNoWeaponAttack = AddAnyStateTransitionIfNotExists(rootStateMachine, noWeaponState);
            if (anyToNoWeaponAttack.conditions == null || anyToNoWeaponAttack.conditions.Length == 0)
            {
                anyToNoWeaponAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                anyToNoWeaponAttack.AddCondition(AnimatorConditionMode.IfNot, 0, "HasWeapon");
                anyToNoWeaponAttack.hasExitTime = false;
                anyToNoWeaponAttack.duration = 0.1f;
            }

            AnimatorStateTransition anyToEat = AddAnyStateTransitionIfNotExists(rootStateMachine, eatState);
            EnsureSingleCondition(anyToEat, AnimatorConditionMode.If, 0, "Eat", 0.1f, false);

            AnimatorStateTransition anyToGet = AddAnyStateTransitionIfNotExists(rootStateMachine, gettingState);
            EnsureSingleCondition(anyToGet, AnimatorConditionMode.If, 0, "Get", 0.1f, false);

            AnimatorStateTransition anyToFixing = AddAnyStateTransitionIfNotExists(rootStateMachine, fixingState);
            EnsureSingleCondition(anyToFixing, AnimatorConditionMode.If, 0, "IsFixing", 0.1f, false);

            AnimatorStateTransition fixingToDefault = AddTransitionIfNotExists(fixingState, defaultState);
            EnsureSingleCondition(fixingToDefault, AnimatorConditionMode.IfNot, 0, "IsFixing", 0.1f, false);

            AnimatorStateTransition anyToSitting = AddAnyStateTransitionIfNotExists(rootStateMachine, sittingState);
            EnsureSingleCondition(anyToSitting, AnimatorConditionMode.If, 0, "IsSitting", 0.1f, false);

            AnimatorStateTransition sittingToDefault = AddTransitionIfNotExists(sittingState, defaultState);
            EnsureSingleCondition(sittingToDefault, AnimatorConditionMode.IfNot, 0, "IsSitting", 0.1f, false);

            AnimatorStateTransition anyToPushPull = AddAnyStateTransitionIfNotExists(rootStateMachine, pushPullState);
            EnsureSingleCondition(anyToPushPull, AnimatorConditionMode.If, 0, "IsPushPull", 0.1f, false);

            AnimatorStateTransition pushPullToDefault = AddTransitionIfNotExists(pushPullState, defaultState);
            pushPullToDefault.hasExitTime = false;

            // Exit transitions
            AnimatorStateTransition hitToDefault = AddTransitionIfNotExists(hitState, defaultState);
            hitToDefault.hasExitTime = true;
            hitToDefault.exitTime = 0.9f;

            AnimatorStateTransition attackToDefault = AddTransitionIfNotExists(meleeWeaponState, defaultState);
            attackToDefault.hasExitTime = true;
            attackToDefault.exitTime = 0.9f;

            AnimatorStateTransition upperToDefault = AddTransitionIfNotExists(upperState, defaultState);
            upperToDefault.hasExitTime = true;
            upperToDefault.exitTime = 0.9f;

            AnimatorStateTransition throwToDefault = AddTransitionIfNotExists(throwState, defaultState);
            throwToDefault.hasExitTime = true;
            throwToDefault.exitTime = 0.9f;

            AnimatorStateTransition noWeaponToDefault = AddTransitionIfNotExists(noWeaponState, defaultState);
            noWeaponToDefault.hasExitTime = true;
            noWeaponToDefault.exitTime = 0.9f;

            AnimatorStateTransition eatToDefault = AddTransitionIfNotExists(eatState, defaultState);
            eatToDefault.hasExitTime = true;
            eatToDefault.exitTime = 0.9f;

            AnimatorStateTransition getToDefault = AddTransitionIfNotExists(gettingState, defaultState);
            getToDefault.hasExitTime = true;
            getToDefault.exitTime = 0.9f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Player Animator Controller validated safely without overwriting manual setups.");
        }

        private static void SetTransitionConditions(AnimatorStateTransition transition, float duration, bool hasExitTime, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            while (transition.conditions != null && transition.conditions.Length > 0)
            {
                transition.RemoveCondition(transition.conditions[0]);
            }

            foreach (var cond in conditions)
            {
                transition.AddCondition(cond.mode, cond.threshold, cond.parameter);
            }
            transition.hasExitTime = hasExitTime;
            transition.duration = duration;
        }

        private static void EnsureSingleCondition(AnimatorStateTransition transition, AnimatorConditionMode mode, float threshold, string parameter, float duration, bool hasExitTime)
        {
            if (transition.conditions == null || transition.conditions.Length == 0)
            {
                transition.AddCondition(mode, threshold, parameter);
            }
            transition.hasExitTime = hasExitTime;
            transition.duration = duration;
        }

        private static AnimatorStateTransition AddTransitionIfNotExists(AnimatorState fromState, AnimatorState toState)
        {
            foreach (var t in fromState.transitions)
            {
                if (t.destinationState == toState) return t;
            }
            return fromState.AddTransition(toState);
        }

        private static AnimatorStateTransition AddAnyStateTransitionIfNotExists(AnimatorStateMachine stateMachine, AnimatorState toState)
        {
            foreach (var t in stateMachine.anyStateTransitions)
            {
                if (t.destinationState == toState) return t;
            }
            return stateMachine.AddAnyStateTransition(toState);
        }

        private static void AddParameterIfNotExists(AnimatorController controller, string paramName, AnimatorControllerParameterType type, float defaultFloat = 0f)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == paramName) return;
            }
            controller.AddParameter(paramName, type);

            if (defaultFloat != 0f)
            {
                AnimatorControllerParameter[] paramsArr = controller.parameters;
                for (int i = 0; i < paramsArr.Length; i++)
                {
                    if (paramsArr[i].name == paramName)
                    {
                        paramsArr[i].defaultFloat = defaultFloat;
                        break;
                    }
                }
                controller.parameters = paramsArr;
            }
        }

        private static AnimatorState GetOrAddState(AnimatorStateMachine stateMachine, string stateName, Motion motion, Vector3 defaultPos)
        {
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name == stateName)
                {
                    if (childState.state.motion == null && motion != null)
                        childState.state.motion = motion;
                    return childState.state;
                }
            }
            AnimatorState newState = stateMachine.AddState(stateName, defaultPos);
            newState.motion = motion;
            return newState;
        }

        private static AnimationClip ConfigureAndLoadFbxClip(string fbxPath, string expectedClipName)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Cannot set up player animation because the FBX was not found: {fbxPath}");
                return null;
            }

            bool importerChanged = false;
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                importerChanged = true;
            }

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importerChanged = true;
            }

            ModelImporterClipAnimation[] configuredClips = importer.clipAnimations;
            bool usesDefaultClips = configuredClips == null || configuredClips.Length == 0;
            if (usesDefaultClips)
            {
                configuredClips = importer.defaultClipAnimations;
            }

            if ((configuredClips == null || configuredClips.Length == 0) && importerChanged)
            {
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                configuredClips = importer != null ? importer.defaultClipAnimations : null;
                importerChanged = false;
                usesDefaultClips = true;
            }

            if (configuredClips == null || configuredClips.Length == 0)
            {
                Debug.LogError($"Cannot set up player animation because the FBX contains no animation takes: {fbxPath}");
                return null;
            }

            for (int i = 0; i < configuredClips.Length; i++)
            {
                ModelImporterClipAnimation clipSettings = configuredClips[i];
                if (clipSettings.loopTime || clipSettings.loopPose || !clipSettings.lockRootRotation ||
                    !clipSettings.lockRootHeightY || !clipSettings.lockRootPositionXZ)
                {
                    clipSettings.loopTime = false;
                    clipSettings.loopPose = false;
                    clipSettings.lockRootRotation = true;
                    clipSettings.lockRootHeightY = true;
                    clipSettings.lockRootPositionXZ = true;
                    importerChanged = true;
                }
            }

            if (usesDefaultClips)
            {
                importerChanged = true;
            }

            if (importerChanged)
            {
                importer.clipAnimations = configuredClips;
                importer.SaveAndReimport();
            }

            return LoadAnimationClipFromFbx(fbxPath, expectedClipName);
        }

        private static AnimationClip LoadAnimationClipFromFbx(string fbxPath, string expectedClipName)
        {
            AnimationClip firstAnimationClip = null;
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (UnityEngine.Object asset in assets)
            {
                if (!(asset is AnimationClip animationClip) ||
                    animationClip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (firstAnimationClip == null)
                {
                    firstAnimationClip = animationClip;
                }

                if (animationClip.name.Equals(expectedClipName, System.StringComparison.OrdinalIgnoreCase) ||
                    animationClip.name.IndexOf(expectedClipName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return animationClip;
                }
            }

            if (firstAnimationClip != null)
            {
                Debug.LogWarning($"No clip named like '{expectedClipName}' was found in {fbxPath}; using '{firstAnimationClip.name}'.");
                return firstAnimationClip;
            }

            Debug.LogError($"Cannot set up player animation because no AnimationClip sub-asset was found in: {fbxPath}");
            return null;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                if (childState.state.name == stateName)
                {
                    return childState.state;
                }
            }

            return null;
        }

        private static void RemoveLegacyEmote(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            string stateName,
            string triggerName)
        {
            AnimatorState legacyState = FindState(stateMachine, stateName);
            if (legacyState != null)
            {
                foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
                {
                    if (transition.destinationState == legacyState)
                    {
                        stateMachine.RemoveAnyStateTransition(transition);
                    }
                }

                stateMachine.RemoveState(legacyState);
            }

            for (int i = controller.parameters.Length - 1; i >= 0; i--)
            {
                if (controller.parameters[i].name == triggerName)
                {
                    controller.RemoveParameter(i);
                }
            }
        }

        private static AnimatorState GetOrAddEmoteState(AnimatorStateMachine stateMachine, EmoteDefinition definition, AnimationClip clip)
        {
            AnimatorState state = FindState(stateMachine, definition.StateName);
            if (state == null)
            {
                state = stateMachine.AddState(definition.StateName, definition.Position);
            }

            state.motion = clip;
            state.speed = clip.length > 0f && clip.length < MinimumEmoteDurationSeconds
                ? clip.length / MinimumEmoteDurationSeconds
                : 1f;
            state.tag = EmoteStateTag;
            return state;
        }

        private static void ConfigureEmoteAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState emoteState, string triggerName)
        {
            AnimatorStateTransition transition = FindTransition(
                stateMachine.anyStateTransitions,
                emoteState,
                triggerName,
                AnimatorConditionMode.If);

            if (transition == null)
            {
                transition = stateMachine.AddAnyStateTransition(emoteState);
                transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
            }

            transition.hasExitTime = false;
            transition.duration = 0.1f;
            transition.canTransitionToSelf = false;
        }

        private static void ConfigureEmoteReturnTransitions(AnimatorState emoteState, AnimatorState defaultState)
        {
            AnimatorStateTransition movementTransition = FindTransition(
                emoteState.transitions,
                defaultState,
                "IsMoving",
                AnimatorConditionMode.If);

            if (movementTransition == null)
            {
                movementTransition = emoteState.AddTransition(defaultState);
                movementTransition.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
            }

            movementTransition.hasExitTime = false;
            movementTransition.duration = 0.1f;

            AnimatorStateTransition exitTransition = FindUnconditionalTransition(emoteState.transitions, defaultState);
            if (exitTransition == null)
            {
                exitTransition = emoteState.AddTransition(defaultState);
            }

            exitTransition.hasExitTime = true;
            exitTransition.exitTime = 0.9f;
            exitTransition.duration = 0.1f;
        }

        private static AnimatorStateTransition FindTransition(
            AnimatorStateTransition[] transitions,
            AnimatorState destinationState,
            string parameter,
            AnimatorConditionMode mode)
        {
            foreach (AnimatorStateTransition transition in transitions)
            {
                AnimatorCondition[] conditions = transition.conditions;
                if (transition.destinationState == destinationState && conditions != null && conditions.Length == 1 &&
                    conditions[0].parameter == parameter && conditions[0].mode == mode)
                {
                    return transition;
                }
            }

            return null;
        }

        private static AnimatorStateTransition FindUnconditionalTransition(AnimatorStateTransition[] transitions, AnimatorState destinationState)
        {
            foreach (AnimatorStateTransition transition in transitions)
            {
                if (transition.destinationState == destinationState &&
                    (transition.conditions == null || transition.conditions.Length == 0))
                {
                    return transition;
                }
            }

            return null;
        }

        private static bool HasParameter(AnimatorController controller, string parameterName, AnimatorControllerParameterType parameterType)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == parameterName && parameter.type == parameterType)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasParameterWithDifferentType(AnimatorController controller, string parameterName, AnimatorControllerParameterType expectedType)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == parameterName)
                {
                    return parameter.type != expectedType;
                }
            }

            return false;
        }
    }
}
