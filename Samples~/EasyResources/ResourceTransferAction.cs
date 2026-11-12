using System;
using EasyResources;
using UnityEngine;

namespace EasyInteraction.Integrations.Resources
{
    public enum ResourceTransferDirection
    {
        Collect,
        Deposit
    }

    [RequireComponent(typeof(ResourceContainer)), AddComponentMenu("Easy Interaction/Integrations/Resource Transfer Action")]
    public class ResourceTransferAction : InteractionAction
    {
        [SerializeField]
        private ResourceDefinition resource;
        [SerializeField, Min(1)]
        private int amount = 1;
        [SerializeField]
        private ResourceTransferDirection direction;
        [SerializeField, Tooltip("Empty uses Resource Container on Actor > Player Root.")]
        private ResourceContainer actorContainer;
        private ResourceCost transferCost;
        private readonly ResourceTransaction transaction = new ResourceTransaction();
        public ResourceDefinition Resource
        {
            get => resource;
            set
            {
                resource = value;
                transferCost = null;
            }
        }

        public int Amount
        {
            get => amount;
            set
            {
                amount = Mathf.Max(1, value);
                transferCost = null;
            }
        }

        public ResourceTransferDirection Direction { get => direction; set => direction = value; }
        public ResourceContainer ActorContainer { get => actorContainer; set => actorContainer = value; }
        private ResourceCost Cost => transferCost ?? (transferCost = new ResourceCost(new ResourceAmount(resource, amount)));

        private void Containers(InteractionActor actor, out ResourceContainer source, out ResourceContainer destination)
        {
            var player = actorContainer ? actorContainer : actor.PlayerRoot.GetComponent<ResourceContainer>();
            var stock = GetComponent<ResourceContainer>();
            source = direction == ResourceTransferDirection.Collect ? stock : player;
            destination = direction == ResourceTransferDirection.Collect ? player : stock;
        }

        public override string GetLabel(InteractionActor actor) => direction == ResourceTransferDirection.Collect ? "Pick up" : "Deposit";
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            Containers(actor, out var source, out var destination);
            if (!resource || !source || !destination || source == destination)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "Resource transfer is not configured.", diagnostic: "Assign Resource, add Resource Container to the player root and configure this object's Initial Contents.");
            if (!source.CanAfford(Cost))
                return InteractionAvailability.Unavailable(InteractionFailure.InsufficientResources, "Not enough resources.", "interaction.resources");
            transaction.Clear();
            transaction.Transfer(source.Store, destination.Store, Cost);
            return transaction.CanExecute(out _) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Container cannot accept this resource.", "interaction.full");
        }

        protected override bool OnExecute(InteractionActor actor)
        {
            Containers(actor, out var source, out var destination);
            if (!source.TryTransferTo(destination, Cost, out var failure))
                throw new InvalidOperationException(failure.ToString());
            return false;
        }

        private void OnValidate() => transferCost = null;
    }
}
