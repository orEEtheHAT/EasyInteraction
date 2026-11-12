using EasyFeedbacks;
using UnityEditor;
using UnityEngine;

namespace EasyInteraction.Integrations.Feedbacks.Editor
{
    [CustomEditor(typeof(InteractionFeedbacks))]
    public sealed class InteractionFeedbacksEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var source = ((InteractionFeedbacks)target).ContextProvider;
            if (source && !(source is IFeedbackContextProvider))
                EditorGUILayout.HelpBox("Context Provider must implement IFeedbackContextProvider, for example Player Feedback Context. Leave it empty to search the linked Player Root and its parents.", MessageType.Error);
            else if (source && !source.isActiveAndEnabled)
                EditorGUILayout.HelpBox("The assigned Context Provider is disabled. Its player/device context will not be applied until it is enabled.", MessageType.Warning);
        }
    }
}
