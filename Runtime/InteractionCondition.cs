using System;
using UnityEngine;

namespace EasyInteraction
{
    public abstract class InteractionCondition : MonoBehaviour
    {
        [SerializeField]
        private InteractionConditionPhase phase = InteractionConditionPhase.Both;
        [SerializeField, Tooltip("Disable when this condition is used only inside an explicit action/group list.")]
        private bool standalone = true;
        public InteractionConditionPhase Phase { get => phase; set => phase = value; }
        public bool Standalone { get => standalone; set => standalone = value; }

        public abstract InteractionAvailability Check(InteractionActor actor, InteractionAction action);
        internal bool Applies(bool continuing) => isActiveAndEnabled && (phase == InteractionConditionPhase.Both || phase == (continuing ? InteractionConditionPhase.Continue : InteractionConditionPhase.Start));
        protected virtual void OnEnable()
        {
            if (TryGetComponent<InteractionTarget>(out var target))
                target.Refresh();
        }
    }

    public abstract class InteractionCost : MonoBehaviour
    {
        [SerializeField, Tooltip("Empty applies to every action on this target.")]
        private string actionId;
        public string ActionId { get => actionId; set => actionId = value; }

        public bool Applies(InteractionAction action) => isActiveAndEnabled && (string.IsNullOrEmpty(actionId) || action.ActionId == actionId);
        public abstract InteractionAvailability Check(InteractionActor actor, InteractionAction action);
        /// <summary>Reserve without consuming. Ticket is unique for this actor operation; store per-operation state in the adapter.</summary>
        public abstract bool Reserve(InteractionTicket ticket);
        public virtual bool IsValid(InteractionTicket ticket) => true;
        public virtual void Advance(InteractionTicket ticket, float progress)
        {
        }

        /// <summary>Commit must not fail after a successful reservation. Release reservations when their source is destroyed.</summary>
        public abstract void Commit(InteractionTicket ticket);
        public abstract void Refund(InteractionTicket ticket);
        protected virtual void OnEnable()
        {
            if (TryGetComponent<InteractionTarget>(out var target))
                target.Refresh();
        }
    }
}
