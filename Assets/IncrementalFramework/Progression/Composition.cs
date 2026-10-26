using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public interface IGameModule
    {
        void Register(ModuleBuilder builder);
    }

    public interface ICommandHandler<in T> where T : ISimulationCommand
    {
        CommandResult Build(T command, SimulationReadModel state, SimulationTransaction transaction);
    }

    public interface ISimulationSystem
    {
        void BeforeAdvance(SimulationReadModel state, double target, SimulationSchedule schedule);
    }

    /// <summary>Reserved contract for future input-consuming processes; the MVP does not solve flows.</summary>
    public interface IResourceInputProcess
    {
        IReadOnlyList<ResourceAmount> Inputs { get; }
        IReadOnlyList<ResourceAmount> Outputs { get; }
    }

    public interface IResourceFlowSolver
    {
        void Schedule(IReadOnlyList<IResourceInputProcess> processes, SimulationSchedule schedule);
    }

    public sealed class SimulationReadModel
    {
        private readonly Simulation simulation;
        internal SimulationReadModel(Simulation simulation) { this.simulation = simulation; }
        public double Time => simulation.Time;
        public ContentDatabase Content => simulation.Content;
        public ResourceView Resource(int index) => simulation.Resources[index];
        public ProducerView Producer(int index) => simulation.Producers[index];
        public UpgradeView Upgrade(int index) => simulation.Upgrades[index];
    }

    public sealed class SimulationSchedule
    {
        private readonly Simulation simulation;
        internal SimulationSchedule(Simulation simulation) { this.simulation = simulation; }
        public EventToken Schedule(ISimulationCommand command, double time, int priority = 0) =>
            simulation.Schedule(command, time, priority);
    }

    /// <summary>Command-boundary staging; all affordability checks precede any resource/value write.</summary>
    public sealed class SimulationTransaction
    {
        internal readonly Dictionary<int, BigDouble> Costs = new Dictionary<int, BigDouble>();
        internal readonly Dictionary<int, BigDouble> Grants = new Dictionary<int, BigDouble>();
        internal readonly StateTransaction Values = new StateTransaction();
        private readonly ContentDatabase content;
        private bool sealedTransaction;
        internal SimulationTransaction(ContentDatabase content) { this.content = content; }
        public void Spend(string resourceId, BigDouble amount) => Add(Costs, resourceId, amount);
        public void Grant(string resourceId, BigDouble amount) => Add(Grants, resourceId, amount);
        public void Set<T>(DenseStateStore<T> store, int index, T value) where T : struct
        {
            EnsureOpen(); Values.Set(store, index, value);
        }
        private void Add(Dictionary<int, BigDouble> amounts, string id, BigDouble amount)
        {
            EnsureOpen(); Number.NonNegative(amount, nameof(amount));
            int index = content.ResourceIndex(id);
            amounts.TryGetValue(index, out BigDouble old);
            BigDouble total = old + amount;
            Number.NonNegative(total, "aggregate transaction amount");
            amounts[index] = total;
        }
        private void EnsureOpen()
        {
            if (sealedTransaction) throw new InvalidOperationException("Transaction is closed.");
        }
        internal void Seal() { sealedTransaction = true; }
    }

    /// <summary>Small explicit composition root. Registration order is system execution order.</summary>
    public sealed class ModuleBuilder
    {
        private readonly List<ResourceDefinition> resources = new List<ResourceDefinition>();
        private readonly List<ProducerDefinition> producers = new List<ProducerDefinition>();
        private readonly List<UpgradeDefinition> upgrades = new List<UpgradeDefinition>();
        private readonly List<RuleDefinition> rules = new List<RuleDefinition>();
        private readonly List<Action<Simulation>> registrations = new List<Action<Simulation>>();
        private PrestigeDefinition prestige;
        public ModuleBuilder Add(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            module.Register(this); return this;
        }
        public void Resource(ResourceDefinition definition) => resources.Add(definition);
        public void Producer(ProducerDefinition definition) => producers.Add(definition);
        public void Upgrade(UpgradeDefinition definition) => upgrades.Add(definition);
        public void Rule(RuleDefinition definition) => rules.Add(definition);
        public void Prestige(PrestigeDefinition definition)
        {
            if (prestige != null) throw new InvalidOperationException("Prestige is already registered.");
            prestige = definition ?? throw new ArgumentNullException(nameof(definition));
        }
        public void Command<T>(ICommandHandler<T> handler) where T : ISimulationCommand
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            registrations.Add(simulation => simulation.RegisterHandler(handler));
        }
        public void System(ISimulationSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            registrations.Add(simulation => simulation.RegisterSystem(system));
        }
        public void Snapshot(ISnapshotSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            registrations.Add(simulation => simulation.RegisterSnapshotSection(section));
        }
        public ContentDatabase Compile() => new ContentDatabase(resources.ToArray(), producers.ToArray(),
            upgrades.ToArray(), rules.ToArray(), prestige);
        public Simulation Create(ulong seed = 1, int eventBudget = 10000)
        {
            var simulation = new Simulation(Compile(), seed, eventBudget);
            Attach(simulation); return simulation;
        }
        public void Attach(Simulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            foreach (var registration in registrations) registration(simulation);
        }
    }

    public sealed partial class Simulation
    {
        private interface IRegisteredHandler
        {
            CommandResult Build(ISimulationCommand command, SimulationReadModel state, SimulationTransaction transaction);
        }
        private sealed class RegisteredHandler<T> : IRegisteredHandler where T : ISimulationCommand
        {
            private readonly ICommandHandler<T> handler;
            public RegisteredHandler(ICommandHandler<T> handler) { this.handler = handler; }
            public CommandResult Build(ISimulationCommand command, SimulationReadModel state, SimulationTransaction transaction) =>
                handler.Build((T)command, state, transaction);
        }
        private readonly Dictionary<Type, IRegisteredHandler> commandHandlers = new Dictionary<Type, IRegisteredHandler>();
        private readonly List<ISimulationSystem> systems = new List<ISimulationSystem>();
        private readonly List<ISnapshotSection> snapshotSections = new List<ISnapshotSection>();
        public IReadOnlyList<ISnapshotSection> SnapshotSections => snapshotSections.AsReadOnly();

        public void RegisterHandler<T>(ICommandHandler<T> handler) where T : ISimulationCommand
        {
            AssertWriter();
            if (handler == null || commandHandlers.ContainsKey(typeof(T)))
                throw new ArgumentException("Null or duplicate command handler.");
            commandHandlers.Add(typeof(T), new RegisteredHandler<T>(handler));
        }
        public void RegisterSystem(ISimulationSystem system)
        {
            AssertWriter();
            if (system == null || systems.Contains(system)) throw new ArgumentException("Null or duplicate system.");
            systems.Add(system);
        }
        public void RegisterSnapshotSection(ISnapshotSection section)
        {
            AssertWriter();
            if (section == null || string.IsNullOrWhiteSpace(section.Id) || section.Version < 1)
                throw new ArgumentException("Invalid snapshot section.");
            foreach (var existing in snapshotSections)
                if (existing.Id == section.Id) throw new ArgumentException("Duplicate snapshot section: " + section.Id);
            snapshotSections.Add(section);
        }
        private bool TryExecuteRegistered(ISimulationCommand command, out CommandResult result)
        {
            if (command == null || !commandHandlers.TryGetValue(command.GetType(), out IRegisteredHandler handler))
            { result = default; return false; }
            var transaction = new SimulationTransaction(Content);
            result = handler.Build(command, new SimulationReadModel(this), transaction);
            transaction.Seal();
            if (!result.Success) return true;
            foreach (var cost in transaction.Costs)
                if (balances[cost.Key] < cost.Value)
                { result = CommandResult.Fail("Insufficient resources for module transaction."); return true; }
            foreach (var grant in transaction.Grants)
            {
                transaction.Costs.TryGetValue(grant.Key, out BigDouble spent);
                if (!Number.IsFinite(balances[grant.Key] - spent + grant.Value))
                { result = CommandResult.Fail("Module resource grant overflows."); return true; }
            }
            if (!transaction.Values.Validate())
            { result = CommandResult.Fail("Module typed-state transaction is stale."); return true; }
            foreach (var cost in transaction.Costs) balances[cost.Key] -= cost.Value;
            // GrantResourceCommand owns caps, lifetime tracking, and the resource-change dependencies.
            foreach (var grant in transaction.Grants)
                ExecuteInternal(new GrantResourceCommand(Content.Resources[grant.Key].Id, grant.Value));
            transaction.Values.Commit();
            foreach (var cost in transaction.Costs) RefreshRules(cost.Key);
            return true;
        }
        private void PrepareSystems(double target)
        {
            if (systems.Count == 0) return;
            var state = new SimulationReadModel(this);
            var schedule = new SimulationSchedule(this);
            for (int i = 0; i < systems.Count; i++) systems[i].BeforeAdvance(state, target, schedule);
        }
    }
}
