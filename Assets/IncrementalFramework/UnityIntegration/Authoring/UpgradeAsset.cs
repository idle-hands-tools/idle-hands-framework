using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    [CreateAssetMenu(menuName = "Idle Hands/Upgrade")]
    public sealed class UpgradeAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public bool initiallyUnlocked = true;
        public bool permanent;
        public ResourceAmount[] costs = new ResourceAmount[0];
        public Requirement[] prerequisites = new Requirement[0];
        public ConditionNode[] booleanNodes = new ConditionNode[0];
        public int rootConditionIndex = -1;
        public ProductionModifier[] modifiers = new ProductionModifier[0];
    }
}
