using UnityEngine;

namespace EasyInteraction
{
    [RequireComponent(typeof(GrabAction)), AddComponentMenu("Easy Interaction/Inspect Action")]
    public sealed class InspectAction : InteractionAction
    {
        private GrabAction grab;
        protected override string DefaultChannel => "view";

        public override string GetLabel(InteractionActor actor) => grab && grab.IsInspecting ? "Finish inspection" : "Inspect";
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!grab)
                grab = GetComponent<GrabAction>();
            return grab && grab.IsHeld && grab.Owns(actor) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Pick up this object first.", "interaction.pickUpFirst");
        }

        protected override bool OnExecute(InteractionActor actor)
        {
            grab.SetInspection(true);
            return true;
        }

        protected override void OnTick(InteractionActor actor, float deltaTime)
        {
            if (!grab || !grab.Owns(actor))
                actor.Cancel(this, "Item released.");
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (grab)
                grab.SetInspection(false);
        }

        protected override void OnComplete(InteractionActor actor)
        {
            if (grab)
                grab.SetInspection(false);
        }
    }
}
