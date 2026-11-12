using System.Collections.Generic;
using EasyFeedbacks;
using UnityEngine;

namespace EasyInteraction.Integrations.Feedbacks
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/Interaction Feedbacks")]
    public sealed class InteractionFeedbacks : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional IFeedbackContextProvider, for example the player's feedback context. Empty searches the linked Player Root and parents.")]
        private MonoBehaviour contextProvider;
        public MonoBehaviour ContextProvider { get => contextProvider; set => contextProvider = value; }

        private readonly List<MonoBehaviour> contextCandidates = new List<MonoBehaviour>(8);
        public FeedbackPreset started, committed, completed, cancelled, failed;
        [Tooltip("Optional loop while an action is active. It is paused/resumed and stopped with that exact operation.")]
        public FeedbackPreset running;
        private InteractionActor actor;
        private readonly Dictionary<InteractionTicket, FeedbackHandle> loops = new Dictionary<InteractionTicket, FeedbackHandle>();
        private void OnEnable()
        {
            actor = GetComponent<InteractionActor>();
            actor.ActionStateChanged += Changed;
            FeedbackService.Prewarm(started);
            FeedbackService.Prewarm(committed);
            FeedbackService.Prewarm(completed);
            FeedbackService.Prewarm(cancelled);
            FeedbackService.Prewarm(failed);
            FeedbackService.Prewarm(running);
        }

        private void OnDisable()
        {
            if (actor)
                actor.ActionStateChanged -= Changed;
            foreach (var handle in loops.Values)
                handle.Stop();
            loops.Clear();
        }

        private void Changed(InteractionActionEvent e)
        {
            if (!this || !isActiveAndEnabled)
                return;
            var source = e.Action ? e.Action.transform : transform;
            var context = PrepareContext(FeedbackContext.From(source));
            switch (e.State)
            {
                case InteractionActionState.Started:
                    if (started)
                        FeedbackService.Play(started, context);
                    if (running)
                        loops[e.Ticket] = FeedbackService.Play(running, context);
                    break;
                case InteractionActionState.Completed:
                    Stop(e.Ticket);
                    if (completed)
                        FeedbackService.Play(completed, context);
                    break;
                case InteractionActionState.Committed:
                    if (committed)
                        FeedbackService.Play(committed, context);
                    break;
                case InteractionActionState.Cancelled:
                    Stop(e.Ticket);
                    if (cancelled)
                        FeedbackService.Play(cancelled, context);
                    break;
                case InteractionActionState.Failed:
                    if (failed)
                        FeedbackService.Play(failed, context);
                    break;
                case InteractionActionState.Paused:
                    if (loops.TryGetValue(e.Ticket, out var paused))
                        paused.Pause();
                    break;
                case InteractionActionState.Resumed:
                    if (loops.TryGetValue(e.Ticket, out var resumed))
                        resumed.Resume();
                    break;
            }
        }

        private void Stop(InteractionTicket ticket)
        {
            if (!loops.TryGetValue(ticket, out var handle))
                return;
            loops.Remove(ticket);
            handle.Stop();
        }

        private FeedbackContext PrepareContext(FeedbackContext context)
        {
            if (contextProvider)
                return contextProvider.isActiveAndEnabled && contextProvider is IFeedbackContextProvider explicitProvider ? explicitProvider.Prepare(context) : context;
            if (!actor)
                return context;
            var root = actor.PlayerRoot;
            while (root)
            {
                root.GetComponents(contextCandidates);
                for (int i = 0; i < contextCandidates.Count; i++)
                {
                    var component = contextCandidates[i];
                    if (component && component.isActiveAndEnabled && component is IFeedbackContextProvider provider)
                        return provider.Prepare(context);
                }

                root = root.parent;
            }

            return context;
        }
    }
}
