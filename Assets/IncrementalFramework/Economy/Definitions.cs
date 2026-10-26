using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public static class Number
    {
        public static bool IsFinite(BigDouble value) => !BigDouble.IsNaN(value) && !BigDouble.IsInfinity(value);
        public static void NonNegative(BigDouble value, string name)
        {
            if (!IsFinite(value) || value < BigDouble.Zero) throw new ArgumentOutOfRangeException(name);
        }
        internal static IReadOnlyList<T> Copy<T>(T[] values) => Array.AsReadOnly((T[])(values ?? Array.Empty<T>()).Clone());
        internal static string Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An identifier is required.");
            return value;
        }
    }

    public sealed class ResourceAmount
    {
        public string ResourceId { get; }
        public BigDouble Amount { get; }
        public ResourceAmount(string resourceId, BigDouble amount)
        { ResourceId = Number.Id(resourceId); Number.NonNegative(amount, nameof(amount)); Amount = amount; }
    }

    public sealed class ResourceDefinition
    {
        public string Id { get; }
        public BigDouble Initial { get; }
        public BigDouble? Cap { get; }
        public bool ResetOnPrestige { get; }
        public ResourceDefinition(string id, BigDouble initial = default, BigDouble? cap = null, bool resetOnPrestige = true)
        {
            Id = Number.Id(id); Number.NonNegative(initial, nameof(initial));
            if (cap.HasValue) { Number.NonNegative(cap.Value, nameof(cap)); if (initial > cap.Value) throw new ArgumentException("Initial exceeds cap."); }
            Initial = initial; Cap = cap; ResetOnPrestige = resetOnPrestige;
        }
    }

    public sealed class ProducerDefinition
    {
        public string Id { get; }
        public IReadOnlyList<ResourceAmount> Costs { get; }
        public IReadOnlyList<ResourceAmount> Production { get; }
        public double CostGrowth { get; }
        public bool InitiallyUnlocked { get; }
        public ProducerDefinition(string id, ResourceAmount[] costs, ResourceAmount[] production, double costGrowth = 1.15, bool initiallyUnlocked = true)
        {
            Id = Number.Id(id); Costs = Number.Copy(costs); Production = Number.Copy(production);
            if (double.IsNaN(costGrowth) || double.IsInfinity(costGrowth) || costGrowth < 1) throw new ArgumentOutOfRangeException(nameof(costGrowth));
            CostGrowth = costGrowth; InitiallyUnlocked = initiallyUnlocked;
        }
    }

    public enum ModifierKind { Multiply, Add }

    public sealed class ModifierDefinition
    {
        public string Id { get; }
        public string TargetProducerId { get; }
        public BigDouble Value { get; }
        public ModifierKind Kind { get; }
        public ModifierDefinition(string id, string targetProducerId, BigDouble multiplier, ModifierKind kind = ModifierKind.Multiply)
        {
            Id = Number.Id(id); TargetProducerId = Number.Id(targetProducerId);
            if (kind != ModifierKind.Multiply && kind != ModifierKind.Add) throw new ArgumentOutOfRangeException(nameof(kind));
            Number.NonNegative(multiplier, nameof(multiplier)); Value = multiplier; Kind = kind;
        }
    }

    public sealed class UpgradeDefinition
    {
        public string Id { get; }
        public IReadOnlyList<ResourceAmount> Costs { get; }
        public IReadOnlyList<ModifierDefinition> Modifiers { get; }
        public bool Permanent { get; }
        public bool InitiallyUnlocked { get; }
        public UpgradeDefinition(string id, ResourceAmount[] costs, ModifierDefinition[] modifiers, bool permanent = false, bool initiallyUnlocked = true)
        {
            Id = Number.Id(id); Costs = Number.Copy(costs); Modifiers = Number.Copy(modifiers);
            Permanent = permanent; InitiallyUnlocked = initiallyUnlocked;
        }
    }

    public abstract class RuleAction { }
    public sealed class GrantResourceAction : RuleAction
    {
        public string ResourceId { get; }
        public BigDouble Amount { get; }
        public GrantResourceAction(string resourceId, BigDouble amount)
        { ResourceId = Number.Id(resourceId); Number.NonNegative(amount, nameof(amount)); Amount = amount; }
    }
    public sealed class UnlockProducerAction : RuleAction
    {
        public string ProducerId { get; }
        public UnlockProducerAction(string producerId) { ProducerId = Number.Id(producerId); }
    }
    public sealed class UnlockUpgradeAction : RuleAction
    {
        public string UpgradeId { get; }
        public UnlockUpgradeAction(string upgradeId) { UpgradeId = Number.Id(upgradeId); }
    }
    public sealed class RuleDefinition
    {
        public string Id { get; }
        public string ResourceId { get; }
        public BigDouble Threshold { get; }
        public IReadOnlyList<RuleAction> Actions { get; }
        public bool Repeat { get; }
        public double Cooldown { get; }
        public bool ResetOnPrestige { get; }
        public RuleDefinition(string id, string resourceId, BigDouble threshold, RuleAction[] actions,
            bool repeat = false, double cooldown = 0, bool resetOnPrestige = true)
        {
            Id = Number.Id(id); ResourceId = Number.Id(resourceId); Number.NonNegative(threshold, nameof(threshold));
            if (double.IsNaN(cooldown) || double.IsInfinity(cooldown) || cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown));
            Threshold = threshold; Actions = Number.Copy(actions); Repeat = repeat; Cooldown = cooldown; ResetOnPrestige = resetOnPrestige;
        }
    }

    public sealed class PrestigeDefinition
    {
        public string ResourceId { get; }
        public BigDouble Threshold { get; }
        public BigDouble MultiplierPerPrestige { get; }
        public bool ResetProducers { get; }
        public bool ResetUpgrades { get; }
        public PrestigeDefinition(string resourceId, BigDouble threshold, BigDouble multiplierPerPrestige,
            bool resetProducers = true, bool resetUpgrades = true)
        {
            ResourceId = Number.Id(resourceId); Number.NonNegative(threshold, nameof(threshold));
            if (threshold <= BigDouble.Zero || !Number.IsFinite(multiplierPerPrestige) || multiplierPerPrestige < BigDouble.One)
                throw new ArgumentOutOfRangeException(nameof(multiplierPerPrestige));
            Threshold = threshold; MultiplierPerPrestige = multiplierPerPrestige;
            ResetProducers = resetProducers; ResetUpgrades = resetUpgrades;
        }
    }
}
