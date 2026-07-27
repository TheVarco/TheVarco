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

            // Clear existing parameters
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(i);
            }

            // Add parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsSwimming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsFixing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsSitting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsPushPull", AnimatorControllerParameterType.Bool);
            controller.AddParameter("HasWeapon", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Eat", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Get", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

            // Clear existing states and transitions
            for (int i = rootStateMachine.states.Length - 1; i >= 0; i--)
            {
                rootStateMachine.RemoveState(rootStateMachine.states[i].state);
            }
            for (int i = rootStateMachine.anyStateTransitions.Length - 1; i >= 0; i--)
            {
                rootStateMachine.RemoveAnyStateTransition(rootStateMachine.anyStateTransitions[i]);
            }

            // Load clips
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
            AnimationClip eatClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimFolderPath + "eat.anim");

            // Create states
            AnimatorState defaultState = rootStateMachine.AddState("Default", new Vector3(300, 0, 0));
            defaultState.motion = defaultClip;
            rootStateMachine.defaultState = defaultState;

            AnimatorState swim1State = rootStateMachine.AddState("Swim1", new Vector3(300, 100, 0));
            swim1State.motion = swim1Clip;

            AnimatorState swim2State = rootStateMachine.AddState("Swim2", new Vector3(300, 200, 0));
            swim2State.motion = swim2Clip;

            AnimatorState noWeaponState = rootStateMachine.AddState("NoWeapon", new Vector3(550, 0, 0));
            noWeaponState.motion = noWeaponClip;

            AnimatorState meleeWeaponState = rootStateMachine.AddState("MeleeWeapon", new Vector3(550, 100, 0));
            meleeWeaponState.motion = meleeWeaponClip;

            AnimatorState fixingState = rootStateMachine.AddState("Fixing", new Vector3(550, 200, 0));
            fixingState.motion = fixingClip;

            AnimatorState gettingState = rootStateMachine.AddState("Getting", new Vector3(550, 300, 0));
            gettingState.motion = gettingClip;

            AnimatorState hitState = rootStateMachine.AddState("Hit", new Vector3(550, 400, 0));
            hitState.motion = hitClip;

            AnimatorState pushPullState = rootStateMachine.AddState("PushPull", new Vector3(550, 500, 0));
            pushPullState.motion = pushPullClip;

            AnimatorState sittingState = rootStateMachine.AddState("Sitting", new Vector3(550, 600, 0));
            sittingState.motion = sittingClip;

            AnimatorState upperState = rootStateMachine.AddState("Upper", new Vector3(550, 700, 0));
            upperState.motion = upperClip;

            AnimatorState eatState = rootStateMachine.AddState("Eat", new Vector3(550, 800, 0));
            eatState.motion = eatClip;

            // Movement transitions
            AnimatorStateTransition idleToSwim = defaultState.AddTransition(swim1State);
            idleToSwim.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            idleToSwim.hasExitTime = false;
            idleToSwim.duration = 0.2f;

            AnimatorStateTransition swimToIdle = swim1State.AddTransition(defaultState);
            swimToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
            swimToIdle.hasExitTime = false;
            swimToIdle.duration = 0.2f;

            AnimatorStateTransition swim1ToSwim2 = swim1State.AddTransition(swim2State);
            swim1ToSwim2.AddCondition(AnimatorConditionMode.Greater, 3.0f, "Speed");
            swim1ToSwim2.hasExitTime = false;
            swim1ToSwim2.duration = 0.2f;

            AnimatorStateTransition swim2ToSwim1 = swim2State.AddTransition(swim1State);
            swim2ToSwim1.AddCondition(AnimatorConditionMode.Less, 3.0f, "Speed");
            swim2ToSwim1.hasExitTime = false;
            swim2ToSwim1.duration = 0.2f;

            // AnyState transitions
            AnimatorStateTransition anyToHit = rootStateMachine.AddAnyStateTransition(hitState);
            anyToHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            anyToHit.hasExitTime = false;
            anyToHit.duration = 0.1f;

            AnimatorStateTransition anyToMeleeAttack = rootStateMachine.AddAnyStateTransition(meleeWeaponState);
            anyToMeleeAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            anyToMeleeAttack.AddCondition(AnimatorConditionMode.If, 0, "HasWeapon");
            anyToMeleeAttack.hasExitTime = false;
            anyToMeleeAttack.duration = 0.1f;

            AnimatorStateTransition anyToNoWeaponAttack = rootStateMachine.AddAnyStateTransition(noWeaponState);
            anyToNoWeaponAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            anyToNoWeaponAttack.AddCondition(AnimatorConditionMode.IfNot, 0, "HasWeapon");
            anyToNoWeaponAttack.hasExitTime = false;
            anyToNoWeaponAttack.duration = 0.1f;

            AnimatorStateTransition anyToEat = rootStateMachine.AddAnyStateTransition(eatState);
            anyToEat.AddCondition(AnimatorConditionMode.If, 0, "Eat");
            anyToEat.hasExitTime = false;
            anyToEat.duration = 0.1f;

            AnimatorStateTransition anyToGet = rootStateMachine.AddAnyStateTransition(gettingState);
            anyToGet.AddCondition(AnimatorConditionMode.If, 0, "Get");
            anyToGet.hasExitTime = false;
            anyToGet.duration = 0.1f;

            AnimatorStateTransition anyToFixing = rootStateMachine.AddAnyStateTransition(fixingState);
            anyToFixing.AddCondition(AnimatorConditionMode.If, 0, "IsFixing");
            anyToFixing.hasExitTime = false;

            AnimatorStateTransition fixingToDefault = fixingState.AddTransition(defaultState);
            fixingToDefault.AddCondition(AnimatorConditionMode.IfNot, 0, "IsFixing");
            fixingToDefault.hasExitTime = false;

            AnimatorStateTransition anyToSitting = rootStateMachine.AddAnyStateTransition(sittingState);
            anyToSitting.AddCondition(AnimatorConditionMode.If, 0, "IsSitting");
            anyToSitting.hasExitTime = false;

            AnimatorStateTransition sittingToDefault = sittingState.AddTransition(defaultState);
            sittingToDefault.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSitting");
            sittingToDefault.hasExitTime = false;

            AnimatorStateTransition anyToPushPull = rootStateMachine.AddAnyStateTransition(pushPullState);
            anyToPushPull.AddCondition(AnimatorConditionMode.If, 0, "IsPushPull");
            anyToPushPull.hasExitTime = false;

            AnimatorStateTransition pushPullToDefault = pushPullState.AddTransition(defaultState);
            pushPullToDefault.AddCondition(AnimatorConditionMode.IfNot, 0, "IsPushPull");
            pushPullToDefault.hasExitTime = false;

            // Action exit transitions
            AnimatorStateTransition hitToDefault = hitState.AddTransition(defaultState);
            hitToDefault.hasExitTime = true;
            hitToDefault.exitTime = 0.9f;

            AnimatorStateTransition attackToDefault = meleeWeaponState.AddTransition(defaultState);
            attackToDefault.hasExitTime = true;
            attackToDefault.exitTime = 0.9f;

            AnimatorStateTransition noWeaponToDefault = noWeaponState.AddTransition(defaultState);
            noWeaponToDefault.hasExitTime = true;
            noWeaponToDefault.exitTime = 0.9f;

            AnimatorStateTransition eatToDefault = eatState.AddTransition(defaultState);
            eatToDefault.hasExitTime = true;
            eatToDefault.exitTime = 0.9f;

            AnimatorStateTransition getToDefault = gettingState.AddTransition(defaultState);
            getToDefault.hasExitTime = true;
            getToDefault.exitTime = 0.9f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Player Animator Controller successfully updated with all animation clips.");
        }
    }
}
