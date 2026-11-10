using UnityEngine;

/// <summary>Use this on the goal trigger or the kill zone below the map.</summary>
public class QH_Zone : MonoBehaviour
{
    public enum ZoneType
    {
        BoxGoal,
        FallZone
    }

    [SerializeField] private ZoneType zoneType;
    [SerializeField] private Rigidbody boxToCheck;
    [SerializeField] private QH_RoundUI roundUI;

    private void OnTriggerEnter(Collider other)
    {
        if (zoneType == ZoneType.BoxGoal)
        {
            if (boxToCheck != null && other.attachedRigidbody == boxToCheck)
                roundUI?.SetMessage("The box reached the goal!");
            return;
        }

        if (boxToCheck != null && other.attachedRigidbody == boxToCheck)
        {
            roundUI?.SetMessage("The box fell! Reset Scene to try again.");
            return;
        }
        QH_NPCIntent fallenNpc = other.GetComponentInParent<QH_NPCIntent>();
        if (fallenNpc == null || fallenNpc.IsEliminated) return;

        fallenNpc.Eliminate();
        roundUI?.SetMessage(fallenNpc.name + " fell off the map!");
        fallenNpc.gameObject.SetActive(false);
    }
}
