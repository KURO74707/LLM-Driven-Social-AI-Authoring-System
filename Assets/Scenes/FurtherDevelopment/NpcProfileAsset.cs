using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    [CreateAssetMenu(menuName = "Social AI/NPC Profile", fileName = "NPC Profile")]
    public sealed class NpcProfileAsset : ScriptableObject
    {
        [Tooltip("Stable, unique ID. The player ID is reserved.")]
        public string id = "npc";
        public string displayName = "NPC";
        public Color color = Color.white;
        public string factionId = "red-wolves";
        [Range(0, 100)] public int loyalty = 80;
        [TextArea] public string background;
        [TextArea] public string personality;

        public NpcProfile CreateProfile() => new NpcProfile(id, displayName, factionId, loyalty);
    }
}
