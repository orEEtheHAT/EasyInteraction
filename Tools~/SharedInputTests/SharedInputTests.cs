using System.Collections;
using System.Collections.Generic;
using EasyFeedbacks;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction.Integrations.Movement;
using EasyInteraction.Integrations.Feedbacks;
using EasyMovement.FirstPerson;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.SharedInput
{
    public sealed class RecordingPlayerFeedbackContext : MonoBehaviour, IFeedbackContextProvider
    {
        public PlayerFeedbackContext PlayerContext;
        public FeedbackContext Last;
        public int Calls;
        public FeedbackContext Prepare(FeedbackContext context)
        {
            Calls++;
            return Last = PlayerContext.Prepare(context);
        }
    }

    public sealed class SharedInputTests
    {
        private readonly List<Object> objects = new List<Object>();
        private PlayerInputSettings player;
        private InteractionActor actor;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private InteractionInput legacy;
        private EasyInteractionInput shared;
        private InteractionPresenter presenter;
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
            gamepad = InputSystem.AddDevice<Gamepad>();
            var root = New("Shared input actor");
            root.SetActive(false);
            root.transform.position = new Vector3(0, 145, 0);
            player = root.AddComponent<PlayerInputSettings>();
            player.ControlsCursor = false;
            actor = root.AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            legacy = root.AddComponent<InteractionInput>();
            presenter = root.AddComponent<InteractionPresenter>();
            shared = root.AddComponent<EasyInteractionInput>();
            shared.Player = player;
            root.SetActive(true);
            player.PairDevices(keyboard, gamepad);
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
            if (gamepad != null && gamepad.added)
                InputSystem.RemoveDevice(gamepad);
            InputSystem.settings.updateMode = oldUpdate;
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
        }

        private T Target<T>()
            where T : InteractionAction
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(go);
            go.transform.position = actor.transform.position + Vector3.forward * 2;
            var result = go.AddComponent<T>();
            Physics.SyncTransforms();
            actor.RefreshTarget();
            return result;
        }

        [Test]
        public void SharedMovementReaderCannotOverwriteScriptedApproachOrClearItOnDisable()
        {
            var standalone = actor.gameObject.AddComponent<FirstPersonCharacterControllerInput>();
            var movement = actor.gameObject.AddComponent<EasyMovementInput>();
            var character = movement.GetComponent<FirstPersonCharacterController>();
            var motor = actor.gameObject.AddComponent<EasyMovementInteractionMotor>();
            var point = New("Approach destination");
            point.transform.position = character.GetPosition() + Vector3.right * 2;
            motor.Begin(point.transform);
            motor.Tick(.1f);
            var commanded = character.GetMovementDirection();
            Assert.Greater(commanded.x, .9f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            movement.SendMessage("Update");
            Assert.AreEqual(commanded, character.GetMovementDirection());
            Assert.IsTrue(movement.enabled);
            Assert.IsTrue(player.Action("Gameplay/Move").enabled);
            movement.enabled = false;
            standalone.SendMessage("Update");
            Assert.AreEqual(commanded, character.GetMovementDirection());
            Assert.IsTrue(character.InputBlocked);
            motor.Stop();
            Assert.IsFalse(character.InputBlocked);
            Assert.IsTrue(standalone.enabled);
        }

        [UnityTest]
        public IEnumerator RebindingAndDevicePresentationReachPresenterFromTheSharedRuntimeAsset()
        {
            presenter.Refresh();
            Assert.IsFalse(legacy.enabled);
            Assert.AreSame(shared, presenter.ActiveInputSource);
            var runtime = player.Action("Gameplay/Interact");
            runtime.ApplyBindingOverride(0, "<Keyboard>/k");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            yield return null;
            Assert.AreEqual("<Keyboard>/k", runtime.bindings[0].effectivePath);
            Assert.AreEqual(player.GetPresentation(runtime).Text, presenter.Prompt.Binding);
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
            InputSystem.Update();
            yield return null;
            Assert.AreSame(gamepad, player.ActiveGamepad);
            Assert.AreEqual(shared.BindingLabel, presenter.Prompt.Binding);
            Assert.AreSame(shared.BindingIcon, presenter.Prompt.BindingIcon);
            shared.enabled = false;
            yield return null;
            presenter.Refresh();
            Assert.IsTrue(legacy.enabled);
            Assert.AreSame(legacy, presenter.ActiveInputSource);
            Assert.AreEqual(legacy.BindingLabel, presenter.Prompt.Binding);
        }

        [Test]
        public void UiUsesSharedHoldToggleAndAnalogWithoutTheDisabledLegacyReader()
        {
            shared.ToggleInsteadOfHold = true;
            var action = Target<EventAction>();
            action.Execution = InteractionExecutionMode.Timed;
            action.Duration = 10;
            action.InputGesture = InteractionInputGesture.HoldWhileActive;
            Assert.IsTrue(presenter.Activate(action));
            presenter.Release(action);
            Assert.IsTrue(actor.GetTicket(action).IsActive);
            shared.ToggleInsteadOfHold = false;
            presenter.Release(action);
            Assert.IsFalse(actor.GetTicket(action).IsActive);
            Object.DestroyImmediate(action);
            var mechanical = actor.CurrentTarget.gameObject.AddComponent<MechanicalAction>();
            mechanical.ActionId = "adjust";
            Assert.IsTrue(presenter.Activate(mechanical));
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = Vector2.right });
            InputSystem.Update();
            shared.SendMessage("Update");
            actor.Tick(.2f);
            Assert.Greater(mechanical.Value, 0);
            Assert.IsFalse(legacy.enabled);
            Assert.IsTrue(shared.IsConsumingLook);
            Assert.IsFalse(player.Action("Gameplay/Look").enabled);
            presenter.Release(mechanical);
            Assert.IsFalse(shared.IsConsumingLook);
            Assert.IsFalse(player.Action("Gameplay/Look").enabled, "A held stick must not jump the camera when analog use ends.");
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();
            player.SendMessage("Update");
            Assert.IsTrue(player.Action("Gameplay/Look").enabled, "Look must resume after the stick returns to neutral.");
        }

        [Test]
        public void InteractionFeedbackUsesThePairedPlayersGamepadAndPreservesExplicitTargets()
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
            InputSystem.Update();
            Assert.AreSame(gamepad, player.ActiveGamepad);
            var playerContext = actor.gameObject.AddComponent<PlayerFeedbackContext>();
            var record = actor.gameObject.AddComponent<RecordingPlayerFeedbackContext>();
            record.PlayerContext = playerContext;
            var feedback = actor.gameObject.AddComponent<InteractionFeedbacks>();
            feedback.ContextProvider = record;
            var action = Target<EventAction>();
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.Greater(record.Calls, 0);
            Assert.AreSame(gamepad, record.Last.TargetGamepad);
            Assert.AreSame(action.transform, record.Last.Source);
            var other = InputSystem.AddDevice<Gamepad>();
            try
            {
                var context = new FeedbackContext(Vector3.one, Quaternion.identity, actor.transform, actor.transform, .3f, .4f, Vector3.one * 2, FeedbackPositionMode.WorldPosition, other);
                var prepared = playerContext.Prepare(context);
                Assert.AreSame(other, prepared.TargetGamepad);
                Assert.AreEqual(.3f, prepared.Intensity);
                Assert.AreEqual(.4f, prepared.AudioMultiplier);
                Assert.AreEqual(Vector3.one * 2, prepared.VisualScale);
                var muted = playerContext.Prepare(context.WithoutGamepad());
                Assert.IsTrue(muted.HasExplicitGamepadTarget);
                Assert.IsNull(muted.TargetGamepad);
                Assert.AreEqual(context.Source, muted.Source);
            }
            finally
            {
                InputSystem.RemoveDevice(other);
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            Assert.IsNull(player.ActiveGamepad);
            Assert.IsTrue(actor.TryExecuteDirect(action));
            Assert.IsTrue(record.Last.HasExplicitGamepadTarget, "A keyboard-owned interaction must explicitly suppress the global gamepad fallback.");
            Assert.IsNull(record.Last.TargetGamepad);
        }
    }
}
