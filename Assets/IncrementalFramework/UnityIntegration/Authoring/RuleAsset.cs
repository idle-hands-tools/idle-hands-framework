using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Unlock Rule")]
    public sealed class RuleAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public IncrementalFramework.RuleTriggerKind triggerKind;
        [Tooltip("Optional resource/producer/upgrade/flag ID for the corresponding trigger; blank matches any.")]
        public string triggerTargetId;
        [Tooltip("Absolute simulation time for Time triggers only.")]
        [Min(0)] public double triggerTime;
        [Tooltip("Optional primary resource threshold. Defaults to the click resource.")]
        public ResourceAsset triggerResource;
        public string threshold = "0";
        [Tooltip("All conditions must be satisfied to unlock this milestone.")]
        public Requirement[] conditions = new Requirement[0];
        [Tooltip("Optional boolean expression, ANDed with threshold and conditions. Nodes reference children by index.")]
        public ConditionNode[] booleanNodes = new ConditionNode[0];
        [Tooltip("-1 disables the boolean expression; otherwise indexes booleanNodes.")]
        public int rootConditionIndex = -1;
        public RuleActionEntry[] actions = new RuleActionEntry[0];
        public bool repeat;
        [Min(0)] public float cooldownSeconds;
        public bool resetOnPrestige = true;
    }
}
