using EasyResources;
using UnityEngine;

namespace EasyInteraction.Integrations.Resources
{
    [AddComponentMenu("Easy Interaction/Conditions/Resource Condition")]
    public sealed class ResourceCondition : InteractionCondition
    {
        public ResourceContainer container;
        public ResourceCost required = new ResourceCost();
        public string reason = "Required resources are missing.";
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            var source = container ? container : actor.PlayerRoot.GetComponent<ResourceContainer>();
            return source && source.CanAfford(required) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.InsufficientResources, reason, "interaction.resources");
        }
    }
}
