using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Interaction Highlight")]
    public sealed class InteractionHighlight : MonoBehaviour
    {
        [SerializeField]
        private InteractionActor actor;
        [SerializeField, Tooltip("Enable a separate outline/marker object. Its renderer/material is configured by the project.")]
        private GameObject highlight;
        private InteractionTarget target;
        private void OnEnable()
        {
            target = GetComponentInParent<InteractionTarget>();
            if (actor)
                actor.TargetChanged += Changed;
            Changed(actor ? actor.CurrentTarget : null);
        }

        private void OnDisable()
        {
            if (actor)
                actor.TargetChanged -= Changed;
            if (highlight && highlight != gameObject)
                highlight.SetActive(false);
        }

        private void Changed(InteractionTarget selected)
        {
            if (highlight && highlight != gameObject)
                highlight.SetActive(selected && selected == target);
        }

        public void Configure(InteractionActor player, GameObject visual)
        {
            if (actor)
                actor.TargetChanged -= Changed;
            actor = player;
            highlight = visual;
            target = GetComponentInParent<InteractionTarget>();
            if (isActiveAndEnabled && actor)
                actor.TargetChanged += Changed;
            Changed(actor ? actor.CurrentTarget : null);
        }
    }
}
