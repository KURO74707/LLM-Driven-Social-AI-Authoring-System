using System;
using UnityEngine;

/// <summary>Authoritative NPC identity and clamped emotion, editable in the Inspector and by the AI Director.</summary>
[DisallowMultipleComponent]
public sealed class NPCState : MonoBehaviour
{
    public enum EmotionControlMode
    {
        Manual,
        [InspectorName("AI Director")] AIDirector
    }

    [Header("Identity & Motivation")]
    [TextArea(6, 18)] [SerializeField] private string personaPrompt = string.Empty;
    [TextArea(3, 8)] [SerializeField] private string goalPrompt =
        "Help get the heavy box onto the target platform while preserving your own judgment and safety. Another NPC is required before it can be lifted.";
    [Header("Emotional / Relationship State")]
    [Tooltip("Manual keeps Inspector values fixed during conversation. AI Director evaluates each player message. Switching preserves the current values.")]
    [SerializeField] private EmotionControlMode controlMode = EmotionControlMode.AIDirector;
    [Range(0, 100)] [SerializeField] private float anger = 20;
    [Range(0, 100)] [SerializeField] private float trustInPlayer = 50;
    [Tooltip("Directional trust in the assigned teammate. Unused by the Iteration 1 dialogue flow.")]
    [Range(0, 100)] [SerializeField] private float trustInTeammate = 50;

    public event Action Changed;
    public EmotionControlMode ControlMode
    {
        get => controlMode;
        set { if (controlMode == value) return; controlMode = value; NotifyChanged(); }
    }
    public bool UsesAIDirector => controlMode == EmotionControlMode.AIDirector;
    public string PersonaPrompt { get => personaPrompt; set { personaPrompt = value ?? string.Empty; NotifyChanged(); } }
    public string GoalPrompt { get => goalPrompt; set { goalPrompt = value ?? string.Empty; NotifyChanged(); } }
    public float Anger { get => Clamp(anger); set { anger = Clamp(value); NotifyChanged(); } }
    public float TrustInPlayer { get => Clamp(trustInPlayer); set { trustInPlayer = Clamp(value); NotifyChanged(); } }
    public float TrustInTeammate { get => Clamp(trustInTeammate); set { trustInTeammate = Clamp(value); NotifyChanged(); } }
    private bool inspectorChanged;
    private float previousAnger = float.NaN;
    private float previousTrust = float.NaN;
    private float previousTeammateTrust = float.NaN;
    private EmotionControlMode previousControlMode;

    public void SetEmotions(float newAnger, float newTrust)
    {
        anger = Clamp(newAnger);
        trustInPlayer = Clamp(newTrust);
        NotifyChanged();
    }

    public void SetEmotions(float newAnger, float newPlayerTrust, float newTeammateTrust)
    {
        anger = Clamp(newAnger);
        trustInPlayer = Clamp(newPlayerTrust);
        trustInTeammate = Clamp(newTeammateTrust);
        NotifyChanged();
    }

    private static float Clamp(float value) => float.IsNaN(value) ? 0 : Mathf.Clamp(value, 0, 100);
    private void OnEnable() => NotifyChanged();
    private void OnValidate()
    {
        anger = Clamp(anger);
        trustInPlayer = Clamp(trustInPlayer);
        trustInTeammate = Clamp(trustInTeammate);
        // OnValidate can run off the main thread. Dispatch UI changes on the next frame.
        inspectorChanged = true;
    }
    private void LateUpdate()
    {
        if (inspectorChanged || previousAnger != anger || previousTrust != trustInPlayer || previousTeammateTrust != trustInTeammate || previousControlMode != controlMode)
            NotifyChanged();
    }
    private void NotifyChanged()
    {
        anger = Clamp(anger);
        trustInPlayer = Clamp(trustInPlayer);
        trustInTeammate = Clamp(trustInTeammate);
        previousAnger = anger;
        previousTrust = trustInPlayer;
        previousTeammateTrust = trustInTeammate;
        previousControlMode = controlMode;
        inspectorChanged = false;
        Changed?.Invoke();
    }
}
