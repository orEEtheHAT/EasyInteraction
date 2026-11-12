using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Conditions/Flag Condition")]
    public sealed class FlagCondition : InteractionCondition
    {
        [SerializeField]
        private bool allowed = true;
        [SerializeField]
        private string reason = "Not available yet.";
        [SerializeField]
        private string textKey = "interaction.requirement";
        public bool Allowed { get => allowed; set => allowed = value; }
        public string Reason { get => reason; set => reason = value; }

        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action) => allowed ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, reason, textKey);
    }
}
