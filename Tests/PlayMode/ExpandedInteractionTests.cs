using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class CountedInteractionCost : InteractionCost
    {
        public int available = 10, reserves, commits, refunds;
        public bool rejectReserve, valid = true;
        private readonly HashSet<InteractionTicket> held = new HashSet<InteractionTicket>();
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action) => available > 0 ? InteractionAvailability.Available : InteractionAvailability.Unavailable("No charge.");
        public override bool Reserve(InteractionTicket ticket)
        {
            if (rejectReserve || available <= 0)
                return false;
            available--;
            reserves++;
            held.Add(ticket);
            return true;
        }

        public override bool IsValid(InteractionTicket ticket) => valid && held.Contains(ticket);
        public override void Commit(InteractionTicket ticket)
        {
            if (held.Remove(ticket))
                commits++;
        }

        public override void Refund(InteractionTicket ticket)
        {
            if (held.Remove(ticket))
            {
                refunds++;
                available++;
            }
        }
    }

    public sealed class ExpandedInteractionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private GameObject New(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            objects.Add(go);
            return go;
        }

        private GameObject Cube(Vector3 position, float size = .4f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            objects.Add(go);
            return go;
        }

        private EventAction Event(Vector3 position)
        {
            var action = Cube(position).AddComponent<EventAction>();
            Physics.SyncTransforms();
            return action;
        }

        private InteractionActor Actor(Vector3 position)
        {
            var result = New("Actor", position).AddComponent<InteractionActor>();
            result.AutomaticUpdate = false;
            result.Range = 6;
            return result;
        }

        private GrabAction Grab()
        {
            var go = Cube(new Vector3(0, 50, 2.5f));
            go.AddComponent<Rigidbody>().useGravity = true;
            var grab = go.AddComponent<GrabAction>();
            Physics.SyncTransforms();
            return grab;
        }

        [SetUp]
        public void SetUp()
        {
            actor = Actor(new Vector3(0, 50, 0));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void SphereCastAssistsSmallTargetsOutsideTheThinRay()
        {
            var action = Event(new Vector3(.25f, 50, 3));
            Assert.IsNull(actor.RefreshTarget());
            actor.SelectionMode = InteractionSelectionMode.SphereCast;
            actor.CastRadius = .15f;
            Assert.AreSame(action.Target, actor.RefreshTarget());
        }

        [Test]
        public void CursorRayUsesAssignedCameraAndPointer()
        {
            var camera = actor.gameObject.AddComponent<Camera>();
            actor.CursorCamera = camera;
            actor.SelectionMode = InteractionSelectionMode.Cursor;
            actor.SetPointerPosition(new Vector2(Screen.width * .5f, Screen.height * .5f));
            var action = Event(new Vector3(0, 50, 3));
            Assert.AreSame(action.Target, actor.RefreshTarget());
        }

        [Test]
        public void NearestIncludesTargetsBehindTheActor()
        {
            Event(new Vector3(0, 50, 3));
            var nearest = Event(new Vector3(0, 50, -1));
            actor.SelectionMode = InteractionSelectionMode.Nearest;
            Assert.AreSame(nearest.Target, actor.RefreshTarget());
        }

        [Test]
        public void ContactUsesItsOwnSmallRadius()
        {
            var close = Event(new Vector3(.3f, 50, 0));
            Event(new Vector3(0, 50, 1));
            actor.SelectionMode = InteractionSelectionMode.Contact;
            actor.ContactRadius = .5f;
            Assert.AreSame(close.Target, actor.RefreshTarget());
            close.transform.position += Vector3.right;
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget());
        }

        [Test]
        public void ExplicitTargetAndNpcExecutionKeepRangeAndWallChecks()
        {
            var action = Event(new Vector3(2, 50, 0));
            Assert.IsTrue(actor.SelectTarget(action.Target));
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Cube(new Vector3(1, 50, 0), .7f);
            Physics.SyncTransforms();
            Assert.IsFalse(actor.TryExecuteDirect(action));
            Assert.AreEqual(InteractionFailure.Occluded, actor.LastFailure.Code);
        }

        [Test]
        public void MultipointFocusFindsTheVisiblePartOfAnOccludedObject()
        {
            var action = Event(new Vector3(0, 50, 3));
            var side = Cube(new Vector3(1.2f, 50, 3));
            side.transform.SetParent(action.transform, true);
            action.Target.ExtraFocusPoints = new[]
            {
                side.transform
            };
            action.Target.Refresh();
            Cube(new Vector3(0, 50, 1.5f), .7f);
            actor.SelectionMode = InteractionSelectionMode.Focus;
            Physics.SyncTransforms();
            Assert.AreSame(action.Target, actor.RefreshTarget());
            Assert.Greater(actor.LastVisibilityChecks, 1);
        }

        [Test]
        public void ExplicitPriorityAndCyclingAreStableAcrossRefreshes()
        {
            var first = Event(new Vector3(-.5f, 50, 3));
            var second = Event(new Vector3(.5f, 50, 3));
            first.Target.FocusPriority = 5;
            actor.SelectionMode = InteractionSelectionMode.Focus;
            Assert.AreSame(first.Target, actor.RefreshTarget());
            Assert.IsTrue(actor.CycleTarget());
            Assert.AreSame(second.Target, actor.RefreshTarget());
            actor.ReleaseTargetLock();
            Assert.AreSame(first.Target, actor.RefreshTarget());
        }

        [Test]
        public void ObjectPartsExposeOnlyTheirAssignedActions()
        {
            var root = New("Object", new Vector3(0, 50, 3));
            var first = root.AddComponent<EventAction>();
            first.ActionId = "first";
            var second = root.AddComponent<EventAction>();
            second.ActionId = "second";
            var collider = Cube(new Vector3(0, 50, 3));
            collider.transform.SetParent(root.transform, true);
            var point = collider.AddComponent<InteractionPoint>();
            point.actionIds = new[]
            {
                "second"
            };
            first.Target.Refresh();
            Physics.SyncTransforms();
            actor.RefreshTarget();
            Assert.AreSame(second, actor.SelectedAction);
            Assert.IsFalse(actor.TryExecute(first));
            Assert.IsTrue(actor.TryExecute(second));
        }

        [Test]
        public void TimedActionPausesResumesAndCommitsOnce()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 2;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(actor.TryBegin(action, out var ticket));
            actor.Tick(.5f);
            Assert.AreEqual(.25f, actor.GetProgress(action), .001f);
            Assert.IsTrue(actor.Pause(action));
            actor.Tick(10);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(actor.Resume(action));
            actor.Tick(1.5f);
            Assert.AreEqual(1, calls);
            Assert.IsFalse(ticket.IsActive);
            Assert.IsFalse(ticket.Complete());
            Assert.IsFalse(ticket.Cancel());
        }

        [Test]
        public void AnimationTicketCannotFinishANewerRun()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.AnimationEvent;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(actor.TryBegin(action, out var old));
            Assert.IsTrue(old.Cancel());
            Assert.IsTrue(actor.TryBegin(action, out var current));
            Assert.IsFalse(old.Commit());
            Assert.AreEqual(0, calls);
            Assert.IsTrue(current.Commit());
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ConfirmationCanBeReleasedOrLostBeforeAnyEffect()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.InputGesture = InteractionInputGesture.HoldToConfirm;
            action.ConfirmationSeconds = .5f;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(actor.Press(action));
            actor.Tick(.2f);
            actor.Release(action);
            Assert.AreEqual(0, calls);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsTrue(actor.Press(action));
            actor.transform.rotation = Quaternion.Euler(0, 180, 0);
            actor.Tick(.6f);
            Assert.AreEqual(0, calls);
            Assert.IsNull(actor.ActiveAction);
            actor.transform.rotation = Quaternion.identity;
            Assert.IsTrue(actor.Press(action));
            actor.Tick(.6f);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void AccessibilityToggleKeepsConfirmationTimeWithoutRequiringHolding()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.InputGesture = InteractionInputGesture.HoldToConfirm;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(actor.Press(action, true));
            actor.Release(action, true);
            Assert.AreEqual(0, calls);
            actor.Tick(.6f);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ConditionsCanDifferBetweenStartAndContinuation()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 2;
            var condition = action.gameObject.AddComponent<FlagCondition>();
            condition.Phase = InteractionConditionPhase.Start;
            Assert.IsTrue(actor.TryExecute(action));
            condition.Allowed = false;
            actor.Tick(.5f);
            Assert.IsNotNull(actor.ActiveAction);
            actor.Cancel();
            condition.Allowed = true;
            condition.Phase = InteractionConditionPhase.Both;
            Assert.IsTrue(actor.TryExecute(action));
            condition.Allowed = false;
            actor.Tick(.1f);
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void AnyConditionAndCycleDiagnosticsAreExplicit()
        {
            var action = Event(new Vector3(0, 50, 3));
            var a = action.gameObject.AddComponent<FlagCondition>();
            var b = action.gameObject.AddComponent<FlagCondition>();
            a.Allowed = false;
            b.Allowed = true;
            action.ConditionMatch = InteractionConditionMatch.Any;
            actor.RefreshTarget();
            Assert.IsTrue(actor.Check(action).IsAvailable);
            var group = action.gameObject.AddComponent<ConditionGroup>();
            group.conditions = new InteractionCondition[]
            {
                group
            };
            action.Conditions = new InteractionCondition[]
            {
                group
            };
            Assert.AreEqual(InteractionFailure.InvalidConfiguration, actor.Check(action).Code);
        }

        [Test]
        public void IndependentChannelsAllowHoldingWhileOpeningADoor()
        {
            var grab = Grab();
            var door = Cube(new Vector3(1, 50, 2)).AddComponent<DoorAction>();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            Assert.IsTrue(actor.TryExecuteDirect(door));
            Assert.IsTrue(door.IsOpen);
            Assert.IsTrue(grab.IsHeld);
            var action = door.gameObject.AddComponent<EventAction>();
            action.Channel = "carry";
            Assert.IsFalse(actor.TryExecuteDirect(action));
            Assert.AreEqual(InteractionFailure.Busy, actor.LastFailure.Code);
        }

        [Test]
        public void TargetReservationBlocksConflictingActionsAcrossActors()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.Timed;
            action.UseScope = InteractionUseScope.Target;
            var otherAction = action.gameObject.AddComponent<EventAction>();
            var other = Actor(actor.transform.position);
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.IsFalse(other.TryExecuteDirect(otherAction));
            actor.Cancel();
            Assert.IsTrue(other.TryExecuteDirect(otherAction));
        }

        [Test]
        public void CostsReserveCommitRefundAndDoNotCollideBetweenActorTickets()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 1;
            action.MaxParticipants = 2;
            var cost = action.gameObject.AddComponent<CountedInteractionCost>();
            var other = Actor(actor.transform.position);
            Assert.IsTrue(actor.TryBegin(action, out var one));
            Assert.IsTrue(other.TryBegin(action, out var two));
            Assert.AreNotEqual(one, two);
            actor.Tick(1);
            other.Cancel();
            Assert.AreEqual(2, cost.reserves);
            Assert.AreEqual(1, cost.commits);
            Assert.AreEqual(1, cost.refunds);
            Assert.AreEqual(9, cost.available);
        }

        [Test]
        public void PartialReservationFailureRollsBackWithoutExecuting()
        {
            var action = Event(new Vector3(0, 50, 3));
            var first = action.gameObject.AddComponent<CountedInteractionCost>();
            var second = action.gameObject.AddComponent<CountedInteractionCost>();
            second.rejectReserve = true;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsFalse(actor.TryExecute(action));
            Assert.AreEqual(0, calls);
            Assert.AreEqual(1, first.refunds);
            Assert.AreEqual(10, first.available);
            Assert.AreEqual(0, action.OwnerCount);
            Assert.AreEqual(0, action.Target.ParticipantCount);
        }

        [Test]
        public void InvalidReservationCancelsBeforeTimedEffect()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.Timed;
            var cost = action.gameObject.AddComponent<CountedInteractionCost>();
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(actor.TryExecute(action));
            cost.valid = false;
            actor.Tick(5);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(1, cost.refunds);
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void ReentrantCancellationPreservesCSharpAndUnityEventOrder()
        {
            var grab = Grab();
            var events = new List<string>();
            actor.ActionStateChanged += e =>
            {
                events.Add("C# " + e.State);
                if (e.State == InteractionActionState.Started)
                    actor.Cancel();
            };
            grab.OnStarted.AddListener(() => events.Add("Unity Started"));
            grab.OnCancelled.AddListener(() => events.Add("Unity Cancelled"));
            actor.TryExecute(grab);
            CollectionAssert.AreEqual(new[] { "C# Started", "Unity Started", "C# Committed", "C# Cancelled", "Unity Cancelled" }, events);
            Assert.IsTrue(grab.Body.useGravity);
            Assert.IsNull(grab.Owner);
        }

        [Test]
        public void ThrowingObserverDoesNotStopOtherObserversOrCleanup()
        {
            var grab = Grab();
            int received = 0;
            actor.ActionStateChanged += _ => throw new InvalidOperationException("observer test");
            actor.ActionStateChanged += _ => received++;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: observer test"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: observer test"));
            actor.TryExecute(grab);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: observer test"));
            actor.Cancel();
            Assert.AreEqual(3, received);
            Assert.IsTrue(grab.Body.useGravity);
        }

        [Test]
        public void CooperativeWorkHasOneWorldResultAndSupportsParticipantLoss()
        {
            var action = Cube(new Vector3(0, 50, 3)).AddComponent<CooperativeAction>();
            action.WorkSeconds = 2;
            var other = Actor(actor.transform.position);
            int results = 0;
            action.OnWorkFinished.AddListener(() => results++);
            Physics.SyncTransforms();
            Assert.GreaterOrEqual(action.MaxParticipants, 2);
            Assert.IsTrue(actor.TryExecuteDirect(action));
            actor.Tick(1);
            Assert.AreEqual(0, action.WorkProgress);
            Assert.IsTrue(other.TryExecuteDirect(action));
            other.Cancel();
            actor.Tick(1);
            Assert.AreEqual(0, action.WorkProgress);
            Assert.IsTrue(other.TryExecuteDirect(action));
            actor.Tick(1);
            Assert.AreEqual(1, results);
            Assert.AreEqual(1, action.WorkProgress);
            Assert.AreEqual(0, action.OwnerCount);
        }

        [UnityTest]
        public IEnumerator QueueStartsTheWaitingActorAfterRelease()
        {
            var grab = Grab();
            var other = Actor(actor.transform.position);
            Assert.IsTrue(actor.TryExecute(grab));
            Assert.IsTrue(other.Enqueue(grab));
            Assert.AreEqual(1, grab.Target.QueueCount);
            actor.Cancel();
            yield return null;
            yield return null;
            Assert.AreSame(other, grab.Owner);
            Assert.AreEqual(0, grab.Target.QueueCount);
            other.Cancel();
        }

        [Test]
        public void TwoHandsAndTransferRestoreOnlyAfterLastRelease()
        {
            var grab = Grab();
            grab.MaxParticipants = 2;
            var other = Actor(actor.transform.position);
            Assert.IsTrue(actor.TryExecuteDirect(grab));
            Assert.IsTrue(other.TryExecuteDirect(grab));
            actor.Cancel();
            Assert.IsTrue(grab.IsHeld);
            Assert.IsFalse(grab.Body.useGravity);
            other.Cancel();
            Assert.IsTrue(grab.Body.useGravity);
            grab.MaxParticipants = 1;
            Assert.IsTrue(actor.TryExecuteDirect(grab));
            Assert.IsTrue(grab.TryTransferTo(other));
            Assert.AreSame(other, grab.Owner);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsFalse(grab.Body.useGravity);
            other.Complete(grab);
            Assert.IsTrue(grab.Body.useGravity);
            Assert.IsFalse(grab.IsHeld);
        }

        [Test]
        public void SocketInsertionIsCompleteBeforeItsCompletionEvent()
        {
            var grab = Grab();
            var item = grab.gameObject.AddComponent<Socketable>();
            item.category = "battery";
            var socket = Cube(new Vector3(1, 50, 2)).AddComponent<SocketAction>();
            socket.AcceptedCategories = new[]
            {
                "battery"
            };
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            bool completeSawInserted = false;
            actor.ActionStateChanged += e =>
            {
                if (e.Action == socket && e.State == InteractionActionState.Completed)
                    completeSawInserted = socket.Occupant == item;
            };
            Assert.IsTrue(actor.TryExecuteDirect(socket));
            Assert.IsTrue(completeSawInserted);
            Assert.IsFalse(grab.IsHeld);
            Assert.IsTrue(item.Body.isKinematic);
            Assert.IsTrue(socket.Remove());
            Assert.IsFalse(item.Body.isKinematic);
            Assert.IsTrue(item.Body.useGravity);
        }

        [Test]
        public void SocketDisablePreservesItemAndRestoresPhysics()
        {
            var grab = Grab();
            var item = grab.gameObject.AddComponent<Socketable>();
            var socket = Cube(new Vector3(1, 50, 2)).AddComponent<SocketAction>();
            Assert.IsTrue(socket.TryInsert(item));
            Object.DestroyImmediate(socket.gameObject);
            Assert.IsTrue(item);
            Assert.IsFalse(item.Body.isKinematic);
            Assert.IsTrue(item.Body.useGravity);
        }

        [Test]
        public void MechanicalValueClampsAndStateRestores()
        {
            var action = Cube(new Vector3(0, 50, 3)).AddComponent<MechanicalAction>();
            action.Motion = MechanicalMotion.Linear;
            action.Axis = Vector3.right;
            action.Minimum = 0;
            action.Maximum = 1;
            action.SetValue(2);
            Assert.AreEqual(1, action.Value);
            Assert.AreEqual(1, action.transform.position.x, .001f);
            var saved = action.CaptureState();
            action.SetValue(0);
            action.RestoreState(saved);
            Assert.AreEqual(1, action.Value);
        }

        [Test]
        public void WorldStateHasStableIdsAndValidatesBeforeChangingObjects()
        {
            var door = Cube(new Vector3(0, 50, 3)).AddComponent<DoorAction>();
            door.Target.PersistentId = "door-a";
            door.SetOpen(true);
            door.IsLocked = true;
            var saved = door.Target.CaptureState();
            door.SetOpen(false);
            door.IsLocked = false;
            Assert.IsTrue(door.Target.TryRestoreState(saved, out _));
            Assert.IsTrue(door.IsOpen);
            Assert.IsTrue(door.IsLocked);
            door.SetOpen(false);
            saved.actions.Add(new InteractionStateEntry { actionId = "missing", type = "missing", json = "{}" });
            Assert.IsFalse(door.Target.TryRestoreState(saved, out _));
            Assert.IsFalse(door.IsOpen);
        }

        [Test]
        public void DuplicateWorldIdsNeverResolveToAnArbitraryObject()
        {
            var first = Event(new Vector3(-1, 50, 3));
            var second = Event(new Vector3(1, 50, 3));
            first.Target.PersistentId = second.Target.PersistentId = "duplicate";
            Assert.IsNull(InteractionWorld.Find("duplicate"));
            Assert.IsTrue(InteractionWorld.HasDuplicateId("duplicate"));
            Assert.Throws<InvalidOperationException>(() => first.Target.CaptureState());
        }

        [Test]
        public void PresenterUsesLocalizedReasonsAndExposesRunningProgress()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Available = false;
            action.UnavailableReason = "Need a key.";
            var text = actor.gameObject.AddComponent<InteractionTextProvider>();
            text.SetTranslations(new[] { new InteractionTextProvider.Translation { key = "Need a key.", text = "Нужен ключ" } });
            var presenter = actor.gameObject.AddComponent<InteractionPresenter>();
            presenter.TextProvider = text;
            actor.RefreshTarget();
            presenter.Refresh();
            Assert.AreEqual("Нужен ключ", presenter.Prompt.Reason);
            action.Available = true;
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 2;
            actor.TryExecute(action);
            actor.Tick(.5f);
            presenter.Refresh();
            Assert.IsTrue(presenter.Prompt.Running);
            Assert.AreEqual(.25f, presenter.Prompt.Progress, .001f);
        }

        [Test]
        public void SelectionAndWarmedInstantExecutionDoNotAllocate()
        {
            var action = Event(new Vector3(0, 50, 3));
            actor.SelectionMode = InteractionSelectionMode.Focus;
            bool previous = Profiler.enabled;
            try
            {
                Profiler.enabled = true;
                var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                long calibration;
                using (var control = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!control.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the main-thread recorder could not start.");
                    GC.KeepAlive(new byte[512]);
                    control.Stop();
                    if (!control.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the main-thread recorder is not active.");
                    calibration = control.Count == 0 ? 0 : control.GetSample(0).Count;
                }

                if (calibration <= 0)
                    Assert.Inconclusive("GC.Alloc measurement unavailable: the recorder missed the 512-byte positive control.");
                for (int i = 0; i < 64; i++)
                {
                    actor.RefreshTarget();
                    actor.TryExecute(action);
                }

                long allocations;
                bool selected = true, executed = true;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid)
                        Assert.Inconclusive("GC.Alloc measurement unavailable: the measured recorder could not start.");
                    // No yields or assertions inside the measured main-thread window.
                    for (int i = 0; i < 128; i++)
                    {
                        selected &= actor.RefreshTarget() == action.Target;
                        executed &= actor.TryExecute(action);
                    }
                    recorder.Stop();
                    if (!recorder.Valid || !Profiler.enabled)
                        Assert.Inconclusive("GC.Alloc measurement became unavailable during selection/execution.");
                    allocations = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }

                Assert.IsTrue(selected && executed, "Every measured selection and instant action must succeed.");
                TestContext.WriteLine($"GC.Alloc: control={calibration} events; 128 selection/execution pairs={allocations} main-thread events.");
                Assert.That(allocations, Is.Zero, "GC.Alloc events in warmed selection and instant execution.");
            }
            finally
            {
                Profiler.enabled = previous;
            }
        }

        [Test]
        public void SequencePreservesPauseAndRequiresTheMatchingSignal()
        {
            var sequence = Cube(new Vector3(0, 50, 3)).AddComponent<SequenceAction>();
            sequence.Stages = new[]
            {
                new InteractionStage
                {
                    seconds = 1,
                    label = "Prepare"
                },
                new InteractionStage
                {
                    seconds = 1,
                    label = "Finish",
                    waitForSignal = true
                }
            };
            int entered = 0, finished = 0;
            sequence.Stages[1].onEntered.AddListener(() => entered++);
            sequence.Stages[1].onCompleted.AddListener(() => finished++);
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryBegin(sequence, out var ticket));
            actor.Tick(1);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(1, sequence.StageIndex);
            actor.Pause(sequence);
            actor.Tick(5);
            Assert.AreEqual(0, finished);
            actor.Resume(sequence);
            actor.Tick(1);
            Assert.IsTrue(ticket.IsActive);
            Assert.IsFalse(sequence.SignalStage(ticket, 0));
            Assert.IsTrue(sequence.SignalStage(ticket, 1));
            actor.Tick(0);
            Assert.AreEqual(1, finished);
            Assert.IsFalse(ticket.IsActive);
        }

        [Test]
        public void CancellationFromStageEntryPreventsLaterEffects()
        {
            var sequence = Cube(new Vector3(0, 50, 3)).AddComponent<SequenceAction>();
            sequence.Stages = new[]
            {
                new InteractionStage
                {
                    seconds = 0
                },
                new InteractionStage
                {
                    seconds = 0
                }
            };
            int effects = 0;
            sequence.Stages[0].onEntered.AddListener(() => actor.Cancel());
            sequence.Stages[1].onEntered.AddListener(() => effects++);
            Physics.SyncTransforms();
            actor.TryExecute(sequence);
            actor.Tick(1);
            Assert.AreEqual(0, effects);
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void TimedDoorRechecksLockAndPreservesItsOriginalIntent()
        {
            var door = Cube(new Vector3(0, 50, 3)).AddComponent<DoorAction>();
            door.Execution = InteractionExecutionMode.Timed;
            door.Duration = 1;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(door));
            door.IsLocked = true;
            actor.Tick(1);
            Assert.IsFalse(door.IsOpen);
            door.IsLocked = false;
            Assert.IsTrue(actor.TryExecuteDirect(door));
            door.SetOpen(true);
            actor.Tick(1);
            Assert.IsTrue(door.IsOpen);
        }

        [Test]
        public void FocusUsesPartActionsInsteadOfAnUnfilteredDuplicateCentre()
        {
            var root = New("Console", new Vector3(0, 50, 3));
            var first = root.AddComponent<EventAction>();
            first.ActionId = "first";
            var second = root.AddComponent<EventAction>();
            second.ActionId = "second";
            var part = Cube(root.transform.position);
            part.transform.SetParent(root.transform, true);
            var point = part.AddComponent<InteractionPoint>();
            point.actionIds = new[]
            {
                "second"
            };
            first.Target.Refresh();
            actor.SelectionMode = InteractionSelectionMode.Focus;
            Physics.SyncTransforms();
            actor.RefreshTarget();
            Assert.AreSame(second, actor.SelectedAction);
            Assert.IsFalse(actor.TryExecute(first));
        }

        [Test]
        public void ReachAndVisibilityMustBeSatisfiedAtTheSamePoint()
        {
            var action = Event(new Vector3(0, 50, 2));
            var far = New("Far focus point", new Vector3(6, 50, 2));
            action.Target.FocusPoint = action.transform;
            action.Target.ExtraFocusPoints = new[]
            {
                far.transform
            };
            actor.Range = 3;
            Cube(new Vector3(0, 50, 1), .8f);
            Physics.SyncTransforms();
            Assert.IsFalse(actor.TryExecuteDirect(action));
        }

        [Test]
        public void RaycastCanUseTheReachableSurfaceOfALargeObject()
        {
            var action = Event(new Vector3(0, 50, 5));
            action.transform.localScale = new Vector3(1, 1, 8);
            actor.Range = 3;
            Physics.SyncTransforms();
            Assert.AreSame(action.Target, actor.RefreshTarget());
            Assert.IsTrue(actor.TryExecute(action));
        }

        [Test]
        public void PausedAnimationCompletionWaitsForResume()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.Execution = InteractionExecutionMode.AnimationEvent;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            actor.TryBegin(action, out var ticket);
            actor.Pause(action);
            Assert.IsTrue(ticket.Commit());
            Assert.AreEqual(0, calls);
            Assert.IsTrue(ticket.IsActive);
            actor.Resume(action);
            Assert.AreEqual(1, calls);
            Assert.IsFalse(ticket.IsActive);
        }

        [Test]
        public void CompletionCannotSkipInputConfirmation()
        {
            var action = Event(new Vector3(0, 50, 3));
            action.InputGesture = InteractionInputGesture.HoldToConfirm;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            actor.Press(action);
            Assert.IsFalse(actor.GetTicket(action).Complete());
            Assert.AreEqual(0, calls);
            actor.Release(action);
        }

        [Test]
        public void AnimationCommitCanStartASustainedGrabWithoutCompletingIt()
        {
            var grab = Grab();
            grab.Execution = InteractionExecutionMode.AnimationEvent;
            actor.TryBegin(grab, out var ticket);
            Assert.IsFalse(grab.IsHeld);
            Assert.IsTrue(ticket.Commit());
            Assert.IsTrue(ticket.IsActive);
            Assert.IsTrue(grab.IsHeld);
            Assert.IsFalse(ticket.Commit());
            ticket.Cancel();
            Assert.IsTrue(grab.Body.useGravity);
        }
    }
}
