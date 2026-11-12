using System.Collections.Generic;
using EasyInteraction.Integrations.Movement;
using EasyMovement.FirstPerson;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class MovementCoordinationTests
    {
        private readonly List<Object> objects = new List<Object>();
        private InteractionActor actor;
        private FirstPersonCharacterController character;
        private FirstPersonCharacterControllerInput reader;
        private EasyMovementInteractionMotor motor;
        private Keyboard keyboard;
        private InputSettings.UpdateMode oldUpdate;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditor;
        private GameObject New(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        [SetUp]
        public void SetUp()
        {
            oldUpdate = InputSystem.settings.updateMode;
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            var root = New("Movement coordination actor");
            root.SetActive(false);
            root.transform.position = new Vector3(0, 130, 0);
            actor = root.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            reader = root.AddComponent<FirstPersonCharacterControllerInput>();
            character = root.GetComponent<FirstPersonCharacterController>();
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            objects.Add(asset);
            var map = asset.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            reader.inputActionsAsset = asset;
            motor = root.AddComponent<EasyMovementInteractionMotor>();
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (actor)
                Object.DestroyImmediate(actor.gameObject);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i])
                    Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = oldUpdate;
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
        }

        private Transform Destination()
        {
            var go = New("Use position");
            go.transform.position = character.GetPosition() + Vector3.right * 2;
            return go.transform;
        }

        [Test]
        public void LegacyReaderCannotOverwriteApproachIncludingWithZeroInput()
        {
            motor.Begin(Destination());
            motor.Tick(.1f);
            Vector3 commanded = character.GetMovementDirection();
            Assert.Greater(commanded.x, .9f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            reader.SendMessage("Update");
            Assert.That(character.GetMovementDirection(), Is.EqualTo(commanded));
            Assert.IsTrue(reader.enabled);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            reader.SendMessage("Update");
            Assert.That(character.GetMovementDirection(), Is.EqualTo(commanded));
            motor.Stop();
            Assert.IsFalse(character.InputBlocked);
        }

        [Test]
        public void ApproachAndStationReleaseOnlyTheirOwnRequests()
        {
            var station = actor.gameObject.AddComponent<EasyMovementInteractionLock>();
            var externalOwner = New("Other blocker");
            using var external = character.AcquireInputBlock(externalOwner);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = actor.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            action.Channel = "body";
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(action));
            motor.Begin(Destination());
            motor.Stop();
            Assert.IsTrue(character.InputBlocked);
            actor.Cancel();
            Assert.IsTrue(character.InputBlocked);
            external.Dispose();
            Assert.IsFalse(character.InputBlocked);
            Assert.IsTrue(reader.enabled);
            motor.Begin(Destination());
            motor.enabled = false;
            Assert.IsFalse(character.InputBlocked);
            motor.enabled = true;
            motor.Begin(Destination());
            Object.DestroyImmediate(motor);
            Assert.IsFalse(character.InputBlocked);
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Object.DestroyImmediate(station);
            Assert.IsFalse(character.InputBlocked);
        }

        [Test]
        public void DisablingMotorCancelsPendingApproachAndFreesInput()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = actor.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            action.ApproachPoint = Destination();
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            Physics.SyncTransforms();
            Assert.IsTrue(actor.TryBegin(action, out var ticket));
            Assert.IsTrue(character.InputBlocked);
            motor.enabled = false;
            actor.Tick(0);
            Assert.IsFalse(ticket.IsActive);
            Assert.IsFalse(character.InputBlocked);
            Assert.IsTrue(reader.enabled);
        }

        [Test]
        public void CameraHostedActorMeasuresTheUsePositionFromTheCharacterBody()
        {
            var eye = New("Camera actor");
            eye.SetActive(false);
            eye.transform.SetParent(character.transform, false);
            eye.transform.localPosition = Vector3.up * 1.6f;
            var eyeActor = eye.AddComponent<InteractionActor>();
            eyeActor.AutomaticUpdate = false;
            var eyeMotor = eye.AddComponent<EasyMovementInteractionMotor>();
            eyeMotor.character = character;
            eye.SetActive(true);
            var point = New("Standing position");
            point.transform.SetPositionAndRotation(character.transform.position, character.transform.rotation);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = character.transform.position + Vector3.forward * 2;
            var action = target.AddComponent<EventAction>();
            action.ApproachPoint = point.transform;
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            Physics.SyncTransforms();
            Assert.IsTrue(eyeActor.TryExecuteDirect(action));
            Assert.IsTrue(eyeActor.IsRunning(action));
            Assert.IsFalse(eyeMotor.IsApproaching);
            Assert.IsFalse(character.InputBlocked);
        }
    }
}
