using UnityEngine;
using UnityEngine.AI;

/// <summary>Cai_W10-only Idle gate around the existing navigation, push and shove behaviors.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(QH_NPCIntent), typeof(QH_NPCNavigation))]
[DefaultExecutionOrder(-1000)]
public sealed class NPCIdleController : MonoBehaviour
{
    [SerializeField] private QH_NPCIntent intent;
    [SerializeField] private QH_NPCNavigation navigation;
    [SerializeField] private QH_NPCShove shove;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Rigidbody body;
    [Tooltip("Applies only when this Cai_W10 NPC starts playing. Reset Round retains its original Push behavior.")]
    [SerializeField] private bool startIdle = true;

    private bool idle;
    private bool resumeNavigation;
    private bool resumeShove;
    private bool ownsAgentStop;
    private bool previousAgentStop;

    public bool IsIdle => Application.isPlaying ? idle : startIdle;

    private void Awake()
    {
        if (intent == null) intent = GetComponent<QH_NPCIntent>();
        if (navigation == null) navigation = GetComponent<QH_NPCNavigation>();
        if (shove == null) shove = GetComponent<QH_NPCShove>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (body == null) body = GetComponent<Rigidbody>();
        if (startIdle) ChooseIdle();
    }

    public void ChooseIdle()
    {
        if (intent == null || intent.IsEliminated) return;
        if (!idle)
        {
            resumeNavigation = navigation != null && navigation.enabled;
            resumeShove = shove != null && shove.enabled;
            idle = true;
        }
        // AvoidBox cannot contribute box force. Movement is separately suspended,
        // so no avoidance destination is executed while this gate is active.
        intent.SetAvoidBox();
        if (navigation != null) navigation.enabled = false;
        if (shove != null) shove.enabled = false;
        StopAgent();
    }

    public void ChoosePush() => Resume(QH_NPCIntent.Intent.PushBox);
    public void ChooseAvoid() => Resume(QH_NPCIntent.Intent.AvoidBox);
    public void ChooseAttack() => Resume(QH_NPCIntent.Intent.AttackOther);

    private void Resume(QH_NPCIntent.Intent next)
    {
        if (intent == null || intent.IsEliminated) return;
        intent.SetIntent(next);
        ReleaseIdle();
    }

    private void Update()
    {
        if (idle)
        {
            // The original ResetRound restores PushBox. Honor that reset without
            // resetting AI state or replacing the teammate's reset implementation.
            if (intent != null && !intent.IsEliminated && intent.CurrentIntent != QH_NPCIntent.Intent.AvoidBox)
                ReleaseIdle();
            else StopAgent();
        }
        else RestoreAgentStop();
    }

    private bool CanControlAgent => intent != null && !intent.IsEliminated
        && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
        && (body == null || body.isKinematic);

    private void StopAgent()
    {
        // Never re-enable an agent, freeze a dynamic body, or undo knockback.
        if (!CanControlAgent) return;
        if (!ownsAgentStop) { previousAgentStop = agent.isStopped; ownsAgentStop = true; }
        if (!agent.isStopped) agent.isStopped = true;
        if (agent.hasPath || agent.pathPending) agent.ResetPath();
        if (agent.velocity.sqrMagnitude > 0.0001f) agent.velocity = Vector3.zero;
    }

    private void ReleaseIdle()
    {
        if (idle)
        {
            idle = false;
            if (resumeNavigation && navigation != null) navigation.enabled = true;
            if (resumeShove && shove != null) shove.enabled = true;
        }
        RestoreAgentStop();
    }

    private void RestoreAgentStop()
    {
        // If a shove disabled the agent, defer restoration until the original
        // ResetCharacter makes it safe; do not intervene in physics recovery.
        if (!ownsAgentStop || !CanControlAgent) return;
        agent.isStopped = previousAgentStop;
        ownsAgentStop = false;
    }

    private void OnDisable()
    {
        if (Application.isPlaying && gameObject.activeInHierarchy) ReleaseIdle();
    }
}
