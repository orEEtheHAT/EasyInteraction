using UnityEngine;

namespace EasyInteraction
{
    [CreateAssetMenu(menuName = "Easy Interaction/Interaction Preset", fileName = "Interaction Preset")]
    public sealed class InteractionPreset : ScriptableObject
    {
        [Header("Player selection")]
        public InteractionSelectionMode selection = InteractionSelectionMode.Focus;
        [Min(.1f)]
        public float range = 3;
        [Range(1, 179)]
        public float focusAngle = 60;
        [Range(0, 1)]
        public float directionWeight = .75f;
        [Range(0, .5f)]
        public float switchThreshold = .08f;
        [Header("Action execution")]
        public InteractionInputGesture input = InteractionInputGesture.Press;
        public InteractionExecutionMode execution;
        [Min(.01f)]
        public float duration = 2;
        [Min(.05f)]
        public float confirmation = .5f;
        public bool requireRangeWhileRunning, requireVisibilityWhileRunning;
        /// <summary>Copies configuration. The preset never owns runtime progress, focus, participants or reservations.</summary>
        public void ApplyTo(InteractionActor actor)
        {
            if (!actor)
                return;
            actor.SelectionMode = selection;
            actor.Range = range;
            actor.FocusAngle = focusAngle;
            actor.DirectionWeight = directionWeight;
            actor.SwitchThreshold = switchThreshold;
        }

        public void ApplyTo(InteractionAction action)
        {
            if (!action)
                return;
            action.InputGesture = input;
            action.Execution = execution;
            action.Duration = duration;
            action.ConfirmationSeconds = confirmation;
            action.RequireRangeWhileRunning = requireRangeWhileRunning;
            action.RequireVisibilityWhileRunning = requireVisibilityWhileRunning;
        }
    }
}
