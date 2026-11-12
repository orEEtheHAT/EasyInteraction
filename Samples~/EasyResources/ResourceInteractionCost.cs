using System;
using System.Collections.Generic;
using EasyResources;
using UnityEngine;

namespace EasyInteraction.Integrations.Resources
{
    [AddComponentMenu("Easy Interaction/Integrations/Resource Interaction Cost")]
    public sealed class ResourceInteractionCost : InteractionCost
    {
        [SerializeField, Tooltip("Empty uses Resource Container on Actor > Player Root.")]
        private ResourceContainer container;
        [SerializeField]
        private ResourceCost cost = new ResourceCost();
        [SerializeField, Range(1, 32), Tooltip("1 pays at the commit point. More installments consume with progress; cancellation releases the unpaid remainder.")]
        private int installments = 1;
        private ResourceCost[] prepared;
        private bool[] nonempty;
        private readonly Dictionary<InteractionTicket, ReservationSet> active = new Dictionary<InteractionTicket, ReservationSet>();
        private readonly Stack<ReservationSet> pool = new Stack<ReservationSet>();
        private sealed class ReservationSet
        {
            public ResourceReservation Token;
            public ResourceCost[] Steps;
            public bool[] Nonempty;
            public int Committed;
        }

        public ResourceContainer Container { get => container; set => container = value; }

        public ResourceCost Cost
        {
            get => cost;
            set
            {
                cost = value ?? new ResourceCost();
                prepared = null;
            }
        }

        public int Installments
        {
            get => installments;
            set
            {
                installments = Mathf.Clamp(value, 1, 32);
                prepared = null;
            }
        }

        private ResourceContainer Source(InteractionActor actor) => container ? container : actor.PlayerRoot.GetComponent<ResourceContainer>();
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            var source = Source(actor);
            if (!source)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "No resource container.", diagnostic: "Add Resource Container to Actor > Player Root, or assign Container on Resource Interaction Cost.");
            return source.CanAfford(cost) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.InsufficientResources, "Not enough resources.", "interaction.resources");
        }

        private void Prepare()
        {
            if (prepared != null)
                return;
            cost.Prepare();
            prepared = new ResourceCost[installments];
            nonempty = new bool[installments];
            var items = cost.Items;
            for (int step = 0; step < installments; step++)
            {
                var amounts = new ResourceAmount[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    long quantity = items[i].Amount, remainder = quantity % installments;
                    long part = quantity / installments + remainder * (step + 1) / installments - remainder * step / installments;
                    amounts[i] = new ResourceAmount(items[i].Resource, part);
                    nonempty[step] |= part > 0;
                }

                prepared[step] = new ResourceCost(amounts);
            }
        }

        public override bool Reserve(InteractionTicket ticket)
        {
            if (active.ContainsKey(ticket))
                return true;
            var source = Source(ticket.Actor);
            if (!source)
                return false;
            Prepare();
            if (!source.TryReserve(cost, out var reservation))
                return false;
            var set = pool.Count > 0 ? pool.Pop() : new ReservationSet();
            set.Token = reservation;
            set.Steps = prepared;
            set.Nonempty = nonempty;
            set.Committed = 0;
            active.Add(ticket, set);
            return true;
        }

        public override bool IsValid(InteractionTicket ticket) => isActiveAndEnabled && active.TryGetValue(ticket, out var set) && set.Token.IsActive;
        public override void Advance(InteractionTicket ticket, float progress)
        {
            if (!active.TryGetValue(ticket, out var set) || set.Steps.Length <= 1)
                return;
            int count = Mathf.Min(set.Steps.Length - 1, Mathf.FloorToInt(Mathf.Clamp01(progress) * set.Steps.Length));
            while (set.Committed < count)
            {
                if (set.Nonempty[set.Committed] && !set.Token.TryCommit(set.Steps[set.Committed], out _))
                    throw new InvalidOperationException("Resource reservation expired.");
                set.Committed++;
            }
        }

        public override void Commit(InteractionTicket ticket)
        {
            if (!active.TryGetValue(ticket, out var set))
                return;
            if (!set.Token.Commit())
                throw new InvalidOperationException("Resource reservation expired.");
            active.Remove(ticket);
            ReleaseSet(set);
        }

        public override void Refund(InteractionTicket ticket)
        {
            if (!active.TryGetValue(ticket, out var set))
                return;
            active.Remove(ticket);
            ReleaseSet(set);
        }

        private void ReleaseSet(ReservationSet set)
        {
            set.Token.Cancel();
            set.Token = default;
            set.Steps = null;
            set.Nonempty = null;
            set.Committed = 0;
            pool.Push(set);
        }

        private void OnValidate() => prepared = null;
        private void OnDisable()
        {
            if (active.Count == 0)
                return;
            var tickets = new InteractionTicket[active.Count];
            active.Keys.CopyTo(tickets, 0);
            foreach (var ticket in tickets)
            {
                Refund(ticket);
                ticket.Cancel();
            }
        }
    }
}
