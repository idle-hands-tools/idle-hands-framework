using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Producer")]
    public sealed class ProducerAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public string category;
        public bool initiallyUnlocked = true;
        public ResourceAmount[] costs = new ResourceAmount[0];
        public string costGrowth = "1.15";
        public ResourceAmount[] productionPerSecond = new ResourceAmount[0];
        public Requirement[] prerequisites = new Requirement[0];
        public ConditionNode[] booleanNodes = new ConditionNode[0];
        public int rootConditionIndex = -1;
        [Tooltip("Override the game-wide producer reset setting.")]
        public bool usePrestigePolicy;
        public IncrementalFramework.ResetPolicy prestigePolicy;
        [Tooltip("Non-negative owned-count multiplier used only by Transform; fractional counts are floored.")]
        public string prestigeTransform = "1";
    }
}
