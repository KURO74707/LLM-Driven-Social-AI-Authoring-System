using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Cai_W10 tuning and two-person gate around the unchanged QH pushing implementation.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(QH_BoxPush), typeof(Rigidbody))]
[DefaultExecutionOrder(-500)]
public sealed class CooperativePushController : MonoBehaviour
{
    [Header("Existing scene references")]
    [SerializeField] private QH_BoxPush boxPush;
    [SerializeField] private Rigidbody boxBody;
    [SerializeField] private QH_NPCGroup group;
    [SerializeField] private QH_NPCNavigation alex;
    [SerializeField] private QH_NPCNavigation sam;
    [SerializeField] private TMP_Text pusherCountText;
    [Header("Optional moving bridge")]
    [SerializeField] private BothW10MovingBridge boxBridge;
    [SerializeField] private float bridgeEntryZ = 14.5f;
    [SerializeField] private float bridgeExitZ = 22.5f;

    [Header("Speeds (world units per second)")]
    [Min(0.1f)] [SerializeField] private float walkingSpeed = 1.5f;
    [Min(0.025f)] [SerializeField] private float pushingSpeed = 0.6f;
    [Tooltip("Horizontal box velocity limit after each physics step; vertical gravity is preserved.")]
    [Min(0.01f)] [SerializeField] private float boxSpeedLimit = 0.5f;

    public float WalkingSpeed => Positive(walkingSpeed, 1.5f, 0.1f);
    public float PushingSpeed => Mathf.Clamp(Positive(pushingSpeed, 0.6f, 0.025f), 0.025f, WalkingSpeed * 0.95f);
    public float BoxSpeedLimit => Mathf.Min(Positive(boxSpeedLimit, 0.5f, 0.01f), PushingSpeed);
    public int ReadyPushers { get; private set; }
    public bool IsPushingTogether { get; private set; }
    public bool IsWaitingForBridge { get; private set; }
    public int ActivePushers => IsPushingTogether && boxPush != null && boxPush.enabled ? boxPush.ActivePushers : 0;

    private RigidbodyConstraints originalConstraints;
    private bool originalPushEnabled;
    private NavMeshAgent alexAgent;
    private NavMeshAgent samAgent;
    private float originalAlexSpeed;
    private float originalSamSpeed;
    private Coroutine physicsCompletion;
    private bool initialized;

    private static float Positive(float value, float fallback, float minimum)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (boxPush == null) boxPush = GetComponent<QH_BoxPush>();
        if (boxBody == null) boxBody = GetComponent<Rigidbody>();
        if (boxPush == null || boxBody == null || group == null || alex == null || sam == null
            || alex == sam || alex.NpcNumber != 1 || sam.NpcNumber != 2
            || boxPush.gameObject != gameObject || boxBody.gameObject != gameObject)
        {
            Debug.LogError("Assign the existing Cai_W10 box, group, Alex (1), and Sam (2) to CooperativePushController.", this);
            enabled = false;
            return;
        }
        originalConstraints = boxBody.constraints;
        originalPushEnabled = boxPush.enabled;
        alexAgent = alex.GetComponent<NavMeshAgent>();
        samAgent = sam.GetComponent<NavMeshAgent>();
        if (alexAgent != null) originalAlexSpeed = alexAgent.speed;
        if (samAgent != null) originalSamSpeed = samAgent.speed;
        initialized = true;
        RefreshGate();
        physicsCompletion = StartCoroutine(AfterPhysics());
    }

    private bool Ready(QH_NPCNavigation npc)
    {
        if (npc == null || group == null || !npc.isActiveAndEnabled || !group.Npcs.Contains(npc) || !npc.CanPushBox()) return false;
        var idle = npc.GetComponent<NPCIdleController>();
        var agent = npc.GetComponent<NavMeshAgent>();
        var body = npc.GetComponent<Rigidbody>();
        return (idle == null || !idle.IsIdle) && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
            && (body == null || body.isKinematic) && boxPush.IsNearPushPoint(npc);
    }

    private void FixedUpdate() => RefreshGate();
    private void Update() => UpdateSpeeds();

    private void RefreshGate()
    {
        if (!initialized || boxPush == null || boxBody == null) return;
        ReadyPushers = (Ready(alex) ? 1 : 0) + (Ready(sam) ? 1 : 0);
        IsWaitingForBridge = boxBridge != null && !boxBridge.IsOpen &&
            boxBody.position.z >= bridgeEntryZ && boxBody.position.z < bridgeExitZ;
        IsPushingTogether = ReadyPushers == 2 && originalPushEnabled && !boxBody.isKinematic && !IsWaitingForBridge;
        // Runs before the original QH FixedUpdate; it can add force only with both
        // distinct, willing, upright NPCs at their own assigned push points.
        boxPush.enabled = IsPushingTogether;
        var constraints = IsPushingTogether ? originalConstraints
            : originalConstraints | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        if (boxBody.constraints != constraints) boxBody.constraints = constraints;
        LimitBoxVelocity();
        UpdateSpeeds();
    }

    private void UpdateSpeeds()
    {
        if (!initialized) return;
        float speed = IsPushingTogether ? PushingSpeed : WalkingSpeed;
        if (alexAgent != null && alexAgent.speed != speed) alexAgent.speed = speed;
        if (samAgent != null && samAgent.speed != speed) samAgent.speed = speed;
    }

    private IEnumerator AfterPhysics()
    {
        var wait = new WaitForFixedUpdate();
        while (true)
        {
            yield return wait;
            // Re-check after movement/physics too, so a shove or departure removes
            // cooperation immediately. Horizontal constraints prevent solo collision
            // pushing, not just the force emitted by QH_BoxPush.
            RefreshGate();
        }
    }

    private void LimitBoxVelocity()
    {
        if (boxBody == null || boxBody.isKinematic) return;
        Vector3 velocity = boxBody.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0, velocity.z);
        Vector3 limited = IsPushingTogether ? Vector3.ClampMagnitude(horizontal, BoxSpeedLimit) : Vector3.zero;
        if ((horizontal - limited).sqrMagnitude > 0.000001f)
            boxBody.linearVelocity = new Vector3(limited.x, velocity.y, limited.z);
    }

    private void LateUpdate()
    {
        // QH_RoundUI still owns its other labels and ResetRound. Its box counter
        // would otherwise retain the last count while QH_BoxPush is gated off.
        if (initialized && pusherCountText != null)
            pusherCountText.text = "NPCs pushing box: " + ActivePushers;
    }

    private void OnDisable()
    {
        if (!initialized) return;
        if (physicsCompletion != null) StopCoroutine(physicsCompletion);
        physicsCompletion = null;
        if (boxBody != null) boxBody.constraints = originalConstraints;
        if (boxPush != null) boxPush.enabled = originalPushEnabled;
        if (alexAgent != null) alexAgent.speed = originalAlexSpeed;
        if (samAgent != null) samAgent.speed = originalSamSpeed;
        IsPushingTogether = false;
        IsWaitingForBridge = false;
        ReadyPushers = 0;
        initialized = false;
    }

    private void OnValidate()
    {
        walkingSpeed = WalkingSpeed;
        pushingSpeed = PushingSpeed;
        boxSpeedLimit = BoxSpeedLimit;
    }
}
