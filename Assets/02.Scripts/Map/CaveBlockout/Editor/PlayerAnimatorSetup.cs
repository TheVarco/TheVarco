using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CaveBlockout.Editor
{
    public static class PlayerAnimatorSetup
    {
        private const string ControllerPath = "Assets/05.Animations/Player/Controller.controller";
        private const string AnimFolderPath = "Assets/05.Animations/Player/";

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
    }
}
