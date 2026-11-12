using Unity.Profiling;
using UnityEngine;

namespace EasyInteraction
{
    public enum GrabRotation
    {
        Free,
        KeepInitial,
        FollowView
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("Easy Interaction/Grab Action")]
    public sealed class GrabAction : InteractionAction
    {
        [SerializeField, Min(.1f)]
        private float grabRange = 3;
        [SerializeField, Min(.01f)]
        private float maximumMass = 20;
        [SerializeField, Min(.1f)]
        private float holdDistance = 2;
        [SerializeField]
        private Transform gripPoint;
        [SerializeField]
        private Transform holdPoint;
        [SerializeField, Range(.5f, 12)]
        private float frequency = 3;
        [SerializeField, Range(.2f, 2)]
        private float dampingRatio = 1;
        [SerializeField, Min(1)]
        private float maximumAcceleration = 80;
        [SerializeField, Min(.1f)]
        private float breakDistance = 5;
        [SerializeField]
        private GrabRotation rotationMode;
        [SerializeField, Min(.1f)]
        private float minimumDistance = .5f;
        [SerializeField, Min(.1f)]
        private float maximumDistance = 4;
        [SerializeField, Min(0)]
        private float throwSpeed = 6;
        [SerializeField, Min(.1f)]
        private float inspectionDistance = .65f;
        private Rigidbody body;
        private bool captured, oldGravity, inspecting;
        private RigidbodyInterpolation oldInterpolation;
        private CollisionDetectionMode oldCollisionMode;
        private float currentDistance;
        private Quaternion initialRotation, viewOffset, manualRotation = Quaternion.identity, twoHandRotation;
        private Vector3 initialHandAxis;
        private static readonly ProfilerMarker marker = new ProfilerMarker("EasyInteraction.HoldPhysics");
        protected override string DefaultChannel => "carry";
        public override bool CanPause => false;
        public Rigidbody Body => body ? body : (body = GetComponent<Rigidbody>());
        public bool IsHeld => captured && Owner;
        public bool IsInspecting => inspecting;
        public float GrabRange { get => grabRange; set => grabRange = Mathf.Max(.1f, value); }
        public float MaximumMass { get => maximumMass; set => maximumMass = Mathf.Max(.01f, value); }

        public float HoldDistance
        {
            get => holdDistance;
            set
            {
                holdDistance = Mathf.Max(.1f, value);
                currentDistance = holdDistance;
            }
        }

        public Transform GripPoint { get => gripPoint; set => gripPoint = value; }
        public Transform HoldPoint { get => holdPoint; set => holdPoint = value; }
        public float Frequency { get => frequency; set => frequency = Mathf.Clamp(value, .5f, 12); }
        public float DampingRatio { get => dampingRatio; set => dampingRatio = Mathf.Clamp(value, .2f, 2); }
        public float MaximumAcceleration { get => maximumAcceleration; set => maximumAcceleration = Mathf.Max(1, value); }
        public float BreakDistance { get => breakDistance; set => breakDistance = Mathf.Max(.1f, value); }
        public GrabRotation RotationMode { get => rotationMode; set => rotationMode = value; }
        public float ThrowSpeed { get => throwSpeed; set => throwSpeed = Mathf.Max(0, value); }
        public Vector3 GripPosition => gripPoint ? gripPoint.position : Body ? Body.worldCenterOfMass : transform.position;

        public override string GetLabel(InteractionActor actor) => IsHeld && Owns(actor) ? "Release" : "Grab";
        public bool Owns(InteractionActor actor)
        {
            for (int i = 0; i < OwnerCount; i++)
                if (GetOwner(i) == actor)
                    return true;
            return false;
        }

        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!Body)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object cannot be picked up.", diagnostic: "Add a Rigidbody.");
            if (Body.isKinematic)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object is fixed in place.", diagnostic: "Turn off Rigidbody > Is Kinematic, or remove it from its socket first.");
            if (!Body.detectCollisions)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object cannot be picked up.", diagnostic: "Enable Rigidbody Detect Collisions.");
            if (Body.mass > maximumMass)
                return InteractionAvailability.Unavailable(InteractionFailure.TooHeavy, "Too heavy.", "interaction.tooHeavy", "Reduce Rigidbody Mass or increase Maximum Mass.");
            if ((GripPosition - actor.View.position).sqrMagnitude > grabRange * grabRange)
                return InteractionAvailability.Unavailable(InteractionFailure.OutOfRange, "Move closer to grab this object.", "interaction.outOfRange");
            return InteractionAvailability.Available;
        }

        public override InteractionAvailability GetContinuationAvailability(InteractionActor actor) => captured ? InteractionAvailability.Available : GetAvailability(actor);
        protected override bool OnExecute(InteractionActor actor)
        {
            if (!captured)
            {
                body = Body;
                oldGravity = body.useGravity;
                oldInterpolation = body.interpolation;
                oldCollisionMode = body.collisionDetectionMode;
                captured = true;
                currentDistance = holdDistance;
                inspecting = false;
                manualRotation = Quaternion.identity;
                initialRotation = body.rotation;
                viewOffset = Quaternion.Inverse(actor.View.rotation) * body.rotation;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.WakeUp();
            }

            if (OwnerCount == 2)
            {
                initialHandAxis = Destination(GetOwner(1)) - Destination(GetOwner(0));
                twoHandRotation = body.rotation;
            }

            return true;
        }

        private Vector3 Destination(InteractionActor actor) => holdPoint ? holdPoint.position : actor.HandAnchor ? actor.HandAnchor.position : actor.View.position + actor.View.forward * (inspecting ? inspectionDistance : currentDistance);
        public void AdjustDistance(float delta)
        {
            if (IsHeld)
                currentDistance = Mathf.Clamp(currentDistance + delta, minimumDistance, maximumDistance);
        }

        public void Rotate(Vector2 degrees)
        {
            if (IsHeld)
                manualRotation = Quaternion.Euler(-degrees.y, degrees.x, 0) * manualRotation;
        }

        public void SetInspection(bool value)
        {
            if (IsHeld)
                inspecting = value;
        }

        public bool Throw(InteractionActor actor)
        {
            if (!Owns(actor) || OwnerCount != 1 || !body)
                return false;
            Vector3 velocity = Vector3.ClampMagnitude(body.linearVelocity + actor.View.forward * throwSpeed, 30);
            actor.Cancel(this, "Thrown.");
            if (body)
                body.linearVelocity = velocity;
            return true;
        }

        public bool TryTransferTo(InteractionActor receiver)
        {
            if (!IsHeld || OwnerCount != 1 || !receiver || receiver == Owner)
                return false;
            return Owner.TransferGrab(this, receiver);
        }

        private void FixedUpdate()
        {
            if (!captured)
                return;
            using (marker.Auto())
            {
                if (!Owner || !body || body.isKinematic || !Target.isActiveAndEnabled)
                {
                    Cancel("Holding interrupted.");
                    Restore();
                    return;
                }

                Vector3 destination = Vector3.zero;
                int count = 0;
                for (int i = OwnerCount - 1; i >= 0; i--)
                {
                    var actor = GetOwner(i);
                    if (!actor || !actor.isActiveAndEnabled)
                    {
                        if (actor)
                            actor.Cancel(this);
                        continue;
                    }

                    destination += Destination(actor);
                    count++;
                }

                if (count == 0)
                {
                    Restore();
                    return;
                }

                destination /= count;
                var point = GripPosition;
                var error = destination - point;
                if (error.sqrMagnitude > breakDistance * breakDistance)
                {
                    Cancel("Object is too far from its hold point.");
                    return;
                }

                float omega = 2 * Mathf.PI * frequency, kp = omega * omega, kd = 2 * dampingRatio * omega, dt = Time.fixedDeltaTime;
                float denominator = 1 + kd * dt + kp * dt * dt;
                body.AddForceAtPosition(Vector3.ClampMagnitude((kp * error - kd * body.GetPointVelocity(point)) / denominator, maximumAcceleration), point, ForceMode.Acceleration);
                bool rotated = rotationMode != GrabRotation.Free || inspecting || manualRotation != Quaternion.identity || count > 1;
                if (rotated)
                {
                    var desired = rotationMode == GrabRotation.KeepInitial ? initialRotation : Owner.View.rotation * viewOffset;
                    if (count > 1 && initialHandAxis.sqrMagnitude > .001f)
                        desired = Quaternion.FromToRotation(initialHandAxis, Destination(GetOwner(1)) - Destination(GetOwner(0))) * twoHandRotation;
                    desired *= manualRotation;
                    var delta = desired * Quaternion.Inverse(body.rotation);
                    if (delta.w < 0)
                        delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                    delta.ToAngleAxis(out float degrees, out var axis);
                    if (!float.IsNaN(axis.x) && axis.sqrMagnitude > .001f)
                    {
                        var acceleration = (kp * axis * (degrees * Mathf.Deg2Rad) - kd * body.angularVelocity) / denominator;
                        body.AddTorque(Vector3.ClampMagnitude(acceleration, maximumAcceleration), ForceMode.Acceleration);
                    }
                }
            }
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (OwnerCount == 0)
                Restore();
        }

        protected override void OnComplete(InteractionActor actor)
        {
            if (OwnerCount == 0)
                Restore();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (OwnerCount == 0)
                Restore();
        }

        private void OnDestroy() => Restore();
        private void Restore()
        {
            if (!captured)
                return;
            captured = false;
            inspecting = false;
            if (!body)
                return;
            body.useGravity = oldGravity;
            body.interpolation = oldInterpolation;
            body.collisionDetectionMode = oldCollisionMode;
        }
    }
}
