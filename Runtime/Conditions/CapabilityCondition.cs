using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Conditions/Capability Condition")]
    public sealed class CapabilityCondition : InteractionCondition
    {
        public string capability = "key";
        public string reason = "A key is required.";
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action) => actor.HasCapability(capability) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.MissingCapability, reason, "interaction.capability");
    }
}
