using UnityEngine;

/// <summary>Lets any willing NPC push the Rigidbody box after reaching a push point.</summary>
public class QH_BoxPush : MonoBehaviour
{
    [Header("Box")]
    [SerializeField] private Rigidbody boxBody;
    [SerializeField] private Transform targetPoint;
    [SerializeField] private Transform leftPushPoint;
    [SerializeField] private Transform rightPushPoint;
    [SerializeField] private QH_NPCGroup group;

    [Header("Push tuning")]
    [SerializeField, Min(0.1f)] private float reachDistance = 1.5f;
    [SerializeField, Min(0f)] private float pushForce = 35f;
    [SerializeField, Min(0f)] private float stopDistance = 1.5f;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool hasReachedTarget;
    public int ActivePushers { get; private set; }

    private void Awake()
    {
        if (boxBody == null) boxBody = GetComponent<Rigidbody>();
        if (boxBody != null)
        {
            startPosition = boxBody.position;
            startRotation = boxBody.rotation;
        }
    }

    private void FixedUpdate()
    {
        ActivePushers = 0;
        if (hasReachedTarget || boxBody == null || targetPoint == null) return;
        if (Vector3.Distance(boxBody.position, targetPoint.position) <= stopDistance)
        {
            hasReachedTarget = true;
            return;
        }

        if (group == null) return;
        foreach (QH_NPCNavigation npc in group.Npcs)
            PushFrom(npc);
    }

    public Transform GetPushPoint(int npcNumber)
    {
        if (npcNumber % 2 == 1) return leftPushPoint;
        return rightPushPoint;
    }

    public bool IsNearPushPoint(QH_NPCNavigation npc)
    {
        if (npc == null) return false;
        Transform pushPoint = GetPushPoint(npc.NpcNumber);
        if (pushPoint == null) return false;

        // The push point is above the floor. Only the ground distance matters here.
        Vector3 difference = npc.transform.position - pushPoint.position;
        difference.y = 0f;
        return difference.magnitude <= reachDistance;
    }

    private void PushFrom(QH_NPCNavigation npc)
    {
        if (npc == null || !npc.CanPushBox()) return;
        if (!IsNearPushPoint(npc)) return;

        Vector3 direction = targetPoint.position - boxBody.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            boxBody.AddForce(direction.normalized * pushForce, ForceMode.Force);
            ActivePushers++;
        }
    }

    public void ResetBox()
    {
        if (boxBody == null) return;
        boxBody.linearVelocity = Vector3.zero;
        boxBody.angularVelocity = Vector3.zero;
        boxBody.position = startPosition;
        boxBody.rotation = startRotation;
        boxBody.Sleep();
        hasReachedTarget = false;
    }
}
