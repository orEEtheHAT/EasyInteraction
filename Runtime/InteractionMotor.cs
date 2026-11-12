using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Optional movement adapter. EasyMovement/ECM2 and NavMesh adapters live in Samples.</summary>
    public abstract class InteractionMotor : MonoBehaviour
    {
        public abstract void Begin(Transform destination);
        public abstract void Tick(float deltaTime);
        public abstract void Stop();
    }
}
