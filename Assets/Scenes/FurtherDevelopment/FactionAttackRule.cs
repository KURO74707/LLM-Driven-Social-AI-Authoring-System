using System.Linq;

namespace FurtherDevelopment.SocialAI
{
    // A typed dialogue input shared by preset choices and any future text parser.
    public sealed class VerificationRequest
    {
        public int EventId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public string Question { get; }

        public VerificationRequest(int eventId, string sourceId, string targetId, string question)
        {
            EventId = eventId;
            SourceId = sourceId;
            TargetId = targetId;
            Question = question;
        }
    }

    public sealed class ActionRequest
    {
        public string ActorId { get; }
        public SocialAction Action { get; }
        public string TargetId { get; }
        public int? EventId { get; }
        public string Reason { get; }
        public string Question { get; }

        internal ActionRequest(string actorId, SocialAction action, string targetId, int? eventId,
            string reason, string question = null)
        {
            ActorId = actorId;
            Action = action;
            TargetId = targetId;
            EventId = eventId;
            Reason = reason;
            Question = question;
        }

        public override string ToString() => $"{ActorId}: {Action}({TargetId}) | {Reason}";
    }

    public sealed class FactionAttackRule
    {
        public bool AllowVerification { get; }
        public int MinimumConfidence { get; }
        public int MinimumLoyalty { get; }

        public FactionAttackRule(bool allowVerification, int minimumConfidence, int minimumLoyalty)
        {
            AllowVerification = allowVerification;
            MinimumConfidence = SocialWorld.ClampScore(minimumConfidence);
            MinimumLoyalty = SocialWorld.ClampScore(minimumLoyalty);
        }

        public ActionRequest Evaluate(SocialWorld world, string npcId, string encounteredActorId, int eventId,
            VerificationRequest request = null)
        {
            var npc = world.GetNpc(npcId);
            var evidence = npc.Knowledge.Where(item => item.EventId == eventId)
                .OrderBy(item => item.Acquisition == Acquisition.Reported)
                .ThenByDescending(item => item.Confidence)
                .ThenBy(item => item.SawOnlyEnd)
                .FirstOrDefault();
            if (evidence == null)
                return new ActionRequest(npcId, SocialAction.ContinuePatrol, null, null, "No personal knowledge of the attack.");

            var attack = world.Events[eventId - 1];
            var victim = world.GetNpc(attack.TargetId).Profile;
            if (encounteredActorId != attack.ActorId || npcId == encounteredActorId
                || string.IsNullOrEmpty(npc.Profile.FactionId) || npc.Profile.FactionId != victim.FactionId)
                return new ActionRequest(npcId, SocialAction.ContinuePatrol, null, eventId,
                    "The encounter does not qualify for the faction attack rule.");

            if (evidence.Confidence < MinimumConfidence || npc.Profile.Loyalty < MinimumLoyalty)
                return new ActionRequest(npcId, SocialAction.ContinuePatrol, null, eventId,
                    $"Faction attack rule: confidence {evidence.Confidence}/{MinimumConfidence}, loyalty {npc.Profile.Loyalty}/{MinimumLoyalty}.");

            bool validRequest = request != null && request.EventId == eventId
                && request.SourceId == encounteredActorId && request.TargetId == attack.TargetId
                && request.TargetId != npcId && !string.IsNullOrWhiteSpace(request.Question);
            if (AllowVerification && validRequest && evidence.SawOnlyEnd)
                return new ActionRequest(npcId, SocialAction.VerifyWith, request.TargetId, eventId,
                    $"Faction attack rule: verification enabled and {evidence.SourceId}'s observation was partial.",
                    request.Question);

            string reason = !AllowVerification ? "Verification is disabled."
                : !validRequest ? "No valid request to verify with the victim."
                : "The evidence does not indicate a partial observation.";
            return new ActionRequest(npcId, SocialAction.Confront, encounteredActorId, eventId,
                $"Faction attack rule: credible evidence from {evidence.SourceId}. {reason}");
        }
    }
}
