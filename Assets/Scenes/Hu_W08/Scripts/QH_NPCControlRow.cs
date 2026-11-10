using UnityEngine;
using TMPro;

/// <summary>Connects one row of existing UI buttons to one NPC.</summary>
public class QH_NPCControlRow : MonoBehaviour
{
    [SerializeField] private QH_NPCNavigation npc;
    [SerializeField] private TMP_Text label;

    private void Update()
    {
        if (npc != null && npc.Intent != null && label != null)
        {
            if (npc.Intent.IsEliminated)
                label.text = "NPC " + npc.NpcNumber + " - Eliminated";
            else
                label.text = "NPC " + npc.NpcNumber + " - " + npc.Intent.CurrentIntent;
        }
    }

    public void ChoosePush()
    {
        if (npc != null && npc.Intent != null)
            npc.Intent.SetPushBox();
    }

    public void ChooseAvoid()
    {
        if (npc != null && npc.Intent != null)
            npc.Intent.SetAvoidBox();
    }

    public void ChooseAttack()
    {
        if (npc != null && npc.Intent != null)
            npc.Intent.SetAttackOther();
    }
}
