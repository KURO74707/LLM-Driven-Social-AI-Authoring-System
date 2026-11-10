using UnityEngine;
using UnityEngine.AI;

/// <summary>Chooses a NavMesh destination from this NPC's current intention.</summary>
public class QH_NPCNavigation : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField, Min(1)] private int npcNumber = 1;
    [Tooltip("0 means choose the nearest available NPC.")]
    [SerializeField, Min(0)] private int partnerNumber = 0;

    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private QH_NPCIntent intent;
    [SerializeField] private QH_NPCKnockback knockback;
    [SerializeField] private QH_NPCGroup group;
    [SerializeField] private QH_BoxPush boxPush;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float fleeStep = 5f;
    [SerializeField, Min(0.1f)] private float secondsBetweenTargets = 0.25f;
    [SerializeField] private Vector3 arenaCenter = Vector3.zero;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float nextTargetTime;
    private Vector3 avoidDestination;
    private bool hasAvoidDestination;

    public int NpcNumber { get { return npcNumber; } }
    public QH_NPCIntent Intent { get { return intent; } }

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (intent == null) intent = GetComponent<QH_NPCIntent>();
        if (knockback == null) knockback = GetComponent<QH_NPCKnockback>();
        if (group == null) group = FindAnyObjectByType<QH_NPCGroup>();
        if (boxPush == null) boxPush = FindAnyObjectByType<QH_BoxPush>();

        startPosition = transform.position;
        startRotation = transform.rotation;
        if (group != null) group.Register(this);
    }

    private void OnDestroy()
    {
        if (group != null) group.Unregister(this);
    }

    private void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh ||
            intent == null || intent.IsEliminated)
            return;

        FaceBoxWhenReady();
        if (Time.time < nextTargetTime) return;

        nextTargetTime = Time.time + secondsBetweenTargets;
        Vector3 destination;
        if (intent.CurrentIntent != QH_NPCIntent.Intent.AvoidBox)
            hasAvoidDestination = false;

        if (intent.CurrentIntent == QH_NPCIntent.Intent.AttackOther)
        {
            QH_NPCNavigation partner = GetPartner();
            if (partner == null) return;
            // Approach from the inside so the shove tends to point toward the map edge.
            Vector3 outward = partner.transform.position - arenaCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f)
                destination = partner.transform.position;
            else
                destination = partner.transform.position - outward.normalized * 0.6f;
        }
        else if (intent.CurrentIntent == QH_NPCIntent.Intent.AvoidBox)
        {
            if (boxPush == null) return;
            if (!hasAvoidDestination)
            {
                Vector3 away = transform.position - boxPush.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;

                Vector3 wantedPoint = transform.position + away.normalized * fleeStep;
                NavMeshHit edge;
                if (NavMesh.Raycast(transform.position, wantedPoint, out edge, agent.areaMask))
                    wantedPoint = edge.position - away.normalized * 0.5f;

                NavMeshHit safePoint;
                if (!NavMesh.SamplePosition(wantedPoint, out safePoint, 1f, agent.areaMask)) return;
                avoidDestination = safePoint.position;
                hasAvoidDestination = true;
            }
            destination = avoidDestination;
        }
        else
        {
            if (boxPush == null || boxPush.GetPushPoint(npcNumber) == null) return;
            destination = boxPush.GetPushPoint(npcNumber).position;
        }

        NavMeshHit hit;
        if (NavMesh.SamplePosition(destination, out hit, 2f, agent.areaMask))
            agent.SetDestination(hit.position);
    }

    private void FaceBoxWhenReady()
    {
        bool atBox = boxPush != null && CanPushBox() && boxPush.IsNearPushPoint(this);

        agent.updateRotation = !atBox;
        if (!atBox) return;

        Vector3 look = boxPush.transform.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);
    }

    public QH_NPCNavigation GetPartner()
    {
        if (group == null) return null;
        return group.FindPartner(this, partnerNumber);
    }

    public bool CanPushBox()
    {
        if (intent == null || intent.IsEliminated ||
            intent.CurrentIntent != QH_NPCIntent.Intent.PushBox)
            return false;

        return true;
    }

    public void ResetNpc()
    {
        gameObject.SetActive(true);
        if (knockback != null) knockback.ResetCharacter(startPosition, startRotation);
        else if (intent != null) intent.ResetIntent();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.updateRotation = true;
            agent.Warp(startPosition);
            agent.ResetPath();
        }
        nextTargetTime = 0f;
        hasAvoidDestination = false;
    }
}
