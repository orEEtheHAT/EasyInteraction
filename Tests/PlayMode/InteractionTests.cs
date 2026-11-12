using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace EasyInteraction.Tests
{
    public sealed class InteractionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private float oldStep;
        private InteractionActor actor;
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        private GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(go);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }

        private EventAction ActionAt(Vector3 position, string name = "Target") => Cube(name, position, Vector3.one * .3f).AddComponent<EventAction>();
        private GrabAction Crate()
        {
            var go = Cube("Crate", new Vector3(0, 10, 2.5f), Vector3.one * .5f);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 2;
            body.useGravity = true;
            body.linearDamping = .37f;
            body.angularDamping = .51f;
            return go.AddComponent<GrabAction>();
        }

        [SetUp]
        public void SetUp()
        {
            oldStep = Time.fixedDeltaTime;
            actor = New("Actor").AddComponent<InteractionActor>();
            actor.transform.position = new Vector3(0, 10, 0);
            actor.Range = 8;
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = oldStep;
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [TestCase(InteractionSelectionMode.Raycast)]
        [TestCase(InteractionSelectionMode.Focus)]
        public void WallImmediatelyClearsTargetAndRejectsStaleAction(InteractionSelectionMode mode)
        {
            actor.SelectionMode = mode;
            var action = ActionAt(new Vector3(0, 10, 3));
            Physics.SyncTransforms();
            Assert.AreEqual(action.Target, actor.RefreshTarget());
            int changed = 0;
            actor.TargetChanged += _ => changed++;
            Cube("Wall", new Vector3(0, 10, 1.5f), new Vector3(4, 4, .2f));
            Physics.SyncTransforms();
            Assert.IsFalse(actor.TryExecute(action));
            Assert.IsNull(actor.CurrentTarget);
            Assert.AreEqual(1, changed);
        }

        [TestCase(InteractionSelectionMode.Raycast)]
        [TestCase(InteractionSelectionMode.Focus)]
        public void DoorExecutesAndWorldStateSurvivesLossOfFocus(InteractionSelectionMode mode)
        {
            actor.SelectionMode = mode;
            var go = Cube("Door", new Vector3(0, 10, 3), Vector3.one);
            var door = go.AddComponent<DoorAction>();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(door));
            Assert.IsTrue(door.IsOpen);
            actor.transform.rotation = Quaternion.Euler(0, 180, 0);
            actor.RefreshTarget();
            Assert.IsNull(actor.CurrentTarget);
            Assert.IsTrue(door.IsOpen);
            door.SetOpen(false);
            Assert.IsFalse(door.IsOpen);
        }

        [Test]
        public void MultipleActionsReasonsAndOrderedEventsArePublic()
        {
            var first = ActionAt(new Vector3(0, 10, 3));
            var second = first.gameObject.AddComponent<EventAction>();
            second.Available = false;
            second.UnavailableReason = "Need a key.";
            Physics.SyncTransforms();
            actor.RefreshTarget();
            Assert.AreEqual(2, actor.Actions.Count);
            Assert.AreSame(first, actor.Actions[0]);
            Assert.AreEqual("Need a key.", actor.Check(second).Reason);
            Assert.IsFalse(actor.TryExecute(second));
            var states = new List<InteractionActionState>();
            actor.ActionStateChanged += e => states.Add(e.State);
            int calls = 0;
            second.OnExecuteEvent.AddListener(() => calls++);
            second.Available = true;
            Assert.IsTrue(actor.TryExecute(second));
            Assert.AreEqual(1, calls);
            CollectionAssert.AreEqual(new[] { InteractionActionState.Started, InteractionActionState.Committed, InteractionActionState.Completed }, states);
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void FocusHysteresisResistsJitterButAllowsDeliberateSwitch()
        {
            actor.SelectionMode = InteractionSelectionMode.Focus;
            var left = ActionAt(new Vector3(-.45f, 10, 3), "Left");
            var right = ActionAt(new Vector3(.45f, 10, 3), "Right");
            actor.transform.rotation = Quaternion.Euler(0, -7, 0);
            Physics.SyncTransforms();
            Assert.AreEqual(left.Target, actor.RefreshTarget());
            actor.transform.rotation = Quaternion.Euler(0, 1, 0);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(left.Target, actor.RefreshTarget());
            actor.transform.rotation = Quaternion.Euler(0, 12, 0);
            Assert.AreEqual(right.Target, actor.RefreshTarget());
        }

        [Test]
        public void FocusIgnoresOutOfRangeBehindAndOutsideCone()
        {
            actor.SelectionMode = InteractionSelectionMode.Focus;
            actor.Range = 3;
            ActionAt(new Vector3(0, 10, 4));
            ActionAt(new Vector3(0, 10, -2));
            ActionAt(new Vector3(2, 10, 1));
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget());
        }

        [Test]
        public void FocusFindsVisibleRunnerUpWhenBestIsOccluded()
        {
            actor.SelectionMode = InteractionSelectionMode.Focus;
            ActionAt(new Vector3(0, 10, 3));
            var visible = ActionAt(new Vector3(1.2f, 10, 3));
            Cube("Narrow wall", new Vector3(0, 10, 1.5f), new Vector3(.5f, 2, .1f));
            Physics.SyncTransforms();
            Assert.AreEqual(visible.Target, actor.RefreshTarget());
            Assert.GreaterOrEqual(actor.LastVisibilityChecks, 2);
        }

        [Test]
        public void FocusOverflowDoesNotDropBestCandidate()
        {
            actor.SelectionMode = InteractionSelectionMode.Focus;
            for (int i = 0; i < 140; i++)
                ActionAt(new Vector3(2 + (i % 10) * .02f, 10 + (i / 10) * .02f, 4));
            var best = ActionAt(new Vector3(0, 10, 2));
            Physics.SyncTransforms();
            Assert.AreEqual(best.Target, actor.RefreshTarget());
            Assert.Greater(actor.OverlapCapacity, 140);
            Assert.Greater(actor.LastCandidateCount, 130);
            Assert.AreEqual(best.Target, actor.RefreshTarget());
            Assert.AreEqual(2, actor.LastPhysicsQueries);
        }

        [Test]
        public void RayBufferOverflowStillIgnoresAllPlayerColliders()
        {
            for (int i = 0; i < 40; i++)
            {
                var child = Cube("Self", new Vector3(0, 10, .3f + i * .04f), new Vector3(.1f, .1f, .01f));
                child.transform.SetParent(actor.transform, true);
            }

            var action = ActionAt(new Vector3(0, 10, 3));
            Physics.SyncTransforms();
            Assert.AreEqual(action.Target, actor.RefreshTarget());
        }

        [TestCase(InteractionSelectionMode.Raycast)]
        [TestCase(InteractionSelectionMode.Focus)]
        public void GrabReleaseRestoresExactlyTheSettingsItChanges(InteractionSelectionMode mode)
        {
            actor.SelectionMode = mode;
            var grab = Crate();
            var body = grab.Body;
            body.interpolation = RigidbodyInterpolation.Extrapolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.Interact());
            Assert.IsTrue(grab.IsHeld);
            Assert.IsFalse(actor.TryExecute(grab));
            Assert.IsTrue(actor.Interact());
            Assert.IsFalse(grab.IsHeld);
            Assert.IsTrue(body.useGravity);
            Assert.AreEqual(RigidbodyInterpolation.Extrapolate, body.interpolation);
            Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, body.collisionDetectionMode);
            Assert.AreEqual(.37f, body.linearDamping);
            Assert.AreEqual(.51f, body.angularDamping);
            actor.Cancel();
            Assert.IsNull(actor.ActiveAction);
        }

        [TestCase("actor")]
        [TestCase("target")]
        [TestCase("action")]
        [TestCase("input")]
        public void DisablingParticipantRestoresGrab(string participant)
        {
            var grab = Crate();
            var input = actor.gameObject.AddComponent<InteractionInput>();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            if (participant == "actor")
                actor.enabled = false;
            if (participant == "target")
                grab.Target.enabled = false;
            if (participant == "action")
                grab.enabled = false;
            if (participant == "input")
                input.enabled = false;
            Assert.IsFalse(grab.IsHeld);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsTrue(grab.Body.useGravity);
        }

        [Test]
        public void DestroyingHeldObjectClearsActorAndNotifiesOnce()
        {
            var grab = Crate();
            int cancelled = 0;
            actor.ActionStateChanged += e =>
            {
                if (e.State == InteractionActionState.Cancelled)
                    cancelled++;
            };
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            Object.DestroyImmediate(grab.gameObject);
            actor.RefreshTarget();
            actor.Cancel();
            Assert.IsNull(actor.ActiveAction);
            Assert.IsNull(actor.CurrentTarget);
            Assert.AreEqual(1, cancelled);
        }

        [Test]
        public void TwoPlayersCannotOwnOneGrab()
        {
            var grab = Crate();
            var other = New("Other actor").AddComponent<InteractionActor>();
            other.transform.position = actor.transform.position;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            Assert.IsFalse(other.TryExecute(grab));
            Assert.AreSame(actor, grab.Owner);
            actor.Cancel();
            Assert.IsTrue(other.TryExecute(grab));
            other.Cancel();
        }

        [Test]
        public void GrabRejectsMassRangeAndKinematicBodyWithReasons()
        {
            var grab = Crate();
            Physics.SyncTransforms();
            actor.RefreshTarget();
            grab.Body.mass = 100;
            StringAssert.Contains("heavy", actor.Check(grab).Reason);
            grab.Body.mass = 1;
            grab.GrabRange = 1;
            StringAssert.Contains("closer", actor.Check(grab).Reason);
            grab.GrabRange = 3;
            grab.Body.isKinematic = true;
            Assert.AreEqual(InteractionFailure.InvalidConfiguration, actor.Check(grab).Code);
            StringAssert.Contains("Kinematic", actor.Check(grab).Diagnostic);
        }

        [Test]
        public void CancellingFromStartedCallbackCannotLeaveOwnershipBehind()
        {
            var grab = Crate();
            actor.ActionStateChanged += e =>
            {
                if (e.State == InteractionActionState.Started)
                    actor.Cancel();
            };
            Physics.SyncTransforms();
            actor.TryExecute(grab);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsNull(grab.Owner);
            Assert.IsTrue(grab.Body.useGravity);
        }

        [Test]
        public void DefaultInputExecutesEAndCancelsQ()
        {
            var oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var grab = Crate();
                actor.gameObject.AddComponent<InteractionInput>();
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                InputSystem.Update();
                Assert.IsTrue(grab.IsHeld);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
                InputSystem.Update();
                Assert.IsFalse(grab.IsHeld);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = oldBackground;
#if UNITY_EDITOR
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
#endif
            }
        }

        [UnityTest]
        public IEnumerator PhysicalHoldCollidesWithWallAndRestoresAfterRelease()
        {
            var grab = Crate();
            grab.HoldDistance = 4;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            Cube("Barrier", new Vector3(0, 10, 3.2f), new Vector3(6, 6, .2f));
            Physics.SyncTransforms();
            for (int i = 0; i < 70; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(grab.Body.position.z, 3.1f, "Dynamic body must not pass through the wall.");
            Assert.Greater(grab.Body.position.z, 2.6f);
            Assert.IsTrue(grab.IsHeld);
            actor.Cancel();
            Assert.IsTrue(grab.Body.useGravity);
        }

        [UnityTest]
        public IEnumerator HoldConvergesAtDifferentPhysicsSteps()
        {
            foreach (float step in new[]
            {
                1f / 30f,
                .02f,
                1f / 100f
            }

            )
            {
                Time.fixedDeltaTime = step;
                var grab = Crate();
                grab.HoldDistance = 2;
                Physics.SyncTransforms();
                Assert.IsTrue(actor.TryExecute(grab));
                int frames = Mathf.CeilToInt(1.2f / step);
                for (int i = 0; i < frames; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * 2), .08f, "step=" + step);
                actor.Cancel();
                Object.DestroyImmediate(grab.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator MovingPlayerTooFarReleasesInsteadOfTeleportingBody()
        {
            var grab = Crate();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecute(grab));
            actor.transform.position += Vector3.right * 20;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(grab.IsHeld);
            Assert.IsTrue(grab.Body.useGravity);
        }
    }
}
