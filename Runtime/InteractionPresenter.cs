using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    public readonly struct InteractionPrompt
    {
        public InteractionTarget Target { get; }
        public InteractionAction Action { get; }
        public string TargetName { get; }
        public string Label { get; }
        public string Reason { get; }
        public string Binding { get; }
        public Sprite BindingIcon { get; }
        public bool Available { get; }
        public bool Running { get; }
        public float Progress { get; }

        public InteractionPrompt(InteractionTarget target, InteractionAction action, string targetName, string label, string reason, string binding, bool available, bool running, float progress) : this(target, action, targetName, label, reason, binding, available, running, progress, null)
        {
        }

        public InteractionPrompt(InteractionTarget target, InteractionAction action, string targetName, string label, string reason, string binding, bool available, bool running, float progress, Sprite bindingIcon)
        {
            Target = target;
            Action = action;
            TargetName = targetName;
            Label = label;
            Reason = reason;
            Binding = binding;
            BindingIcon = bindingIcon;
            Available = available;
            Running = running;
            Progress = progress;
        }

        internal bool Same(InteractionPrompt other) => Target == other.Target && Action == other.Action && TargetName == other.TargetName && Label == other.Label && Reason == other.Reason && Binding == other.Binding && BindingIcon == other.BindingIcon && Available == other.Available && Running == other.Running && Mathf.Abs(Progress - other.Progress) < .001f;
    }

    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Interaction Presenter")]
    public sealed class InteractionPresenter : MonoBehaviour
    {
        [SerializeField]
        private MonoBehaviour textProvider;
        [SerializeField, Tooltip("Optional IInteractionInputSource. Empty uses an active source on this actor, preferring an adapter over standalone Interaction Input. Disabled sources are never read.")]
        private MonoBehaviour inputSource;
        [SerializeField, Min(.02f)]
        private float refreshInterval = .1f;
        private InteractionActor actor;
        private MonoBehaviour resolvedInputComponent;
        private IInteractionInputSource resolvedInput;
        private readonly List<MonoBehaviour> inputCandidates = new List<MonoBehaviour>(8);
        private float nextRefresh;
        private bool dirty = true;
        private readonly InteractionSignal<InteractionPrompt> changed = new InteractionSignal<InteractionPrompt>();
        public InteractionActor Actor => actor ? actor : (actor = GetComponent<InteractionActor>());
        public InteractionPrompt Prompt { get; private set; }

        public event Action<InteractionPrompt> Changed { add => changed.Add(value); remove => changed.Remove(value); }

        public MonoBehaviour TextProvider
        {
            get => textProvider;
            set
            {
                textProvider = value;
                dirty = true;
            }
        }

        /// <summary>Explicit source. A disabled explicit source stays assigned but supplies no presentation or toggle setting.</summary>
        public MonoBehaviour InputSource
        {
            get => inputSource;
            set
            {
                inputSource = value;
                dirty = true;
                if (Application.isPlaying && isActiveAndEnabled)
                    Refresh();
            }
        }

        public IInteractionInputSource ActiveInputSource => ResolveInput();
        public bool ToggleInsteadOfHold => ResolveInput()?.ToggleInsteadOfHold ?? false;

        private void OnEnable()
        {
            actor = Actor;
            actor.TargetChanged += Target;
            actor.ActionStateChanged += State;
            dirty = true;
            ResolveInput();
        }

        private void OnDisable()
        {
            if (actor)
            {
                actor.TargetChanged -= Target;
                actor.ActionStateChanged -= State;
            }

            BindInput(null);
            inputCandidates.Clear();
        }

        private IInteractionInputSource ResolveInput()
        {
            MonoBehaviour candidate = null;
            if (inputSource)
            {
                if (inputSource.isActiveAndEnabled && inputSource is IInteractionInputSource)
                    candidate = inputSource;
            }
            else
            {
                Actor.GetComponents(inputCandidates);
                for (int i = 0; i < inputCandidates.Count; i++)
                {
                    var component = inputCandidates[i];
                    if (!component || !component.isActiveAndEnabled || !(component is IInteractionInputSource))
                        continue;
                    if (!candidate || candidate is InteractionInput)
                        candidate = component;
                    if (!(candidate is InteractionInput))
                        break;
                }
            }

            if (Application.isPlaying && isActiveAndEnabled)
                BindInput(candidate);
            return candidate as IInteractionInputSource;
        }

        private void BindInput(MonoBehaviour component)
        {
            if (ReferenceEquals(resolvedInputComponent, component))
                return;
            if (resolvedInput != null)
                resolvedInput.PresentationChanged -= InputPresentationChanged;
            resolvedInputComponent = component;
            resolvedInput = component as IInteractionInputSource;
            if (resolvedInput != null)
                resolvedInput.PresentationChanged += InputPresentationChanged;
            dirty = true;
        }

        private void InputPresentationChanged()
        {
            dirty = true;
        }

        private void Target(InteractionTarget _) => dirty = true;
        private void State(InteractionActionEvent _) => dirty = true;
        private void LateUpdate()
        {
            if (dirty || Time.unscaledTime >= nextRefresh)
                Refresh();
        }

        public string Translate(string key, string fallback) => textProvider is IInteractionTextProvider provider ? provider.Translate(key, fallback) : fallback;
        public InteractionAvailability Check(InteractionAction action)
        {
            if (!action)
                return InteractionAvailability.Unavailable(InteractionFailure.MissingTarget, "");
            if (Actor.CommandAdapter && Actor.CommandAdapter.IsRunning(action))
                return InteractionAvailability.Available;
            if (Actor.CommandAdapter)
                return Actor.CommandAdapter.Check(action);
            if (Actor.GetTicket(action).IsActive)
                return InteractionAvailability.Available;
            return Actor.Check(action);
        }

        public void Refresh()
        {
            var source = ResolveInput();
            dirty = false;
            nextRefresh = Time.unscaledTime + refreshInterval;
            var action = Actor.SelectedAction;
            if (!action)
                action = Actor.ActiveAction;
            bool running = action && (Actor.GetTicket(action).IsActive || (Actor.CommandAdapter && Actor.CommandAdapter.IsRunning(action)));
            var check = running ? InteractionAvailability.Available : Check(action);
            var target = action ? action.Target : Actor.CurrentTarget;
            string label = action ? Translate(action.LabelKey, action.GetLabel(Actor)) : "";
            float progress = action ? (Actor.CommandAdapter ? Actor.CommandAdapter.GetProgress(action) : action.GetProgress(Actor)) : 0;
            string targetName = Actor.CurrentPoint && !string.IsNullOrEmpty(Actor.CurrentPoint.displayName) ? Actor.CurrentPoint.displayName : target ? target.DisplayName : "";
            var next = new InteractionPrompt(target, action, targetName, label, Translate(check.TextKey, check.Reason), source?.BindingLabel ?? "", check.IsAvailable, running, progress, source?.BindingIcon);
            if (!Prompt.Same(next))
            {
                Prompt = next;
                changed.Send(next, this);
            }
        }

        public bool Activate(InteractionAction action = null)
        {
            if (!action)
                action = Prompt.Action;
            if (!action)
                return false;
            if (Actor.CommandAdapter)
                return Actor.CommandAdapter.Press(action, ToggleInsteadOfHold);
            if (Actor.GetTicket(action).IsActive)
                return Actor.Cancel(action);
            return Actor.Press(action, ToggleInsteadOfHold);
        }

        public void Release(InteractionAction action)
        {
            if (action)
                Actor.Release(action, ToggleInsteadOfHold);
        }

        /// <summary>Route UI analog input through the same local/server command path as player input.</summary>
        public bool SetValue(InteractionAction action, float value)
        {
            if (!action || float.IsNaN(value) || float.IsInfinity(value))
                return false;
            return Actor.CommandAdapter ? Actor.CommandAdapter.SetValue(action, value) : Actor.SetValue(action, value);
        }
    }
}
