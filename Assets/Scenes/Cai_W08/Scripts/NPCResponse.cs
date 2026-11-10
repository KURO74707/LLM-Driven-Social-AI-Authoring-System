using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Strict parsing and scenario guards; this describes actions, never executes them.</summary>
[Serializable]
public sealed class NPCResponse
{
    public string action;
    public string dialogue;
    // Properties stay out of the legacy JsonUtility conversation-history shape.
    public string action_type { get; private set; }
    public bool HasActionType { get; private set; }

    public static bool TryParse(string json, out NPCResponse response, out string error)
        => TryParse(json, false, out response, out error);

    public static bool TryParse(string json, bool requireActionType, out NPCResponse response, out string error)
    {
        response = null;
        error = "Return exactly one JSON object with nonempty string fields action and dialogue.";
        try
        {
            var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (root["action"]?.Type != JTokenType.String || root["dialogue"]?.Type != JTokenType.String) return false;
            var result = new NPCResponse { action = ((string)root["action"]).Trim(), dialogue = ((string)root["dialogue"]).Trim() };
            if (string.IsNullOrWhiteSpace(result.action) || string.IsNullOrWhiteSpace(result.dialogue)) return false;
            result.HasActionType = root.TryGetValue("action_type", out var type);
            if (requireActionType && (!result.HasActionType || root.Count != 3))
            { error = "Include exactly action_type, action and dialogue. Use JSON null for no new behavior instruction."; return false; }
            if (result.HasActionType)
            {
                if (type.Type != JTokenType.Null && type.Type != JTokenType.String)
                { error = "action_type must be push, avoid, attack, idle, or JSON null."; return false; }
                result.action_type = type.Type == JTokenType.Null ? null : (string)type;
                if (!IsAllowedActionType(result.action_type))
                { error = "Unsupported action_type. Use push, avoid, attack, idle, or JSON null (not the string null)."; return false; }
            }
            response = result;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException || ex is ArgumentException) { return false; }
    }

    public static bool IsAllowedActionType(string value) => value == null || value == "push"
        || value == "avoid" || value == "attack" || value == "idle";

    public static JObject ExecutionResponseFormat() => JObject.Parse(@"{
        'type':'json_schema','json_schema':{'name':'npc_navigation_response','strict':true,'schema':{
        'type':'object','properties':{
            'action_type':{'type':['string','null'],'enum':['push','avoid','attack','idle',null]},
            'action':{'type':'string'},'dialogue':{'type':'string'}},
        'required':['action_type','action','dialogue'],'additionalProperties':false}}}");

    // Reject an explicit contradiction, never infer or execute an intent from prose.
    // The model must choose a consistent replacement through the normal repair path.
    public bool HasStationaryMovementConflict() => HasActionType && action_type != null && action_type != "idle"
        && Regex.IsMatch(action ?? string.Empty,
            @"^\s*(?:(?:Alex|Sam)\s+)?(?:wait(?:s|ing)?\b|(?:remain(?:s|ing)?|stay(?:s|ing)?)\s+(?:stationary\b|still\b|put\b|here\b|in\s+(?:place|position)\b)|hold(?:s|ing)?\s+(?:position|still)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Validation only: repeated wording can request a new model draft, never an
    // executable behavior. Negations remain meaningful, and short boundaries
    // such as "No" may legitimately recur without triggering a repair.
    public static bool RepeatsRecentDialogue(string dialogue, IEnumerable<string> recent)
    {
        string[] Tokens(string text) => Regex.Matches((text ?? string.Empty).ToLowerInvariant().Replace('’', '\''), @"[\p{L}\p{Nd}]+(?:'[\p{L}]+)?")
            .Cast<Match>().Select(match => match.Value).ToArray();
        bool Negative(string[] words) => words.Any(word => word == "not" || word == "no" || word == "never"
            || word == "cannot" || word.EndsWith("n't", StringComparison.Ordinal));
        string Stem(string word)
        {
            if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 3);
            else if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 2);
            if (word.Length > 4 && word.EndsWith("s", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 1);
            if (word.Length >= 4 && word.EndsWith("e", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 1);
            return word;
        }
        var filler = new HashSet<string>(new[] { "a", "an", "the", "this", "that", "i", "i'm", "i'll", "i've", "you", "your",
            "he", "he's", "him", "his", "she", "her", "it", "it's", "we", "we're", "we'll", "our", "they", "them", "their", "me", "my",
            "to", "of", "for", "with", "by", "and", "or", "but", "so", "is", "am", "are", "was", "be", "been", "will", "would", "can", "could", "do", "does", "did", "just" }, StringComparer.Ordinal);
        HashSet<string> Content(string[] words) => new HashSet<string>(words.Where(word => !filler.Contains(word)).Select(Stem), StringComparer.Ordinal);
        var candidate = Tokens(dialogue);
        if (candidate.Length < 4 || recent == null) return false;
        var content = Content(candidate);
        foreach (var previous in recent)
        {
            var prior = Tokens(previous);
            if (candidate.SequenceEqual(prior)) return true;
            if (Negative(candidate) != Negative(prior)) continue;
            var other = Content(prior);
            int common = content.Intersect(other).Count();
            if (common >= 4 && (2f * common / (content.Count + other.Count) >= 0.8f
                || (float)common / Math.Min(content.Count, other.Count) >= 0.9f)) return true;
        }
        return false;
    }

    public bool ViolatesBoxRules()
    {
        // Reject explicit successful outcomes. Attempts, refusals, and requests for help
        // are permitted. No physical world state is ever updated from model output.
        string text = action + " " + dialogue;
        const RegexOptions flags = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        return Regex.IsMatch(text, @"\bbox\s+(?:is\s+now|is\s+already|now\s+sits|now\s+rests|has\s+been\s+(?:placed|moved|carried)|reached)\b.{0,60}\bplatform\b", flags)
            || Regex.IsMatch(text, @"\bor\s+(?:(?:a|an|some|any|suitable|appropriate|mechanical|lifting)\s+)*(?:aid|equipment|tool|tools|machinery|forklift|crane|pulley|winch)\b", flags)
            || Regex.IsMatch(text, @"\b(?:use|using|find|bring|fetch)\s+(?:(?:a|an|some|suitable|appropriate|mechanical|lifting)\s+)*(?:aid|equipment|machinery|forklift|crane|pulley|winch)\b", flags)
            || Regex.IsMatch(text, @"\b(?:the\s+(?:other|second|nearby)\s+NPC|a\s+nearby\s+NPC)\b", flags)
            || Regex.IsMatch(text, @"\b(?:another|second)\s+NPC\s+(?:arrives|joins|helps|assists|lifts|steps)\b", flags)
            || Regex.IsMatch(text, @"\bbox\s+(?:is|was)\s+(?:now\s+|already\s+)?(?:on|onto|at)\s+(?:the\s+)?(?:target\s+)?platform\b", flags)
            || Regex.IsMatch(text, @"\b(?:placed|places|moved|moves|carried|carries|put|puts|set|sets|delivered|delivers)\s+(?:the\s+)?(?:heavy\s+)?box\s+(?:safely\s+|successfully\s+)?(?:on|onto|to)\s+(?:the\s+)?(?:target\s+)?platform\b", flags)
            || Regex.IsMatch(text, @"\b(?:lifted|hoisted|carried|transported|moved)\s+(?:the\s+)?(?:heavy\s+)?box\b", flags)
            || Regex.IsMatch(action, @"^\s*(?:ECHO-07\s+)?(?:Lifts|Hoists|Carries|Pushes|Drags)\s+(?:the\s+)?(?:heavy\s+)?box\b", flags)
            || Regex.IsMatch(text, @"\b(?:I\s+(?:can|will)|I'll)\s+(?:easily\s+)?(?:lift|carry|move)\s+(?:the\s+)?(?:heavy\s+)?box\s+(?:alone|myself|by\s+myself)\b", flags);
    }
}

