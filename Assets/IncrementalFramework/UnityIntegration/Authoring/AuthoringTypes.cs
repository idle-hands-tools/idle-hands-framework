using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    [Serializable]
    public sealed class ResourceAmount
    {
        public ResourceAsset resource;
        public string amount = "1";
    }

    public enum RequirementKind { Resource, LifetimeResource, Producer, Upgrade, Prestige }

    [Serializable]
    public sealed class Requirement
    {
        public RequirementKind kind;
        [Tooltip("ID of resource, producer or upgrade. Ignored for prestige.")]
        public string targetId;
        public string amount = "1";
    }

    public enum ConditionOperator { Requirement, Flag, All, Any, Not }

    [Serializable]
    public sealed class ConditionNode
    {
        public ConditionOperator operation;
        public Requirement requirement = new Requirement();
        public string flagId;
        public bool expected = true;
        [Tooltip("Indices in the owner's booleanNodes array. Not requires exactly one child.")]
        public int[] children = new int[0];
    }

    public enum AuthoredModifierKind { Flat, AddPercent, Multiply, Override }
    public enum AuthoredModifierScope { Producer, Resource, ProducerCategory, Global, Click, ResourceCategory }

    [Serializable]
    public sealed class ProductionModifier
    {
        public AuthoredModifierScope scope;
        [Tooltip("Producer, resource or category ID. Global requires blank; blank click target applies to all resources.")]
        public string targetId;
        public AuthoredModifierKind kind = AuthoredModifierKind.Multiply;
        public int priority;
        [Tooltip("AddPercent is a fractional bonus: 0.1 means +10%.")]
        public string value = "2";
    }

    public enum RuleActionKind { GrantResource, UnlockProducer, UnlockUpgrade, SpendResource, SetFlag, ScheduleGrantResource, SetModifier }

    [Serializable]
    public sealed class RuleActionEntry
    {
        public RuleActionKind kind;
        [Tooltip("Resource/producer/upgrade/flag ID. Leave blank for SetModifier; it uses modifierId.")]
        public string targetId;
        public string amount = "1";
        public bool flagValue = true;
        [Min(0)] public float delaySeconds;
        public int priority;
        public string modifierId;
        public bool defineModifier = true;
        public ProductionModifier modifier = new ProductionModifier();
        public bool enabled = true;
    }
}
