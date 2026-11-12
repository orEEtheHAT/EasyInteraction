using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [RequireComponent(typeof(InteractionTarget))]
    public abstract class InteractionAction : MonoBehaviour
    {
        [SerializeField]
        private string actionId;
        [SerializeField]
        private string actionLabel = "Interact";
        [SerializeField]
        private string labelKey;
        [SerializeField]
        private Sprite icon;
        [SerializeField]
        private bool hideWhenUnavailable;
        [SerializeField, Tooltip("Actions using the same non-empty channel cannot run together on one actor.")]
        private string channel = "hands";
        [SerializeField]
        private bool useDefaultChannel = true;
        [SerializeField]
        private InteractionInputGesture inputGesture;
        [SerializeField]
        private bool useDefaultGesture = true;
        [SerializeField, Min(0.05f)]
        private float confirmationSeconds = .5f;
        [SerializeField]
        private InteractionExecutionMode execution;
        [SerializeField, Min(0.01f)]
        private float duration = 2;
        [SerializeField, Min(0), Tooltip("0 disables the timeout. Animation-driven actions should normally have a timeout.")]
        private float timeout;
        [SerializeField]
        private bool requireRangeWhileRunning;
        [SerializeField]
        private bool requireVisibilityWhileRunning;
        [SerializeField]
        private InteractionUseScope useScope;
        [SerializeField]
        private string useGroup;
        [SerializeField, Min(1)]
        private int maxParticipants = 1;
        [SerializeField]
        private bool useDefaultParticipants = true;
        [SerializeField]
        private InteractionConditionMatch conditionMatch;
        [SerializeField, Tooltip("Empty evaluates standalone conditions on this object. Explicit lists allow groups and per-action requirements.")]
        private InteractionCondition[] conditions = Array.Empty<InteractionCondition>();
        [SerializeField]
        private InteractionCostTiming costTiming;
        [SerializeField]
        private Transform approachPoint;
        [SerializeField, Min(.01f)]
        private float approachDistance = .2f;
        [SerializeField, Range(0, 180)]
        private float approachAngle = 20;
        [SerializeField]
        private UnityEvent onStarted = new UnityEvent();
        [SerializeField]
        private UnityEvent onCompleted = new UnityEvent();
        [SerializeField]
        private UnityEvent onCommitted = new UnityEvent();
        [SerializeField]
        private UnityEvent onCancelled = new UnityEvent();
        [SerializeField]
        private UnityEvent<float> onProgress = new UnityEvent<float>();
        private readonly List<InteractionActor> owners = new List<InteractionActor>(2);
        private InteractionTarget target;
        public InteractionTarget Target => target ? target : (target = GetComponent<InteractionTarget>());

        public string ActionId
        {
            get
            {
                if (string.IsNullOrEmpty(actionId))
                    actionId = GetType().Name;
                return actionId;
            }

            set => actionId = value;
        }

        public string ActionLabel { get => actionLabel; set => actionLabel = value; }
        public string LabelKey { get => labelKey; set => labelKey = value; }
        public Sprite Icon { get => icon; set => icon = value; }
        public bool HideWhenUnavailable { get => hideWhenUnavailable; set => hideWhenUnavailable = value; }
        protected virtual string DefaultChannel => "hands";

        public string Channel
        {
            get => useDefaultChannel ? DefaultChannel : channel;
            set
            {
                channel = value;
                useDefaultChannel = false;
            }
        }

        protected virtual InteractionInputGesture DefaultInputGesture => InteractionInputGesture.Press;
        protected virtual int DefaultMaxParticipants => 1;

        public InteractionInputGesture InputGesture
        {
            get => useDefaultGesture ? DefaultInputGesture : inputGesture;
            set
            {
                inputGesture = value;
                useDefaultGesture = false;
            }
        }

        public float ConfirmationSeconds { get => confirmationSeconds; set => confirmationSeconds = Mathf.Max(.05f, value); }
        public InteractionExecutionMode Execution { get => execution; set => execution = value; }
        public float Duration { get => duration; set => duration = Mathf.Max(.01f, value); }
        public float Timeout { get => timeout; set => timeout = Mathf.Max(0, value); }
        public bool RequireRangeWhileRunning { get => requireRangeWhileRunning; set => requireRangeWhileRunning = value; }
        public bool RequireVisibilityWhileRunning { get => requireVisibilityWhileRunning; set => requireVisibilityWhileRunning = value; }
        public InteractionUseScope UseScope { get => useScope; set => useScope = value; }
        public string UseGroup { get => useGroup; set => useGroup = value; }

        public int MaxParticipants
        {
            get => useDefaultParticipants ? DefaultMaxParticipants : maxParticipants;
            set
            {
                maxParticipants = Mathf.Max(1, value);
                useDefaultParticipants = false;
            }
        }

        public InteractionConditionMatch ConditionMatch { get => conditionMatch; set => conditionMatch = value; }
        public InteractionCondition[] Conditions { get => conditions; set => conditions = value ?? Array.Empty<InteractionCondition>(); }
        public InteractionCostTiming CostTiming { get => costTiming; set => costTiming = value; }
        public Transform ApproachPoint { get => approachPoint; set => approachPoint = value; }
        public float ApproachDistance { get => approachDistance; set => approachDistance = Mathf.Max(.01f, value); }
        public float ApproachAngle { get => approachAngle; set => approachAngle = Mathf.Clamp(value, 0, 180); }
        public InteractionActor Owner => owners.Count > 0 ? owners[0] : null;
        public int OwnerCount => owners.Count;

        public InteractionActor GetOwner(int index) => owners[index];
        public UnityEvent OnStarted => onStarted;
        public UnityEvent OnCompleted => onCompleted;
        public UnityEvent OnCommitted => onCommitted;
        public UnityEvent OnCancelled => onCancelled;
        public UnityEvent<float> OnProgress => onProgress;

        public virtual string GetLabel(InteractionActor actor) => actionLabel;
        public virtual float GetProgress(InteractionActor actor) => actor.GetProgress(this);
        public virtual bool CanPause => true;

        public virtual InteractionAvailability GetAvailability(InteractionActor actor) => InteractionAvailability.Available;
        public virtual InteractionAvailability GetContinuationAvailability(InteractionActor actor) => InteractionAvailability.Available;
        protected virtual void OnBegin(InteractionActor actor)
        {
        }

        /// <summary>Called once at the commit point. Return true for a sustained action, false for an instant action.</summary>
        protected abstract bool OnExecute(InteractionActor actor);
        protected virtual void OnTick(InteractionActor actor, float deltaTime)
        {
        }

        protected virtual void OnCancel(InteractionActor actor)
        {
        }

        protected virtual void OnComplete(InteractionActor actor)
        {
        }

        protected virtual void OnPause(InteractionActor actor)
        {
        }

        protected virtual void OnResume(InteractionActor actor)
        {
        }

        protected virtual void OnValueChanged(InteractionActor actor, float value)
        {
        }

        protected virtual void OnEnable() => Target.Refresh();
        protected virtual void OnDisable() => CancelOwner("Action disabled.");
        protected virtual void Reset()
        {
            if (string.IsNullOrEmpty(actionId))
                actionId = GetType().Name;
        }

        internal void Acquire(InteractionActor actor) => owners.Add(actor);
        internal bool Execute(InteractionActor actor) => OnExecute(actor);
        internal void Prepare(InteractionActor actor) => OnBegin(actor);
        internal void Tick(InteractionActor actor, float dt) => OnTick(actor, dt);
        internal void Pause(InteractionActor actor) => OnPause(actor);
        internal void Resume(InteractionActor actor) => OnResume(actor);
        internal void SetValue(InteractionActor actor, float value) => OnValueChanged(actor, value);
        internal void Release(InteractionActor actor, bool cancelled)
        {
            owners.Remove(actor);
            if (cancelled)
                OnCancel(actor);
            else
                OnComplete(actor);
        }

        internal void Notify(InteractionActionEvent e)
        {
            switch (e.State)
            {
                case InteractionActionState.Started:
                    onStarted.Invoke();
                    break;
                case InteractionActionState.Completed:
                    onCompleted.Invoke();
                    break;
                case InteractionActionState.Committed:
                    onCommitted.Invoke();
                    break;
                case InteractionActionState.Cancelled:
                    onCancelled.Invoke();
                    break;
                case InteractionActionState.Progress:
                    onProgress.Invoke(e.Progress);
                    break;
            }
        }

        protected void Complete()
        {
            if (Owner)
                Owner.Complete(this);
        }

        protected void Complete(InteractionActor actor)
        {
            if (actor)
                actor.Complete(this);
        }

        protected void Cancel(string reason) => CancelOwner(reason);
        internal void CancelOwner(string reason)
        {
            // Removal happens before callbacks. The guard prevents reentrant listeners from restarting a disabled action.
            for (int i = owners.Count - 1; i >= 0; i--)
            {
                if (i >= owners.Count)
                    continue;
                var actor = owners[i];
                if (actor)
                    actor.Cancel(this, reason);
                else
                    owners.RemoveAt(i);
            }
        }
    }
}
