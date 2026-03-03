using UnityEditor;
using UnityEngine;

namespace ProjectGuild.View
{
    [CustomEditor(typeof(OverworldPreview))]
    public class OverworldPreviewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (GUILayout.Button("Refresh Preview", GUILayout.Height(30)))
            {
                ((OverworldPreview)target).Rebuild();
            }
        }
    }
}
