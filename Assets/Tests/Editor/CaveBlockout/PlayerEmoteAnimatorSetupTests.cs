using System;
using System.Linq;
using System.Reflection;
using CaveBlockout.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CaveBlockout.Tests
{
    public sealed class PlayerEmoteAnimatorSetupTests
    {
        private const string ControllerPath = "Assets/05.Animations/Player/Controller.controller";
        private const string PlayerAnimFolderPath = "Assets/99.Resources/PlayerAnim/";

        private sealed class EmoteExpectation
        {
            public readonly string StateName;
            public readonly string TriggerName;
            public readonly string FbxPath;

            public EmoteExpectation(string stateName, string triggerName, string fbxFileName)
            {
                StateName = stateName;
                TriggerName = triggerName;
                FbxPath = PlayerAnimFolderPath + fbxFileName;
            }
        }

        private static readonly EmoteExpectation[] Emotes =
        {
            new EmoteExpectation("FemaleStanding", "EmoteFemaleStanding", "X Bot@Female Standing Pose.fbx"),
            new EmoteExpectation("FemaleLaying", "EmoteFemaleLaying", "X Bot@Female Laying Pose.fbx"),
            new EmoteExpectation("Waving", "EmoteWaving", "X Bot@Waving.fbx"),
            new EmoteExpectation("No", "EmoteNo", "X Bot@No.fbx"),
            new EmoteExpectation("Salute", "EmoteSalute", "X Bot@Salute.fbx")
        };

        [Test]
        public void SetupPlayerEmotesAndPunching_IsIdempotentAndComplete()
        {
            MethodInfo setupMethod = typeof(PlayerAnimatorSetup).GetMethod(
                "SetupPlayerEmotesAndPunching",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(setupMethod, Is.Not.Null);

            // Verify that the checked-in assets are already usable before the
            // idempotency calls get a chance to repair them.
            AssertStoredAssetsAreConfigured();

            setupMethod.Invoke(null, null);
            setupMethod.Invoke(null, null);

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState defaultState = FindSingleState(root, "Default");
            AnimatorState noWeaponState = FindSingleState(root, "NoWeapon");
            Assert.That(controller.parameters.Any(parameter => parameter.name == "EmoteMaleStanding"), Is.False);
            Assert.That(root.states.Any(child => child.state.name == "MaleStanding"), Is.False);
            Assert.That(controller.parameters.Any(parameter => parameter.name == "EmoteThinking"), Is.False);
            Assert.That(root.states.Any(child => child.state.name == "Thinking"), Is.False);

            int firstEmoteTransitionIndex = Array.FindIndex(root.anyStateTransitions, IsEmoteTransition);
            int lastNonEmoteTransitionIndex = Array.FindLastIndex(root.anyStateTransitions, transition => !IsEmoteTransition(transition));
            Assert.That(firstEmoteTransitionIndex, Is.GreaterThan(lastNonEmoteTransitionIndex),
                "Emote Any State transitions must remain behind gameplay transitions.");

            Assert.That(AssetDatabase.GetAssetPath(noWeaponState.motion),
                Is.EqualTo(PlayerAnimFolderPath + "X Bot@Punching.fbx"));

            foreach (EmoteExpectation emote in Emotes)
            {
                Assert.That(controller.parameters.Count(parameter => parameter.name == emote.TriggerName), Is.EqualTo(1));
                AnimatorControllerParameter parameter = controller.parameters.Single(item => item.name == emote.TriggerName);
                Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Trigger));

                AnimatorState state = FindSingleState(root, emote.StateName);
                Assert.That(state.tag, Is.EqualTo("Emote"));
                Assert.That(AssetDatabase.GetAssetPath(state.motion), Is.EqualTo(emote.FbxPath));
                AnimationClip clip = state.motion as AnimationClip;
                Assert.That(clip, Is.Not.Null);
                if (clip.length > 0f && clip.length < 2f)
                {
                    Assert.That(state.speed, Is.EqualTo(clip.length / 2f).Within(0.0001f));
                    Assert.That(clip.length / state.speed, Is.EqualTo(2f).Within(0.0001f));
                }
                else
                {
                    Assert.That(state.speed, Is.EqualTo(1f).Within(0.0001f));
                }

                AnimatorStateTransition[] anyStateTransitions = root.anyStateTransitions
                    .Where(transition => transition.destinationState == state &&
                                         HasSingleCondition(transition, emote.TriggerName, AnimatorConditionMode.If))
                    .ToArray();
                Assert.That(anyStateTransitions, Has.Length.EqualTo(1));
                Assert.That(anyStateTransitions[0].hasExitTime, Is.False);
                Assert.That(anyStateTransitions[0].duration, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(anyStateTransitions[0].canTransitionToSelf, Is.False);

                AnimatorStateTransition[] movementReturns = state.transitions
                    .Where(transition => transition.destinationState == defaultState &&
                                         HasSingleCondition(transition, "IsMoving", AnimatorConditionMode.If))
                    .ToArray();
                Assert.That(movementReturns, Has.Length.EqualTo(1));
                Assert.That(movementReturns[0].hasExitTime, Is.False);
                Assert.That(movementReturns[0].duration, Is.EqualTo(0.1f).Within(0.0001f));

                AnimatorStateTransition[] timedReturns = state.transitions
                    .Where(transition => transition.destinationState == defaultState &&
                                         transition.conditions.Length == 0)
                    .ToArray();
                Assert.That(timedReturns, Has.Length.EqualTo(1));
                Assert.That(timedReturns[0].hasExitTime, Is.True);
                Assert.That(timedReturns[0].exitTime, Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(timedReturns[0].duration, Is.EqualTo(0.1f).Within(0.0001f));
            }

            string[] fbxPaths = Emotes.Select(emote => emote.FbxPath)
                .Append(PlayerAnimFolderPath + "X Bot@Punching.fbx")
                .ToArray();
            foreach (string fbxPath in fbxPaths)
            {
                AssertAnimationImporterSettings(fbxPath);
            }
        }

        private static void AssertStoredAssetsAreConfigured()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState noWeaponState = FindSingleState(root, "NoWeapon");
            Assert.That(controller.parameters.Any(parameter => parameter.name == "EmoteMaleStanding"), Is.False);
            Assert.That(root.states.Any(child => child.state.name == "MaleStanding"), Is.False);
            Assert.That(controller.parameters.Any(parameter => parameter.name == "EmoteThinking"), Is.False);
            Assert.That(root.states.Any(child => child.state.name == "Thinking"), Is.False);
            Assert.That(AssetDatabase.GetAssetPath(noWeaponState.motion),
                Is.EqualTo(PlayerAnimFolderPath + "X Bot@Punching.fbx"));

            foreach (EmoteExpectation emote in Emotes)
            {
                Assert.That(controller.parameters.Count(parameter => parameter.name == emote.TriggerName), Is.EqualTo(1));
                AnimatorState state = FindSingleState(root, emote.StateName);
                Assert.That(state.tag, Is.EqualTo("Emote"));
                Assert.That(AssetDatabase.GetAssetPath(state.motion), Is.EqualTo(emote.FbxPath));
                AssertAnimationImporterSettings(emote.FbxPath);
            }

            AssertAnimationImporterSettings(PlayerAnimFolderPath + "X Bot@Punching.fbx");
        }

        private static bool IsEmoteTransition(AnimatorStateTransition transition)
        {
            return Emotes.Any(emote => HasSingleCondition(transition, emote.TriggerName, AnimatorConditionMode.If));
        }

        private static AnimatorState FindSingleState(AnimatorStateMachine stateMachine, string stateName)
        {
            AnimatorState[] matches = stateMachine.states
                .Select(child => child.state)
                .Where(state => state.name == stateName)
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), $"Expected one state named '{stateName}'.");
            return matches[0];
        }

        private static bool HasSingleCondition(
            AnimatorStateTransition transition,
            string parameter,
            AnimatorConditionMode mode)
        {
            return transition.conditions.Length == 1 &&
                   transition.conditions[0].parameter == parameter &&
                   transition.conditions[0].mode == mode;
        }

        private static void AssertAnimationImporterSettings(string fbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, fbxPath);
            Assert.That(importer.importAnimation, Is.True, fbxPath);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human), fbxPath);
            Assert.That(importer.clipAnimations, Is.Not.Empty, fbxPath);

            foreach (ModelImporterClipAnimation clip in importer.clipAnimations)
            {
                Assert.That(clip.loopTime, Is.False, fbxPath);
                Assert.That(clip.loopPose, Is.False, fbxPath);
                Assert.That(clip.lockRootRotation, Is.True, fbxPath);
                Assert.That(clip.lockRootHeightY, Is.True, fbxPath);
                Assert.That(clip.lockRootPositionXZ, Is.True, fbxPath);
            }
        }
    }
}
