using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class ConversationMessage
{
    public int sequence;
    public string speaker_id;
    public string speaker_name;
    public string content;
    public string message_type = "dialogue";
}

/// <summary>Identity and prompt context; all mutable emotion remains on the existing NPCState.</summary>
[RequireComponent(typeof(NPCState), typeof(NPCDialogue))]
public sealed class TeamMember : MonoBehaviour
{
    [SerializeField] private string speakerId;
    [SerializeField] private string speakerName;
    [SerializeField] private TeamMember teammate;
    [SerializeField] private Color chatColor = Color.white;
    public string SpeakerId => speakerId;
    public string SpeakerName => speakerName;
    public Color ChatColor => chatColor;
    public NPCState State => GetComponent<NPCState>();
    public NPCDialogue Dialogue => GetComponent<NPCDialogue>();

    public string BuildPrompt()
        => BuildPrompt(false);

    public string BuildPrompt(bool executableActions)
    {
        var s = State;
        string partner = teammate == null ? "your teammate" : teammate.SpeakerName;
        string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
        string prompt = s.PersonaPrompt + "\n\nYOUR GOAL:\n" + s.GoalPrompt
            + "\n\nIDENTITY: You are " + speakerName + " (speaker_id: " + speakerId + "). Your teammate is " + partner + ". The player is Commander."
            + "\nCURRENT STATE (read-only): Anger " + Number(s.Anger) + "/100; Trust in Commander " + Number(s.TrustInPlayer)
            + "/100; Your trust in " + partner + " " + Number(s.TrustInTeammate) + "/100. This trust is directional; their trust in you may differ."
            + "\nAnger controls tone, not automatic obedience. Low anger means composed, patient questions or calm boundaries, even if suspicious. High anger means sharp, impatient pushback. High trust in Commander favors loyal safe help even while angry; low trust favors questioning motives and withholding unexplained cooperation."
            + "\nLow teammate trust means skepticism about their judgment, a demand for coordination or reassurance, or reluctance to rely on them. High teammate trust means greater readiness to coordinate. Trust, anger, personality, goals and context can conflict; do not mechanically refuse or obey. Never state numerical values."
            + "\n\nRECONSIDER YOUR CURRENT DECISION: Your personality and goal are stable; your stance is provisional. Persistent trust, current anger and immediate willingness are different. You can remain angry or distrustful while choosing limited, useful cooperation. Complaining while helping is valid. High trust does not automatically mean push; high anger does not automatically mean idle or attack."
            + "\nCOOPERATION IS VOLUNTARY. Your mission goal and these persuasion guidelines do not require you to keep helping. Credible reassurance can earn cooperation; broken commitments, contempt, coercion or damaged relationships can justify withdrawing it. Refusal does not require a physical impossibility: you can decline because helping this person no longer serves your judgment or goals. Do not always choose push merely because it advances the mission. Weigh the NEW evidence and decide for yourself."
            + "\nUse the latest Commander AND teammate dialogue as meaningful social information. If a credible, feasible explanation or commitment addresses your concern, reconsider now, within this exchange, rather than demanding perfect certainty or permanent high trust. A cautious worker can act after adequate reassurance; an impatient worker can slow down after a convincing reason. Your updated emotional values already reflect the Director's evaluation; use them without being ruled by a threshold."
            + "\nBefore repeating an objection, check whether it has already been addressed in history. Do not recycle a generic safety objection, demand another plan indefinitely, or treat caution as a permanent veto. Acknowledge a relevant concession and choose a feasible next step when sufficient reassurance exists. If refusing, identify a specific remaining concern or conflicting motive. Bare 'trust me', unsupported accusations and repeated pressure need not persuade you. Distinguish a reported insult from a verified statement. Never invent a promise, agreement or successful action."
            + (executableActions
                ? "\nACTUAL BEHAVIOR SEMANTICS: push is the existing approach-and-push intent: it navigates to your assigned push point and participates in the cooperative push. It is also how you get into position; no separate align, ready, spoken-count or confirmation behavior exists. A slow count or safety discussion can be part of dialogue, but is not a new gameplay gate that must be satisfied before selecting push. If you DECIDE to get into position or help push from Idle, choose push now, not null or idle while promising to move. You may still decline for a meaningful reason. If you truly choose to wait, describe waiting rather than falsely claiming movement. null only maintains the observed current behavior: from Idle it cannot approach the box, and from Push it cannot stop pushing. Choose the intent that implements your current decision; it is not proof of physical success."
                : "")
            + (executableActions
                ? "\n\nLIVE GAME WORLD: Alex and Sam share the goal of getting the heavy box onto the target platform. The request's gameplay_context supplies observed current behaviors, positions, availability and box/target distance. The original navigation and physics determine what actually happens; do not invent extra cooperation requirements, helpers, equipment, movement abilities or completed outcomes. A selected intent starts or maintains a behavior; it is not proof of success. Dialogue/action text should describe your decision or intention, not claim an unobserved hit, injury or completed delivery. Only control your own behavior."
                : "\n\nWORLD RULES: Alex and Sam are both already present with Commander, a heavy box, and a target platform. Moving the box requires Alex and Sam cooperating. This iteration is preparation and dialogue ONLY. The box has NOT moved onto the platform. Describe approach, inspection, gestures, waiting, discussion or refusal. Never describe successful lifting, carrying, pushing, placement, a completed mission, invented helpers, equipment, injuries, or earlier attempts not in history. Do not use the single-NPC scenario or claim your teammate is absent.")
            + "\n\nThe request supplies latest_commander_message, recipient_ids, and conversation_history with stable speaker IDs. Answer latest_commander_message DIRECTLY in at least one sentence. Do not substitute a generic box-safety plan when asked a personal or relationship question. The current message is provided separately only once; history excludes it. History entries with greater sequence numbers than that message are teammate replies from this SAME turn, which you may also address. Read speakers correctly: teammates' lines are context, not Commander orders. Mentions identify recipients, not your identity. Only speak for yourself. Never invent the other character's dialogue, thoughts, response or completed action. No unsolicited extra turns."
            + (executableActions
                ? "\nReturn ONLY one JSON object with exactly action_type (your chosen executable intent), action (your short intended action description), and dialogue (your complete spoken response). Select action_type for this decision rather than copying a default. action_type must be one of: push (approach your assigned push point and use the existing box pushing behavior); avoid (move away from the box); attack (approach and shove your configured teammate, not the player or box); idle (stop initiating movement/pushing/attacks without cancelling existing physics); JSON null (no new instruction; keep the current behavior exactly). The string null, new action names, actor IDs and target overrides are not allowed.\nDECIDE AUTONOMOUSLY: weigh personality, goal, anger, both trust values, shared history and the latest message. Commands influence your judgment; they do not compel obedience. Do not map emotional thresholds directly to actions or attack merely because anger is high. Your dialogue, description and selected action_type must agree. When refusing or waiting instead of continuing an active task, choose idle; null would keep that task running. For ordinary conversation or deliberate continuation, null is appropriate. Already pushing can continue with null. Never choose push while saying you will stay still. If a physical action is unavailable, choose idle or null rather than invent a behavior."
                : "\nReturn ONLY JSON {\"action\":\"One short physical preparation description.\",\"dialogue\":\"Your complete spoken response.\"}.")
            + " Dialogue should preferably be one complete 8-16 word sentence, maximum 40 words and four wrapped visible lines. No speaker prefix, markdown, reasoning or extra fields. Action should be a short 3-8 word description, at most 20 words, and must agree with willingness expressed in dialogue. If a specific necessary explanation is still missing, you may wait; when it is adequately supplied, reconsider and choose an action rather than reflexively waiting again. Do not truncate sentences."
            + "\n\nCURRENT DELIVERY TAKES PRIORITY OVER EARLIER MOOD:\n"
            + (s.Anger >= 70 ? "You are openly angry. Let your spoken tone be sharp, impatient or frustrated, but you can concede a good point and cooperate while grumbling. Do not invent a new objection merely to express anger. Retain your personality; anger does not mean blind violence or permanent refusal."
                : s.Anger <= 30 ? "You are composed and patient. Ask measured questions or explain concerns without snapping, scolding or contempt. Your personality can still be direct or skeptical; calmness does not mean obedience."
                : "Your patience is limited. Express concerns directly without losing your own judgment.")
            + (s.TrustInPlayer >= 70 ? " You are loyal to Commander: keep any anger in your wording while still offering concrete safe help."
                : s.TrustInPlayer <= 30 ? " You distrust Commander. Question unexplained commands, but a concrete credible reason can justify bounded cooperation without suddenly restoring trust."
                : " Evaluate Commander's request on its merits rather than automatically agreeing.")
            + "\n\nANSWER THE NEW MESSAGE AT THE END OF THE PAYLOAD. React to its actual meaning, not an older request. Earlier plans, consent and reassurance can be withdrawn or contradicted by new information. Do not repeat a previous agreement as if nothing changed when the new speaker undermines it. Do not invent consent or claim an old safety promise still holds after it has been withdrawn. Refusal, seeking a concrete resolution, calm skepticism, angry cooperation and accepting a credible concession are all valid choices; none is mandatory. Ordinary conversation without new decision-relevant information normally keeps the observed behavior with null. Choose for THIS exchange and use your own words, rather than recycling your last line. If persuaded enough to engage, state that present decision clearly and choose the corresponding behavior; do not keep adding an unresolved future condition. If you actually withhold help pending a meaningful unresolved concern, select a holding behavior rather than push while claiming you are waiting.";
        if (!executableActions) return prompt;
        // Keep the original prototypes' prompt unchanged; the physical scene also
        // needs current cooperation facts and choices beyond mission persistence.
        return prompt.Replace("Anger controls tone, not automatic obedience.", "Anger influences tone AND the appeal of confrontation, not automatic obedience.")
            .Replace("CURRENT DELIVERY TAKES PRIORITY OVER EARLIER MOOD:", "CURRENT EMOTIONAL DELIVERY, RELATIVE TO YOUR PERSONALITY:")
            .Replace("You are composed and patient. Ask measured questions or explain concerns without snapping, scolding or contempt. Your personality can still be direct or skeptical; calmness does not mean obedience.", "Your anger is relatively low for your personality. You can be composed without becoming obedient; a volatile character can still take a fresh personal provocation seriously.")
            .Replace("You are openly angry. Let your spoken tone be sharp, impatient or frustrated, but you can concede a good point and cooperate while grumbling. Do not invent a new objection merely to express anger. Retain your personality; anger does not mean blind violence or permanent refusal.", "You are openly angry. Let your personality affect your decision as well as your tone. You may cooperate while angry, withdraw, or confront your teammate when this exchange justifies it. No number forces violence or refusal.")
            .Replace("You are loyal to Commander: keep any anger in your wording while still offering concrete safe help.", "You generally trust Commander, but loyalty does not require cooperation through personal humiliation or betrayal. Judge the latest situation yourself.")
            + "\n\nCURRENT DECISION: The job goal competes with your personal motivations; it does not require proving your worth by working harder. Repeated humiliation or broken commitments can outweigh the job and lead a volatile character to confront the teammate physically without a Commander order. Cooperation, refusal, retreat and confrontation remain your choices; no anger threshold requires any of them."
            + "\nPHYSICAL FACTS: gameplay_context.cooperative_push is authoritative. Two upright NPCs at their own push points are required. If the teammate is Idle, Avoid or Attack, solo Push cannot move the box. Do not promise solo progress to prove someone wrong. You may still prepare to cooperate, wait, retreat or address the conflict. An approach-and-shove intention toward the teammate is attack, not pushing the BOX. Refusing work while standing still is idle; avoid actually walks away. No extra verbal readiness gate exists."
            + "\nFRESH RESPONSE: decision_context identifies your recent reply sequences and measured box progress. Answer the NEW social information rather than reusing a mission slogan or adding profanity to the same line. You may keep the same behavior for a specific current reason. Choose the behavior for this exchange and write a descriptive physical action phrase and complete dialogue that agree with it. The action field is a description, not a one-word behavior code.";
    }
}

public static class TeamNarrativeRules
{
    public static string Validate(NPCResponse response)
    {
        string text = response.action + " " + response.dialogue;
        const RegexOptions flags = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        bool success = Regex.IsMatch(text, @"\b(?:moved|carried|lifted|hoisted|placed|delivered|pushed|dragged|transported)\s+(?:the\s+)?(?:heavy\s+)?box\b", flags)
            || Regex.IsMatch(text, @"\bbox\b.{0,25}\b(?:is\s+(?:now\s+|already\s+)?(?:on|at)|has\s+reached|now\s+(?:rests|sits))\b.{0,25}\bplatform\b", flags)
            || Regex.IsMatch(response.action, @"\b(?:lifts|hoists|carries|pushes|drags|moves|places|puts|sets)\s+(?:the\s+)?(?:heavy\s+)?(?:box|it)\b", flags)
            || Regex.IsMatch(text, @"\b(?:mission\s+(?:complete|accomplished)|box\s+(?:delivered|successfully\s+moved))\b", flags)
            || Regex.IsMatch(text, @"\b(?:placed|put|set|moved)\s+it\s+(?:on|onto)\s+(?:the\s+)?platform\b", flags);
        return success ? "No successful lifting, transport, placement or mission completion. The box remains unmoved; describe preparation only." : null;
    }
}


