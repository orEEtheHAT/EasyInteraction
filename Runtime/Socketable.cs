using UnityEngine;

namespace EasyInteraction
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody)), AddComponentMenu("Easy Interaction/Socketable")]
    public sealed class Socketable : MonoBehaviour
    {
        public string category = "item";
        public Vector3 localPosition;
        public Vector3 localRotation;
        private Rigidbody body;
        private bool oldKinematic, oldGravity;
        private RigidbodyInterpolation oldInterpolation;
        private CollisionDetectionMode oldCollision;
        public SocketAction Socket { get; private set; }
        public Rigidbody Body => body ? body : (body = GetComponent<Rigidbody>());

        internal void Attach(SocketAction socket)
        {
            body = Body;
            oldKinematic = body.isKinematic;
            oldGravity = body.useGravity;
            oldInterpolation = body.interpolation;
            oldCollision = body.collisionDetectionMode;
            Socket = socket;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.useGravity = false;
            Align(false);
        }

        internal void Detach()
        {
            if (!Socket)
                return;
            Socket = null;
            if (body)
            {
                body.isKinematic = oldKinematic;
                body.useGravity = oldGravity;
                body.interpolation = oldInterpolation;
                body.collisionDetectionMode = oldCollision;
            }
        }

        private void Align(bool physics)
        {
            if (!Socket || !Body)
                return;
            var anchor = Socket.Anchor;
            var position = anchor.TransformPoint(localPosition);
            var rotation = anchor.rotation * Quaternion.Euler(localRotation);
            if (physics)
            {
                body.MovePosition(position);
                body.MoveRotation(rotation);
            }
            else
            {
                body.position = position;
                body.rotation = rotation;
            }
        }

        private void FixedUpdate()
        {
            if (Socket)
                Align(true);
        }

        private void OnDisable()
        {
            if (Socket)
                Socket.Remove(this);
        }
    }
}
