using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FurtherDevelopment.SocialAI
{
    public enum Acquisition { Experienced, Witnessed, Reported }
    public enum SocialAction { ContinuePatrol, Confront, VerifyWith }

    public sealed class NpcProfile
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string FactionId { get; }
        public int Loyalty { get; }

        public NpcProfile(string id, string displayName, string factionId, int loyalty)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An NPC needs an ID.", nameof(id));
            Id = id;
            DisplayName = displayName;
            FactionId = factionId;
            Loyalty = SocialWorld.ClampScore(loyalty);
        }
    }

    public sealed class AttackEvent
    {
        public int Id { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public IReadOnlyDictionary<string, bool> Witnesses { get; }

        internal AttackEvent(int id, string actorId, string targetId, Dictionary<string, bool> witnesses)
        {
            Id = id;
            ActorId = actorId;
            TargetId = targetId;
            // Value indicates whether the witness saw only the end of the encounter.
            Witnesses = new ReadOnlyDictionary<string, bool>(witnesses);
        }
    }

    public sealed class KnowledgeRecord
    {
        public int EventId { get; }
        public Acquisition Acquisition { get; }
        public string SourceId { get; }
        public int Confidence { get; }
        public bool SawOnlyEnd { get; }

        internal KnowledgeRecord(int eventId, Acquisition acquisition, string sourceId, int confidence, bool sawOnlyEnd)
        {
            EventId = eventId;
            Acquisition = acquisition;
            SourceId = sourceId;
            Confidence = confidence;
            SawOnlyEnd = sawOnlyEnd;
        }
    }

    public sealed class NpcRuntimeState
    {
        private readonly List<KnowledgeRecord> knowledge = new List<KnowledgeRecord>();
        public NpcProfile Profile { get; }
        public IReadOnlyList<KnowledgeRecord> Knowledge { get; }

        internal NpcRuntimeState(NpcProfile profile)
        {
            Profile = profile;
            Knowledge = knowledge.AsReadOnly();
        }

        internal void Learn(KnowledgeRecord record) => knowledge.Add(record);
    }

    public sealed class SocialWorld
    {
        private readonly Dictionary<string, NpcRuntimeState> npcs;
        private readonly Dictionary<(string observer, string subject), int> trust =
            new Dictionary<(string observer, string subject), int>();
        private readonly List<AttackEvent> events = new List<AttackEvent>();

        public string PlayerId { get; }
        public IReadOnlyList<AttackEvent> Events { get; }

        public SocialWorld(string playerId, IEnumerable<NpcProfile> profiles)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("The player needs an ID.", nameof(playerId));
            PlayerId = playerId;
            npcs = profiles.ToDictionary(profile => profile.Id, profile => new NpcRuntimeState(profile), StringComparer.Ordinal);
            if (npcs.ContainsKey(playerId)) throw new ArgumentException("The player ID must be distinct from NPC IDs.");
            Events = events.AsReadOnly();
        }

        public NpcRuntimeState GetNpc(string id) => npcs[id];

        public int GetTrust(string observer, string subject) =>
            trust.TryGetValue((observer, subject), out var score) ? score : 0;

        public void SetTrust(string observer, string subject, int score)
        {
            if (!npcs.ContainsKey(observer) || !IsActor(subject) || observer == subject)
                throw new ArgumentException("Trust requires a known NPC observer and a distinct known subject.");
            trust[(observer, subject)] = ClampScore(score);
        }

        public AttackEvent RecordAttack(string actorId, string targetId, IReadOnlyDictionary<string, bool> witnesses)
        {
            if (!IsActor(actorId) || !npcs.ContainsKey(targetId) || actorId == targetId)
                throw new ArgumentException("An attack requires a known actor and a distinct NPC target.");

            var observers = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var witness in witnesses)
            {
                if (!npcs.ContainsKey(witness.Key) || witness.Key == actorId || witness.Key == targetId)
                    throw new ArgumentException("Witness IDs must identify NPCs outside the attack participants.");
                observers.Add(witness.Key, witness.Value);
            }

            var attack = new AttackEvent(events.Count + 1, actorId, targetId, observers);
            events.Add(attack);
            npcs[targetId].Learn(new KnowledgeRecord(attack.Id, Acquisition.Experienced, targetId, 100, false));
            if (npcs.TryGetValue(actorId, out var attacker))
                attacker.Learn(new KnowledgeRecord(attack.Id, Acquisition.Experienced, actorId, 100, false));
            foreach (var witness in observers)
                npcs[witness.Key].Learn(new KnowledgeRecord(attack.Id, Acquisition.Witnessed, witness.Key, 100, witness.Value));

            return attack;
        }

        public bool TryReport(int eventId, string senderId, string receiverId, bool canCommunicate, out string reason)
        {
            if (!canCommunicate || senderId == null || receiverId == null || senderId == receiverId
                || !npcs.TryGetValue(senderId, out var sender) || !npcs.TryGetValue(receiverId, out var receiver))
            {
                reason = "The NPCs cannot communicate.";
                return false;
            }

            var source = sender.Knowledge.FirstOrDefault(item =>
                item.EventId == eventId && item.Acquisition != Acquisition.Reported);
            if (source == null)
            {
                reason = "The sender has no firsthand knowledge of this event.";
                return false;
            }

            if (receiver.Knowledge.Any(item => item.EventId == eventId
                && item.Acquisition == Acquisition.Reported && item.SourceId == senderId))
            {
                reason = "This source already reported the event to this NPC.";
                return false;
            }

            int confidence = GetTrust(receiverId, senderId);
            receiver.Learn(new KnowledgeRecord(eventId, Acquisition.Reported, senderId, confidence, source.SawOnlyEnd));
            reason = $"{receiverId} heard event #{eventId} from {senderId} (confidence {confidence}, partial observation: {source.SawOnlyEnd}).";
            return true;
        }

        internal static int ClampScore(int value) => Math.Max(0, Math.Min(100, value));

        private bool IsActor(string id) => id != null && (id == PlayerId || npcs.ContainsKey(id));
    }
}
