using System.Collections.Generic;
using EasyFeedbacks;
using EasyInteraction.Integrations.Feedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class TestInteractionFeedbackContext : MonoBehaviour, IFeedbackContextProvider
    {
        public Gamepad Gamepad;
        public int Calls;
        public FeedbackContext Incoming, Prepared;
        public FeedbackContext Prepare(FeedbackContext context)
        {
            Calls++;
            Incoming = context;
            Prepared = context.WithDefaultGamepad(Gamepad);
            return Prepared;
        }
    }

    public sealed class FeedbackContextTests
    {
        private readonly List<Object> objects = new List<Object>();
        private Gamepad gamepad;
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (gamepad != null && gamepad.added)
                InputSystem.RemoveDevice(gamepad);
        }

        [Test]
        public void FeedbackUsesExplicitOrLinkedPlayerContextWithoutLosingActionSource()
        {
            gamepad = InputSystem.AddDevice<Gamepad>();
            var root = New("Linked feedback player");
            root.transform.position = new Vector3(0, 135, 0);
            var inherited = root.AddComponent<TestInteractionFeedbackContext>();
            inherited.Gamepad = gamepad;
            var child = New("Actor");
            child.transform.SetParent(root.transform, false);
            var actor = child.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            actor.PlayerRoot = root.transform;
            var feedback = child.AddComponent<InteractionFeedbacks>();
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = root.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.Greater(inherited.Calls, 0);
            Assert.AreSame(action.transform, inherited.Incoming.Source);
            Assert.AreEqual(action.transform.position, inherited.Prepared.WorldPosition);
            Assert.AreSame(gamepad, inherited.Prepared.TargetGamepad);
            var explicitProvider = New("Explicit context").AddComponent<TestInteractionFeedbackContext>();
            feedback.ContextProvider = explicitProvider;
            int previous = inherited.Calls;
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.Greater(explicitProvider.Calls, 0);
            Assert.AreEqual(previous, inherited.Calls);
            explicitProvider.enabled = false;
            previous = explicitProvider.Calls;
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.AreEqual(previous, explicitProvider.Calls);
        }

        [Test]
        public void DisabledFeedbackCannotEmitFromAnAlreadyQueuedCallback()
        {
            var root = New("Feedback lifecycle actor");
            root.transform.position = new Vector3(0, 135, 0);
            var actor = root.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            InteractionFeedbacks feedback = null;
            actor.ActionStateChanged += _ =>
            {
                if (feedback)
                    feedback.enabled = false;
            };
            feedback = root.AddComponent<InteractionFeedbacks>();
            var provider = root.AddComponent<TestInteractionFeedbackContext>();
            feedback.ContextProvider = provider;
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = root.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.IsFalse(feedback.enabled);
            Assert.AreEqual(0, provider.Calls);
        }
    }
}
