using UnityEngine;

namespace FurtherDevelopment.SocialAI
{
    [CreateAssetMenu(menuName = "Social AI/Faction Attack Rule", fileName = "Faction Attack Rule")]
    public sealed class SocialRuleAsset : ScriptableObject
    {
        public bool ruleEnabled = true;
        public bool allowVerification = true;
        [Range(0, 100)] public int minimumConfidence = 60;
        [Range(0, 100)] public int minimumLoyalty = 60;

        public FactionAttackRule CreateRule() =>
            new FactionAttackRule(allowVerification, minimumConfidence, minimumLoyalty, ruleEnabled);
    }
}
