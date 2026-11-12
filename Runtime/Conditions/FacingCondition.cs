using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Conditions/Facing Condition")]
    public sealed class FacingCondition : InteractionCondition
    {
        [Tooltip("Its forward direction points toward the usable side. Default: this Transform.")]
        public Transform usableSide;
        [Range(0, 180)]
        public float angle = 90;
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            var side = usableSide ? usableSide : transform;
            var offset = actor.View.position - side.position;
            return Vector3.Angle(side.forward, offset) <= angle * .5f ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.WrongSide, "Use the other side.", "interaction.wrongSide");
        }
    }
}
