using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyInteraction
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Interaction Input")]
    public sealed class InteractionInput : MonoBehaviour, IInteractionInputSource
    {
        [SerializeField]
        private InputActionReference interactAction;
        [SerializeField]
        private InputActionReference cancelAction;
        [SerializeField]
        private InputActionReference nextAction;
        [SerializeField]
        private InputActionReference secondaryAction;
        [SerializeField]
        private InputActionReference analogAction;
        [SerializeField, Tooltip("Accessibility: press once to start confirmation or continuous use, press again to cancel. Confirmation still takes its configured time.")]
        private bool toggleInsteadOfHold;
        [SerializeField]
        private bool itemControls = true;
        private InteractionActor actor;
        private readonly InputAction[] actions = new InputAction[10];
        private readonly bool[] owned = new bool[10], enabledHere = new bool[10];
        private InteractionTicket pressedTicket;
        private InteractionAction pressedAction;
        public bool ToggleInsteadOfHold
        {
            get => toggleInsteadOfHold;
            set
            {
                if (toggleInsteadOfHold == value)
                    return;
                toggleInsteadOfHold = value;
                NotifyPresentationChanged();
            }
        }

        public string BindingLabel { get; private set; } = "E";
        public Sprite BindingIcon => null;

        public event Action PresentationChanged;
        private InputAction Connect(int index, InputActionReference reference, string name, string binding, string gamepad = null, InputActionType type = InputActionType.Button)
        {
            var action = reference ? reference.action : null;
            var player = GetComponent<PlayerInput>();
            if (action == null && player && player.actions)
                action = player.actions.FindAction(name, false);
            owned[index] = action == null;
            if (action == null)
            {
                action = new InputAction(name, type, binding);
                if (gamepad != null)
                    action.AddBinding(gamepad);
            }

            actions[index] = action;
            enabledHere[index] = !action.enabled;
            if (enabledHere[index])
                action.Enable();
            return action;
        }

        private void OnEnable()
        {
            actor = GetComponent<InteractionActor>();
            Connect(0, interactAction, "Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth").performed += Press;
            actions[0].canceled += Release;
            Connect(1, cancelAction, "CancelInteraction", "<Keyboard>/q", "<Gamepad>/buttonEast").performed += Cancel;
            Connect(2, nextAction, "NextInteraction", "<Keyboard>/tab", "<Gamepad>/rightShoulder").performed += Next;
            Connect(3, secondaryAction, "SecondaryInteraction", "<Keyboard>/f", "<Gamepad>/buttonWest").performed += Secondary;
            Connect(4, analogAction, "InteractionValue", "<Mouse>/delta/x", "<Gamepad>/rightStick/x", InputActionType.Value);
            Connect(5, null, "NextTarget", "<Keyboard>/v", "<Gamepad>/leftShoulder").performed += NextTarget;
            if (itemControls)
            {
                Connect(6, null, "ThrowItem", "<Keyboard>/g", "<Gamepad>/buttonNorth").performed += Throw;
                Connect(7, null, "RotateItem", "<Keyboard>/r");
                Connect(8, null, "ItemRotation", "<Mouse>/delta", null, InputActionType.Value);
                Connect(9, null, "ItemDistance", "<Mouse>/scroll/y", null, InputActionType.Value);
            }

            RefreshBinding();
            InputSystem.onActionChange += OnActionChange;
            NotifyPresentationChanged();
        }

        private void RefreshBinding()
        {
            if (actions[0] == null)
                return;
            string next = actions[0].GetBindingDisplayString();
            if (BindingLabel == next)
                return;
            BindingLabel = next;
            NotifyPresentationChanged();
        }

        private void NotifyPresentationChanged()
        {
            try
            {
                PresentationChanged?.Invoke();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
        }

        private void OnActionChange(object changed, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged && (ReferenceEquals(changed, actions[0]) || ReferenceEquals(changed, actions[0]?.actionMap) || ReferenceEquals(changed, actions[0]?.actionMap?.asset)))
                RefreshBinding();
        }

        private void Press(InputAction.CallbackContext _)
        {
            if (!actor)
                return;
            actor.RefreshTarget();
            var action = actor.SelectedAction;
            actor.Press(action, toggleInsteadOfHold);
            pressedAction = action;
            pressedTicket = action ? actor.GetTicket(action) : default;
        }

        private void Release(InputAction.CallbackContext _)
        {
            if (actor && actor.CommandAdapter && pressedAction)
                actor.Release(pressedAction, toggleInsteadOfHold);
            else if (pressedTicket.IsActive)
                actor.Release(pressedTicket.Action, toggleInsteadOfHold);
            pressedTicket = default;
            pressedAction = null;
        }

        private void Cancel(InputAction.CallbackContext _)
        {
            if (!actor)
                return;
            if (actor.CommandAdapter)
                actor.CommandAdapter.Cancel();
            else
                actor.Cancel();
        }

        private void Next(InputAction.CallbackContext _)
        {
            if (actor)
                actor.CycleAction();
        }

        private void NextTarget(InputAction.CallbackContext _)
        {
            if (actor)
                actor.CycleTarget();
        }

        private void Secondary(InputAction.CallbackContext _)
        {
            if (!actor)
                return;
            actor.RefreshTarget();
            var selected = actor.SelectedAction;
            actor.CycleAction();
            var next = actor.SelectedAction;
            if (next && next != selected)
                actor.Press(next, toggleInsteadOfHold);
            else
            {
                var held = actor.GetActive<GrabAction>();
                if (held)
                {
                    var use = held.Target.FindAction("use");
                    if (use)
                        actor.TryExecuteDirect(use);
                }
            }
        }

        private void Throw(InputAction.CallbackContext _)
        {
            if (actor)
                actor.GetActive<GrabAction>()?.Throw(actor);
        }

        private void Update()
        {
            if (!actor || !actor.isActiveAndEnabled)
                return;
            var analog = actor.GetActive(InteractionInputGesture.Analog);
            if (analog)
                actor.SetValue(analog, actions[4].ReadValue<float>() * .1f);
            else if (actor.CommandAdapter)
            {
                analog = pressedAction ? pressedAction : actor.SelectedAction;
                if (analog && analog.InputGesture == InteractionInputGesture.Analog && actor.CommandAdapter.IsRunning(analog))
                    actor.CommandAdapter.SetValue(analog, actions[4].ReadValue<float>() * .1f);
            }

            var held = actor.GetActive<GrabAction>();
            if (!itemControls || !held)
                return;
            if (actions[7].IsPressed())
                held.Rotate(actions[8].ReadValue<Vector2>() * .25f);
            float scroll = actions[9].ReadValue<float>();
            if (scroll != 0)
                held.AdjustDistance(scroll * .002f);
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
            if (actor)
            {
                if (actor.CommandAdapter)
                    actor.CommandAdapter.Cancel();
                actor.Cancel("Input disabled.");
            }

            if (actions[0] != null)
            {
                actions[0].performed -= Press;
                actions[0].canceled -= Release;
            }

            if (actions[1] != null)
                actions[1].performed -= Cancel;
            if (actions[2] != null)
                actions[2].performed -= Next;
            if (actions[3] != null)
                actions[3].performed -= Secondary;
            if (actions[5] != null)
                actions[5].performed -= NextTarget;
            if (actions[6] != null)
                actions[6].performed -= Throw;
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] == null)
                    continue;
                if (enabledHere[i])
                    actions[i].Disable();
                if (owned[i])
                    actions[i].Dispose();
                actions[i] = null;
            }

            pressedTicket = default;
            pressedAction = null;
            NotifyPresentationChanged();
        }
    }
}
