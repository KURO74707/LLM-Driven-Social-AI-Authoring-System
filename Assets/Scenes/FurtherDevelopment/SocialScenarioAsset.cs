using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    [CreateAssetMenu(menuName = "Social AI/Scenario", fileName = "Social Scenario")]
    public sealed class SocialScenarioAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Spawn
        {
            public NpcProfileAsset profile;
            public Vector3 position = new Vector3(0, 1, 0);
        }

        [Header("Roles in the attack-report scenario")]
        public Spawn victim = new Spawn();
        public Spawn witness = new Spawn();
        public Spawn responder = new Spawn();
        public InitialRelationshipsAsset initialRelationships;
        public SocialRuleAsset socialRule;
        [Header("Player")]
        public Vector3 playerPosition = new Vector3(-7, 1, -2);
        public Color playerColor = new Color(0.76f, 0.7f, 0.36f);
        [Header("Interaction")]
        [Min(0.1f)] public float witnessRadius = 7;
        [Min(0.1f)] public float npcMoveSpeed = 4;

        public Spawn[] GetSpawns() => new[] { victim, witness, responder };

        public bool TryValidate(out string error)
        {
            error = null;
            if (socialRule == null || initialRelationships == null)
                error = "Assign a social rule and initial relationships.";
            var ids = new HashSet<string>(StringComparer.Ordinal) { "player" };
            var profiles = new HashSet<NpcProfileAsset>();
            var positions = new List<Vector3> { playerPosition };
            foreach (var spawn in GetSpawns())
            {
                if (spawn == null || spawn.profile == null)
                {
                    error = "Assign profiles to all three scenario roles.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(spawn.profile.id) || !ids.Add(spawn.profile.id))
                    error = "NPC IDs must be unique, nonempty, and different from player.";
                if (string.IsNullOrWhiteSpace(spawn.profile.displayName))
                    error = "Each NPC needs a display name.";
                profiles.Add(spawn.profile);
                positions.Add(spawn.position);
            }
            foreach (var position in positions)
                if (!IsValidPosition(position))
                    error = "Spawn positions need Y = 1 and finite X/Z within -38 to 38.";
            for (int i = 0; i < positions.Count; i++)
                for (int j = i + 1; j < positions.Count; j++)
                    if (Vector3.Distance(positions[i], positions[j]) < 1.1f)
                        error = "Spawn capsules need at least 1.1 metres separation.";
            if (float.IsNaN(witnessRadius) || float.IsInfinity(witnessRadius) || witnessRadius <= 0
                || float.IsNaN(npcMoveSpeed) || float.IsInfinity(npcMoveSpeed) || npcMoveSpeed <= 0)
                error = "Witness radius and movement speed must be finite and positive.";
            if (initialRelationships == null) return false;
            if (initialRelationships.relationships == null)
            {
                error = "Initialize the relationships list (an empty list is valid).";
                return false;
            }
            var edges = new HashSet<(NpcProfileAsset, NpcProfileAsset)>();
            foreach (var edge in initialRelationships.relationships)
                if (edge == null || !profiles.Contains(edge.observer) || !profiles.Contains(edge.subject)
                    || edge.observer == edge.subject || !edges.Add((edge.observer, edge.subject)))
                    error = "Relationships must reference two distinct scenario profiles, with one entry per direction.";
            return error == null;
        }

        private static bool IsValidPosition(Vector3 position) =>
            !float.IsNaN(position.x) && !float.IsNaN(position.z) && Mathf.Abs(position.y - 1) < 0.001f
            && Mathf.Abs(position.x) <= 38 && Mathf.Abs(position.z) <= 38;
    }
}
