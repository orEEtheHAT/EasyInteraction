using UnityEngine;

namespace EasyInteraction
{
    public interface IInteractionLocalView
    {
        void SetLocalActive(bool local);
    }

    /// <summary>Optional asynchronous command bridge, e.g. networking. Local games do not need this component.</summary>
    public abstract class InteractionCommandAdapter : MonoBehaviour
    {
        public abstract bool Press(InteractionAction action, bool toggleInsteadOfHold = false);
        public abstract void Release(InteractionAction action);
        public abstract void Cancel();
        public abstract bool SetValue(InteractionAction action, float value);
        public abstract bool IsRunning(InteractionAction action);
        public abstract float GetProgress(InteractionAction action);
        public virtual string LastReason => "";

        public virtual InteractionAvailability Check(InteractionAction action) => InteractionAvailability.Available;
    }
}
