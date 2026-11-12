using UnityEditor;
using UnityEngine;

namespace EasyInteraction.Editor
{
    [CustomEditor(typeof(InteractionPresenter))]
    public sealed class InteractionPresenterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var source = ((InteractionPresenter)target).InputSource;
            if (source && !(source is IInteractionInputSource))
                EditorGUILayout.HelpBox("Input Source must implement IInteractionInputSource. Assign your input adapter or leave this field empty for automatic resolution.", MessageType.Error);
            else if (source && !source.isActiveAndEnabled)
                EditorGUILayout.HelpBox("The assigned input source is disabled. Binding presentation and hold-toggle settings are unavailable until it is enabled.", MessageType.Info);
            else if (!source)
                EditorGUILayout.HelpBox("An active input source on this actor supplies binding text, icon and hold-toggle settings. An adapter takes priority over standalone Interaction Input.", MessageType.Info);
        }
    }
}
