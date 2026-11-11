using System;
using System.Collections.Generic;
using System.Linq;

namespace FurtherDevelopment.SocialAI
{
    public static class AlexSamMorganDemo
    {
        public static IReadOnlyList<string> Run(int morganTrustInSam, int morganLoyalty,
            int minimumConfidence, int minimumLoyalty)
        {
            const string faction = "red-wolves";
            var world = new SocialWorld("player", new[]
            {
                new NpcProfile("alex", "Alex", faction, 80),
                new NpcProfile("sam", "Sam", faction, 80),
                new NpcProfile("morgan", "Morgan", faction, morganLoyalty)
            });
            world.SetTrust("morgan", "sam", morganTrustInSam);
            world.SetTrust("sam", "morgan", 40);

            var allow = new FactionAttackRule(true, minimumConfidence, minimumLoyalty);
            var deny = new FactionAttackRule(false, minimumConfidence, minimumLoyalty);
            var attack = world.RecordAttack(world.PlayerId, "alex",
                new Dictionary<string, bool> { ["sam"] = true });

            var timeline = new List<string>
            {
                $"Attack #{attack.Id}: {attack.ActorId} → {attack.TargetId}",
                DescribeKnowledge(world.GetNpc("alex")),
                DescribeKnowledge(world.GetNpc("sam")),
                DescribeKnowledge(world.GetNpc("morgan")),
                $"Before report: {allow.Evaluate(world, "morgan", world.PlayerId, attack.Id)}",
                $"Trust: Morgan → Sam {world.GetTrust("morgan", "sam")}, Sam → Morgan {world.GetTrust("sam", "morgan")}"
            };

            if (!world.TryReport(attack.Id, "sam", "morgan", true, out var report))
                throw new InvalidOperationException(report);
            timeline.Add($"Report: {report}");
            timeline.Add(DescribeKnowledge(world.GetNpc("morgan")));

            var request = new VerificationRequest(attack.Id, world.PlayerId, "alex",
                "What happened before the player hit you?");
            timeline.Add($"Player requests verification with {request.TargetId}: {request.Question}");
            timeline.Add($"Verification disabled: {deny.Evaluate(world, "morgan", world.PlayerId, attack.Id, request)}");
            timeline.Add($"Verification enabled: {allow.Evaluate(world, "morgan", world.PlayerId, attack.Id, request)}");
            return timeline.AsReadOnly();
        }

        private static string DescribeKnowledge(NpcRuntimeState npc)
        {
            if (npc.Knowledge.Count == 0) return $"{npc.Profile.DisplayName}: no knowledge";
            return $"{npc.Profile.DisplayName}: " + string.Join(", ", npc.Knowledge.Select(item =>
                $"{item.Acquisition} #{item.EventId} from {item.SourceId} (confidence {item.Confidence})"));
        }
    }
}
