using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Dispatches accepted Cai_W10 responses; never interprets action-description text.</summary>
[DisallowMultipleComponent]
public sealed class NPCActionBridge : MonoBehaviour
{
    [Serializable]
    private sealed class Binding
    {
        public TeamMember member;
        public QH_NPCNavigation navigation;
        public NPCIdleController idleController;
    }

    [SerializeField] private Binding alex = new Binding();
    [SerializeField] private Binding sam = new Binding();
    [SerializeField] private Rigidbody boxBody;
    [SerializeField] private Transform boxTarget;
    [SerializeField] private BothW10MovingBridge teammateBridge;
    [SerializeField] private BothW10MovingBridge boxBridge;
    private readonly Dictionary<TeamMember, int> lastAcceptedTurn = new Dictionary<TeamMember, int>();
    public int AppliedActionCount { get; private set; }
    public int AcceptedResponseCount { get; private set; }

    private bool Resolve(TeamMember origin, out Binding binding, out string error)
    {
        binding = null;
        error = "The action's originating TeamMember is not a configured physical NPC.";
        if (origin == null) return false;
        int number;
        if (origin == alex.member && origin.SpeakerId == "alex") { binding = alex; number = 1; }
        else if (origin == sam.member && origin.SpeakerId == "sam") { binding = sam; number = 2; }
        else return false;
        if (binding.navigation == null || binding.idleController == null
            || binding.navigation.NpcNumber != number
            || !origin.transform.IsChildOf(binding.navigation.transform)
            || binding.idleController.gameObject != binding.navigation.gameObject
            || binding.navigation.Intent == null)
        { error = "Assign the correct member, navigation and Idle controller for " + origin.SpeakerName + "."; return false; }
        error = null;
        return true;
    }

    public string ValidateSelection(TeamMember origin, NPCResponse response)
    {
        if (response == null || !response.HasActionType || !NPCResponse.IsAllowedActionType(response.action_type))
            return "Include a valid action_type: push, avoid, attack, idle, or JSON null.";
        if (!Resolve(origin, out var binding, out var error)) return error;
        // Null is deliberately a no-op, even when movement is temporarily unavailable.
        if (response.action_type == null) return null;
        if (!isActiveAndEnabled || !binding.idleController.isActiveAndEnabled
            || !binding.navigation.gameObject.activeInHierarchy || binding.navigation.Intent.IsEliminated)
            return "Your physical controller is unavailable. Return action_type null without claiming a new action.";
        if (response.action_type == "idle") return null;
        var agent = binding.navigation.GetComponent<NavMeshAgent>();
        var body = binding.navigation.GetComponent<Rigidbody>();
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || (body != null && !body.isKinematic)
            || (!binding.navigation.enabled && !binding.idleController.IsIdle))
            return "Navigation is unavailable or knockback is active. Choose idle or null; do not claim to move.";
        if ((response.action_type == "push" || response.action_type == "avoid") && boxBody == null)
            return "The box reference is unavailable. Choose idle or null.";
        if (response.action_type == "attack")
        {
            var other = binding == alex ? sam : alex;
            var target = binding.navigation.GetPartner();
            if (other.navigation == null || target != other.navigation || !target.gameObject.activeInHierarchy
                || target.Intent == null || target.Intent.IsEliminated || binding.navigation.GetComponent<QH_NPCShove>() == null)
                return "Your configured teammate cannot be attacked now. Choose idle or null; do not invent another target.";
        }
        return null;
    }

    public bool TryApply(TeamMember origin, NPCResponse response, int commanderSequence, out string error)
    {
        error = null;
        if (commanderSequence < 0 || !Resolve(origin, out var binding, out error))
        { error = error ?? "Invalid conversation turn."; return false; }
        // Commit at most one accepted response per NPC per Commander turn. Null also
        // consumes that response, so a duplicate callback cannot later issue a command.
        if (lastAcceptedTurn.TryGetValue(origin, out int previous) && commanderSequence <= previous) return true;
        error = ValidateSelection(origin, response);
        if (error != null) return false;
        lastAcceptedTurn[origin] = commanderSequence;
        AcceptedResponseCount++;
        try
        {
            switch (response.action_type)
            {
                case null: return true;
                // The existing Idle adapter releases its gate and calls the original
                // intent API; it never resets positions or restores knockback physics.
                case "push": binding.idleController.ChoosePush(); break;
                case "avoid": binding.idleController.ChooseAvoid(); break;
                case "attack": binding.idleController.ChooseAttack(); break;
                case "idle": binding.idleController.ChooseIdle(); break;
            }
            AppliedActionCount++;
            return true;
        }
        catch (Exception ex)
        {
            error = "Could not apply " + origin.SpeakerName + "'s action: " + ex.Message;
            return false;
        }
    }

    private static string Behavior(Binding binding)
    {
        if (binding.navigation == null || binding.navigation.Intent == null) return "unavailable";
        if (binding.navigation.Intent.IsEliminated) return "eliminated";
        if (binding.idleController != null && binding.idleController.IsIdle) return "idle";
        switch (binding.navigation.Intent.CurrentIntent)
        {
            case QH_NPCIntent.Intent.PushBox: return "push";
            case QH_NPCIntent.Intent.AvoidBox: return "avoid";
            case QH_NPCIntent.Intent.AttackOther: return "attack";
            default: return "unavailable";
        }
    }

    public object CaptureContext()
    {
        var gate = boxBody == null ? null : boxBody.GetComponent<CooperativePushController>();
        object cooperation = gate == null ? null : new
        {
            required_pushers = 2, ready_pushers = gate.ReadyPushers,
            active_pushers = gate.ActivePushers, movement_enabled = gate.IsPushingTogether,
            blocked_reason = gate.IsWaitingForBridge ? "The box bridge is closed; wait for it to open." :
                gate.IsPushingTogether ? null : "Both NPCs must choose Push and be upright at their assigned push points."
        };
        float[] Position(Vector3 value) => new[] { value.x, value.y, value.z };
        object Snapshot(Binding binding, string id)
        {
            var nav = binding.navigation;
            var agent = nav == null ? null : nav.GetComponent<NavMeshAgent>();
            var body = nav == null ? null : nav.GetComponent<Rigidbody>();
            return new { npc_id = id, current_behavior = Behavior(binding),
                available = nav != null && nav.gameObject.activeInHierarchy && nav.Intent != null && !nav.Intent.IsEliminated,
                can_navigate = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && (body == null || body.isKinematic),
                position = nav == null ? null : Position(nav.transform.position) };
        }
        object level = teammateBridge == null && boxBridge == null ? null : new
        {
            separated_islands = true,
            teammate_bridge = teammateBridge == null ? null : new { open = teammateBridge.IsOpen, status = teammateBridge.Status },
            box_bridge = boxBridge == null ? null : new { open = boxBridge.IsOpen, status = boxBridge.Status },
            note = "The bridges periodically connect separate islands. Push chooses an intention; navigation waits for a usable bridge. Never claim crossing or box delivery before it is observed."
        };
        return new { npcs = new[] { Snapshot(alex, "alex"), Snapshot(sam, "sam") },
            cooperative_push = cooperation,
            level = level,
            box_position = boxBody == null ? null : Position(boxBody.position),
            box_velocity = boxBody == null ? null : Position(boxBody.linearVelocity),
            target_position = boxTarget == null ? null : Position(boxTarget.position),
            box_target_distance = boxBody == null || boxTarget == null ? (float?)null : Vector3.Distance(boxBody.position, boxTarget.position),
            note = "These are observed snapshots, not action-completion events. Physics determines results. Attack targets only the configured teammate." };
    }
}
