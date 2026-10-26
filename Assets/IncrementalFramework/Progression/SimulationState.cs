using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    // Detached DTOs are copied on both import and export; none are live writable simulation state.
    public sealed class SimulationState
    {
        public double Time { get; set; }
        public ulong RandomState { get; set; }
        public long PrestigeCount { get; set; }
        public BigDouble PermanentMultiplier { get; set; } = BigDouble.One;
        public List<ResourceState> Resources { get; set; } = new List<ResourceState>();
        public List<ProducerState> Producers { get; set; } = new List<ProducerState>();
        public List<UpgradeState> Upgrades { get; set; } = new List<UpgradeState>();
        public List<RuleState> Rules { get; set; } = new List<RuleState>();
        public long NextEventSequence { get; set; } = 1;
        public List<EventState> Events { get; set; } = new List<EventState>();
        public double? PendingTarget { get; set; }
    }
    public sealed class ResourceState
    {
        public string Id { get; set; }
        public BigDouble Balance { get; set; }
    }
    public sealed class ProducerState
    {
        public string Id { get; set; }
        public long Owned { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Unlocked { get; set; }
    }
    public sealed class UpgradeState
    {
        public string Id { get; set; }
        public bool Purchased { get; set; }
        public bool Unlocked { get; set; }
    }
    public sealed class RuleState
    {
        public string Id { get; set; }
        public bool Fired { get; set; }
        public double NextEligibleTime { get; set; }
        public bool Disabled { get; set; }
    }
    public sealed class EventState
    {
        public double Time { get; set; }
        public int Priority { get; set; }
        public long Sequence { get; set; }
        public SimulationEventKind Kind { get; set; }
        public string TargetId { get; set; }
        public BigDouble Amount { get; set; }
        public int Count { get; set; } = 1;
        public bool Enabled { get; set; } = true;
    }
}
