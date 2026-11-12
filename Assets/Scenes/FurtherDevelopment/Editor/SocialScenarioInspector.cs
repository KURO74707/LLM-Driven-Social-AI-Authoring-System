using UnityEditor;
using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    [CustomEditor(typeof(SocialScenarioAsset))]
    public sealed class SocialScenarioInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var scenario = (SocialScenarioAsset)target;
            EditorGUILayout.Space();
            if (!scenario.TryValidate(out var error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
            else
                EditorGUILayout.HelpBox("Assign this asset to Playground's Scenario field. Enter Play or use Reset / Reload configuration to apply edits.", MessageType.Info);
            EditorGUILayout.HelpBox("Scenarios reference shared NPC, relationship and rule assets. Duplicate those assets too when you need independent values. Background and personality are reserved for later LLM integration.", MessageType.Info);
        }
    }
}
