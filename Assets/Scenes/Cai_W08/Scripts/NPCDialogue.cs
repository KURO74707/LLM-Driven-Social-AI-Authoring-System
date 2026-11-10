using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public class ChatMessage
{
    public string role;
    public string content;
    public ChatMessage(string role, string content) { this.role = role; this.content = content; }
}

/// <summary>Owns accepted conversation turns and structured text intentions; never moves the NPC.</summary>
[RequireComponent(typeof(NPCState))]
public class NPCDialogue : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private LLMClient llm;
    [SerializeField] private NPCState state;
    [Tooltip("Evaluates emotions when NPCState Control Mode is AI Director. Manual mode skips evaluation.")]
    [SerializeField] private AIDirector director;
    [SerializeField] private DialogueUI dialogueUI;
    [SerializeField] private NPCActionUI actionUI;
    [Tooltip("Group conversation owns requests/history. Off preserves the single-NPC submission flow.")]
    [SerializeField] private bool externallyManagedConversation;
    public bool ExternallyManagedConversation => externallyManagedConversation;
    // Kept for serialized compatibility. Identity is edited on NPCState after migration.
    [HideInInspector] [SerializeField] private string systemPrompt = string.Empty;
    [Header("Response Validation")]
    [Min(1)] [SerializeField] private int maxResponseLines = 4;
    [Min(1)] [SerializeField] private int maxResponseWords = 40;
    [Header("Memory")]
    [Min(1)] [SerializeField] private int maxHistoryTurns = 12;
    [Header("World Context — text simulation only")]
    [TextArea(4, 10)] [SerializeField] private string worldContext = InitialWorldContext;
    public const string InitialWorldContext = "The player and one NPC are in a room with a heavy box and a target platform. The shared objective is to move the box onto the platform. Lifting and moving this box requires TWO NPCs cooperating; only ONE NPC exists. The human player is not a second NPC. The NPC may approach, examine, try and fail to lift, ask for another NPC, refuse, gesture, or wait. The NPC cannot successfully lift or move the box alone. The box has NOT reached the platform. All actions are text descriptions only. Never claim successful box transport or invent another NPC, equipment, or a changed world state.";

    public event Action<string> OnNpcReplied;
    public event Action<string> OnPlayerSaid;
    public event Action<string> OnActionChanged;
    public event Action<string> OnError;
    public string CurrentAction { get; private set; } = "Idle";
    public string OriginalGeneratedResponse { get; private set; }
    public string LatestGeneratedResponse { get; private set; }
    public string LastSharedRequestPayload { get; private set; }
    public bool IsThinking => awaitingReply;
    private readonly List<ChatMessage> history = new List<ChatMessage>();
    private string context = string.Empty;
    private string pendingPlayerMessage;
    private bool awaitingReply;
    private int repairAttempts;
    private int turnVersion;
    private Action<string> sharedRequestFailure;

    private void OnEnable()
    {
        if (state == null) state = GetComponent<NPCState>();
        if (director == null) director = GetComponent<AIDirector>();
        if (llm == null) llm = FindAnyObjectByType<LLMClient>();
        if (externallyManagedConversation) return;
        if (dialogueUI != null) dialogueUI.OnPlayerSubmitted += PlayerSaid;
        if (llm != null) { llm.OnReply += HandleReply; llm.OnError += HandleClientError; }
    }
    private void OnDisable()
    {
        if (externallyManagedConversation) CancelSharedReply();
        // Invalidate the continuation before cancelling an in-flight Director evaluation.
        awaitingReply = false;
        turnVersion++;
        if (director != null) director.CancelEvaluation();
        if (dialogueUI != null) { dialogueUI.OnPlayerSubmitted -= PlayerSaid; if (!externallyManagedConversation) dialogueUI.SetBusy(false); }
        if (llm != null) { llm.OnReply -= HandleReply; llm.OnError -= HandleClientError; }
        pendingPlayerMessage = null;
    }

    public void PlayerSaid(string text)
    {
        if (externallyManagedConversation) return;
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text) || awaitingReply) return;
        if (llm == null || !llm.isActiveAndEnabled) { HandleError("The NPC's LLMClient is missing or disabled."); return; }
        if (llm.IsBusy) { HandleError("The model is busy. Please try again shortly."); return; }
        if (state == null || string.IsNullOrWhiteSpace(state.PersonaPrompt)) { HandleError("Set Persona Prompt on the NPCState component."); return; }
        if (dialogueUI == null) { HandleError("Assign the existing DialogueUI."); return; }
        int retained = (Mathf.Max(1, maxHistoryTurns) - 1) * 2;
        while (history.Count > retained) history.RemoveRange(0, 2);
        pendingPlayerMessage = text.Trim();
        repairAttempts = 0;
        OriginalGeneratedResponse = null;
        LatestGeneratedResponse = null;
        awaitingReply = true;
        int turn = ++turnVersion;
        dialogueUI.SetBusy(true);
        dialogueUI.ShowThinking();
        OnPlayerSaid?.Invoke(pendingPlayerMessage);
        if (state.UsesAIDirector && director != null && director.isActiveAndEnabled)
            director.Evaluate(pendingPlayerMessage, history.ToArray(), () => BeginNpcReply(turn));
        else BeginNpcReply(turn);
    }

    private void BeginNpcReply(int turn)
    {
        if (!isActiveAndEnabled || !awaitingReply || turn != turnVersion) return;
        // Rebuild the prompt only after the Director has applied state and notified the UI.
        llm.SendMessage(pendingPlayerMessage, RequestHistory().ToArray());
    }

    private List<ChatMessage> RequestHistory()
    {
        var messages = new List<ChatMessage> { new ChatMessage("system", BuildSystemPrompt()) };
        messages.AddRange(history);
        return messages;
    }

    private string BuildSystemPrompt()
    {
        string persona = state == null ? systemPrompt : state.PersonaPrompt;
        string goal = state == null ? string.Empty : state.GoalPrompt;
        float anger = state == null ? 20 : state.Anger;
        float trust = state == null ? 50 : state.TrustInPlayer;
        return persona + "\n\nNPC GOAL:\n" + goal
            + "\n\nCURRENT EMOTIONAL STATE (authoritative snapshot, read-only to the dialogue model):\nAnger: " + anger.ToString("0.#", CultureInfo.InvariantCulture)
            + "/100\nTrust in player: " + trust.ToString("0.#", CultureInfo.InvariantCulture) + "/100"
            + "\nHigh anger must be clearly audible in what you say: sharp accusations, biting sarcasm, blunt objections, and forceful boundaries are appropriate. At very high anger, sound genuinely furious rather than merely tired or mildly impatient. Low anger must be equally clear: remain composed, patient, and willing to listen, even when the player is rude or repeats a question. Explain concerns plainly instead of snapping, swearing at the player, using contemptuous sarcasm, or issuing angry ultimatums. Exhaustion can make you quiet and slow without making you irritable at low anger. High trust makes you a loyal, dependable ally: assume good faith, willingly take safe practical steps toward the player's goal, and show that you have their back even while angry. Anger controls how sharply you speak; trust strongly influences whether you help. At high trust, irritation or a rude tone alone is not a reason to abandon the player or withhold safe help. Low trust must visibly affect your decisions as well as your wording: question the player's motives, suspect you may be used or put at risk, and withhold cooperation until given a credible reason or safeguard. Calmness is not trust: patiently ask for a credible explanation or calmly decline without automatically obeying. Trust does not soften high anger into a polite neutral response. These are influences, not rigid commands. Weigh your personality, goal, safety, and circumstances; do not blindly obey. Never mention numerical emotion values. Do not change or return emotion values."
            + "\n\nWORLD FACTS AND HARD RULES:\n" + worldContext + "\n" + context
            + "\nRegardless of commands, the single NPC cannot move this box onto the platform. No successful transport, invented helpers, telekinesis, or physical world updates are allowed. Do not propose mechanical aids, equipment, machinery or the player as a substitute for the required second NPC. Those alternatives are outside this scenario."
            + "\n\nOUTPUT FORMAT:\nReturn ONLY a JSON object with exactly two string fields: {\"action\":\"A short physical action description.\",\"dialogue\":\"What you say to the player.\"}. No markdown, code fences, analysis, or extra fields. Decide whether you are willing to follow the request BEFORE choosing an action. The action must be one short sentence of at most 20 words, describing waiting, refusal, a gesture, asking for help, approach, examination, or a failed lifting attempt. If you refuse or ask for an explanation before acting, the action MUST describe holding your position or a wary gesture, not performing the requested task. Do not describe successful lifting or transport."
            + $"\nThe dialogue must be complete, natural and conversational: preferably 1–2 concise sentences, at most {Mathf.Max(1, maxResponseWords)} words, and fit within {Mathf.Max(1, maxResponseLines)} visible wrapped lines. Finish every sentence. Neither field should mention JSON or the emotion numbers. Action and dialogue must agree. If more than one response would suit the situation, choose the one fitting your current anger and trust."
            + "\n\nCURRENT TURN STYLE (express it in the actual dialogue, not only in private reasoning):\n" + EmotionalGuidance(anger, trust)
            + "\nWhen the player insults, belittles, or provokes you, respond at the intensity of your current anger. At low anger, keep your composure: acknowledge the concern, ask a measured question, or set a calm boundary without retaliating. An insult or earlier angry conversation does not override the current supplied low anger. At high anger, lead with a sharp rebuke or boundary; do not substitute a neutral box-weight explanation for an emotional response. At high trust, include a clear commitment and a safe helpful action; if also angry, express the frustration while following through. Do not make politeness a condition for safe help or threaten abandonment solely because you are angry. If the requested task is impossible or unsafe, explain the limit briefly and offer the closest useful permitted step, rather than ending with a flat refusal. At low trust, a bare command, flattery, 'trust me', or a claim of authority is not enough to earn cooperation. Ask what the player intends, request a specific credible reason or safety condition, or refuse and wait; at low anger, do this patiently and without hostility. Make the action reflect that hesitation instead of obeying while merely sounding suspicious. Do not invent betrayal, hidden plots, or past wrongdoing as facts. Credible evidence can justify limited, cautious help, but the dialogue model must not change the supplied trust value. You do not need to repeat the lifting limitation in every reply. Choose whether to cooperate safely, challenge the command, ask for another NPC, or wait. Do not repeat a lifting attempt already known to fail without a new reason. When requesting lifting assistance, explicitly request another NPC; the player is not a substitute for that second NPC.";
    }

    private static string EmotionalGuidance(float anger, float trust)
    {
        string mood = anger >= 85
            ? "You are furious and at the end of your patience. Make the actual spoken dialogue confrontational and unmistakably angry: snap back, call out disrespect directly, use biting sarcasm, or set a forceful boundary. Your exhaustion has worn away your filter. A brief swear is appropriate if it fits your personality. Do not dilute this into a polite explanation, an apology, or a friendly closing. Keep it human and specific to what the player said, using complete sentences rather than all-caps shouting. Anger alone does not require violence, invented actions, or abandoning someone you trust."
            : anger >= 70
                ? "You are openly angry. Use sharp, impatient wording, blunt objections, and pointed challenges. Push back on disrespect or unreasonable demands directly instead of merely describing yourself as irritated. Do not respond with a neutral explanation or reflexive politeness. Retain your personality and speaking style."
                : anger <= 15
                    ? "You are especially composed and patient. Take the player's words seriously without taking the bait, even after insults or repeated questions. Use measured, conversational sentences, a gentle clarification, or a brief reasonable explanation. No snapping, contempt, hostile swearing, scolding, or impatient ultimatums. Let the action show steady, unhurried composure rather than aggressive glaring or clenched fists. Remain a tired human with boundaries, not an endlessly apologetic assistant. With low trust, patiently ask what the player intends and wait for a credible answer; calmness does not mean agreement or earned trust."
                    : anger <= 30 ? "You are calm and patient. Listen, explain concerns evenly, and allow clarification without irritation. Handle rudeness with a composed boundary rather than retaliation. Keep any suspicion or refusal measured and reasonable; do not obey merely to sound friendly."
                : anger >= 45 ? "You are irritated and your patience is running out. Let your reply sound curt and skeptical; challenge unreasonable demands."
                : "Your patience is limited, but you can still reason.";
        string relationship = trust <= 15
            ? "You deeply distrust this player. Treat unexplained orders as possible attempts to use you or shift the risk onto you. Your default response to a bare command is to hold your position and refuse or demand an explanation before acting, even when the requested action is physically possible. Ask a direct question about their motive, evidence, or what protects you. For an unexplained order to approach or examine the box, remain where you are: do NOT walk over, inspect it, or begin the task while asking your question. The action should show waiting, watching the player, or a guarded gesture. Do not append a helpful offer or perform the requested action after saying you distrust them. Your shared goal and their politeness do not establish credibility. Only specific, convincing evidence or a concrete safeguard can justify limited cooperation. At low anger, express this as cool suspicion rather than rage; at high anger, make the refusal confrontational."
            : trust <= 30
                ? "You strongly distrust this player. Make your skepticism explicit and resist unexplained commands: question why you should believe them, request a concrete reason or safeguard, or withhold action. Do not automatically agree and then add a suspicious-sounding disclaimer. Limited help is possible when their actual explanation addresses your concerns. Low anger changes your tone, not your willingness to trust."
                : trust >= 85
                    ? "You deeply trust the player and are firmly on their side. Be a dependable ally: take initiative on safe, achievable requests, give a definite commitment, and show support through your described action. You do not need proof of their motives for an ordinary safe request. Even when furious, complain sharply but still follow through; do not stall, punish them by withholding help, demand an apology first, or threaten to leave them to it. If ordered to move the box alone, you MUST combine the limit with a specific commitment to permitted help in the same reply: examine the box or ask for another NPC. Match your voice to CURRENT anger: patient and reassuring at low anger, sharply frustrated at high anger. Your action must show that helpful step, unless it was already completed in the conversation. Do not end with 'not me', 'do it yourself', or another dismissal. No machine, equipment, or player assistance can replace the required second NPC. Never invent successful lifting, a helper's arrival, or other physical outcomes. Loyalty is strong willingness to help, not unlimited obedience."
                    : trust >= 70 ? "You trust the player and want their plan to succeed. Read reasonable requests in good faith, readily offer concrete safe help, and make your action match that willingness. If also angry, sound frustrated but remain reliable: irritation should change your wording, not turn a safe request into a refusal or a demand to earn your trust. Preserve personal boundaries and world limits; for an impossible task, offer the closest permitted helpful step."
                : "You are guarded and evaluate the request on its merits. Ask for clarification when a command has no clear reason, and prefer limited cooperation over unconditional promises. If angry, express that anger directly even when the request is reasonable.";
        string example = anger >= 70 && trust <= 30
            ? " An example of this attitude is: 'What is your angle? I am not doing a damn thing just because you barked an order.'"
            : trust <= 30 ? " An example of calm distrust is: action 'Waits quietly at a cautious distance.'; dialogue 'I would like to understand your plan before I go over. What do you want me to check?'"
            : anger >= 70 && trust >= 70 ? " An example of angry loyalty is: action 'Approaches the box and examines it.'; dialogue 'Stop barking orders. I have your back, and I will check the box.'"
            : anger >= 70 ? " An example of this attitude after an insult is: 'Keep calling me useless and you can deal with this mess yourself. I am done being your punching bag.'"
            : anger <= 30 && trust >= 70 ? " An example of calm loyalty is: action 'Approaches and examines the box.'; dialogue 'I have your back. I will check the box while we work out the next safe step.'" : string.Empty;
        return mood + " " + relationship + example + " Adapt the attitude to your own persona and this situation; do not mechanically copy the example. These are strong current tendencies, not fixed actions; consider your goal, history, and world constraints.";
    }

    private void HandleReply(string raw)
    {
        if (!awaitingReply) return;
        LatestGeneratedResponse = raw;
        if (OriginalGeneratedResponse == null) OriginalGeneratedResponse = raw;
        NPCResponse response;
        string reason;
        if (!NPCResponse.TryParse(raw, out response, out reason) || !ValidateResponse(response, out reason))
        {
            if (repairAttempts < 2)
            {
                repairAttempts++;
                var messages = RequestHistory();
                messages.Add(new ChatMessage("user", pendingPlayerMessage));
                // Draft and repair instruction are temporary; never commit them as conversation turns.
                messages.Add(new ChatMessage("assistant", raw ?? string.Empty));
                string instruction = "Repair the preceding NPC response. " + reason
                    + " Preserve the NPC's personality, current emotions, goal, and central intent, including patience at low anger, loyalty and willingness to help at high trust, or refusal and suspicion at low trust. Shortening must not turn calm suspicion into hostility or automatic obedience, angry loyalty into abandonment, or high anger into politeness. Return ONLY valid JSON with action and dialogue. Use one complete short dialogue sentence (8–20 words) and one short safe physical action. Remember: only one NPC exists; this box cannot be lifted alone or reach the platform. No invented success. Do not discuss this repair request.";
                llm.SendMessage(instruction, messages.ToArray());
            }
            else HandleError("The model could not provide a valid, world-consistent short action and dialogue. Please try again.");
            return;
        }
        history.Add(new ChatMessage("user", pendingPlayerMessage));
        history.Add(new ChatMessage("assistant", JsonUtility.ToJson(response)));
        pendingPlayerMessage = null;
        awaitingReply = false;
        CurrentAction = response.action;
        dialogueUI.SetBusy(false);
        dialogueUI.ShowNpcLine(response.dialogue);
        OnActionChanged?.Invoke(CurrentAction);
        OnNpcReplied?.Invoke(response.dialogue);
    }

    private bool ValidateResponse(NPCResponse response, out string reason)
    {
        reason = "Respect the world rules: another NPC is required, but does not yet exist here. No successful lifting/transport and no equipment or mechanical aid as a substitute.";
        if (response.ViolatesBoxRules()) return false;
        return ValidatePresentation(response, "NPC", out reason);
    }

    private bool ValidatePresentation(NPCResponse response, string speaker, out string reason)
    {
        reason = "Finish the response completely instead of hitting the token limit.";
        if (llm != null && llm.LastResponseHitTokenLimit) return false;
        reason = "Keep the action to at most 20 words and the dialogue to the configured word and visible-line limits. Never cut a sentence.";
        if (Regex.Matches(response.action, @"\S+").Count > 20 || Regex.Matches(response.dialogue, @"\S+").Count > Mathf.Max(1, maxResponseWords)) return false;
        string ending = response.dialogue.TrimEnd(' ', '\r', '\n', '"', '\'', '”', '’', ')', ']');
        if (ending.EndsWith("...") || ending.EndsWith("…") || !(ending.EndsWith(".") || ending.EndsWith("!") || ending.EndsWith("?"))) return false;
        if (dialogueUI == null || !dialogueUI.ResponseFitsWithinLines(response.dialogue, Mathf.Max(1, maxResponseLines), speaker)) return false;
        if (actionUI != null && !actionUI.CanDisplayAction(response.action)) return false;
        reason = null;
        return true;
    }

    private void HandleClientError(string message) { if (awaitingReply) HandleError(message); }
    private void HandleError(string message)
    {
        awaitingReply = false;
        pendingPlayerMessage = null;
        if (dialogueUI != null) { dialogueUI.SetBusy(false); dialogueUI.DisplayMessage("Unable to get an NPC reply: " + message); }
        OnError?.Invoke(message);
    }
    public void SetContext(string contextLine) { context = contextLine ?? string.Empty; }

    /// <summary>Reuses transport, structured parsing and presentation limits without owning group history or UI busy state.</summary>
    public void GenerateSharedReply(string prompt, string payload, string speaker,
        Func<NPCResponse, string> worldValidation, Action<NPCResponse> accepted, Action<string> failed,
        bool executableActions = false)
    {
        if (!externallyManagedConversation || !isActiveAndEnabled || awaitingReply || llm == null || !llm.isActiveAndEnabled)
        {
            failed?.Invoke("NPC dialogue is missing, disabled, busy, or not configured for the group.");
            return;
        }
        awaitingReply = true;
        int version = ++turnVersion;
        LastSharedRequestPayload = payload;
        int attempts = 0;
        OriginalGeneratedResponse = null;
        LatestGeneratedResponse = null;
        void FailShared(string error)
        {
            if (!awaitingReply || version != turnVersion) return;
            awaitingReply = false;
            sharedRequestFailure = null;
            failed?.Invoke(error);
        }
        sharedRequestFailure = FailShared;
        void Send(string request, ChatMessage[] messages)
        {
            llm.SendRequest(request, messages, raw =>
            {
                if (!awaitingReply || version != turnVersion) return;
                LatestGeneratedResponse = raw;
                if (OriginalGeneratedResponse == null) OriginalGeneratedResponse = raw;
                string error;
                bool valid = NPCResponse.TryParse(raw, executableActions, out var response, out error);
                if (valid)
                {
                    error = worldValidation?.Invoke(response);
                    valid = error == null && ValidatePresentation(response, speaker, out error);
                }
                if (!valid)
                {
                    if (attempts++ < 2)
                        Send("Repair this draft: " + error + (executableActions
                            ? " Reassess the draft using the current persona, emotions, NEW message and observed gameplay context. If only length or presentation is invalid, preserve the central meaning and action_type while shortening. If a decision or repeated wording was rejected, keep the character without retaining the rejected decision or catchphrase; you may independently keep or change the action for a current reason, and no specific action is required. Return ONLY action_type/action/dialogue JSON. Choose push, avoid, attack, idle or JSON null. Null maintains current behavior; idle stops it. Shoving the teammate is attack; pushing the box is push. All three fields must agree. Use one complete 8-14 word dialogue sentence and a 3-8 word intended action. Never invent a completed outcome or speak for another character."
                            : " Preserve personality, trust, anger, and intent. Return only action/dialogue JSON. Use a complete 8-20 word dialogue sentence and a short preparation action; never claim successful lifting or transport. Do not speak for another character."),
                            new[] { new ChatMessage("system", prompt), new ChatMessage("user", payload), new ChatMessage("assistant", raw ?? "") });
                    else FailShared("No valid complete, world-consistent reply after two repair attempts: " + error);
                    return;
                }
                awaitingReply = false;
                sharedRequestFailure = null;
                CurrentAction = response.action;
                OnActionChanged?.Invoke(CurrentAction);
                OnNpcReplied?.Invoke(response.dialogue);
                accepted?.Invoke(response);
            }, FailShared, executableActions ? NPCResponse.ExecutionResponseFormat() : null, this);
        }
        Send(payload, new[] { new ChatMessage("system", prompt) });
    }

    public void CancelSharedReply()
    {
        if (sharedRequestFailure == null) return;
        const string reason = "NPC group response was cancelled.";
        if (llm == null || !llm.CancelRequest(this, reason)) sharedRequestFailure?.Invoke(reason);
    }
}

