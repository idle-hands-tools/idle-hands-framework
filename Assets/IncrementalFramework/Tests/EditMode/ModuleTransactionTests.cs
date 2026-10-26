using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BreakInfinity;
using IncrementalFramework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class ModuleTransactionTests
    {
        private sealed class TradeCommand : ISimulationCommand
        {
            public int Gold { get; set; } = 3;
            public int Wood { get; set; } = 2;
            public BigDouble GrantAmount { get; set; } = 3;
            public bool Reject { get; set; }
        }

        private sealed class TradeModule : IGameModule, ICommandHandler<TradeCommand>
        {
            public DenseStateStore<int> Store { get; } = new DenseStateStore<int>();
            public SimulationTransaction LastTransaction { get; private set; }
            private readonly int trades;
            private readonly BigDouble? gemsCap;
            public TradeModule(bool capGems = true)
            {
                trades = Store.Register("trades", 0);
                gemsCap = capGems ? new BigDouble(5) : (BigDouble?)null;
            }
            public void Register(ModuleBuilder builder)
            {
                builder.Resource(new ResourceDefinition("gold", 10));
                builder.Resource(new ResourceDefinition("wood", 2));
                builder.Resource(new ResourceDefinition("gems", cap: gemsCap));
                builder.Command(this);
            }
            public CommandResult Build(TradeCommand command, SimulationReadModel state, SimulationTransaction transaction)
            {
                LastTransaction = transaction;
                transaction.Spend("gold", command.Gold);
                transaction.Spend("wood", command.Wood);
                transaction.Grant("gems", command.GrantAmount);
                transaction.Set(Store, trades, Store[trades] + 1);
                return command.Reject ? CommandResult.Fail("Rejected by module.") : CommandResult.Ok();
            }
        }

        private sealed class SchedulingSystem : ISimulationSystem
        {
            private readonly string id;
            private readonly List<string> order;
            private bool scheduled;
            public int Calls { get; private set; }
            public SchedulingSystem(string id, List<string> order) { this.id = id; this.order = order; }
            public void BeforeAdvance(SimulationReadModel state, double target, SimulationSchedule schedule)
            {
                Calls++;
                order.Add(id);
                if (!scheduled)
                {
                    scheduled = true;
                    schedule.Schedule(new GrantResourceCommand("gold", 1), 10);
                }
            }
        }

        private sealed class CounterSection : ISnapshotSection
        {
            private readonly DenseStateStore<int> store;
            public string Id { get; }
            public int Version => 1;
            public CounterSection(string id, DenseStateStore<int> store) { Id = id; this.store = store; }
            public string Capture() => store[0].ToString(CultureInfo.InvariantCulture);
            public void Validate(int version, string data)
            {
                if (version != Version || !int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
                    throw new ArgumentException("Invalid counter snapshot.");
            }
            public void Restore(int version, string data)
            {
                Validate(version, data);
                var transaction = new StateTransaction();
                transaction.Set(store, 0, int.Parse(data, CultureInfo.InvariantCulture));
                if (!transaction.Commit()) throw new InvalidOperationException("Counter restore failed.");
            }
        }

        private sealed class ResetParticipant : IPrestigeResetParticipant
        {
            public ResetPolicy Policy { get; }
            public DenseStateStore<int> Store { get; } = new DenseStateStore<int>();
            public int Calls { get; private set; }
            public bool Reject { get; set; }
            public ResetParticipant(ResetPolicy policy) { Policy = policy; Store.Register("value", 8); }
            public CommandResult Prepare(SimulationReadModel state, StateTransaction transaction)
            {
                Calls++;
                int value = Policy == ResetPolicy.Transform ? Store[0] / 2
                    : Policy == ResetPolicy.Recalculate ? (int)state.Resource(0).Balance.ToDouble() : 0;
                transaction.Set(Store, 0, value);
                return Reject ? CommandResult.Fail("Reset rejected.") : CommandResult.Ok();
            }
        }

        [Test]
        public void ModuleTradeCommitsAllResourceAndTypedStateChangesTogether()
        {
            var module = new TradeModule();
            var simulation = new ModuleBuilder().Add(module).Create();
            Assert.That(simulation.Execute(new TradeCommand()).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.Zero);
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(3));
            Assert.That(module.Store[0], Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => module.LastTransaction.Grant("gold", 100));
            Assert.That(simulation.Execute(new TradeCommand()).Success, Is.False);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(3));
            Assert.That(module.Store[0], Is.EqualTo(1));
        }

        [TestCase(true, 3)]
        [TestCase(false, -1)]
        [TestCase(false, 11)]
        public void FailedModuleTradeNeverAppliesAnyStagedWrite(bool reject, int gold)
        {
            var module = new TradeModule();
            var simulation = new ModuleBuilder().Add(module).Create();
            Assert.That(simulation.Execute(new TradeCommand { Reject = reject, Gold = gold }).Success, Is.False);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.Zero);
            Assert.That(module.Store[0], Is.Zero);
        }

        [Test]
        public void LifetimeOverflowInModuleGrantCannotPartiallySpendOrCommitTypedState()
        {
            var huge = BigDouble.FromMantissaExponentNoNormalize(6, long.MaxValue);
            var module = new TradeModule(capGems: false);
            var builder = new ModuleBuilder().Add(module);
            var content = builder.Compile();
            var state = new Simulation(content).ExportState();
            state.Resources.Find(x => x.Id == "gems").LifetimeEarned = huge;
            var simulation = Simulation.FromState(content, state);
            builder.Attach(simulation);
            Assert.That(simulation.Execute(new TradeCommand { GrantAmount = huge }).Success, Is.False);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gems").LifetimeEarned, Is.EqualTo(huge));
            Assert.That(module.Store[0], Is.Zero);
        }

        [Test]
        public void TypedTransactionsReplaceDuplicateWritesAndRejectStaleVersionsAtomically()
        {
            var store = new DenseStateStore<int>();
            int first = store.Register("first", 1);
            int second = store.Register("second", 2);
            Assert.That(store.IndexOf("second"), Is.EqualTo(second));
            Assert.That(store.StableId(first), Is.EqualTo("first"));
            var transaction = new StateTransaction();
            transaction.Set(store, first, 10);
            transaction.Set(store, first, 11);
            transaction.Set(store, second, 20);
            Assert.That(store[first], Is.EqualTo(1));
            Assert.That(transaction.Commit(), Is.True);
            Assert.That(store[first], Is.EqualTo(11));
            Assert.That(store[second], Is.EqualTo(20));
            Assert.That(transaction.Commit(), Is.False);
            var stale = new StateTransaction();
            stale.Set(store, first, 99);
            stale.Set(store, second, 99);
            store.Register("third", 3);
            Assert.That(stale.Commit(), Is.False);
            Assert.That(store[first], Is.EqualTo(11));
            Assert.That(store[second], Is.EqualTo(20));
            Assert.Throws<ArgumentException>(() => store.Register("first", 0));
        }

        [Test]
        public void RegisteredSystemsRunInOrderAndScheduleOnlyExplicitEvents()
        {
            var order = new List<string>();
            var first = new SchedulingSystem("first", order);
            var second = new SchedulingSystem("second", order);
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold"));
            builder.System(first);
            builder.System(second);
            var simulation = builder.Create();
            var result = simulation.AdvanceTo(20);
            Assert.That(result.Completed, Is.True);
            Assert.That(result.ProcessedEvents, Is.EqualTo(2));
            Assert.That(order, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(2));
            simulation.AdvanceTo(30);
            Assert.That(first.Calls, Is.EqualTo(2));
            Assert.That(second.Calls, Is.EqualTo(2));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(2));
        }

        [Test]
        public void ModuleSnapshotSectionRestoresTypedStateAndRegisteredHandler()
        {
            var module = new TradeModule();
            var builder = new ModuleBuilder().Add(module);
            builder.Snapshot(new CounterSection("trades", module.Store));
            var simulation = builder.Create();
            simulation.Execute(new TradeCommand());
            var snapshot = SnapshotService.Deserialize(SnapshotService.Serialize(
                SnapshotService.Capture(simulation, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
            var restoredModule = new TradeModule();
            var restoredBuilder = new ModuleBuilder().Add(restoredModule);
            restoredBuilder.Snapshot(new CounterSection("trades", restoredModule.Store));
            var restored = SnapshotService.Restore(simulation.Content, snapshot, modules: restoredBuilder);
            Assert.That(restoredModule.Store[0], Is.EqualTo(1));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
            restored.Execute(new GrantResourceCommand("wood", 2));
            Assert.That(restored.Execute(new TradeCommand()).Success, Is.True);
            Assert.That(restoredModule.Store[0], Is.EqualTo(2));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
            Assert.Throws<InvalidOperationException>(() => SnapshotService.Restore(simulation.Content, snapshot));
        }

        [Test]
        public void InvalidModuleSectionPreventsAllEarlierSectionWrites()
        {
            var first = new DenseStateStore<int>();
            var second = new DenseStateStore<int>();
            first.Register("counter", 1);
            second.Register("counter", 2);
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold"));
            builder.Snapshot(new CounterSection("first", first));
            builder.Snapshot(new CounterSection("second", second));
            var simulation = builder.Create();
            var snapshot = SnapshotService.Capture(simulation, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            snapshot.Sections[0].Data = "7";
            snapshot.Sections[1].Data = "invalid";
            var restoredFirst = new DenseStateStore<int>();
            var restoredSecond = new DenseStateStore<int>();
            restoredFirst.Register("counter", 0);
            restoredSecond.Register("counter", 0);
            var restoredBuilder = new ModuleBuilder();
            restoredBuilder.Snapshot(new CounterSection("first", restoredFirst));
            restoredBuilder.Snapshot(new CounterSection("second", restoredSecond));
            Assert.Throws<ArgumentException>(() => SnapshotService.Restore(simulation.Content, snapshot, modules: restoredBuilder));
            Assert.That(restoredFirst[0], Is.Zero);
            Assert.That(restoredSecond[0], Is.Zero);
        }

        [TestCase("absent", typeof(InvalidOperationException))]
        [TestCase("unknown", typeof(InvalidOperationException))]
        [TestCase("duplicate", typeof(JsonSerializationException))]
        [TestCase("version", typeof(ArgumentException))]
        public void InvalidModuleSectionManifestFailsClosed(string corruption, Type exceptionType)
        {
            var module = new TradeModule();
            var builder = new ModuleBuilder().Add(module);
            builder.Snapshot(new CounterSection("trades", module.Store));
            var simulation = builder.Create();
            simulation.Execute(new TradeCommand());
            var snapshot = SnapshotService.Capture(simulation, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            if (corruption == "absent") snapshot.Sections.Clear();
            else if (corruption == "unknown") snapshot.Sections[0].Id = "unknown";
            else if (corruption == "duplicate") snapshot.Sections.Add(snapshot.Sections[0]);
            else snapshot.Sections[0].Version = 2;
            var restoredModule = new TradeModule();
            var restoredBuilder = new ModuleBuilder().Add(restoredModule);
            restoredBuilder.Snapshot(new CounterSection("trades", restoredModule.Store));
            Assert.Throws(exceptionType, () => SnapshotService.Restore(simulation.Content, snapshot, modules: restoredBuilder));
            Assert.That(restoredModule.Store[0], Is.Zero);
        }

        [Test]
        public void ModuleResetParticipantsStageAllPoliciesBeforeCoreAndTypedStateCommit()
        {
            var reset = new ResetParticipant(ResetPolicy.Reset);
            var preserve = new ResetParticipant(ResetPolicy.Preserve);
            var transform = new ResetParticipant(ResetPolicy.Transform);
            var recalculate = new ResetParticipant(ResetPolicy.Recalculate);
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold"));
            builder.Prestige(new PrestigeDefinition("gold", 10, 2));
            foreach (var participant in new[] { reset, preserve, transform, recalculate }) builder.Reset(participant);
            builder.State("transformed", transform.Store);
            var simulation = builder.Create();
            simulation.Execute(new GrantResourceCommand("gold", 10));
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.True);
            Assert.That(reset.Store[0], Is.Zero);
            Assert.That(preserve.Store[0], Is.EqualTo(8));
            Assert.That(preserve.Calls, Is.Zero);
            Assert.That(transform.Store[0], Is.EqualTo(4));
            Assert.That(recalculate.Store[0], Is.EqualTo(10));
            Assert.That(simulation.GetReadModel().State<int>("transformed")[0], Is.EqualTo(4));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.PrestigeCount, Is.EqualTo(1));
        }

        [Test]
        public void RejectedModuleResetRollsBackEarlierStagingAndEntireCorePrestige()
        {
            var first = new ResetParticipant(ResetPolicy.Reset);
            var second = new ResetParticipant(ResetPolicy.Transform) { Reject = true };
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold"));
            builder.Prestige(new PrestigeDefinition("gold", 10, 2));
            builder.Reset(first);
            builder.Reset(second);
            var simulation = builder.Create();
            simulation.Execute(new GrantResourceCommand("gold", 10));
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.False);
            Assert.That(first.Store[0], Is.EqualTo(8));
            Assert.That(second.Store[0], Is.EqualTo(8));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.PermanentMultiplier, Is.EqualTo(BigDouble.One));
            Assert.That(simulation.PrestigeCount, Is.Zero);
        }

        [TestCase("data")]
        [TestCase("version")]
        public void LoadRejectsInvalidPrimaryModuleSectionBeforeAttachingAndRecoversBackup(string corruption)
        {
            var directory = "snapshot-test-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory, "save.json");
            try
            {
                var module = new TradeModule();
                var builder = new ModuleBuilder().Add(module);
                builder.Snapshot(new CounterSection("trades", module.Store));
                var simulation = builder.Create();
                var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                simulation.Execute(new TradeCommand());
                SnapshotService.Save(path, simulation, clock);
                simulation.Execute(new GrantResourceCommand("wood", 2));
                simulation.Execute(new TradeCommand());
                SnapshotService.Save(path, simulation, clock);
                Assert.That(module.Store[0], Is.EqualTo(2));
                var root = JObject.Parse(File.ReadAllText(path));
                if (corruption == "data") root["sections"][0]["data"] = "invalid";
                else root["sections"][0]["version"] = 2;
                File.WriteAllText(path, root.ToString());
                var restoredModule = new TradeModule();
                var restoredBuilder = new ModuleBuilder().Add(restoredModule);
                restoredBuilder.Snapshot(new CounterSection("trades", restoredModule.Store));
                var restored = SnapshotService.Load(path, simulation.Content, clock, 0, modules: restoredBuilder);
                Assert.That(restoredModule.Store[0], Is.EqualTo(1));
                Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
                Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(3));
                Assert.Throws<InvalidOperationException>(() => restoredBuilder.Create());
                restored.Execute(new GrantResourceCommand("wood", 2));
                Assert.That(restored.Execute(new TradeCommand()).Success, Is.True);
                Assert.That(restoredModule.Store[0], Is.EqualTo(2));
                Assert.That(Directory.GetFiles(directory, "*.pending-*"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
