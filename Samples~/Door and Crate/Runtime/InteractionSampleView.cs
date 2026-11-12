using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyInteraction.Samples
{
    /// <summary>A stationary inspection stand, not a player movement controller.</summary>
    public sealed class InteractionSampleView : MonoBehaviour
    {
        public InteractionActor actor;
        public DoorAction door;
        public GrabAction crate;
        public GameObject wall;
        private InputAction look, rotate, raycast, focus, lockDoor, heavy, barrier, player;
        private float yaw, pitch;
        private string lastEvent = "Point at the door or crate and press E.";
        private void OnEnable()
        {
            look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            rotate = Button("Rotate view", "<Mouse>/rightButton");
            raycast = Button("Raycast selection", "<Keyboard>/digit1");
            focus = Button("Focus selection", "<Keyboard>/digit2");
            lockDoor = Button("Door lock", "<Keyboard>/l");
            heavy = Button("Crate mass", "<Keyboard>/m");
            barrier = Button("Visibility wall", "<Keyboard>/b");
            player = Button("Player enabled", "<Keyboard>/p");
            look.Enable();
            actor.ActionStateChanged += OnAction;
        }

        private static InputAction Button(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button, binding);
            action.Enable();
            return action;
        }

        private void OnAction(InteractionActionEvent e) => lastEvent = e.State + (string.IsNullOrEmpty(e.Reason) ? "" : ": " + e.Reason);
        private void Update()
        {
            if (rotate.IsPressed())
            {
                Vector2 delta = look.ReadValue<Vector2>() * .12f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -50f, 50f);
                actor.View.localRotation = Quaternion.Euler(pitch, yaw, 0);
            }

            if (raycast.WasPressedThisFrame())
                actor.SelectionMode = InteractionSelectionMode.Raycast;
            if (focus.WasPressedThisFrame())
                actor.SelectionMode = InteractionSelectionMode.Focus;
            if (lockDoor.WasPressedThisFrame())
                door.IsLocked = !door.IsLocked;
            if (heavy.WasPressedThisFrame() && !crate.IsHeld)
                crate.Body.mass = crate.Body.mass > 20 ? 2 : 100;
            if (barrier.WasPressedThisFrame())
                wall.SetActive(!wall.activeSelf);
            if (player.WasPressedThisFrame())
                actor.enabled = !actor.enabled;
        }

        private void OnDisable()
        {
            if (actor)
                actor.ActionStateChanged -= OnAction;
            look?.Dispose();
            rotate?.Dispose();
            raycast?.Dispose();
            focus?.Dispose();
            lockDoor?.Dispose();
            heavy?.Dispose();
            barrier?.Dispose();
            player?.Dispose();
        }

        private void OnGUI()
        {
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.Box(new Rect(12, 12, 610, 210), "EasyInteraction | Door and Crate");
            GUILayout.BeginArea(new Rect(24, 40, 580, 180));
            GUILayout.Label("RMB + mouse: look | E: first action / release | Q: cancel");
            GUILayout.Label("1: Raycast | 2: Focus | L: lock door | M: heavy crate | B: wall | P: enable player");
            GUILayout.Label("Selection: " + actor.SelectionMode + " | Player: " + (actor.enabled ? "enabled" : "disabled"));
            GUILayout.Label("Door locked: " + door.IsLocked + " | Crate mass: " + crate.Body.mass + " kg");
            GUILayout.Label("Target: " + (actor.CurrentTarget ? actor.CurrentTarget.DisplayName : "none"));
            if (actor.ActiveAction)
                GUILayout.Label("E / Q: release held object");
            else
                foreach (var action in actor.Actions)
                {
                    if (!action)
                        continue;
                    var available = actor.Check(action);
                    GUILayout.Label(action.GetLabel(actor) + (available.IsAvailable ? " — available" : " — " + available.Reason));
                }

            GUILayout.Label(lastEvent);
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
            GUI.Label(new Rect(Screen.width * .5f - 5, Screen.height * .5f - 10, 20, 20), "+");
        }
    }
}
