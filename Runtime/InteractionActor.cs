using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Easy Interaction/Interaction Actor")]
    public sealed partial class InteractionActor : MonoBehaviour
    {
        [SerializeField]
        private Transform view;
        [SerializeField]
        private Transform playerRoot;
        [SerializeField, Tooltip("Optional physical hand anchor, used by held items and XR.")]
        private Transform handAnchor;
        [SerializeField]
        private InteractionMotor motor;
        [SerializeField]
        private InteractionCommandAdapter commandAdapter;
        [SerializeField]
        private bool automaticUpdate = true;
        [SerializeField]
        private bool useUnscaledTime;
        [SerializeField]
        private string[] initialCapabilities = Array.Empty<string>();
        [SerializeField]
        private UnityEvent<InteractionTarget> onTargetChanged = new UnityEvent<InteractionTarget>();
        private readonly HashSet<string> capabilities = new HashSet<string>(StringComparer.Ordinal);
        private readonly InteractionSignal<InteractionTarget> targetChanged = new InteractionSignal<InteractionTarget>();
        private readonly InteractionSignal<InteractionActionEvent> actionChanged = new InteractionSignal<InteractionActionEvent>();
        private readonly Queue<Notification> notifications = new Queue<Notification>(16);
        private readonly Queue<FinishRequest> deferredFinish = new Queue<FinishRequest>(8);
        private Session[] sessions;
        private int mutationDepth;
        private bool notifying;
        private bool drainingFinishes;
        private ulong nextTicket;
        private InteractionAction selectedAction;
        private InteractionAction queuedAction;
        private static readonly InteractionAction[] noActions = Array.Empty<InteractionAction>();
        private enum Phase
        {
            Confirming,
            Approaching,
            Running,
            Paused
        }

        private sealed class Session
        {
            public bool Active, Executed, Confirmed, MotorStarted, RequiresSelection, StopRequested, CompleteWhenResumed, CommitWhenResumed;
            public InteractionTicket Ticket;
            public InteractionAction Action;
            public InteractionTarget Target;
            public Phase Phase, BeforePause;
            public float Elapsed, ConfirmElapsed, Progress, LastNotifiedProgress, Value;
            public Transform Approach;
            public InteractionCost[] Costs = new InteractionCost[4];
            public bool[] Reserved = new bool[4];
            public int CostCount;
        }

        private struct Notification
        {
            public bool IsTarget;
            public InteractionTarget Target;
            public InteractionActionEvent Action;
        }

        private struct FinishRequest
        {
            public InteractionTicket Ticket;
            public bool Cancel, Commit;
            public string Reason;
            public InteractionFailure Code;
        }

        public event Action<InteractionTarget> TargetChanged { add => targetChanged.Add(value); remove => targetChanged.Remove(value); }

        public event Action<InteractionActionEvent> ActionStateChanged { add => actionChanged.Add(value); remove => actionChanged.Remove(value); }

        public InteractionTarget CurrentTarget { get; private set; }
        public InteractionPoint CurrentPoint { get; private set; }
        public InteractionAction SelectedAction => selectedAction ? selectedAction : FirstAction();

        public InteractionAction ActiveAction
        {
            get
            {
                if (sessions != null)
                    for (int i = 0; i < sessions.Length; i++)
                        if (sessions[i].Active)
                            return sessions[i].Action;
                return null;
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                if (sessions != null)
                    foreach (var s in sessions)
                        if (s.Active)
                            n++;
                return n;
            }
        }

        public InteractionAction QueuedAction => queuedAction;

        public T GetActive<T>()
            where T : InteractionAction
        {
            if (sessions != null)
                foreach (var session in sessions)
                    if (session.Active && session.Action is T action)
                        return action;
            return null;
        }

        /// <summary>Find a running operation by input gesture, including actions started by UI.</summary>
        public InteractionAction GetActive(InteractionInputGesture gesture)
        {
            if (sessions != null)
                foreach (var session in sessions)
                    if (session.Active && session.Action && session.Action.InputGesture == gesture)
                        return session.Action;
            return null;
        }

        public IReadOnlyList<InteractionAction> Actions => CurrentTarget ? CurrentTarget.Actions : noActions;

        public Transform View
        {
            get
            {
                if (!view)
                    ResolveView();
                return view;
            }

            set => view = value;
        }

        public Transform PlayerRoot { get => playerRoot ? playerRoot : transform; set => playerRoot = value; }
        public Transform HandAnchor { get => handAnchor; set => handAnchor = value; }
        public InteractionMotor Motor { get => motor; set => motor = value; }
        public InteractionCommandAdapter CommandAdapter { get => commandAdapter && commandAdapter.isActiveAndEnabled ? commandAdapter : null; set => commandAdapter = value; }
        public bool AutomaticUpdate { get => automaticUpdate; set => automaticUpdate = value; }
        public InteractionAvailability LastFailure { get; private set; } = InteractionAvailability.Available;

        private void Awake()
        {
            if (!view)
                ResolveView();
            if (!motor)
                motor = GetComponent<InteractionMotor>();
            foreach (var capability in initialCapabilities)
                if (!string.IsNullOrEmpty(capability))
                    capabilities.Add(capability);
            EnsureSessions();
        }

        private void ResolveView()
        {
            var camera = GetComponentInChildren<Camera>();
            view = camera ? camera.transform : transform;
        }

        private void EnsureSessions()
        {
            if (sessions != null)
                return;
            sessions = new Session[4];
            for (int i = 0; i < sessions.Length; i++)
                sessions[i] = new Session();
        }

        private void Update()
        {
            if (automaticUpdate)
                Tick(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        private void OnDisable()
        {
            ClearQueue("Player disabled.");
            Cancel("Player disabled.");
            SetTarget(null, null);
        }

        public bool HasCapability(string capability) => !string.IsNullOrEmpty(capability) && capabilities.Contains(capability);
        public void SetCapability(string capability, bool value)
        {
            if (string.IsNullOrEmpty(capability))
                return;
            if (value)
                capabilities.Add(capability);
            else
                capabilities.Remove(capability);
        }

        private InteractionAction FirstAction()
        {
            var actions = Actions;
            for (int i = 0; i < actions.Count; i++)
                if (actions[i] && (!CurrentPoint || CurrentPoint.Allows(actions[i])))
                    return actions[i];
            return null;
        }

        public bool SelectAction(InteractionAction action)
        {
            if (!action || action.Target != CurrentTarget || (CurrentPoint && !CurrentPoint.Allows(action)))
                return false;
            selectedAction = action;
            return true;
        }

        public void CycleAction(int direction = 1)
        {
            var actions = Actions;
            int count = actions.Count;
            if (count == 0)
                return;
            int index = -1;
            for (int i = 0; i < count; i++)
                if (actions[i] == SelectedAction)
                {
                    index = i;
                    break;
                }

            for (int n = 0; n < count; n++)
            {
                index = (index + (direction < 0 ? -1 : 1) + count) % count;
                var action = actions[index];
                if (!action || (CurrentPoint && !CurrentPoint.Allows(action)))
                    continue;
                if (action.HideWhenUnavailable && !Check(action).IsAvailable)
                    continue;
                selectedAction = action;
                return;
            }
        }

        private void SetTarget(InteractionTarget target, InteractionPoint point)
        {
            if (ReferenceEquals(CurrentTarget, target) && ReferenceEquals(CurrentPoint, point))
                return;
            CurrentTarget = target;
            CurrentPoint = point;
            selectedAction = null;
            notifications.Enqueue(new Notification { IsTarget = true, Target = target });
            FlushNotifications();
        }

        public InteractionAvailability Check(InteractionAction action) => CheckInternal(action, true, false);
        public InteractionAvailability CheckDirect(InteractionAction action) => CheckInternal(action, false, false);
        private InteractionAvailability CheckInternal(InteractionAction action, bool requireSelected, bool continuing)
        {
            if (!isActiveAndEnabled)
                return Denied(InteractionFailure.Disabled, "Player is disabled.");
            if (!action || !action.isActiveAndEnabled || !action.Target.isActiveAndEnabled)
                return Denied(InteractionFailure.Disabled, "Action is disabled or missing.");
            if (requireSelected && (!CurrentTarget || action.Target != CurrentTarget))
                return Denied(InteractionFailure.MissingTarget, "Look at this object first.");
            if (requireSelected && CurrentPoint && !CurrentPoint.Allows(action))
                return Denied(InteractionFailure.Requirement, "Choose the matching part of this object.");
            if (!continuing)
            {
                if (Find(action) != null)
                    return Denied(InteractionFailure.Busy, "This action is already running.");
                if (!string.IsNullOrEmpty(action.Channel) && sessions != null)
                    foreach (var session in sessions)
                        if (session.Active && session.Action && session.Action.Channel == action.Channel)
                            return Denied(InteractionFailure.Busy, "Finish the action using this hand or channel first.");
                if (!IsValidTransfer(action))
                {
                    var claim = action.Target.CheckClaim(this, action);
                    if (!claim.IsAvailable)
                        return claim;
                }
            }

            bool checkRange = !continuing || action.RequireRangeWhileRunning;
            bool checkVisibility = (!continuing && requireVisibility) || (continuing && action.RequireVisibilityWhileRunning);
            var geometry = CheckGeometry(action.Target, action, checkRange, checkVisibility, requireSelected);
            if (!geometry.IsAvailable)
                return geometry;
            try
            {
                var configured = CheckConditions(action, continuing);
                if (!configured.IsAvailable)
                    return configured;
                if (continuing)
                    return action.GetContinuationAvailability(this);
                if (!continuing)
                {
                    var own = action.GetAvailability(this);
                    if (!own.IsAvailable)
                        return own;
                    var costs = action.Target.Costs;
                    for (int i = 0; i < costs.Count; i++)
                        if (costs[i] && costs[i].Applies(action))
                        {
                            var cost = costs[i].Check(this, action);
                            if (!cost.IsAvailable)
                                return cost;
                        }

                    if (action.ApproachPoint && !AtApproach(action, action.ApproachPoint) && (!motor || !motor.isActiveAndEnabled))
                        return InteractionAvailability.Unavailable(InteractionFailure.OutOfRange, "Move to the use position.", diagnostic: "Assign an Interaction Motor to approach automatically, or move the actor to Approach Point.");
                }

                return InteractionAvailability.Available;
            }
            catch (Exception error)
            {
                Debug.LogException(error, action);
                return Denied(InteractionFailure.InvalidConfiguration, "Action configuration failed. See Console.");
            }
        }

        private InteractionAvailability CheckConditions(InteractionAction action, bool continuing)
        {
            bool explicitList = action.Conditions.Length > 0;
            var automatic = action.Target.Conditions;
            int count = explicitList ? action.Conditions.Length : automatic.Count, checkedCount = 0;
            var failure = InteractionAvailability.Unavailable("No requirement is satisfied.");
            for (int i = 0; i < count; i++)
            {
                var condition = explicitList ? action.Conditions[i] : automatic[i];
                if (!condition || (!explicitList && !condition.Standalone) || !condition.Applies(continuing))
                    continue;
                var result = condition.Check(this, action);
                checkedCount++;
                if (result.Code == InteractionFailure.InvalidConfiguration)
                    return result;
                if (action.ConditionMatch == InteractionConditionMatch.All && !result.IsAvailable)
                    return result;
                if (action.ConditionMatch == InteractionConditionMatch.Any && result.IsAvailable)
                    return result;
                if (!result.IsAvailable)
                    failure = result;
            }

            return action.ConditionMatch == InteractionConditionMatch.All || checkedCount == 0 ? InteractionAvailability.Available : failure;
        }

        private static InteractionAvailability Denied(InteractionFailure code, string reason) => InteractionAvailability.Unavailable(code, reason);
        public bool TryExecute(InteractionAction action) => Begin(action, true, false, out _);
        public bool TryExecuteDirect(InteractionAction action) => Begin(action, false, false, out _);
        public bool TryExecute(InteractionTarget target, string actionId)
        {
            if (!target)
                return false;
            var action = target.FindAction(actionId);
            return action && TryExecuteDirect(action);
        }

        public bool TryBegin(InteractionAction action, out InteractionTicket ticket) => Begin(action, false, false, out ticket);
        public bool TryBeginInput(InteractionAction action, out InteractionTicket ticket) => Begin(action, false, action && action.InputGesture == InteractionInputGesture.HoldToConfirm, out ticket);
        private bool Begin(InteractionAction action, bool selected, bool confirming, out InteractionTicket ticket)
        {
            ticket = default;
            if (mutationDepth > 0)
                return false;
            mutationDepth++;
            try
            {
                if (selected)
                    RefreshTarget();
                var check = CheckInternal(action, selected, false);
                if (!check.IsAvailable)
                {
                    Fail(action, check);
                    return false;
                }

                EnsureSessions();
                Session session = null;
                foreach (var item in sessions)
                    if (!item.Active)
                    {
                        session = item;
                        break;
                    }

                if (session == null)
                {
                    int previous = sessions.Length;
                    Array.Resize(ref sessions, previous * 2);
                    for (int i = previous; i < sessions.Length; i++)
                        sessions[i] = new Session();
                    session = sessions[previous];
                }

                ticket = new InteractionTicket(this, action, ++nextTicket);
                session.Ticket = ticket;
                session.Action = action;
                session.Target = action.Target;
                session.Active = true;
                session.Executed = session.Confirmed = session.MotorStarted = session.StopRequested = session.CompleteWhenResumed = session.CommitWhenResumed = false;
                session.Elapsed = session.ConfirmElapsed = session.Progress = session.Value = 0;
                session.LastNotifiedProgress = -1;
                session.CostCount = 0;
                session.Phase = confirming ? Phase.Confirming : Phase.Running;
                session.RequiresSelection = selected;
                session.Approach = action.Target.Acquire(this, action);
                if (!ReserveCosts(session))
                {
                    Finish(session, true, "Resources are no longer available.", InteractionFailure.InsufficientResources);
                    return false;
                }

                if (confirming)
                    Emit(session, InteractionActionState.Confirming);
                else
                    StartSession(session);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, action);
                var session = Find(ticket);
                if (session != null)
                    Finish(session, true, "Action failed.", InteractionFailure.Failed);
                Fail(action, Denied(InteractionFailure.Failed, "Action failed. See Console."));
                return false;
            }
            finally
            {
                EndMutation();
            }
        }

        private bool ReserveCosts(Session session)
        {
            var costs = session.Target.Costs;
            if (session.Costs.Length < costs.Count)
            {
                Array.Resize(ref session.Costs, Mathf.NextPowerOfTwo(costs.Count));
                Array.Resize(ref session.Reserved, session.Costs.Length);
            }

            for (int i = 0; i < costs.Count; i++)
            {
                var cost = costs[i];
                if (!cost || !cost.Applies(session.Action))
                    continue;
                int at = session.CostCount++;
                session.Costs[at] = cost;
                session.Reserved[at] = false;
                if (!cost.Reserve(session.Ticket))
                    return false;
                session.Reserved[at] = true;
            }

            return true;
        }

        private void StartSession(Session session)
        {
            if (session.StopRequested)
                return;
            session.Confirmed = true;
            if (session.Approach && !AtApproach(session.Action, session.Approach))
            {
                if (!motor || !motor.isActiveAndEnabled)
                {
                    Finish(session, true, "Move to the use position.", InteractionFailure.OutOfRange);
                    return;
                }

                foreach (var other in sessions)
                    if (other != session && other.Active && other.MotorStarted)
                    {
                        Finish(session, true, "Another action is moving this actor.", InteractionFailure.Busy);
                        return;
                    }

                motor.Begin(session.Approach);
                session.MotorStarted = true;
                session.Phase = Phase.Approaching;
                return;
            }

            session.Phase = Phase.Running;
            session.Elapsed = 0;
            session.Action.Prepare(this);
            if (session.StopRequested)
                return;
            Emit(session, InteractionActionState.Started);
            if (session.Action.CostTiming == InteractionCostTiming.OnStart)
                CommitCosts(session);
            if (session.Action.Execution == InteractionExecutionMode.Automatic)
                Execute(session);
        }

        private void Execute(Session session)
        {
            if (!session.Active || session.Executed || session.StopRequested)
                return;
            if (!ReservationsValid(session))
            {
                Finish(session, true, "Reserved resources are no longer available.", InteractionFailure.InsufficientResources);
                return;
            }

            var available = session.Action.GetAvailability(this);
            if (!available.IsAvailable)
            {
                Finish(session, true, available.Reason, available.Code);
                return;
            }

            session.Executed = true;
            // Reservations remain live during execution. Cancellation/failure refunds unpaid costs.
            bool sustained = session.Action.Execute(this);
            if (session.StopRequested)
                return;
            Emit(session, InteractionActionState.Committed);
            if (!sustained)
                Finish(session, false, "", InteractionFailure.None);
        }

        private void CommitCosts(Session session)
        {
            for (int i = 0; i < session.CostCount; i++)
                if (session.Reserved[i])
                {
                    if (!session.Costs[i])
                        throw new InvalidOperationException("A reserved interaction cost was destroyed.");
                    session.Costs[i].Commit(session.Ticket);
                    session.Reserved[i] = false;
                }
        }

        public void Tick(float deltaTime)
        {
            if (!isActiveAndEnabled || mutationDepth > 0)
                return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            mutationDepth++;
            try
            {
                RefreshTarget();
                EnsureSessions();
                for (int i = 0; i < sessions.Length; i++)
                {
                    var session = sessions[i];
                    if (!session.Active)
                        continue;
                    try
                    {
                        var check = CheckInternal(session.Action, false, true);
                        if (!check.IsAvailable)
                        {
                            Finish(session, true, check.Reason, check.Code);
                            continue;
                        }

                        if (!ReservationsValid(session))
                        {
                            Finish(session, true, "Reserved resources are no longer available.", InteractionFailure.InsufficientResources);
                            continue;
                        }

                        if (session.Phase == Phase.Paused)
                            continue;
                        if (session.Phase == Phase.Confirming)
                        {
                            if ((session.RequiresSelection && CurrentTarget != session.Target) || !WithinReach(session.Target) || (requireVisibility && !Visible(session.Target)))
                            {
                                Finish(session, true, "Confirmation target lost.", InteractionFailure.Interrupted);
                                continue;
                            }

                            session.ConfirmElapsed += deltaTime;
                            session.Progress = Mathf.Clamp01(session.ConfirmElapsed / session.Action.ConfirmationSeconds);
                            Progress(session);
                            if (session.Progress >= 1)
                            {
                                session.Progress = 0;
                                StartSession(session);
                            }

                            continue;
                        }

                        session.Elapsed += deltaTime;
                        if (session.Action.Timeout > 0 && session.Elapsed > session.Action.Timeout)
                        {
                            Finish(session, true, "Action timed out.", InteractionFailure.Interrupted);
                            continue;
                        }

                        if (session.Phase == Phase.Approaching)
                        {
                            if (!session.Approach || !motor || !motor.isActiveAndEnabled)
                            {
                                Finish(session, true, "Approach position is missing.", InteractionFailure.InvalidConfiguration);
                                continue;
                            }

                            motor.Tick(deltaTime);
                            if (AtApproach(session.Action, session.Approach))
                            {
                                motor.Stop();
                                session.MotorStarted = false;
                                StartSession(session);
                            }

                            continue;
                        }

                        if (!session.Executed && session.Action.Execution == InteractionExecutionMode.Timed)
                        {
                            session.Progress = Mathf.Clamp01(session.Elapsed / session.Action.Duration);
                            Progress(session);
                            if (session.Progress >= 1)
                                Execute(session);
                        }
                        else if (session.Executed)
                        {
                            session.Action.Tick(this, deltaTime);
                            if (session.Active)
                                Progress(session);
                        }
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error, session.Action);
                        Finish(session, true, "Action failed.", InteractionFailure.Failed);
                    }
                }
            }
            finally
            {
                EndMutation();
            }
        }

        private bool AtApproach(InteractionAction action, Transform point) => !point || (Vector3.Distance(PlayerRoot.position, point.position) <= action.ApproachDistance && Quaternion.Angle(PlayerRoot.rotation, point.rotation) <= action.ApproachAngle);
        public bool Pause(InteractionAction action)
        {
            var session = Find(action);
            if (session == null || session.Phase == Phase.Paused || !action.CanPause || mutationDepth > 0)
                return false;
            mutationDepth++;
            try
            {
                session.BeforePause = session.Phase;
                session.Phase = Phase.Paused;
                if (session.MotorStarted && motor)
                    motor.Stop();
                action.Pause(this);
                Emit(session, InteractionActionState.Paused);
                return true;
            }
            finally
            {
                EndMutation();
            }
        }

        public bool Resume(InteractionAction action)
        {
            var session = Find(action);
            if (session == null || session.Phase != Phase.Paused || mutationDepth > 0)
                return false;
            var check = CheckInternal(action, false, true);
            if (!check.IsAvailable)
            {
                Cancel(action, check.Reason);
                return false;
            }

            mutationDepth++;
            try
            {
                session.Phase = session.BeforePause;
                if (session.MotorStarted && motor)
                    motor.Begin(session.Approach);
                action.Resume(this);
                Emit(session, InteractionActionState.Resumed);
                if (session.CommitWhenResumed)
                {
                    session.CommitWhenResumed = false;
                    Commit(session.Ticket);
                }

                if (session.CompleteWhenResumed)
                {
                    session.CompleteWhenResumed = false;
                    RequestFinish(session.Ticket, false, "", InteractionFailure.None);
                }

                return true;
            }
            finally
            {
                EndMutation();
            }
        }

        public bool IsRunning(InteractionAction action)
        {
            var session = Find(action);
            return session != null && session.Phase == Phase.Running;
        }

        public bool IsStopping(InteractionAction action)
        {
            var session = Find(action);
            return session == null || session.StopRequested;
        }

        public bool IsPaused(InteractionAction action)
        {
            var session = Find(action);
            return session != null && session.Phase == Phase.Paused;
        }

        public bool IsActive(InteractionTicket ticket) => Find(ticket) != null;
        public bool IsWaitingForCommit(InteractionTicket ticket)
        {
            var session = Find(ticket);
            return session != null && !session.Executed;
        }

        public InteractionTicket GetTicket(InteractionAction action) => Find(action)?.Ticket ?? default;
        public Transform GetUsePoint(InteractionAction action) => Find(action)?.Approach;
        public float GetProgress(InteractionAction action) => Find(action)?.Progress ?? 0;
        public float GetValue(InteractionAction action) => Find(action)?.Value ?? 0;
        public void SetProgress(InteractionAction action, float progress)
        {
            var session = Find(action);
            if (session != null)
                session.Progress = Mathf.Clamp01(progress);
        }

        public bool SetValue(InteractionAction action, float value)
        {
            var session = Find(action);
            if (session == null || float.IsNaN(value) || float.IsInfinity(value))
                return false;
            session.Value = Mathf.Clamp(value, -1, 1);
            action.SetValue(this, session.Value);
            return true;
        }

        public bool Interact()
        {
            RefreshTarget();
            var action = SelectedAction;
            if (action && Find(action) != null)
            {
                Cancel(action);
                return true;
            }

            if (action && Check(action).IsAvailable)
                return TryExecute(action);
            if (ActiveAction)
            {
                Cancel(ActiveAction);
                return true;
            }

            return action && TryExecute(action);
        }

        public bool Press(InteractionAction action = null, bool toggleInsteadOfHold = false)
        {
            RefreshTarget();
            if (!action)
                action = SelectedAction;
            if (CommandAdapter)
                return CommandAdapter.Press(action, toggleInsteadOfHold);
            if (!action)
            {
                if (ActiveAction)
                {
                    Cancel(ActiveAction);
                    return true;
                }

                return false;
            }

            if (Find(action) != null)
            {
                Cancel(action);
                return true;
            }

            return Begin(action, true, action.InputGesture == InteractionInputGesture.HoldToConfirm, out _);
        }

        public void Release(InteractionAction action, bool toggleInsteadOfHold = false)
        {
            if (CommandAdapter)
            {
                CommandAdapter.Release(action);
                return;
            }

            var session = Find(action);
            if (session == null || toggleInsteadOfHold)
                return;
            if (session.Phase == Phase.Confirming || action.InputGesture == InteractionInputGesture.HoldWhileActive || action.InputGesture == InteractionInputGesture.Analog)
                Cancel(action, "Input released.");
        }

        public void Cancel(string reason = "Cancelled by player.")
        {
            ClearQueue(reason);
            if (sessions == null)
                return;
            for (int i = 0; i < sessions.Length; i++)
                if (sessions[i].Active)
                    RequestFinish(sessions[i].Ticket, true, reason, InteractionFailure.Cancelled);
        }

        public bool Cancel(InteractionAction action, string reason = "Cancelled by player.")
        {
            var session = Find(action);
            return session != null && RequestFinish(session.Ticket, true, reason, InteractionFailure.Cancelled);
        }

        public bool Cancel(InteractionTicket ticket) => RequestFinish(ticket, true, "Cancelled by player.", InteractionFailure.Cancelled);
        public bool Complete(InteractionTicket ticket) => RequestFinish(ticket, false, "", InteractionFailure.None);
        public bool Complete(InteractionAction action)
        {
            var session = Find(action);
            return session != null && Complete(session.Ticket);
        }

        public bool Commit(InteractionTicket ticket)
        {
            var session = Find(ticket);
            if (session == null || session.Executed || !session.Confirmed || session.Action.Execution != InteractionExecutionMode.AnimationEvent || session.Phase == Phase.Approaching || session.Phase == Phase.Confirming)
                return false;
            if (session.Phase == Phase.Paused)
            {
                session.CommitWhenResumed = true;
                return true;
            }

            if (mutationDepth > 0)
            {
                deferredFinish.Enqueue(new FinishRequest { Ticket = ticket, Commit = true });
                return true;
            }

            mutationDepth++;
            try
            {
                var check = CheckInternal(session.Action, false, true);
                if (!check.IsAvailable)
                {
                    Finish(session, true, check.Reason, check.Code);
                    return false;
                }

                Execute(session);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, session.Action);
                Finish(session, true, "Action failed.", InteractionFailure.Failed);
                return false;
            }
            finally
            {
                EndMutation();
            }
        }

        private bool RequestFinish(InteractionTicket ticket, bool cancel, string reason, InteractionFailure code)
        {
            var session = Find(ticket);
            if (session == null)
                return false;
            if (!cancel && !session.Executed)
                return false;
            if (!cancel && (session.Phase == Phase.Confirming || session.Phase == Phase.Approaching))
                return false;
            if (!cancel && session.Phase == Phase.Paused)
            {
                if (session.BeforePause != Phase.Running)
                    return false;
                session.CompleteWhenResumed = true;
                return true;
            }

            if (mutationDepth > 0)
            {
                session.StopRequested = true;
                deferredFinish.Enqueue(new FinishRequest { Ticket = ticket, Cancel = cancel, Reason = reason, Code = code });
                return true;
            }

            mutationDepth++;
            try
            {
                session.StopRequested = false;
                if (session.Active)
                    Finish(session, cancel, reason, code);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, session.Action);
                Finish(session, true, "Action failed.", InteractionFailure.Failed);
                return false;
            }
            finally
            {
                EndMutation();
            }
        }

        private void Finish(Session session, bool cancelled, string reason, InteractionFailure code)
        {
            if (!session.Active)
                return;
            if (!cancelled)
                CommitCosts(session);
            // Clear ownership before any observer or cleanup hook. A stale ticket can never affect a later run.
            session.Active = false;
            var action = session.Action;
            if (session.Target)
                session.Target.Release(this, action);
            if (session.MotorStarted && motor)
                try
                {
                    motor.Stop();
                }
                catch (Exception error)
                {
                    Debug.LogException(error, motor);
                }

            for (int i = 0; i < session.CostCount; i++)
            {
                if (session.Reserved[i] && session.Costs[i])
                    try
                    {
                        session.Costs[i].Refund(session.Ticket);
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error, session.Costs[i]);
                    }

                session.Costs[i] = null;
                session.Reserved[i] = false;
            }

            session.CostCount = 0;
            if (action)
                try
                {
                    action.Release(this, cancelled);
                }
                catch (Exception error)
                {
                    Debug.LogException(error, action);
                }

            Emit(session, cancelled ? InteractionActionState.Cancelled : InteractionActionState.Completed, code, reason);
            session.Action = null;
            session.Target = null;
            session.Approach = null;
        }

        private Session Find(InteractionAction action)
        {
            if (!action || sessions == null)
                return null;
            for (int i = 0; i < sessions.Length; i++)
                if (sessions[i].Active && sessions[i].Action == action)
                    return sessions[i];
            return null;
        }

        private Session Find(InteractionTicket ticket)
        {
            if (ticket.Actor != this || ticket.Id == 0 || sessions == null)
                return null;
            for (int i = 0; i < sessions.Length; i++)
                if (sessions[i].Active && sessions[i].Ticket.Id == ticket.Id)
                    return sessions[i];
            return null;
        }

        private void EndMutation()
        {
            mutationDepth--;
            if (mutationDepth != 0 || drainingFinishes)
                return;
            drainingFinishes = true;
            try
            {
                while (deferredFinish.Count > 0)
                {
                    var request = deferredFinish.Dequeue();
                    if (request.Commit)
                        Commit(request.Ticket);
                    else
                        RequestFinish(request.Ticket, request.Cancel, request.Reason, request.Code);
                }
            }
            finally
            {
                drainingFinishes = false;
            }

            FlushNotifications();
        }

        private void Progress(Session session)
        {
            if (Mathf.Abs(session.Progress - session.LastNotifiedProgress) < .001f)
                return;
            if (session.Confirmed && session.Phase == Phase.Running)
                for (int i = 0; i < session.CostCount; i++)
                    if (session.Reserved[i] && session.Costs[i])
                        session.Costs[i].Advance(session.Ticket, session.Progress);
            session.LastNotifiedProgress = session.Progress;
            Emit(session, InteractionActionState.Progress);
        }

        private void Emit(Session session, InteractionActionState state, InteractionFailure code = InteractionFailure.None, string reason = "")
        {
            notifications.Enqueue(new Notification { Action = new InteractionActionEvent(session.Action, state, session.Ticket, session.Progress, code, reason) });
        }

        private void Fail(InteractionAction action, InteractionAvailability result)
        {
            LastFailure = result;
            notifications.Enqueue(new Notification { Action = new InteractionActionEvent(action, InteractionActionState.Failed, default, 0, result.Code, result.Reason) });
        }

        private void FlushNotifications()
        {
            if (notifying || mutationDepth > 0)
                return;
            notifying = true;
            try
            {
                while (notifications.Count > 0)
                {
                    var notification = notifications.Dequeue();
                    if (notification.IsTarget)
                    {
                        targetChanged.Send(notification.Target, this);
                        try
                        {
                            onTargetChanged.Invoke(notification.Target);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error, this);
                        }
                    }
                    else
                    {
                        actionChanged.Send(notification.Action, this);
                        if (notification.Action.Action)
                            try
                            {
                                notification.Action.Action.Notify(notification.Action);
                            }
                            catch (Exception error)
                            {
                                Debug.LogException(error, notification.Action.Action);
                            }
                    }
                }
            }
            finally
            {
                notifying = false;
            }
        }

        public bool Enqueue(InteractionAction action)
        {
            if (!action || !isActiveAndEnabled)
                return false;
            ClearQueue("Queue replaced.");
            queuedAction = action;
            action.Target.Enqueue(this, action);
            notifications.Enqueue(new Notification { Action = new InteractionActionEvent(action, InteractionActionState.Queued) });
            FlushNotifications();
            return true;
        }

        public void ClearQueue(string reason = "Queue cancelled.")
        {
            var previous = queuedAction;
            queuedAction = null;
            if (!previous)
                return;
            previous.Target.RemoveQueue(this);
        }

        private bool ReservationsValid(Session session)
        {
            for (int i = 0; i < session.CostCount; i++)
                if (session.Reserved[i] && (!session.Costs[i] || !session.Costs[i].IsValid(session.Ticket)))
                    return false;
            return true;
        }

        internal void EnterTransaction() => mutationDepth++;
        internal void ExitTransaction() => EndMutation();
        internal void ReleaseHeldItem(GrabAction grab)
        {
            var session = Find(grab);
            if (session != null)
                Finish(session, true, "Inserted.", InteractionFailure.Cancelled);
        }
    }
}
