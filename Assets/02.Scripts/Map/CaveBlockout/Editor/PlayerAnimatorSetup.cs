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
            AnimationClip swim1Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Swim1.anim");
            AnimationClip swim2Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "Swim2.anim");
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

            AnimatorState swim1State = GetOrAddState(rootStateMachine, "Swim1", swim1Clip, new Vector3(300, 100, 0));
            AnimatorState swim2State = GetOrAddState(rootStateMachine, "Swim2", swim2Clip, new Vector3(300, 200, 0));
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

            // Movement transitions
            AnimatorStateTransition idleToSwim = AddTransitionIfNotExists(defaultState, swim1State);
            EnsureSingleCondition(idleToSwim, AnimatorConditionMode.If, 0, "IsMoving", 0.2f, false);

            AnimatorStateTransition swimToIdle = AddTransitionIfNotExists(swim1State, defaultState);
            EnsureSingleCondition(swimToIdle, AnimatorConditionMode.IfNot, 0, "IsMoving", 0.2f, false);

            AnimatorStateTransition swim1ToSwim2 = AddTransitionIfNotExists(swim1State, swim2State);
            EnsureSingleCondition(swim1ToSwim2, AnimatorConditionMode.Greater, 3.0f, "Speed", 0.2f, false);

            AnimatorStateTransition swim2ToSwim1 = AddTransitionIfNotExists(swim2State, swim1State);
            EnsureSingleCondition(swim2ToSwim1, AnimatorConditionMode.Less, 3.0f, "Speed", 0.2f, false);

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
