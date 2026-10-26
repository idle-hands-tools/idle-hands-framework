using BreakInfinity;

namespace IncrementalFramework
{
    public sealed class GrantResourceCommand : ISimulationCommand
    {
        public string ResourceId { get; }
        public BigDouble Amount { get; }
        public GrantResourceCommand(string resourceId, BigDouble amount) { ResourceId = resourceId; Amount = amount; }
    }
    public sealed class BuyProducerCommand : ISimulationCommand
    {
        public string ProducerId { get; }
        public int Count { get; }
        public BuyProducerCommand(string producerId, int count = 1) { ProducerId = producerId; Count = count; }
    }
    public sealed class BuyUpgradeCommand : ISimulationCommand
    {
        public string UpgradeId { get; }
        public BuyUpgradeCommand(string upgradeId) { UpgradeId = upgradeId; }
    }
    public sealed class ToggleProducerCommand : ISimulationCommand
    {
        public string ProducerId { get; }
        public bool Enabled { get; }
        public ToggleProducerCommand(string producerId, bool enabled) { ProducerId = producerId; Enabled = enabled; }
    }
    public sealed class PrestigeCommand : ISimulationCommand { }

    public enum SimulationEventKind { Rule, GrantResource, BuyProducer, BuyUpgrade, ToggleProducer, Prestige }

    public sealed class SimulationEvent
    {
        public SimulationEventKind Kind { get; }
        public int Index { get; }
        public BigDouble Amount { get; }
        public int Count { get; }
        public bool Enabled { get; }
        public SimulationEvent(SimulationEventKind kind, int index = 0, BigDouble amount = default, int count = 1, bool enabled = true)
        { Kind = kind; Index = index; Amount = amount; Count = count; Enabled = enabled; }
    }
}
