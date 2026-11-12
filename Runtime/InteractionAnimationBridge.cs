using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Interaction Animation Bridge")]
    public sealed class InteractionAnimationBridge : MonoBehaviour
    {
        [SerializeField]
        private InteractionActor actor;
        [SerializeField]
        private Animator animator;
        [SerializeField]
        private string actionId;
        [SerializeField]
        private string startTrigger = "Interact";
        [SerializeField]
        private string cancelTrigger = "CancelInteraction";
        private InteractionTicket ticket;
        private bool paused;
        private float previousSpeed;
        public InteractionActor Actor
        {
            get => actor;
            set
            {
                Unsubscribe();
                actor = value;
                if (isActiveAndEnabled)
                    Subscribe();
            }
        }

        public Animator Animator { get => animator; set => animator = value; }

        private void OnEnable() => Subscribe();
        private void OnDisable()
        {
            Unsubscribe();
            if (ticket.IsActive)
                ticket.Cancel();
            ticket = default;
            RestoreSpeed();
        }

        private void Subscribe()
        {
            if (!actor)
                actor = GetComponentInParent<InteractionActor>();
            if (!animator)
                animator = GetComponent<Animator>();
            if (actor)
                actor.ActionStateChanged += Changed;
        }

        private void Unsubscribe()
        {
            if (actor)
                actor.ActionStateChanged -= Changed;
        }

        private void Changed(InteractionActionEvent e)
        {
            if (!e.Action || (!string.IsNullOrEmpty(actionId) && e.Action.ActionId != actionId))
                return;
            if (e.State == InteractionActionState.Started && e.Action.Execution == InteractionExecutionMode.AnimationEvent)
            {
                ticket = e.Ticket;
                if (animator && !string.IsNullOrEmpty(startTrigger))
                    animator.SetTrigger(startTrigger);
            }
            else if (e.Ticket.Id == ticket.Id && e.State == InteractionActionState.Cancelled)
            {
                ticket = default;
                RestoreSpeed();
                if (animator && !string.IsNullOrEmpty(cancelTrigger))
                    animator.SetTrigger(cancelTrigger);
            }
            else if (e.Ticket.Id == ticket.Id && e.State == InteractionActionState.Paused && animator && !paused)
            {
                previousSpeed = animator.speed;
                animator.speed = 0;
                paused = true;
            }
            else if (e.Ticket.Id == ticket.Id && (e.State == InteractionActionState.Resumed || e.State == InteractionActionState.Completed))
                RestoreSpeed();
            if (e.Ticket.Id == ticket.Id && (e.State == InteractionActionState.Committed || e.State == InteractionActionState.Completed))
                ticket = default;
        }

        /// <summary>Place this Animation Event at the contact/use frame. Tickets make repeated events harmless.</summary>
        public void CommitInteraction()
        {
            var current = ticket;
            current.Commit();
            if (!current.IsWaitingForCommit)
            {
                ticket = default;
                RestoreSpeed();
            }
        }

        public void CancelInteraction()
        {
            var current = ticket;
            ticket = default;
            current.Cancel();
            RestoreSpeed();
        }

        private void RestoreSpeed()
        {
            if (paused && animator)
                animator.speed = previousSpeed;
            paused = false;
        }
    }
}
