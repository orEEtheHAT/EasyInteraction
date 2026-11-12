using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace EasyInteraction.Integrations.XR
{
    [DisallowMultipleComponent, RequireComponent(typeof(XRSimpleInteractable)), AddComponentMenu("Easy Interaction/Integrations/XR Interaction Action")]
    public sealed class XRInteractionAction : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField]
        private InteractionAction action;
        [SerializeField]
        private InteractionAction activateAction;
        private XRSimpleInteractable interactable;
        private readonly Dictionary<XRInteractionActor, IXRSelectInteractor> participants = new Dictionary<XRInteractionActor, IXRSelectInteractor>();
        private readonly HashSet<IXRSelectInteractor> blockedUntilRelease = new HashSet<IXRSelectInteractor>();
        private readonly List<IXRSelectInteractor> released = new List<IXRSelectInteractor>();
        public InteractionAction Action { get => action; set => action = value; }
        public InteractionAction ActivateAction { get => activateAction; set => activateAction = value; }
        public bool canProcess => isActiveAndEnabled;

        private void OnEnable()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (!action)
                action = GetComponent<InteractionAction>();
            if (action && action.MaxParticipants > 1)
                interactable.selectMode = InteractableSelectMode.Multiple;
            interactable.selectFilters.Add(this);
            interactable.selectEntered.AddListener(Entered);
            interactable.selectExited.AddListener(Exited);
            interactable.activated.AddListener(Activated);
            interactable.hoverEntered.AddListener(Hovered);
            interactable.hoverExited.AddListener(Unhovered);
        }

        private void OnDisable()
        {
            if (interactable)
            {
                interactable.selectFilters.Remove(this);
                interactable.selectEntered.RemoveListener(Entered);
                interactable.selectExited.RemoveListener(Exited);
                interactable.activated.RemoveListener(Activated);
                interactable.hoverEntered.RemoveListener(Hovered);
                interactable.hoverExited.RemoveListener(Unhovered);
            }

            while (participants.Count > 0)
            {
                var iterator = participants.GetEnumerator();
                iterator.MoveNext();
                var participant = iterator.Current.Key;
                iterator.Dispose();
                CancelSelection(participant);
            }

            blockedUntilRelease.Clear();
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable target)
        {
            if (blockedUntilRelease.Contains(interactor))
                return false;
            var participant = interactor.transform.GetComponent<XRInteractionActor>();
            return participant && participant.Actor && action && (participant.Actor.GetTicket(action).IsActive || participant.Actor.CheckDirect(action).IsAvailable);
        }

        private void Hovered(HoverEnterEventArgs e)
        {
            var participant = e.interactorObject.transform.GetComponent<XRInteractionActor>();
            if (participant && action)
                participant.Actor.SelectTarget(action.Target);
        }

        private void Unhovered(HoverExitEventArgs e)
        {
            var participant = e.interactorObject.transform.GetComponent<XRInteractionActor>();
            if (participant && action && participant.Actor.CurrentTarget == action.Target && !participants.ContainsKey(participant))
                participant.Actor.SelectTarget(null);
        }

        private void Entered(SelectEnterEventArgs e)
        {
            var participant = e.interactorObject.transform.GetComponent<XRInteractionActor>();
            if (!participant || !action)
                return;
            participants[participant] = e.interactorObject;
            participant.Selected = this;
            participant.Actor.HandAnchor = e.interactorObject.GetAttachTransform(interactable);
            participant.Actor.SelectTarget(action.Target);
            if (!participant.Actor.TryBeginInput(action, out var ticket) || !ticket.IsActive)
                CancelSelection(participant);
        }

        private void Exited(SelectExitEventArgs e)
        {
            var participant = e.interactorObject.transform.GetComponent<XRInteractionActor>();
            if (!participant)
                return;
            participants.Remove(participant);
            if (participant.Selected == this)
                participant.Selected = null;
            if (action)
                participant.Actor.Cancel(action, "XR selection released.");
        }

        private void Activated(ActivateEventArgs e)
        {
            var participant = e.interactorObject.transform.GetComponent<XRInteractionActor>();
            if (participant && activateAction)
                participant.Actor.TryExecuteDirect(activateAction);
        }

        internal void CancelSelection(XRInteractionActor participant)
        {
            if (!participants.TryGetValue(participant, out var interactor))
                return;
            participants.Remove(participant);
            if (interactor.isSelectActive)
                blockedUntilRelease.Add(interactor);
            if (participant && participant.Selected == this)
                participant.Selected = null;
            if (participant && action)
                participant.Actor.Cancel(action, "XR selection ended.");
            if (interactable && interactable.interactionManager)
                interactable.interactionManager.SelectExit(interactor, interactable);
        }

        private void Update()
        {
            if (blockedUntilRelease.Count == 0)
                return;
            released.Clear();
            foreach (var interactor in blockedUntilRelease)
                if ((interactor is Object component && !component) || !interactor.isSelectActive)
                    released.Add(interactor);
            foreach (var interactor in released)
                blockedUntilRelease.Remove(interactor);
        }
    }
}
