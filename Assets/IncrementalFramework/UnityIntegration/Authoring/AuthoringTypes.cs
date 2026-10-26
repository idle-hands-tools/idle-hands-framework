using System;
using UnityEngine;

namespace IdleHands.UnityIntegration
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

    [Serializable]
    public sealed class ProductionModifier
    {
        public string producerId;
        public bool additive;
        public string value = "2";
    }

    public enum RuleActionKind { GrantResource, UnlockProducer, UnlockUpgrade }

    [Serializable]
    public sealed class RuleActionEntry
    {
        public RuleActionKind kind;
        public string targetId;
        public string amount = "1";
    }
}
