using UnityEngine;
using System.Collections.Generic;

namespace EasyInteraction.Integrations.Netcode
{
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class NetcodeInteractionCommands : InteractionCommandAdapter
    {
        public NetworkInteractionActor Network { get; set; }

        private bool toggleHold;
        private readonly Dictionary<InteractionAction, ulong> starts = new Dictionary<InteractionAction, ulong>();
        private readonly List<InteractionAction> completed = new List<InteractionAction>();
        private float cleanupAt, valueAt;
        public override string LastReason => Network ? Network.LastReply.Result.Reason : "";

        public override bool Press(InteractionAction action, bool toggleInsteadOfHold = false)
        {
            if (!Network)
                return false;
            toggleHold = toggleInsteadOfHold;
            if (!action)
            {
                action = Network.FirstRunning();
                if (!action)
                    return false;
            }

            ulong sequence = Network.Submit(action, InteractionRequestKind.Press);
            if (sequence != 0)
                starts[action] = sequence;
            return sequence != 0;
        }

        public override void Release(InteractionAction action)
        {
            if (!Network || !action || toggleHold)
                return;
            if (action.InputGesture == InteractionInputGesture.HoldToConfirm || action.InputGesture == InteractionInputGesture.HoldWhileActive || action.InputGesture == InteractionInputGesture.Analog)
            {
                if (starts.TryGetValue(action, out var sequence))
                    Network.Submit(action, InteractionRequestKind.Cancel, 0, 0, sequence);
                else
                    Network.Send(action, InteractionRequestKind.Cancel, Network.Operation(action));
            }
        }

        public override void Cancel()
        {
            if (!Network)
                return;
            foreach (var pair in starts)
                if (pair.Key)
                    Network.Submit(pair.Key, InteractionRequestKind.Cancel, 0, 0, pair.Value);
            var action = Network.FirstRunning();
            if (action && !starts.ContainsKey(action))
                Network.Send(action, InteractionRequestKind.Cancel, Network.Operation(action));
        }

        public override bool SetValue(InteractionAction action, float value)
        {
            if (!Network || Time.unscaledTime < valueAt)
                return false;
            valueAt = Time.unscaledTime + .05f;
            return Network.Send(action, InteractionRequestKind.Value, Network.Operation(action), value);
        }

        public override bool IsRunning(InteractionAction action) => Network && Network.IsRunning(action);
        public override float GetProgress(InteractionAction action) => Network ? Network.Progress(action) : 0;
        public override InteractionAvailability Check(InteractionAction action) => Network ? Network.Check(action) : InteractionAvailability.Unavailable("Not connected.");
        private void Update()
        {
            if (!Network || Time.unscaledTime < cleanupAt)
                return;
            cleanupAt = Time.unscaledTime + 2;
            completed.Clear();
            foreach (var pair in starts)
                if (!pair.Key || (!Network.IsRunning(pair.Key) && Network.LastReply.Sequence >= pair.Value))
                    completed.Add(pair.Key);
            foreach (var action in completed)
                starts.Remove(action);
        }
    }
}
