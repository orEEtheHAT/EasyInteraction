using System;
using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [DisallowMultipleComponent, AddComponentMenu("Easy Interaction/Door Action")]
    public sealed class DoorAction : InteractionAction, IInteractionState
    {
        [SerializeField]
        private Transform hinge;
        [SerializeField]
        private Vector3 openRotation = new Vector3(0, 90, 0);
        [SerializeField]
        private bool isOpen, isLocked;
        [SerializeField]
        private UnityEvent<bool> onOpenChanged = new UnityEvent<bool>();
        private Quaternion closedRotation;
        private bool initialized;
        private bool requestedOpen;
        public bool IsOpen => isOpen;
        public bool IsLocked { get => isLocked; set => isLocked = value; }
        public UnityEvent<bool> OnOpenChanged => onOpenChanged;

        public Transform Hinge
        {
            get => hinge ? hinge : transform;
            set
            {
                hinge = value;
                initialized = false;
            }
        }

        public Vector3 OpenRotation { get => openRotation; set => openRotation = value; }

        protected override void OnEnable()
        {
            base.OnEnable();
            Initialize();
            Apply();
        }

        private void Initialize()
        {
            if (initialized)
                return;
            closedRotation = Hinge.localRotation;
            initialized = true;
        }

        private void Apply() => Hinge.localRotation = closedRotation * (isOpen ? Quaternion.Euler(openRotation) : Quaternion.identity);
        public void SetOpen(bool open)
        {
            Initialize();
            bool changed = isOpen != open;
            isOpen = open;
            Apply();
            if (changed)
                try
                {
                    onOpenChanged.Invoke(open);
                }
                catch (Exception error)
                {
                    Debug.LogException(error, this);
                }
        }

        public override string GetLabel(InteractionActor actor) => isOpen ? "Close" : "Open";
        public override InteractionAvailability GetAvailability(InteractionActor actor) => isLocked ? InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Door is locked.", "interaction.locked", "Unlock Door Action > Is Locked, or grant access through your game logic.") : InteractionAvailability.Available;
        public override InteractionAvailability GetContinuationAvailability(InteractionActor actor) => GetAvailability(actor);
        protected override void OnBegin(InteractionActor actor) => requestedOpen = !isOpen;
        protected override bool OnExecute(InteractionActor actor)
        {
            SetOpen(requestedOpen);
            return false;
        }

        [Serializable]
        private struct State
        {
            public bool open, locked;
        }

        public string CaptureState() => JsonUtility.ToJson(new State { open = isOpen, locked = isLocked });
        public InteractionAvailability ValidateState(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
                    return InteractionAvailability.Unavailable("Invalid door state.");
                JsonUtility.FromJson<State>(json);
                return InteractionAvailability.Available;
            }
            catch
            {
                return InteractionAvailability.Unavailable("Invalid door state.");
            }
        }

        public void RestoreState(string json)
        {
            var state = JsonUtility.FromJson<State>(json);
            isLocked = state.locked;
            SetOpen(state.open);
        }
    }
}
