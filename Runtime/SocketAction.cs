using System;
using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [DisallowMultipleComponent, AddComponentMenu("Easy Interaction/Socket Action")]
    public sealed class SocketAction : InteractionAction, IInteractionState
    {
        [SerializeField]
        private Transform anchor;
        [SerializeField]
        private string[] acceptedCategories = Array.Empty<string>();
        [SerializeField]
        private UnityEvent<Socketable> onInserted = new UnityEvent<Socketable>();
        [SerializeField]
        private UnityEvent<Socketable> onRemoved = new UnityEvent<Socketable>();
        private bool changing;
        private Socketable expectedOccupant, expectedItem;
        private bool intentPrepared;
        public Transform Anchor { get => anchor ? anchor : transform; set => anchor = value; }
        public string[] AcceptedCategories { get => acceptedCategories; set => acceptedCategories = value ?? Array.Empty<string>(); }
        public Socketable Occupant { get; private set; }
        public UnityEvent<Socketable> OnInserted => onInserted;
        public UnityEvent<Socketable> OnRemoved => onRemoved;

        public override string GetLabel(InteractionActor actor) => Occupant ? "Remove" : "Insert";
        public bool Accepts(Socketable item)
        {
            if (!item || !item.isActiveAndEnabled || item.Socket)
                return false;
            if (acceptedCategories.Length == 0)
                return true;
            for (int i = 0; i < acceptedCategories.Length; i++)
                if (acceptedCategories[i] == item.category)
                    return true;
            return false;
        }

        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (Owner == actor && intentPrepared)
            {
                if (expectedOccupant)
                    return Occupant == expectedOccupant ? InteractionAvailability.Available : InteractionAvailability.Unavailable("Socket contents changed.");
                var held = actor.GetActive<GrabAction>();
                return !Occupant && expectedItem && held && held.IsHeld && held.GetComponent<Socketable>() == expectedItem && Accepts(expectedItem) ? InteractionAvailability.Available : InteractionAvailability.Unavailable("The item is no longer available for insertion.");
            }

            if (Occupant)
                return InteractionAvailability.Available;
            var grab = actor.GetActive<GrabAction>();
            var item = grab && grab.IsHeld ? grab.GetComponent<Socketable>() : null;
            return Accepts(item) ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Hold a compatible item.", "interaction.compatibleItem");
        }

        protected override bool OnExecute(InteractionActor actor)
        {
            if (expectedOccupant)
                Remove(expectedOccupant);
            else
                TryInsert(expectedItem, actor);
            return false;
        }

        protected override void OnBegin(InteractionActor actor)
        {
            expectedOccupant = Occupant;
            var grab = actor.GetActive<GrabAction>();
            expectedItem = grab ? grab.GetComponent<Socketable>() : null;
            intentPrepared = true;
        }

        public override InteractionAvailability GetContinuationAvailability(InteractionActor actor) => GetAvailability(actor);
        protected override void OnCancel(InteractionActor actor)
        {
            intentPrepared = false;
            expectedOccupant = expectedItem = null;
        }

        protected override void OnComplete(InteractionActor actor) => OnCancel(actor);
        public bool TryInsert(Socketable item, InteractionActor actor = null)
        {
            if (changing || Occupant || !Accepts(item))
                return false;
            var grab = item.GetComponent<GrabAction>();
            if (grab && grab.IsHeld && (!actor || grab.OwnerCount != 1 || !grab.Owns(actor)))
                return false;
            changing = true;
            if (actor)
                actor.EnterTransaction();
            try
            {
                if (grab && grab.IsHeld)
                    actor.ReleaseHeldItem(grab);
                if (!item || !item.isActiveAndEnabled)
                    return false;
                Occupant = item;
                item.Attach(this);
                onInserted.Invoke(item);
                return true;
            }
            finally
            {
                changing = false;
                if (actor)
                    actor.ExitTransaction();
            }
        }

        public bool Remove(Socketable item = null)
        {
            if (changing || !Occupant || (item && item != Occupant))
                return false;
            changing = true;
            try
            {
                var previous = Occupant;
                Occupant = null;
                previous.Detach();
                onRemoved.Invoke(previous);
                return true;
            }
            finally
            {
                changing = false;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Remove();
        }

        [Serializable]
        private struct State
        {
            public string occupantId;
        }

        public string CaptureState()
        {
            var target = Occupant ? Occupant.GetComponent<InteractionTarget>() : null;
            if (Occupant && (!target || string.IsNullOrEmpty(target.PersistentId)))
                throw new InvalidOperationException("Socket occupant needs a unique Interaction Target Persistent ID.");
            return JsonUtility.ToJson(new State { occupantId = target ? target.PersistentId : "" });
        }

        public InteractionAvailability ValidateState(string json)
        {
            try
            {
                var state = JsonUtility.FromJson<State>(json);
                if (string.IsNullOrEmpty(state.occupantId))
                    return InteractionAvailability.Available;
                var target = InteractionWorld.Find(state.occupantId);
                var item = target ? target.GetComponent<Socketable>() : null;
                return item && (item.Socket == this || Accepts(item)) ? InteractionAvailability.Available : InteractionAvailability.Unavailable("Socket item is missing or incompatible.");
            }
            catch
            {
                return InteractionAvailability.Unavailable("Invalid socket state.");
            }
        }

        public void RestoreState(string json)
        {
            var state = JsonUtility.FromJson<State>(json);
            if (Occupant && Occupant.GetComponent<InteractionTarget>()?.PersistentId == state.occupantId)
                return;
            Remove();
            if (!string.IsNullOrEmpty(state.occupantId))
                TryInsert(InteractionWorld.Find(state.occupantId).GetComponent<Socketable>());
        }
    }
}
