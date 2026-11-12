using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [DisallowMultipleComponent, AddComponentMenu("Easy Interaction/Cooperative Action")]
    public sealed class CooperativeAction : InteractionAction, IInteractionState
    {
        [SerializeField, Min(1)]
        private int minimumParticipants = 2;
        [SerializeField, Min(.01f)]
        private float workSeconds = 5;
        [SerializeField]
        private bool fasterWithMoreParticipants = true;
        [SerializeField]
        private bool resetWhenAbandoned;
        [SerializeField]
        private float progress;
        [SerializeField]
        private UnityEvent onWorkFinished = new UnityEvent();
        private int lastTickFrame = -1;
        public int MinimumParticipants { get => minimumParticipants; set => minimumParticipants = Mathf.Max(1, value); }
        public float WorkSeconds { get => workSeconds; set => workSeconds = Mathf.Max(.01f, value); }
        public float WorkProgress => progress;
        public UnityEvent OnWorkFinished => onWorkFinished;
        protected override int DefaultMaxParticipants => 4;
        protected override InteractionInputGesture DefaultInputGesture => InteractionInputGesture.HoldWhileActive;

        protected override void Reset()
        {
            base.Reset();
            MaxParticipants = 4;
            ActionLabel = "Work together";
        }

        public override float GetProgress(InteractionActor actor) => progress;
        public override InteractionAvailability GetAvailability(InteractionActor actor) => progress < 1 ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Requirement, "Work is already finished.", "interaction.finished");
        protected override bool OnExecute(InteractionActor actor) => true;
        protected override void OnTick(InteractionActor actor, float dt)
        {
            InteractionActor leader = null;
            int active = 0;
            for (int i = 0; i < OwnerCount; i++)
                if (GetOwner(i) && GetOwner(i).IsRunning(this))
                {
                    if (!leader)
                        leader = GetOwner(i);
                    active++;
                }

            if (actor != leader || active < minimumParticipants)
                return;
            // Only the first running participant advances world-owned progress.
            if (actor.AutomaticUpdate && lastTickFrame == Time.frameCount)
                return;
            lastTickFrame = Time.frameCount;
            progress = Mathf.Clamp01(progress + dt * (fasterWithMoreParticipants ? active : 1) / workSeconds);
            for (int i = 0; i < OwnerCount; i++)
                GetOwner(i).SetProgress(this, progress);
            if (progress < 1)
                return;
            onWorkFinished.Invoke();
            for (int i = OwnerCount - 1; i >= 0; i--)
                Complete(GetOwner(i));
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (resetWhenAbandoned && OwnerCount == 0)
                progress = 0;
        }

        public void ResetWork()
        {
            Cancel("Work reset.");
            progress = 0;
        }

        [System.Serializable]
        private struct State
        {
            public float progress;
        }

        public string CaptureState() => JsonUtility.ToJson(new State { progress = progress });
        public InteractionAvailability ValidateState(string json)
        {
            try
            {
                float value = JsonUtility.FromJson<State>(json).progress;
                return float.IsNaN(value) || value < 0 || value > 1 ? InteractionAvailability.Unavailable("Invalid work progress.") : InteractionAvailability.Available;
            }
            catch
            {
                return InteractionAvailability.Unavailable("Invalid work state.");
            }
        }

        public void RestoreState(string json)
        {
            progress = JsonUtility.FromJson<State>(json).progress;
        }
    }
}
