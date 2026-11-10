using UnityEngine;

/// <summary>Checks for a nearby NPC while this NPC is set to attack.</summary>
public class QH_NPCShove : MonoBehaviour
{
    [SerializeField] private QH_NPCIntent ownIntent;
    [SerializeField] private QH_NPCNavigation ownNavigation;
    [SerializeField, Min(0.1f)] private float attackRange = 1.2f;
    [SerializeField, Min(0.1f)] private float secondsBetweenShoves = 1f;

    private float nextShoveTime;

    private void Awake()
    {
        if (ownIntent == null) ownIntent = GetComponent<QH_NPCIntent>();
        if (ownNavigation == null) ownNavigation = GetComponent<QH_NPCNavigation>();
    }

    private void FixedUpdate()
    {
        if (ownIntent == null || ownIntent.IsEliminated ||
            ownIntent.CurrentIntent != QH_NPCIntent.Intent.AttackOther ||
            Time.time < nextShoveTime)
            return;

        QH_NPCNavigation chosenTarget = ownNavigation != null ? ownNavigation.GetPartner() : null;
        if (chosenTarget == null) return;

        Collider[] nearby = Physics.OverlapSphere(transform.position, attackRange);
        foreach (Collider other in nearby)
        {
            QH_NPCKnockback target = other.GetComponentInParent<QH_NPCKnockback>();
            if (target == null || target.gameObject != chosenTarget.gameObject) continue;

            Vector3 direction = target.transform.position - transform.position;
            target.ApplyShove(direction);
            nextShoveTime = Time.time + secondsBetweenShoves;
            break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
