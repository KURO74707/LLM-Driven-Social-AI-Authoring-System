using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Canonical shared history and deterministic Commander -> Director -> Alex -> Sam turn orchestration.</summary>
public sealed class ConversationManager : MonoBehaviour
{
    [SerializeField] private TeamMember alex;
    [SerializeField] private TeamMember sam;
    [SerializeField] private TeamAIDirector director;
    [SerializeField] private DialogueUI dialogueUI;
    [SerializeField] private LLMClient llm;
    [Tooltip("Cai_W10 only. Off preserves the text-only two-NPC prototype.")]
    [SerializeField] private bool enablePhysicalActions;
    [SerializeField] private NPCActionBridge actionBridge;
    [Tooltip("Evaluate each accepted NPC line's effect on the other NPC before continuing. Off preserves the original group prototype's request count.")]
    [SerializeField] private bool enableNpcSocialInfluence;
    [Min(6)] [SerializeField] private int contextMessageLimit = 36;
    private readonly List<ConversationMessage> history = new List<ConversationMessage>();
    private readonly List<string> turnWarnings = new List<string>();
    private readonly HashSet<string> repliedThisTurn = new HashSet<string>();
    private readonly Dictionary<string, float> lastObservedBoxDistance = new Dictionary<string, float>();
    public IReadOnlyList<ConversationMessage> History => history.AsReadOnly();
    public bool IsProcessing { get; private set; }
    public string[] LastRecipients { get; private set; } = Array.Empty<string>();
    public event Action<ConversationMessage> OnMessageAdded;
    public event Action OnTurnCompleted;
    private int turnVersion;

    private void OnEnable()
    {
        if (dialogueUI != null) dialogueUI.OnPlayerSubmitted += SubmitCommand;
    }
    private void OnDisable()
    {
        turnVersion++;
        IsProcessing = false;
        if (dialogueUI != null) { dialogueUI.OnPlayerSubmitted -= SubmitCommand; dialogueUI.SetBusy(false); }
        if (director != null) director.Cancel();
        if (alex != null) alex.Dialogue.CancelSharedReply();
        if (sam != null) sam.Dialogue.CancelSharedReply();
    }

    public static string[] ResolveRecipients(string text, out string[] unknownMentions)
    {
        bool alex = false, sam = false;
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? "", @"(?<![\w@])@(?<name>[\p{L}\p{Nd}_]+)", RegexOptions.CultureInvariant))
        {
            string name = match.Groups["name"].Value;
            if (name.Equals("Alex", StringComparison.OrdinalIgnoreCase)) alex = true;
            else if (name.Equals("Sam", StringComparison.OrdinalIgnoreCase)) sam = true;
            else unknown.Add(name);
        }
        unknownMentions = new string[unknown.Count]; unknown.CopyTo(unknownMentions);
        Array.Sort(unknownMentions, StringComparer.OrdinalIgnoreCase);
        if (!alex && !sam) return new[] { "alex", "sam" };
        if (alex && sam) return new[] { "alex", "sam" };
        return new[] { alex ? "alex" : "sam" };
    }

    public void SubmitCommand(string text)
    {
        if (!isActiveAndEnabled || IsProcessing || string.IsNullOrWhiteSpace(text)) return;
        if (dialogueUI == null || !dialogueUI.IsGroupConversation) { Debug.LogError("Assign the group-configured existing DialogueUI.", this); return; }
        IsProcessing = true;
        int version = ++turnVersion;
        turnWarnings.Clear();
        repliedThisTurn.Clear();
        dialogueUI.SetBusy(true);
        var command = Add("player", "Commander", text, new Color(0.85f, 0.91f, 1f));
        LastRecipients = ResolveRecipients(text, out var unknown);
        if (unknown.Length > 0)
            Warn("Unknown mention(s): @" + string.Join(", @", unknown) + ". Recipients: " + string.Join(", ", LastRecipients) + ".");
        if (llm == null || !llm.isActiveAndEnabled || llm.IsBusy)
        { Warn("The existing LLMClient is unavailable or busy. Please retry."); Finish(version); return; }
        if (alex != null && sam != null && (alex == sam || alex.State == sam.State))
        { Warn("Alex and Sam must have different GameObjects and independent NPCState components."); Finish(version); return; }
        void StartReplies()
        {
            if (Active(version)) ReplyNext(version, command.sequence, 0);
        }
        bool useDirector = (alex != null && alex.State.UsesAIDirector) || (sam != null && sam.State.UsesAIDirector);
        if (useDirector && director != null && director.isActiveAndEnabled)
        {
            dialogueUI.DisplayMessage("Evaluating team response...");
            director.Evaluate(alex, sam, Context(), command.sequence, LastRecipients, StartReplies);
        }
        else StartReplies();
    }

    private bool Active(int version) => isActiveAndEnabled && IsProcessing && version == turnVersion
        && dialogueUI != null && dialogueUI.isActiveAndEnabled;

    private void ReplyNext(int version, int commandSequence, int index)
    {
        if (!Active(version)) return;
        if (index >= LastRecipients.Length) { Finish(version); return; }
        string id = LastRecipients[index];
        var npc = id == "alex" ? alex : sam;
        if (npc == null || !npc.isActiveAndEnabled || npc.State == null || npc.Dialogue == null || npc.SpeakerId != id)
        {
            Warn("Missing or misconfigured teammate: " + id);
            ReplyNext(version, commandSequence, index + 1);
            return;
        }
        dialogueUI.DisplayMessage(npc.SpeakerName + " is thinking...");
        var priorAndPeerContext = new List<ConversationMessage>();
        foreach (var message in Context()) if (message.sequence != commandSequence) priorAndPeerContext.Add(message);
        var recentOwnReplies = priorAndPeerContext.Where(message => message.speaker_id == npc.SpeakerId).TakeLast(3).ToArray();
        var gameplay = enablePhysicalActions && actionBridge != null ? JObject.FromObject(actionBridge.CaptureContext()) : null;
        float? distance = (float?)gameplay?["box_target_distance"];
        float? progress = distance.HasValue && lastObservedBoxDistance.TryGetValue(npc.SpeakerId, out float previousDistance)
            ? previousDistance - distance.Value : (float?)null;
        string payload = enablePhysicalActions
            ? JsonConvert.SerializeObject(new { responding_npc_id = npc.SpeakerId, recipient_ids = LastRecipients,
                gameplay_context = gameplay,
                decision_context = new { recent_own_reply_sequences = recentOwnReplies.Select(message => message.sequence).ToArray(),
                    observed_box_progress_since_previous_request = progress,
                    progress_units = "Positive means closer to the target; zero means no observed progress. This is not a completion event." },
                conversation_history = priorAndPeerContext, latest_commander_message = history[commandSequence] })
            : JsonConvert.SerializeObject(new { responding_npc_id = npc.SpeakerId,
                recipient_ids = LastRecipients, conversation_history = priorAndPeerContext, latest_commander_message = history[commandSequence] });
        string Validate(NPCResponse response)
        {
            string worldError = enablePhysicalActions
                ? actionBridge == null ? null : actionBridge.ValidateSelection(npc, response)
                : TeamNarrativeRules.Validate(response);
            if (worldError != null) return worldError;
            if (enablePhysicalActions && response.HasStationaryMovementConflict())
                return "Your action description says you stay still, but your selected action_type initiates movement. "
                    + "Decide consistently: idle stands still, avoid walks away, push approaches the box, attack approaches the teammate to shove. "
                    + "Keep or change your decision yourself and make all fields agree; do not describe waiting while issuing a movement command.";
            if (enablePhysicalActions && NPCResponse.RepeatsRecentDialogue(response.dialogue, recentOwnReplies.Select(message => message.content)))
                return "This repeats your recent reply. Reconsider the NEW message, your personal motives, and the actual cooperation/progress snapshot. "
                    + "Do not reuse the same slogan or just add profanity. You may keep the same behavior for a specific current reason; "
                    + "you may also wait, withdraw or confront your teammate when your persona and context justify it. Choose independently, not by an anger threshold.";
            string other = id == "alex" ? "Sam" : "Alex";
            if (Regex.IsMatch(response.dialogue, @"(^|\n)\s*(Alex|Sam|Commander)\s*:", RegexOptions.IgnoreCase)
                || Regex.IsMatch(response.action, "^\\s*" + other + "\\s+", RegexOptions.IgnoreCase))
                return "Speak and act only for yourself, without speaker prefixes or another character's lines/actions.";
            return null;
        }
        npc.Dialogue.GenerateSharedReply(npc.BuildPrompt(enablePhysicalActions), payload, npc.SpeakerName, Validate,
            response =>
            {
                if (!Active(version)) return;
                if (!repliedThisTurn.Add(npc.SpeakerId)) return;
                if (distance.HasValue) lastObservedBoxDistance[npc.SpeakerId] = distance.Value;
                var spoken = Add(npc.SpeakerId, npc.SpeakerName, response.dialogue, npc.ChatColor);
                if (enablePhysicalActions)
                {
                    if (actionBridge == null) Warn("Missing NPCActionBridge; dialogue accepted without changing navigation.");
                    else if (!actionBridge.TryApply(npc, response, commandSequence, out var actionError)) Warn(actionError);
                }
                if (director != null) director.LogDecision(npc, response, commandSequence);
                void Continue() { if (Active(version)) ReplyNext(version, commandSequence, index + 1); }
                var listener = id == "alex" ? sam : alex;
                if (enableNpcSocialInfluence && director != null && director.isActiveAndEnabled
                    && listener != null && listener.State != null && listener.State.UsesAIDirector)
                {
                    dialogueUI.DisplayMessage("Evaluating " + npc.SpeakerName + "'s social influence...");
                    // Only accepted dialogue enters history/evaluation. The listener may
                    // already have spoken; update state but never create another reply.
                    director.EvaluateInteraction(alex, sam, Context(), spoken.sequence, LastRecipients, Continue);
                }
                else Continue();
            }, error =>
            {
                if (!Active(version)) return;
                Warn(npc.SpeakerName + " could not respond: " + error);
                ReplyNext(version, commandSequence, index + 1);
            }, enablePhysicalActions);
    }

    private ConversationMessage Add(string id, string name, string text, Color color)
    {
        var message = new ConversationMessage { sequence = history.Count, speaker_id = id, speaker_name = name, content = text };
        history.Add(message);
        dialogueUI.ShowSpeakerLine(name, text, color);
        OnMessageAdded?.Invoke(message);
        return message;
    }
    private ConversationMessage[] Context()
    {
        // Keep the complete visible/canonical history. Only the request window is bounded.
        int count = Mathf.Min(history.Count, Mathf.Max(6, contextMessageLimit));
        return history.GetRange(history.Count - count, count).ToArray();
    }
    private void Warn(string error)
    {
        turnWarnings.Add(error);
        Debug.LogWarning("[TEAM COMMUNICATION] " + error, this);
    }
    private void Finish(int version)
    {
        if (!Active(version)) return;
        IsProcessing = false;
        dialogueUI.DisplayMessage(turnWarnings.Count == 0 ? "" : string.Join("\n", turnWarnings));
        dialogueUI.SetBusy(false);
        OnTurnCompleted?.Invoke();
    }
}
