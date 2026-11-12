using UnityEngine;

namespace EasyInteraction.Integrations.XR
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/XR Interaction Actor")]
    public sealed class XRInteractionActor : MonoBehaviour
    {
        public Transform playerRoot;
        public InteractionActor Actor { get; private set; }

        internal XRInteractionAction Selected;
        private void OnEnable()
        {
            Actor = GetComponent<InteractionActor>();
            Actor.View = transform;
            Actor.HandAnchor = transform;
            if (playerRoot)
                Actor.PlayerRoot = playerRoot;
            Actor.SelectionMode = InteractionSelectionMode.Explicit;
            Actor.ActionStateChanged += Changed;
        }

        private void OnDisable()
        {
            if (Actor)
                Actor.ActionStateChanged -= Changed;
            if (Selected)
                Selected.CancelSelection(this);
            if (Actor)
                Actor.Cancel("XR interactor disabled.");
        }

        private void Changed(InteractionActionEvent e)
        {
            if (Selected && e.Action == Selected.Action && (e.State == InteractionActionState.Cancelled || e.State == InteractionActionState.Completed))
                Selected.CancelSelection(this);
        }
    }
}
