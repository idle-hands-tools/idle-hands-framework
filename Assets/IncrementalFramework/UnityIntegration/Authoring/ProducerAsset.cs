using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Producer")]
    public sealed class ProducerAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public bool initiallyUnlocked = true;
        public ResourceAmount[] costs = new ResourceAmount[0];
        public string costGrowth = "1.15";
        public ResourceAmount[] productionPerSecond = new ResourceAmount[0];
        public Requirement[] prerequisites = new Requirement[0];
    }
}
