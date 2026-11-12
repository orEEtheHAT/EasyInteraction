namespace EasyInteraction
{
    public enum InteractionFailure
    {
        None,
        MissingTarget,
        Disabled,
        OutOfRange,
        Occluded,
        Busy,
        Requirement,
        MissingCapability,
        WrongSide,
        TooHeavy,
        InvalidConfiguration,
        Cancelled,
        Interrupted,
        InsufficientResources,
        StaleRequest,
        Unauthorized,
        DuplicateId,
        Failed
    }

    /// <summary>Game-facing fallback text + stable code/key. Diagnostic is for developer tooling.</summary>
    public readonly struct InteractionAvailability
    {
        public bool IsAvailable { get; }
        public string Reason { get; }
        public InteractionFailure Code { get; }
        public string TextKey { get; }
        public string Diagnostic { get; }

        private InteractionAvailability(bool available, InteractionFailure code, string reason, string key, string diagnostic)
        {
            IsAvailable = available;
            Code = code;
            Reason = reason ?? "";
            TextKey = key ?? "";
            Diagnostic = diagnostic ?? "";
        }

        public static InteractionAvailability Available => new InteractionAvailability(true, InteractionFailure.None, "", "", "");

        public static InteractionAvailability Unavailable(string reason) => Unavailable(InteractionFailure.Requirement, reason);
        public static InteractionAvailability Unavailable(InteractionFailure code, string reason, string key = "", string diagnostic = "") => new InteractionAvailability(false, code, reason, key, diagnostic);
    }

    // Preserve 0.1 serialized enum values.
    public enum InteractionSelectionMode
    {
        Raycast,
        Focus,
        SphereCast,
        Cursor,
        Nearest,
        Contact,
        Explicit
    }

    public enum InteractionActionState
    {
        Started,
        Completed,
        Cancelled,
        Failed,
        Confirming,
        Progress,
        Paused,
        Resumed,
        Queued,
        Committed
    }

    public enum InteractionInputGesture
    {
        Press,
        HoldToConfirm,
        HoldWhileActive,
        Toggle,
        Analog
    }

    public enum InteractionExecutionMode
    {
        Automatic,
        Timed,
        AnimationEvent
    }

    public enum InteractionUseScope
    {
        Action,
        Target,
        Group
    }

    public enum InteractionConditionPhase
    {
        Start,
        Continue,
        Both
    }

    public enum InteractionConditionMatch
    {
        All,
        Any
    }

    public enum InteractionCostTiming
    {
        OnSuccess,
        OnStart
    }

    public readonly struct InteractionTicket : System.IEquatable<InteractionTicket>
    {
        public InteractionActor Actor { get; }
        public InteractionAction Action { get; }
        public ulong Id { get; }

        internal InteractionTicket(InteractionActor actor, InteractionAction action, ulong id)
        {
            Actor = actor;
            Action = action;
            Id = id;
        }

        public bool IsActive => Actor && Actor.IsActive(this);

        public bool Cancel() => Actor && Actor.Cancel(this);
        public bool Complete() => Actor && Actor.Complete(this);
        public bool Commit() => Actor && Actor.Commit(this);
        public bool IsWaitingForCommit => Actor && Actor.IsWaitingForCommit(this);

        public bool Equals(InteractionTicket other) => ReferenceEquals(Actor, other.Actor) && Id == other.Id;
        public override bool Equals(object value) => value is InteractionTicket other && Equals(other);
        public override int GetHashCode() => unchecked((ReferenceEquals(Actor, null) ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Actor)) * 397 ^ Id.GetHashCode());
    }

    public readonly struct InteractionActionEvent
    {
        public InteractionAction Action { get; }
        public InteractionActionState State { get; }
        public string Reason { get; }
        public InteractionFailure Code { get; }
        public float Progress { get; }
        public InteractionTicket Ticket { get; }

        public InteractionActionEvent(InteractionAction action, InteractionActionState state, string reason = "") : this(action, state, default, 0, InteractionFailure.None, reason)
        {
        }

        public InteractionActionEvent(InteractionAction action, InteractionActionState state, InteractionTicket ticket, float progress = 0, InteractionFailure code = InteractionFailure.None, string reason = "")
        {
            Action = action;
            State = state;
            Ticket = ticket;
            Progress = progress;
            Code = code;
            Reason = reason;
        }
    }
}
