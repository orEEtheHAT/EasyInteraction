using NUnit.Framework;
using UnityEngine;

namespace EasyInteraction.Tests
{
    public sealed class AuthorityTests
    {
        private GameObject player, objectRoot, wall;
        private InteractionActor actor;
        private EventAction action;
        private InteractionAuthority authority;
        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Authority actor");
            actor = player.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            player.transform.position = new Vector3(0, 70, 0);
            objectRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objectRoot.transform.position = new Vector3(0, 70, 2);
            action = objectRoot.AddComponent<EventAction>();
            action.ActionId = "use";
            action.Target.PersistentId = "authority-target";
            authority = new InteractionAuthority();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            authority.Dispose();
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(objectRoot);
            if (wall)
                Object.DestroyImmediate(wall);
        }

        private InteractionRequest Request(ulong id, InteractionRequestKind kind = InteractionRequestKind.Start, ulong operation = 0, float value = 0) => new InteractionRequest(id, "authority-target", "use", kind, operation, value);
        [Test]
        public void RetryAndReorderedRequestsCannotExecuteTwice()
        {
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(authority.Process(actor, Request(1)).Accepted);
            Assert.IsTrue(authority.Process(actor, Request(1)).Accepted);
            Assert.AreEqual(1, calls);
            Assert.IsTrue(authority.Process(actor, Request(3)).Accepted);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(InteractionFailure.StaleRequest, authority.Process(actor, Request(2)).Result.Code);
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void ReusingSequenceWithDifferentPayloadIsRejected()
        {
            Assert.IsTrue(authority.Process(actor, Request(1)).Accepted);
            Assert.IsFalse(authority.Process(actor, Request(1, InteractionRequestKind.Cancel)).Accepted);
        }

        [Test]
        public void AuthorityUsesCurrentWorldVisibilityAndPermissions()
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0, 70, 1);
            Physics.SyncTransforms();
            Assert.AreEqual(InteractionFailure.Occluded, authority.Process(actor, Request(1)).Result.Code);
            Object.DestroyImmediate(wall);
            wall = null;
            Physics.SyncTransforms();
            authority.Permission = (_, __) => false;
            Assert.AreEqual(InteractionFailure.Unauthorized, authority.Process(actor, Request(2)).Result.Code);
        }

        [Test]
        public void OldOperationCannotCancelNewExecution()
        {
            action.Execution = InteractionExecutionMode.Timed;
            var first = authority.Process(actor, Request(1));
            Assert.IsTrue(first.Accepted);
            Assert.IsTrue(authority.Process(actor, Request(2, InteractionRequestKind.Cancel, first.OperationId)).Accepted);
            var next = authority.Process(actor, Request(3));
            Assert.IsTrue(next.Accepted);
            Assert.IsFalse(authority.Process(actor, Request(4, InteractionRequestKind.Cancel, first.OperationId)).Accepted);
            Assert.IsTrue(actor.GetTicket(action).IsActive);
        }

        [Test]
        public void ConfirmationRunsOnAuthorityAndDisconnectCancels()
        {
            action.InputGesture = InteractionInputGesture.HoldToConfirm;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(authority.Process(actor, Request(1)).Accepted);
            actor.Tick(.2f);
            Assert.AreEqual(0, calls);
            authority.Disconnect(actor);
            actor.Tick(1);
            Assert.AreEqual(0, calls);
            Assert.IsNull(actor.ActiveAction);
        }

        [Test]
        public void InvalidAnalogValuesAndRateExcessAreRejected()
        {
            Assert.IsFalse(authority.Process(actor, Request(1, InteractionRequestKind.Value, 0, float.NaN)).Accepted);
            authority.MaxRequestsPerSecond = 1;
            Assert.IsTrue(authority.Process(actor, Request(2)).Accepted);
            Assert.IsFalse(authority.Process(actor, Request(3)).Accepted);
        }

        [Test]
        public void ReleaseBeforeAcknowledgementUsesTheStartSequence()
        {
            action.InputGesture = InteractionInputGesture.HoldToConfirm;
            int calls = 0;
            action.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(authority.Process(actor, Request(10)).Accepted);
            var released = new InteractionRequest(11, "authority-target", "use", InteractionRequestKind.Cancel, sourceSequence: 10);
            Assert.IsTrue(authority.Process(actor, released).Accepted);
            actor.Tick(1);
            Assert.AreEqual(0, calls);
            Assert.IsTrue(authority.Process(actor, Request(12)).Accepted);
            Assert.IsFalse(authority.Process(actor, new InteractionRequest(13, "authority-target", "use", InteractionRequestKind.Cancel, sourceSequence: 10)).Accepted);
            Assert.IsTrue(actor.GetTicket(action).IsActive);
        }
    }
}
