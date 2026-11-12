using System;
using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    [CreateAssetMenu(menuName = "Social AI/Initial Relationships", fileName = "Initial Relationships")]
    public sealed class InitialRelationshipsAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Relationship
        {
            public NpcProfileAsset observer;
            public NpcProfileAsset subject;
            [Range(0, 100)] public int trust = 80;
        }

        [Tooltip("Each entry is directional. Missing entries start at zero trust.")]
        public Relationship[] relationships = Array.Empty<Relationship>();

        public void ApplyTo(SocialWorld world)
        {
            foreach (var edge in relationships)
                world.SetTrust(edge.observer.id, edge.subject.id, edge.trust);
        }
    }
}
