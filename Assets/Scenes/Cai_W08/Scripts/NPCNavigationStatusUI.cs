using TMPro;
using UnityEngine;

/// <summary>Displays navigation intent without issuing commands or changing AI state.</summary>
public sealed class NPCNavigationStatusUI : MonoBehaviour
{
    [SerializeField] private TeamMember member;
    [SerializeField] private QH_NPCNavigation navigation;
    [SerializeField] private TMP_Text behaviorText;
    [SerializeField] private NPCIdleController idleController;
    [SerializeField] private TMP_Text manualControlLabel;

    private void OnEnable() => Refresh();
    private void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (behaviorText == null) return;
        string name = member == null ? "NPC" : member.SpeakerName;
        string behavior = "Unavailable";
        if (navigation != null && navigation.Intent != null)
        {
            if (navigation.Intent.IsEliminated) behavior = "Eliminated";
            else if (idleController != null && idleController.IsIdle) behavior = "Idle";
            else
            {
                switch (navigation.Intent.CurrentIntent)
                {
                    case QH_NPCIntent.Intent.PushBox: behavior = "Push"; break;
                    case QH_NPCIntent.Intent.AvoidBox: behavior = "Avoid"; break;
                    case QH_NPCIntent.Intent.AttackOther: behavior = "Attack"; break;
                }
            }
        }
        string label = name + "  |  " + behavior;
        if (behaviorText.text != label) behaviorText.text = label;
        if (manualControlLabel != null && navigation != null && navigation.Intent != null)
        {
            string intent = behavior == "Idle" || behavior == "Eliminated"
                ? behavior : navigation.Intent.CurrentIntent.ToString();
            string controlLabel = "NPC " + navigation.NpcNumber + " - " + intent;
            if (manualControlLabel.text != controlLabel) manualControlLabel.text = controlLabel;
        }
    }
}
