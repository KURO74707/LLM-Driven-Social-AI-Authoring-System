using TMPro;
using UnityEngine;

public sealed class NPCActionUI : MonoBehaviour
{
    [SerializeField] private NPCDialogue npc;
    [SerializeField] private TMP_Text actionText;
    private void OnEnable()
    {
        if (npc != null) npc.OnActionChanged += DisplayAction;
        DisplayAction(npc == null ? "Idle" : npc.CurrentAction);
    }
    private void OnDisable()
    {
        if (npc != null) npc.OnActionChanged -= DisplayAction;
    }
    public void DisplayAction(string action)
    {
        if (actionText != null) actionText.text = string.IsNullOrWhiteSpace(action) ? "Idle" : action;
    }
    public bool CanDisplayAction(string action)
    {
        if (actionText == null) return false;
        string previous = actionText.text;
        try
        {
            var size = actionText.GetPreferredValues(action, actionText.rectTransform.rect.width, Mathf.Infinity);
            return size.y <= actionText.rectTransform.rect.height;
        }
        finally { actionText.SetText(previous); }
    }
}
