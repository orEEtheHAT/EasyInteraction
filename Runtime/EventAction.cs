using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Event Action")]
    public sealed class EventAction : InteractionAction
    {
        [SerializeField]
        private bool available = true;
        [SerializeField]
        private string unavailableReason = "This action is currently unavailable.";
        [SerializeField]
        private UnityEvent onExecute = new UnityEvent();
        public bool Available { get => available; set => available = value; }
        public string UnavailableReason { get => unavailableReason; set => unavailableReason = value; }
        public UnityEvent OnExecuteEvent => onExecute;

        public override InteractionAvailability GetAvailability(InteractionActor actor) => available ? InteractionAvailability.Available : InteractionAvailability.Unavailable(unavailableReason);
        protected override bool OnExecute(InteractionActor actor)
        {
            onExecute.Invoke();
            return false;
        }
    }
}
