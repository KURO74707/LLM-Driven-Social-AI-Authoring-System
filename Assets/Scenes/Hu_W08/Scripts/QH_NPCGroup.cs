using System.Collections.Generic;
using UnityEngine;

/// <summary>Keeps track of the NPCs in this round.</summary>
public class QH_NPCGroup : MonoBehaviour
{
    [SerializeField] private List<QH_NPCNavigation> npcs = new List<QH_NPCNavigation>();

    public List<QH_NPCNavigation> Npcs
    {
        get { return npcs; }
    }

    public void Register(QH_NPCNavigation npc)
    {
        if (npc == null || npcs.Contains(npc)) return;

        foreach (QH_NPCNavigation other in npcs)
        {
            if (other != null && other.NpcNumber == npc.NpcNumber)
                Debug.LogWarning("Two NPCs have number " + npc.NpcNumber + ". Give each NPC a different number.", npc);
        }

        npcs.Add(npc);
    }

    public void Unregister(QH_NPCNavigation npc)
    {
        npcs.Remove(npc);
    }

    public QH_NPCNavigation FindPartner(QH_NPCNavigation self, int wantedNumber)
    {
        if (wantedNumber > 0)
        {
            foreach (QH_NPCNavigation npc in npcs)
            {
                if (npc != null && npc != self && npc.NpcNumber == wantedNumber &&
                    npc.Intent != null && !npc.Intent.IsEliminated)
                    return npc;
            }
            return null;
        }

        QH_NPCNavigation closest = null;
        float closestDistance = float.MaxValue;

        foreach (QH_NPCNavigation npc in npcs)
        {
            if (npc == null || npc == self || npc.Intent == null || npc.Intent.IsEliminated)
                continue;
            float distance = Vector3.Distance(self.transform.position, npc.transform.position);
            if (distance < closestDistance)
            {
                closest = npc;
                closestDistance = distance;
            }
        }

        return closest;
    }
}
