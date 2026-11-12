using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    /// <summary>World state only. Validate must be read-only; Restore must accept every state Validate accepts.</summary>
    public interface IInteractionState
    {
        string CaptureState();
        InteractionAvailability ValidateState(string json);
        void RestoreState(string json);
    }

    [Serializable]
    public sealed class InteractionTargetState
    {
        public int version = 1;
        public string targetId;
        public List<InteractionStateEntry> actions = new List<InteractionStateEntry>();
    }

    [Serializable]
    public sealed class InteractionStateEntry
    {
        public string actionId;
        public string type;
        public string json;
    }

    public static class InteractionWorld
    {
        private static readonly Dictionary<string, List<InteractionTarget>> targets = new Dictionary<string, List<InteractionTarget>>(StringComparer.Ordinal);
        internal static void Register(InteractionTarget target)
        {
            if (string.IsNullOrEmpty(target.PersistentId))
                return;
            if (!targets.TryGetValue(target.PersistentId, out var group))
            {
                group = new List<InteractionTarget>(1);
                targets.Add(target.PersistentId, group);
            }

            if (!group.Contains(target))
                group.Add(target);
        }

        internal static void Unregister(InteractionTarget target)
        {
            if (string.IsNullOrEmpty(target.PersistentId) || !targets.TryGetValue(target.PersistentId, out var group))
                return;
            group.Remove(target);
            if (group.Count == 0)
                targets.Remove(target.PersistentId);
        }

        public static InteractionTarget Find(string id) => !string.IsNullOrEmpty(id) && targets.TryGetValue(id, out var group) && group.Count == 1 ? group[0] : null;
        public static bool HasDuplicateId(string id) => !string.IsNullOrEmpty(id) && targets.TryGetValue(id, out var group) && group.Count > 1;
        public static void CopyTargets(List<InteractionTarget> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            foreach (var group in targets.Values)
                if (group.Count == 1 && group[0])
                    destination.Add(group[0]);
        }
    }

    public sealed partial class InteractionTarget
    {
        public InteractionTargetState CaptureState(bool requireIdle = true)
        {
            if (requireIdle && ParticipantCount > 0)
                throw new InvalidOperationException("Finish or cancel interactions before saving this target.");
            if (string.IsNullOrEmpty(PersistentId) || InteractionWorld.HasDuplicateId(PersistentId))
                throw new InvalidOperationException("Assign a unique Persistent ID to this Interaction Target.");
            var state = new InteractionTargetState
            {
                targetId = PersistentId
            };
            foreach (var action in actions)
                if (action is IInteractionState saved)
                {
                    if (FindAction(action.ActionId) != action)
                        throw new InvalidOperationException("Action IDs must be unique within the target.");
                    state.actions.Add(new InteractionStateEntry { actionId = action.ActionId, type = action.GetType().FullName, json = saved.CaptureState() });
                }

            return state;
        }

        public InteractionAvailability ValidateState(InteractionTargetState state, bool requireIdle = true)
        {
            if (state == null || state.version != 1 || state.actions == null)
                return InteractionAvailability.Unavailable("Unsupported interaction state.");
            if (string.IsNullOrEmpty(PersistentId) || state.targetId != PersistentId || InteractionWorld.HasDuplicateId(PersistentId))
                return InteractionAvailability.Unavailable(InteractionFailure.DuplicateId, "Target identity does not match.");
            if (requireIdle && ParticipantCount > 0)
                return InteractionAvailability.Unavailable(InteractionFailure.Busy, "Finish or cancel active interactions before restoring.");
            for (int i = 0; i < state.actions.Count; i++)
            {
                var entry = state.actions[i];
                if (entry == null || string.IsNullOrEmpty(entry.actionId) || entry.json == null)
                    return InteractionAvailability.Unavailable("Missing action state.");
                for (int j = 0; j < i; j++)
                    if (state.actions[j].actionId == entry.actionId)
                        return InteractionAvailability.Unavailable("Duplicate action state.");
                var action = FindAction(entry.actionId);
                if (!(action is IInteractionState saved) || action.GetType().FullName != entry.type)
                    return InteractionAvailability.Unavailable("Unknown action state.");
                var check = saved.ValidateState(entry.json);
                if (!check.IsAvailable)
                    return check;
            }

            return InteractionAvailability.Available;
        }

        public bool TryRestoreState(InteractionTargetState state, out InteractionAvailability result, bool requireIdle = true)
        {
            result = ValidateState(state, requireIdle);
            if (!result.IsAvailable)
                return false;
            foreach (var entry in state.actions)
                ((IInteractionState)FindAction(entry.actionId)).RestoreState(entry.json);
            return true;
        }
    }
}
