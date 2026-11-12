using UnityEngine;
using UnityEngine.InputSystem;
using EasyInteraction.UI;

namespace EasyInteraction.Samples.Gallery
{
    /// <summary>Camera station navigation for a laboratory scene. This is not an FPS movement controller.</summary>
    public sealed class InteractionGalleryControls : MonoBehaviour
    {
        public InteractionActor actor, helper;
        public DoorAction door;
        public GrabAction item;
        public CooperativeAction cooperative;
        public GameObject wall;
        public InteractionHUD hud;
        public Transform[] stations;
        private InputAction look, lookHeld, lockDoor, mass, barrier, help, layout, resetWork;
        private readonly InputAction[] modes = new InputAction[6], views = new InputAction[4];
        private float yaw, pitch;
        private string message = "E: act · Q: cancel · Tab/F: actions · C: menu";
        private static InputAction Button(string name, string binding)
        {
            var value = new InputAction(name, InputActionType.Button, binding);
            value.Enable();
            return value;
        }

        private void OnEnable()
        {
            look = new InputAction("GalleryLook", InputActionType.Value, "<Mouse>/delta");
            look.Enable();
            lookHeld = Button("Look held", "<Mouse>/rightButton");
            lockDoor = Button("Lock door", "<Keyboard>/l");
            mass = Button("Crate mass", "<Keyboard>/m");
            barrier = Button("Wall", "<Keyboard>/b");
            help = Button("Helper", "<Keyboard>/n");
            layout = Button("Menu layout", "<Keyboard>/z");
            resetWork = Button("Reset work", "<Keyboard>/h");
            for (int i = 0; i < modes.Length; i++)
                modes[i] = Button("Selection " + i, "<Keyboard>/digit" + (i + 1));
            for (int i = 0; i < views.Length; i++)
                views[i] = Button("Station " + i, "<Keyboard>/f" + (i + 1));
            actor.ActionStateChanged += Changed;
        }

        private void Changed(InteractionActionEvent state)
        {
            if (state.State != InteractionActionState.Progress)
                message = state.State + (string.IsNullOrEmpty(state.Reason) ? "" : ": " + state.Reason);
        }

        private void Update()
        {
            if (lookHeld.IsPressed() && (!hud || !hud.MenuOpen))
            {
                var delta = look.ReadValue<Vector2>() * .12f;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -75, 75);
                actor.View.rotation = Quaternion.Euler(pitch, yaw, 0);
            }

            var choices = newModeChoices;
            for (int i = 0; i < modes.Length; i++)
                if (modes[i].WasPressedThisFrame())
                    actor.SelectionMode = choices[i];
            for (int i = 0; i < views.Length; i++)
                if (views[i].WasPressedThisFrame())
                    GoToStation(i);
            if (lockDoor.WasPressedThisFrame())
                door.IsLocked = !door.IsLocked;
            if (mass.WasPressedThisFrame() && !item.IsHeld)
                item.Body.mass = item.Body.mass > 20 ? 2 : 100;
            if (barrier.WasPressedThisFrame())
                wall.SetActive(!wall.activeSelf);
            if (help.WasPressedThisFrame())
            {
                if (helper.GetTicket(cooperative).IsActive)
                    helper.Cancel();
                else
                    helper.TryExecuteDirect(cooperative);
            }

            if (resetWork.WasPressedThisFrame())
                cooperative.ResetWork();
            if (layout.WasPressedThisFrame() && hud)
                hud.Layout = hud.Layout == InteractionMenuLayout.List ? InteractionMenuLayout.Radial : InteractionMenuLayout.List;
        }

        private static readonly InteractionSelectionMode[] newModeChoices =
        {
            InteractionSelectionMode.Raycast,
            InteractionSelectionMode.Focus,
            InteractionSelectionMode.SphereCast,
            InteractionSelectionMode.Nearest,
            InteractionSelectionMode.Contact,
            InteractionSelectionMode.Cursor
        };
        public void GoToStation(int index)
        {
            if (stations == null || index < 0 || index >= stations.Length || !stations[index])
                return;
            actor.Cancel("Changed test station.");
            actor.ReleaseTargetLock();
            actor.transform.SetPositionAndRotation(stations[index].position, stations[index].rotation);
            yaw = actor.View.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, actor.View.eulerAngles.x);
            Physics.SyncTransforms();
        }

        private void OnDisable()
        {
            if (actor)
                actor.ActionStateChanged -= Changed;
            look?.Dispose();
            lookHeld?.Dispose();
            lockDoor?.Dispose();
            mass?.Dispose();
            barrier?.Dispose();
            help?.Dispose();
            layout?.Dispose();
            resetWork?.Dispose();
            foreach (var input in modes)
                input?.Dispose();
            foreach (var input in views)
                input?.Dispose();
        }

        private void OnGUI()
        {
            if (hud && hud.MenuOpen)
                return;
            var previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.Box(new Rect(12, 12, 760, 168), "EasyInteraction — Interaction Gallery");
            GUILayout.BeginArea(new Rect(24, 40, 736, 136));
            GUILayout.Label("RMB + mouse: look · E / Q: act / cancel · Tab / F: actions · C: menu · Z: list / radial");
            GUILayout.Label("1 Raycast · 2 Focus · 3 SphereCast · 4 Nearest · 5 Contact · 6 Cursor · V: cycle target");
            GUILayout.Label("G: throw · R + mouse: rotate · wheel: hold distance · L: door lock · M: mass · B: wall");
            GUILayout.Label("F1: items · F2: actions · F3: mechanics · F4: cooperation · N: helper · H: reset work");
            GUILayout.Label(message);
            GUILayout.EndArea();
            GUI.matrix = previous;
        }
    }
}
