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
        public static BigDouble Add(BigDouble left, BigDouble right)
        {
            if (!IsFinite(left) || !IsFinite(right) || left < BigDouble.Zero || right < BigDouble.Zero)
                throw new ArithmeticException("Invalid nonnegative addition.");
            if (left == BigDouble.Zero) return right;
            if (right == BigDouble.Zero) return left;
            BigDouble larger = left.Exponent >= right.Exponent ? left : right;
            BigDouble smaller = left.Exponent >= right.Exponent ? right : left;
            if (smaller.Exponent <= long.MaxValue - 17 && larger.Exponent > smaller.Exponent + 17) return larger;
            BigDouble result = left + right;
            BigDouble maximum = left >= right ? left : right;
            if (!IsFinite(result) || result.Exponent < maximum.Exponent) throw new ArithmeticException("BigDouble addition overflows.");
            // The vendored adder rounds significant digits; a nonnegative add must never lose existing value.
            return result < maximum ? maximum : result;
        }
        public static BigDouble Multiply(BigDouble left, BigDouble right)
        {
            if (!IsFinite(left) || !IsFinite(right) || left < BigDouble.Zero || right < BigDouble.Zero)
                throw new ArithmeticException("Invalid nonnegative multiplication.");
            if (left == BigDouble.Zero || right == BigDouble.Zero) return BigDouble.Zero;
            double mantissa = left.Mantissa * right.Mantissa;
            bool normalize = mantissa >= 10;
            if (normalize) mantissa /= 10;
            long exponent;
            try
            {
                exponent = checked(left.Exponent + right.Exponent);
                if (normalize) exponent = checked(exponent + 1);
            }
            catch (OverflowException error)
            {
                decimal exactExponent = (decimal)left.Exponent + right.Exponent + (normalize ? 1 : 0);
                if (exactExponent < long.MinValue) return BigDouble.Zero;
                if (exactExponent > long.MaxValue) throw new ArithmeticException("BigDouble exponent overflows.", error);
                exponent = (long)exactExponent;
            }
            return BigDouble.FromMantissaExponentNoNormalize(mantissa, exponent);
        }
        public static BigDouble AddSigned(BigDouble left, BigDouble right)
        {
            if (!IsFinite(left) || !IsFinite(right)) throw new ArithmeticException("Nonfinite signed addition.");
            if (left >= BigDouble.Zero && right >= BigDouble.Zero) return Add(left, right);
            if (left <= BigDouble.Zero && right <= BigDouble.Zero) return -Add(-left, -right);
            BigDouble leftMagnitude = BigDouble.Abs(left), rightMagnitude = BigDouble.Abs(right);
            BigDouble larger = leftMagnitude >= rightMagnitude ? left : right;
            BigDouble smaller = leftMagnitude >= rightMagnitude ? right : left;
            if (smaller.Exponent <= long.MaxValue - 17 && larger.Exponent > smaller.Exponent + 17) return larger;
            BigDouble result = left + right;
            if (!IsFinite(result) || BigDouble.Abs(result) > BigDouble.Abs(larger)) throw new ArithmeticException("Signed addition exceeds the exponent range.");
            return result;
        }
        public static BigDouble Pow(BigDouble value, long exponent)
        {
            if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent));
            BigDouble result = BigDouble.One;
            while (exponent > 0)
            {
                if ((exponent & 1) != 0) result = Multiply(result, value);
                exponent >>= 1;
                if (exponent > 0) value = Multiply(value, value);
            }
            return result;
        }
        public static double RatioToDouble(BigDouble numerator, BigDouble denominator)
        {
            NonNegative(numerator, nameof(numerator)); NonNegative(denominator, nameof(denominator));
            if (denominator == BigDouble.Zero) return double.PositiveInfinity;
            if (numerator == BigDouble.Zero) return 0;
            long exponent;
            try { exponent = checked(numerator.Exponent - denominator.Exponent); }
            catch (OverflowException) { return numerator.Exponent > denominator.Exponent ? double.PositiveInfinity : 0; }
            if (exponent > 309) return double.PositiveInfinity;
            if (exponent < -325) return 0;
            double mantissa = numerator.Mantissa / denominator.Mantissa;
            if (exponent > 308) return (mantissa * 10) * Math.Pow(10, exponent - 1);
            if (exponent < -308) return (mantissa * 1e-16) * Math.Pow(10, exponent + 16);
            return mantissa * Math.Pow(10, exponent);
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
        public string Category { get; }
        public ResetPolicy PrestigePolicy { get; }
        public BigDouble PrestigeTransform { get; }
        public ResourceDefinition(string id, BigDouble initial = default, BigDouble? cap = null, bool resetOnPrestige = true, string category = null,
            ResetPolicy? prestigePolicy = null, BigDouble? prestigeTransform = null)
        {
            Id = Number.Id(id); Number.NonNegative(initial, nameof(initial));
            if (cap.HasValue) { Number.NonNegative(cap.Value, nameof(cap)); if (initial > cap.Value) throw new ArgumentException("Initial exceeds cap."); }
            Initial = initial; Cap = cap; ResetOnPrestige = resetOnPrestige;
            Category = category;
            PrestigePolicy = prestigePolicy ?? (resetOnPrestige ? ResetPolicy.Reset : ResetPolicy.Preserve);
            ResetOnPrestige = PrestigePolicy != ResetPolicy.Preserve;
            if (!Enum.IsDefined(typeof(ResetPolicy), PrestigePolicy)) throw new ArgumentOutOfRangeException(nameof(prestigePolicy));
            PrestigeTransform = prestigeTransform ?? BigDouble.One; Number.NonNegative(PrestigeTransform, nameof(prestigeTransform));
        }
    }

    public sealed class ProducerDefinition
    {
        public string Id { get; }
        public IReadOnlyList<ResourceAmount> Costs { get; }
        public IReadOnlyList<ResourceAmount> Production { get; }
        public double CostGrowth { get; }
        public bool InitiallyUnlocked { get; }
        public IReadOnlyList<RequirementDefinition> Prerequisites { get; }
        public ConditionDefinition PrerequisiteCondition { get; }
        public string Category { get; }
        public ResetPolicy? PrestigePolicy { get; }
        public BigDouble PrestigeTransform { get; }
        public ProducerDefinition(string id, ResourceAmount[] costs, ResourceAmount[] production, double costGrowth = 1.15, bool initiallyUnlocked = true,
            RequirementDefinition[] prerequisites = null, string category = null, ResetPolicy? prestigePolicy = null, BigDouble? prestigeTransform = null,
            ConditionDefinition prerequisiteCondition = null)
        {
            Id = Number.Id(id); Costs = Number.Copy(costs); Production = Number.Copy(production);
            if (double.IsNaN(costGrowth) || double.IsInfinity(costGrowth) || costGrowth < 1) throw new ArgumentOutOfRangeException(nameof(costGrowth));
            CostGrowth = costGrowth; InitiallyUnlocked = initiallyUnlocked;
            Prerequisites = Number.Copy(prerequisites);
            PrerequisiteCondition = ConditionDefinition.All(ConditionDefinition.FromRequirements(Prerequisites),
                prerequisiteCondition ?? ConditionDefinition.All());
            Category = category;
            if (prestigePolicy.HasValue && !Enum.IsDefined(typeof(ResetPolicy), prestigePolicy.Value)) throw new ArgumentOutOfRangeException(nameof(prestigePolicy));
            PrestigePolicy = prestigePolicy; PrestigeTransform = prestigeTransform ?? BigDouble.One;
            Number.NonNegative(PrestigeTransform, nameof(prestigeTransform));
        }
    }

    public enum ModifierKind { Multiply, Add, AddPercent = Add, Flat, Override }
    public enum ModifierTarget { ProducerProduction, ResourceProduction, GlobalProduction, Click, ResourceCategoryProduction, ProducerCategoryProduction }

    public sealed class ModifierDefinition
    {
        public string Id { get; }
        public string TargetProducerId { get; }
        public string TargetId { get; }
        public ModifierTarget Target { get; }
        public BigDouble Value { get; }
        public ModifierKind Kind { get; }
        public int Priority { get; }
        public ModifierDefinition(string id, string targetProducerId, BigDouble multiplier, ModifierKind kind = ModifierKind.Multiply, int priority = 0)
            : this(id, ModifierTarget.ProducerProduction, targetProducerId, multiplier, kind, priority) { }
        public ModifierDefinition(string id, ModifierTarget target, string targetId, BigDouble multiplier, ModifierKind kind = ModifierKind.Multiply, int priority = 0)
        {
            Id = Number.Id(id);
            if (!Enum.IsDefined(typeof(ModifierTarget), target)) throw new ArgumentOutOfRangeException(nameof(target));
            Target = target; TargetId = targetId;
            if (target == ModifierTarget.ProducerProduction || target == ModifierTarget.ResourceProduction ||
                target == ModifierTarget.ResourceCategoryProduction || target == ModifierTarget.ProducerCategoryProduction) Number.Id(targetId);
            TargetProducerId = target == ModifierTarget.ProducerProduction ? targetId : null;
            if (!Enum.IsDefined(typeof(ModifierKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Number.IsFinite(multiplier)) throw new ArgumentOutOfRangeException(nameof(multiplier));
            if (kind == ModifierKind.Multiply || kind == ModifierKind.Override) Number.NonNegative(multiplier, nameof(multiplier));
            Value = multiplier; Kind = kind; Priority = priority;
        }
    }

    public sealed class UpgradeDefinition
    {
        public string Id { get; }
        public IReadOnlyList<ResourceAmount> Costs { get; }
        public IReadOnlyList<ModifierDefinition> Modifiers { get; }
        public bool Permanent { get; }
        public bool InitiallyUnlocked { get; }
        public IReadOnlyList<RequirementDefinition> Prerequisites { get; }
        public ConditionDefinition PrerequisiteCondition { get; }
        public UpgradeDefinition(string id, ResourceAmount[] costs, ModifierDefinition[] modifiers, bool permanent = false, bool initiallyUnlocked = true,
            RequirementDefinition[] prerequisites = null, ConditionDefinition prerequisiteCondition = null)
        {
            Id = Number.Id(id); Costs = Number.Copy(costs); Modifiers = Number.Copy(modifiers);
            Permanent = permanent; InitiallyUnlocked = initiallyUnlocked;
            Prerequisites = Number.Copy(prerequisites);
            PrerequisiteCondition = ConditionDefinition.All(ConditionDefinition.FromRequirements(Prerequisites),
                prerequisiteCondition ?? ConditionDefinition.All());
        }
    }

    public enum RequirementKind { Resource, LifetimeResource, Producer, Upgrade, Prestige }
    public sealed class RequirementDefinition
    {
        public RequirementKind Kind { get; }
        public string TargetId { get; }
        public BigDouble Amount { get; }
        public RequirementDefinition(RequirementKind kind, string targetId, BigDouble amount)
        {
            if (!Enum.IsDefined(typeof(RequirementKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind != RequirementKind.Prestige) Number.Id(targetId);
            Number.NonNegative(amount, nameof(amount)); Kind = kind; TargetId = targetId; Amount = amount;
        }
    }
        public sealed class FlagDefinition
        {
            public string Id { get; }
            public bool Initial { get; }
            public bool ResetOnPrestige { get; }
            public FlagDefinition(string id, bool initial = false, bool resetOnPrestige = true)
            { Id = Number.Id(id); Initial = initial; ResetOnPrestige = resetOnPrestige; }
        }
        public enum ConditionKind { Requirement, Flag, And, Or, Not }
        public sealed class ConditionDefinition
        {
            public ConditionKind Kind { get; }
            public RequirementDefinition Requirement { get; }
            public string FlagId { get; }
            public bool Expected { get; }
            public IReadOnlyList<ConditionDefinition> Children { get; }
            private ConditionDefinition(ConditionKind kind, RequirementDefinition requirement, string flagId, bool expected, ConditionDefinition[] children)
            { Kind = kind; Requirement = requirement; FlagId = flagId; Expected = expected; Children = Number.Copy(children); }
            public static ConditionDefinition Require(RequirementDefinition requirement) =>
                new ConditionDefinition(ConditionKind.Requirement, requirement ?? throw new ArgumentNullException(nameof(requirement)), null, false, null);
            public static ConditionDefinition Flag(string id, bool expected = true) =>
                new ConditionDefinition(ConditionKind.Flag, null, Number.Id(id), expected, null);
            public static ConditionDefinition All(params ConditionDefinition[] children) => Composite(ConditionKind.And, children);
            public static ConditionDefinition Any(params ConditionDefinition[] children) => Composite(ConditionKind.Or, children);
            public static ConditionDefinition Not(ConditionDefinition child) => Composite(ConditionKind.Not, new[] { child });
            private static ConditionDefinition Composite(ConditionKind kind, ConditionDefinition[] children)
            {
                foreach (var child in children ?? Array.Empty<ConditionDefinition>()) if (child == null) throw new ArgumentException("Null condition.");
                return new ConditionDefinition(kind, null, null, false, children);
            }
            internal static ConditionDefinition FromRequirements(IReadOnlyList<RequirementDefinition> requirements)
            {
                var children = new ConditionDefinition[requirements.Count];
                for (int i = 0; i < children.Length; i++) children[i] = Require(requirements[i]);
                return All(children);
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
        public IReadOnlyList<RequirementDefinition> Conditions { get; }
        public ConditionDefinition Condition { get; }
        public RuleTriggerDefinition Trigger { get; }
        public RuleDefinition(string id, string resourceId, BigDouble threshold, RuleAction[] actions,
            bool repeat = false, double cooldown = 0, bool resetOnPrestige = true, RuleTriggerDefinition trigger = null)
        {
            Id = Number.Id(id); ResourceId = Number.Id(resourceId); Number.NonNegative(threshold, nameof(threshold));
            if (double.IsNaN(cooldown) || double.IsInfinity(cooldown) || cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown));
            Threshold = threshold; Actions = Number.Copy(actions); Repeat = repeat; Cooldown = cooldown; ResetOnPrestige = resetOnPrestige;
            Conditions = Number.Copy(new[] { new RequirementDefinition(RequirementKind.Resource, resourceId, threshold) });
            Condition = ConditionDefinition.FromRequirements(Conditions);
            Trigger = trigger ?? new RuleTriggerDefinition(RuleTriggerKind.Automatic);
        }
        public RuleDefinition(string id, RequirementDefinition[] conditions, RuleAction[] actions,
            bool repeat = false, double cooldown = 0, bool resetOnPrestige = true, RuleTriggerDefinition trigger = null)
        {
            Id = Number.Id(id);
            if (double.IsNaN(cooldown) || double.IsInfinity(cooldown) || cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown));
            Conditions = Number.Copy(conditions); Actions = Number.Copy(actions);
            Repeat = repeat; Cooldown = cooldown; ResetOnPrestige = resetOnPrestige;
            Condition = ConditionDefinition.FromRequirements(Conditions);
            Trigger = trigger ?? new RuleTriggerDefinition(RuleTriggerKind.Automatic);
        }
        public RuleDefinition(string id, ConditionDefinition condition, RuleAction[] actions,
            bool repeat = false, double cooldown = 0, bool resetOnPrestige = true, RuleTriggerDefinition trigger = null)
        {
            Id = Number.Id(id); Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            if (double.IsNaN(cooldown) || double.IsInfinity(cooldown) || cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown));
            Conditions = Number.Copy<RequirementDefinition>(null); Actions = Number.Copy(actions);
            Repeat = repeat; Cooldown = cooldown; ResetOnPrestige = resetOnPrestige;
            Trigger = trigger ?? new RuleTriggerDefinition(RuleTriggerKind.Automatic);
        }
    }

    public sealed class PrestigeDefinition
    {
        public string ResourceId { get; }
        public BigDouble Threshold { get; }
        public BigDouble MultiplierPerPrestige { get; }
        public bool ResetProducers { get; }
        public bool ResetUpgrades { get; }
        public string RewardResourceId { get; }
        public double RewardExponent { get; }
        public BigDouble RewardMultiplier { get; }
        public BigDouble ProductionBonusPerReward { get; }
        public bool UsesLifetimeReward => RewardResourceId != null;
        public PrestigeDefinition(string resourceId, BigDouble threshold, BigDouble multiplierPerPrestige,
            bool resetProducers = true, bool resetUpgrades = true)
        {
            ResourceId = Number.Id(resourceId); Number.NonNegative(threshold, nameof(threshold));
            if (threshold <= BigDouble.Zero || !Number.IsFinite(multiplierPerPrestige) || multiplierPerPrestige < BigDouble.One)
                throw new ArgumentOutOfRangeException(nameof(multiplierPerPrestige));
            Threshold = threshold; MultiplierPerPrestige = multiplierPerPrestige;
            ResetProducers = resetProducers; ResetUpgrades = resetUpgrades;
        }
        public PrestigeDefinition(string resourceId, string rewardResourceId, BigDouble threshold, double rewardExponent = 0.5,
            BigDouble? rewardMultiplier = null, BigDouble? productionBonusPerReward = null, bool resetProducers = true, bool resetUpgrades = true)
            : this(resourceId, threshold, BigDouble.One, resetProducers, resetUpgrades)
        {
            RewardResourceId = Number.Id(rewardResourceId);
            if (resourceId == rewardResourceId) throw new ArgumentException("Prestige reward currency must differ from its source.");
            if (double.IsNaN(rewardExponent) || double.IsInfinity(rewardExponent) || rewardExponent <= 0) throw new ArgumentOutOfRangeException(nameof(rewardExponent));
            RewardExponent = rewardExponent; RewardMultiplier = rewardMultiplier ?? BigDouble.One;
            ProductionBonusPerReward = productionBonusPerReward ?? new BigDouble(0.1);
            Number.NonNegative(RewardMultiplier, nameof(rewardMultiplier)); Number.NonNegative(ProductionBonusPerReward, nameof(productionBonusPerReward));
            if (RewardMultiplier == BigDouble.Zero) throw new ArgumentOutOfRangeException(nameof(rewardMultiplier));
        }
    }
}
