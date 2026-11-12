using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Conditions/Distance Condition")]
    public sealed class DistanceCondition : InteractionCondition
    {
        [Min(0)]
        public float minimum;
        [Min(.01f)]
        public float maximum = 3;
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            float distance = Vector3.Distance(actor.View.position, action.Target.AimPosition);
            return distance >= minimum && distance <= maximum ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.OutOfRange, "Move into reach.", "interaction.outOfRange");
        }
    }
}
