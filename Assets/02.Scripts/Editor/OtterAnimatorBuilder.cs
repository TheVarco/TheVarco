using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class OtterAnimatorBuilder
{
    [MenuItem("Tools/Build Otter Animator Controller")]
    public static void BuildController()
    {
        string controllerPath = "Assets/05.Animations/OtterAnimatorController.controller";
        
        // 1. AnimatorController 생성
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        // 2. 파라미터 추가
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsSwimming", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Eat", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Getting", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Fixing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
        controller.AddParameter("PushPull", AnimatorControllerParameterType.Bool);

        // 3. 애니메이션 클립 로드
        AnimationClip defaultClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Default.anim");
        AnimationClip swim1Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Swim1.anim");
        AnimationClip swim2Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Swim2.anim");
        AnimationClip hitClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Hit.anim");
        AnimationClip fixingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Fixing.anim");
        AnimationClip gettingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Getting.anim");
        AnimationClip pushPullClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/PushPull.anim");
        AnimationClip sittingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Sitting.anim");
        AnimationClip eatClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/eat.anim");
        AnimationClip upperClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/05.Animations/Upper.anim");

        // 4. State Machine 구성
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        // States 생성
        AnimatorState idleState = rootStateMachine.AddState("Idle", new Vector3(300, 0, 0));
        idleState.motion = defaultClip;
        rootStateMachine.defaultState = idleState;

        AnimatorState swim1State = rootStateMachine.AddState("Swim", new Vector3(300, 100, 0));
        swim1State.motion = swim1Clip;

        AnimatorState swim2State = rootStateMachine.AddState("FastSwim", new Vector3(550, 100, 0));
        swim2State.motion = swim2Clip;

        AnimatorState hitState = rootStateMachine.AddState("Hit", new Vector3(300, -100, 0));
        hitState.motion = hitClip;

        AnimatorState fixingState = rootStateMachine.AddState("Fixing", new Vector3(550, 0, 0));
        fixingState.motion = fixingClip;

        AnimatorState gettingState = rootStateMachine.AddState("Getting", new Vector3(550, -100, 0));
        gettingState.motion = gettingClip;

        AnimatorState pushPullState = rootStateMachine.AddState("PushPull", new Vector3(300, 200, 0));
        pushPullState.motion = pushPullClip;

        AnimatorState sittingState = rootStateMachine.AddState("Sitting", new Vector3(550, 200, 0));
        sittingState.motion = sittingClip;

        AnimatorState eatState = rootStateMachine.AddState("Eat", new Vector3(550, -200, 0));
        eatState.motion = eatClip;

        // 5. Transitions 생성
        // Idle <-> Swim
        var idleToSwim = idleState.AddTransition(swim1State);
        idleToSwim.AddCondition(AnimatorConditionMode.If, 0, "IsSwimming");
        idleToSwim.hasExitTime = false;

        var swimToIdle = swim1State.AddTransition(idleState);
        swimToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSwimming");
        swimToIdle.hasExitTime = false;

        // Swim <-> FastSwim (Speed)
        var swim1To2 = swim1State.AddTransition(swim2State);
        swim1To2.AddCondition(AnimatorConditionMode.Greater, 2f, "Speed");
        swim1To2.hasExitTime = false;

        var swim2To1 = swim2State.AddTransition(swim1State);
        swim2To1.AddCondition(AnimatorConditionMode.Less, 2f, "Speed");
        swim2To1.hasExitTime = false;

        // AnyState -> Hit
        var anyToHit = rootStateMachine.AddAnyStateTransition(hitState);
        anyToHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        anyToHit.hasExitTime = false;
        var hitToIdle = hitState.AddTransition(idleState);
        hitToIdle.hasExitTime = true;
        hitToIdle.exitTime = 0.9f;

        // AnyState -> Eat
        var anyToEat = rootStateMachine.AddAnyStateTransition(eatState);
        anyToEat.AddCondition(AnimatorConditionMode.If, 0, "Eat");
        anyToEat.hasExitTime = false;
        var eatToIdle = eatState.AddTransition(idleState);
        eatToIdle.hasExitTime = true;

        // AnyState -> Getting
        var anyToGetting = rootStateMachine.AddAnyStateTransition(gettingState);
        anyToGetting.AddCondition(AnimatorConditionMode.If, 0, "Getting");
        anyToGetting.hasExitTime = false;
        var gettingToIdle = gettingState.AddTransition(idleState);
        gettingToIdle.hasExitTime = true;

        // Idle <-> Fixing
        var idleToFixing = idleState.AddTransition(fixingState);
        idleToFixing.AddCondition(AnimatorConditionMode.If, 0, "Fixing");
        idleToFixing.hasExitTime = false;

        var fixingToIdle = fixingState.AddTransition(idleState);
        fixingToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Fixing");
        fixingToIdle.hasExitTime = false;

        // Idle <-> Sitting
        var idleToSitting = idleState.AddTransition(sittingState);
        idleToSitting.AddCondition(AnimatorConditionMode.If, 0, "Sitting");
        idleToSitting.hasExitTime = false;

        var sittingToIdle = sittingState.AddTransition(idleState);
        sittingToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Sitting");
        sittingToIdle.hasExitTime = false;

        // Idle <-> PushPull
        var idleToPushPull = idleState.AddTransition(pushPullState);
        idleToPushPull.AddCondition(AnimatorConditionMode.If, 0, "PushPull");
        idleToPushPull.hasExitTime = false;

        var pushPullToIdle = pushPullState.AddTransition(idleState);
        pushPullToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "PushPull");
        pushPullToIdle.hasExitTime = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[OtterAnimatorBuilder] Animator Controller successfully built with all states!");

        // 6. 씬 내 3D Otter에 할당
        GameObject otterObj = GameObject.Find("3D Otter");
        if (otterObj != null)
        {
            Animator animator = otterObj.GetComponent<Animator>();
            if (animator == null)
            {
                animator = otterObj.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = controller;
            Debug.Log("[OtterAnimatorBuilder] Assigned AnimatorController to 3D Otter in scene.");
        }
    }
}
