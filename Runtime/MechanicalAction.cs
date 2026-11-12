using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    public enum MechanicalMotion
    {
        Linear,
        Angular
    }

    [DisallowMultipleComponent, AddComponentMenu("Easy Interaction/Mechanical Action")]
    public sealed class MechanicalAction : InteractionAction, IInteractionState
    {
        [SerializeField]
        private Transform movingPart;
        [SerializeField]
        private MechanicalMotion motion;
        [SerializeField]
        private Vector3 axis = Vector3.up;
        [SerializeField]
        private float minimum;
        [SerializeField]
        private float maximum = 90;
        [SerializeField, Range(0, 1)]
        private float value;
        [SerializeField, Min(.01f)]
        private float speed = .5f;
        [SerializeField, Min(0)]
        private float resistance;
        [SerializeField, Tooltip("0 is continuous, 2 snaps to endpoints, 3 adds a middle detent.")]
        [Min(0)]
        private int detents;
        [SerializeField, Tooltip("Optional physics joint. Configure its allowed linear/angular axis in the Inspector.")]
        private ConfigurableJoint joint;
        [SerializeField]
        private UnityEvent<float> onValueChanged = new UnityEvent<float>();
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private bool initialized;
        public Transform MovingPart
        {
            get => movingPart ? movingPart : transform;
            set
            {
                movingPart = value;
                initialized = false;
            }
        }

        public MechanicalMotion Motion { get => motion; set => motion = value; }
        public Vector3 Axis { get => axis; set => axis = value.sqrMagnitude > .0001f ? value.normalized : Vector3.up; }
        public float Minimum { get => minimum; set => minimum = value; }
        public float Maximum { get => maximum; set => maximum = value; }
        public float Value => value;
        public float Speed { get => speed; set => speed = Mathf.Max(.01f, value); }
        public ConfigurableJoint Joint { get => joint; set => joint = value; }
        public new UnityEvent<float> OnValueChanged => onValueChanged;
        protected override InteractionInputGesture DefaultInputGesture => InteractionInputGesture.Analog;

        protected override void Reset()
        {
            base.Reset();
            InputGesture = InteractionInputGesture.Analog;
            ActionLabel = "Adjust";
        }

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
            initialPosition = MovingPart.localPosition;
            initialRotation = MovingPart.localRotation;
            initialized = true;
        }

        protected override bool OnExecute(InteractionActor actor) => true;
        public override float GetProgress(InteractionActor actor) => value;
        protected override void OnTick(InteractionActor actor, float dt)
        {
            SetValue(value + actor.GetValue(this) * speed * dt / (1 + resistance));
            actor.SetProgress(this, value);
        }

        public void SetValue(float normalized)
        {
            if (float.IsNaN(normalized) || float.IsInfinity(normalized))
                return;
            Initialize();
            float next = Mathf.Clamp01(normalized);
            if (Mathf.Approximately(next, value))
                return;
            value = next;
            Apply();
            onValueChanged.Invoke(value);
        }

        private void Apply()
        {
            var direction = axis.sqrMagnitude > .0001f ? axis.normalized : Vector3.up;
            float amount = Mathf.Lerp(minimum, maximum, value);
            if (joint)
            {
                if (motion == MechanicalMotion.Linear)
                    joint.targetPosition = -direction * amount;
                else
                    joint.targetRotation = Quaternion.Inverse(Quaternion.AngleAxis(amount, direction));
                return;
            }

            if (motion == MechanicalMotion.Linear)
                MovingPart.localPosition = initialPosition + direction * amount;
            else
                MovingPart.localRotation = initialRotation * Quaternion.AngleAxis(amount, direction);
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (detents > 1)
                SetValue(Mathf.Round(value * (detents - 1)) / (detents - 1));
        }

        protected override void OnComplete(InteractionActor actor) => OnCancel(actor);
        [System.Serializable]
        private struct State
        {
            public float value;
        }

        public string CaptureState() => JsonUtility.ToJson(new State { value = value });
        public InteractionAvailability ValidateState(string json)
        {
            try
            {
                float state = JsonUtility.FromJson<State>(json).value;
                return float.IsNaN(state) || state < 0 || state > 1 ? InteractionAvailability.Unavailable("Invalid mechanical value.") : InteractionAvailability.Available;
            }
            catch
            {
                return InteractionAvailability.Unavailable("Invalid mechanical state.");
            }
        }

        public void RestoreState(string json)
        {
            Initialize();
            value = JsonUtility.FromJson<State>(json).value;
            Apply();
        }
    }
}
