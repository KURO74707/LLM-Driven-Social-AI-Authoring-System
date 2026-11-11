using UnityEditor;
using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    public sealed class SocialAiDemoWindow : EditorWindow
    {
        [SerializeField] private int trust = 80;
        [SerializeField] private int loyalty = 80;
        [SerializeField] private int minimumConfidence = 60;
        [SerializeField] private int minimumLoyalty = 60;
        private string trace;
        private Vector2 scroll;

        [MenuItem("Tools/Social AI/Alex Sam Morgan Demo")]
        private static void Open() => GetWindow<SocialAiDemoWindow>("Social AI Demo");

        private void OnGUI()
        {
            trust = EditorGUILayout.IntSlider("Morgan → Sam trust", trust, 0, 100);
            loyalty = EditorGUILayout.IntSlider("Morgan faction loyalty", loyalty, 0, 100);
            minimumConfidence = EditorGUILayout.IntSlider("Minimum confidence", minimumConfidence, 0, 100);
            minimumLoyalty = EditorGUILayout.IntSlider("Minimum loyalty", minimumLoyalty, 0, 100);

            if (GUILayout.Button("Run both rule settings"))
                trace = string.Join("\n\n", AlexSamMorganDemo.Run(trust, loyalty, minimumConfidence, minimumLoyalty));

            if (string.IsNullOrEmpty(trace)) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(trace, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
