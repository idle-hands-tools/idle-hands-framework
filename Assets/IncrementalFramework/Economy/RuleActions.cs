using System;
using BreakInfinity;

namespace IncrementalFramework
{
    public enum RuleTriggerKind { Automatic, Time, Manual, ResourceThreshold, ProducerPurchased, UpgradePurchased, FlagChanged, Prestige, ResourceChanged, ScheduledEvent }
    public enum ScheduledEventTriggerKind { GrantResource = 1, BuyProducer, BuyUpgrade, ToggleProducer, Prestige, Click, SetFlag, SetModifier, TriggerRule }
    public sealed class RuleTriggerDefinition
    {
        public RuleTriggerKind Kind { get; }
        public string TargetId { get; }
        public double Time { get; }
        public ScheduledEventTriggerKind? ScheduledEventKind { get; }
        public RuleTriggerDefinition(RuleTriggerKind kind, string targetId = null, double time = 0,
            ScheduledEventTriggerKind? scheduledEventKind = null)
        {
            if (!Enum.IsDefined(typeof(RuleTriggerKind), kind) || double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; TargetId = targetId; Time = time;
            if (scheduledEventKind.HasValue && (kind != RuleTriggerKind.ScheduledEvent ||
                !Enum.IsDefined(typeof(ScheduledEventTriggerKind), scheduledEventKind.Value)))
                throw new ArgumentOutOfRangeException(nameof(scheduledEventKind));
            ScheduledEventKind = scheduledEventKind;
        }
    }
    public sealed class SpendResourceAction : RuleAction
    {
        public string ResourceId { get; }
        public BigDouble Amount { get; }
        public SpendResourceAction(string resourceId, BigDouble amount)
        { ResourceId = Number.Id(resourceId); Number.NonNegative(amount, nameof(amount)); Amount = amount; }
    }
    public sealed class SetFlagAction : RuleAction
    {
        public string FlagId { get; }
        public bool Value { get; }
        public SetFlagAction(string flagId, bool value) { FlagId = Number.Id(flagId); Value = value; }
    }
    public sealed class ScheduleGrantResourceAction : RuleAction
    {
        public string ResourceId { get; }
        public BigDouble Amount { get; }
        public double Delay { get; }
        public int Priority { get; }
        public ScheduleGrantResourceAction(string resourceId, BigDouble amount, double delay, int priority = 0)
        {
            ResourceId = Number.Id(resourceId);
            if (!Number.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
            if (double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0) throw new ArgumentOutOfRangeException(nameof(delay));
            Amount = amount; Delay = delay; Priority = priority;
        }
    }
    public sealed class SetModifierAction : RuleAction
    {
        public string ModifierId { get; }
        public ModifierDefinition Definition { get; }
        public bool Enabled { get; }
        public SetModifierAction(ModifierDefinition modifier, bool enabled = true)
        { Definition = modifier ?? throw new ArgumentNullException(nameof(modifier)); ModifierId = modifier.Id; Enabled = enabled; }
        public SetModifierAction(string modifierId, bool enabled = true) { ModifierId = Number.Id(modifierId); Enabled = enabled; }
    }
}
