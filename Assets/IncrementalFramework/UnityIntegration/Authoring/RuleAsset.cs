using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    [CreateAssetMenu(menuName = "Idle Hands/Unlock Rule")]
    public sealed class RuleAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public IncrementalFramework.Economy.RuleTriggerKind triggerKind;
        [Tooltip("Optional resource/producer/upgrade/flag ID for the corresponding trigger; blank matches any.")]
        public string triggerTargetId;
        public bool filterScheduledEvent;
        public IncrementalFramework.Economy.ScheduledEventTriggerKind scheduledEventKind = IncrementalFramework.Economy.ScheduledEventTriggerKind.GrantResource;
        [Tooltip("Absolute simulation time for Time triggers only.")]
        [Min(0)] public double triggerTime;
        [Tooltip("Optional primary resource threshold. Defaults to the click resource.")]
        public ResourceAsset triggerResource;
        [Tooltip("Disable for event/flag-only conditions, especially rules matching negative balances.")]
        public bool useResourceThreshold = true;
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
