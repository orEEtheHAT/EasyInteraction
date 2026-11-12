using System;
using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Interaction Point")]
    public sealed class InteractionPoint : MonoBehaviour
    {
        [Tooltip("Optional label for this part, such as Handle or Drawer.")]
        public string displayName;
        [Tooltip("Empty offers every action. Otherwise only these stable Action IDs are offered at this point.")]
        public string[] actionIds = Array.Empty<string>();
        [Tooltip("Reserve this point as a participant position, for example a seat at a workbench.")]
        public bool participantPosition;
        public bool Allows(InteractionAction action)
        {
            if (actionIds == null || actionIds.Length == 0)
                return true;
            for (int i = 0; i < actionIds.Length; i++)
                if (actionIds[i] == action.ActionId)
                    return true;
            return false;
        }

        private void OnEnable() => GetComponentInParent<InteractionTarget>()?.Refresh();
        private void OnDisable() => GetComponentInParent<InteractionTarget>()?.Refresh();
    }
}
