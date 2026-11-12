using EasyCombat;
using UnityEngine;

namespace EasyInteraction.Integrations.Combat
{
    [AddComponentMenu("Easy Interaction/Conditions/Health Condition")]
    public sealed class HealthCondition : InteractionCondition
    {
        public bool requireStanding = true;
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            var health = actor.PlayerRoot.GetComponent<Health>();
            return health && (requireStanding ? health.IsStanding : health.IsAlive) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Participant cannot interact in this state.", "interaction.incapacitated");
        }
    }
}
