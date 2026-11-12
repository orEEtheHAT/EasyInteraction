using EasyBuilding;
using UnityEngine;

namespace EasyInteraction.Integrations.Building
{
    public enum BuildingInteraction
    {
        Remove,
        SelectForPlacement
    }

    [AddComponentMenu("Easy Interaction/Integrations/Building Action")]
    public sealed class BuildingAction : InteractionAction
    {
        public BuildingInteraction operation;
        public PlacedBuilding placedBuilding;
        public BuildingSystem buildingSystem;
        public BuildingDefinition definition;
        public override string GetLabel(InteractionActor actor) => operation == BuildingInteraction.Remove ? "Dismantle" : "Build";
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (operation == BuildingInteraction.Remove)
            {
                if (!placedBuilding)
                    placedBuilding = GetComponentInParent<PlacedBuilding>();
                if (!placedBuilding || !placedBuilding.System)
                    return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object is not a placed building.");
                return placedBuilding.System.IsBusy ? InteractionAvailability.Unavailable(InteractionFailure.Busy, "Another building operation is running.") : InteractionAvailability.Available;
            }

            return buildingSystem && definition ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "Building is not configured.", diagnostic: "Assign Building System and Definition.");
        }

        protected override bool OnExecute(InteractionActor actor)
        {
            var context = actor.PlayerRoot.GetComponent<BuildingInteractionContext>();
            if (!context)
                context = actor.PlayerRoot.gameObject.AddComponent<BuildingInteractionContext>();
            if (operation == BuildingInteraction.Remove)
            {
                if (!placedBuilding.System.TryRemove(placedBuilding, out var result, context.Actor))
                {
                    actor.Cancel(this, result.Reason);
                    return true;
                }
            }
            else if (!context.GetSession(buildingSystem).Select(definition))
            {
                actor.Cancel(this, "Building selection is unavailable.");
                return true;
            }

            return false;
        }
    }
}
