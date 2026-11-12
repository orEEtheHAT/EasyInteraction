using System;
using UnityEngine;
using UnityEngine.Events;

namespace EasyInteraction
{
    [Serializable]
    public sealed class InteractionStage
    {
        public string label = "Work";
        [Min(0)]
        public float seconds = 1;
        public bool waitForSignal;
        public UnityEvent onEntered = new UnityEvent();
        public UnityEvent onCompleted = new UnityEvent();
        public UnityEvent onCancelled = new UnityEvent();
    }

    [DisallowMultipleComponent, AddComponentMenu("Easy Interaction/Sequence Action")]
    public sealed class SequenceAction : InteractionAction
    {
        [SerializeField]
        private InteractionStage[] stages =
        {
            new InteractionStage()
        };
        private int stageIndex;
        private float elapsed;
        private bool signalled;
        public InteractionStage[] Stages { get => stages; set => stages = value ?? Array.Empty<InteractionStage>(); }
        public int StageIndex => stageIndex;

        public override string GetLabel(InteractionActor actor) => Owner == actor && stageIndex < stages.Length ? stages[stageIndex].label : ActionLabel;
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (MaxParticipants != 1 || stages.Length == 0)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "Sequence is not configured.", diagnostic: "Add at least one stage and keep Max Participants = 1. Use Cooperative Action for shared work.");
            for (int i = 0; i < stages.Length; i++)
                if (stages[i] == null || float.IsNaN(stages[i].seconds) || float.IsInfinity(stages[i].seconds) || stages[i].seconds < 0)
                    return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "Invalid stage duration.");
            return InteractionAvailability.Available;
        }

        protected override bool OnExecute(InteractionActor actor)
        {
            stageIndex = 0;
            elapsed = 0;
            signalled = false;
            stages[0].onEntered.Invoke();
            return true;
        }

        protected override void OnTick(InteractionActor actor, float deltaTime)
        {
            int guard = stages.Length;
            while (stageIndex < stages.Length && guard-- > 0 && !actor.IsStopping(this))
            {
                var stage = stages[stageIndex];
                float consumed = Mathf.Min(deltaTime, Mathf.Max(0, stage.seconds - elapsed));
                elapsed += consumed;
                deltaTime -= consumed;
                float part = stage.seconds > 0 ? Mathf.Clamp01(elapsed / stage.seconds) : signalled || !stage.waitForSignal ? 1 : 0;
                actor.SetProgress(this, (stageIndex + part) / stages.Length);
                if (elapsed < stage.seconds || (stage.waitForSignal && !signalled))
                    return;
                stage.onCompleted.Invoke();
                if (actor.IsStopping(this))
                    return;
                stageIndex++;
                elapsed = 0;
                signalled = false;
                if (stageIndex == stages.Length)
                {
                    Complete(actor);
                    return;
                }

                stages[stageIndex].onEntered.Invoke();
                if (deltaTime <= 0 && stages[stageIndex].seconds > 0)
                    return;
            }
        }

        public bool SignalStage(InteractionTicket ticket, int expectedStage)
        {
            if (!ticket.IsActive || ticket.Action != this || ticket.Actor != Owner || expectedStage != stageIndex)
                return false;
            signalled = true;
            return true;
        }

        public void SignalStage(int expectedStage)
        {
            if (Owner)
                SignalStage(Owner.GetTicket(this), expectedStage);
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (stageIndex < stages.Length && stages[stageIndex] != null)
                stages[stageIndex].onCancelled.Invoke();
        }
    }
}
