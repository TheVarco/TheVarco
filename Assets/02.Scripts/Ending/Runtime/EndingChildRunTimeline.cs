using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Varco.Ending
{
    [Serializable]
    public sealed class EndingChildRunClip : PlayableAsset, ITimelineClipAsset
    {
        [Min(0f)] public float MovementStart = 9.6f;
        [Min(0.01f)] public float MovementDuration = 4.5f;
        public AnimationCurve Easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<EndingChildRunBehaviour>.Create(graph);
            EndingChildRunBehaviour behaviour = playable.GetBehaviour();
            behaviour.MovementStart = MovementStart;
            behaviour.MovementDuration = MovementDuration;
            behaviour.Easing = Easing;
            return playable;
        }
    }

    public sealed class EndingChildRunBehaviour : PlayableBehaviour
    {
        public float MovementStart;
        public float MovementDuration;
        public AnimationCurve Easing;

        public float EvaluateProgress(double localTime)
        {
            float linear = Mathf.Clamp01(((float)localTime - MovementStart) / Mathf.Max(0.01f, MovementDuration));
            return Easing != null ? Mathf.Clamp01(Easing.Evaluate(linear)) : linear;
        }
    }

    public sealed class EndingChildRunMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var controller = playerData as EndingChildRunController;
            if (controller == null || !controller.IsConfigured) return;

            int count = playable.GetInputCount();
            for (int i = 0; i < count; ++i)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<EndingChildRunBehaviour>)playable.GetInput(i);
                controller.Apply(input.GetBehaviour().EvaluateProgress(input.GetTime()));
                return;
            }
        }
    }

    [TrackColor(0.1f, 0.75f, 0.9f)]
    [TrackClipType(typeof(EndingChildRunClip))]
    [TrackBindingType(typeof(EndingChildRunController))]
    public sealed class EndingChildRunTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<EndingChildRunMixer>.Create(graph, inputCount);
        }
    }
}
