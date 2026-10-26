using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Unlock Rule")]
    public sealed class RuleAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        [Tooltip("Optional primary resource threshold. Defaults to the click resource.")]
        public ResourceAsset triggerResource;
        public string threshold = "0";
        [Tooltip("All conditions must be satisfied to unlock this milestone.")]
        public Requirement[] conditions = new Requirement[0];
        public RuleActionEntry[] actions = new RuleActionEntry[0];
        public bool repeat;
        [Min(0)] public float cooldownSeconds;
        public bool resetOnPrestige = true;
    }
}
