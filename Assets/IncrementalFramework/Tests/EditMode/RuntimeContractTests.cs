using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class RuntimeContractTests
    {
        private static ContentDatabase Economy(BigDouble? cap = null) => new ContentDatabase(
            new[] { new ResourceDefinition("gold", 10, cap) },
            new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 2) },
                new[] { new ResourceAmount("gold", 1) }) },
            new[] { new UpgradeDefinition("boost", new[] { new ResourceAmount("gold", 1) },
                new[] { new ModifierDefinition("double", "worker", 2) }) });
        private static SimulationChange Delta(SimulationChangeSet set, SimulationChangeKind kind, int index = 0) =>
            set.Deltas.Single(x => x.Kind == kind && x.Index == index);
        private static RuleDefinition Rule(string id, RuleTriggerKind kind, RuleAction[] actions, string target = null,
            bool repeat = false, ConditionDefinition condition = null, ScheduledEventTriggerKind? scheduled = null) =>
            new RuleDefinition(id, condition ?? ConditionDefinition.All(), actions, repeat: repeat,
                trigger: new RuleTriggerDefinition(kind, target, scheduledEventKind: scheduled));

        [Test]
        public void JournalContainsIndexedAtomicProducerResourceAndRateChanges()
        {
            var simulation = new Simulation(Economy());
            int calls = 0;
            simulation.Changed += changes =>
            {
                calls++;
                Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(1));
                var resource = Delta(changes, SimulationChangeKind.Resource);
                Assert.That(resource.Before.Amount.ToDouble(), Is.EqualTo(10));
                Assert.That(resource.After.Amount.ToDouble(), Is.EqualTo(8));
                Assert.That(resource.After.Rate.ToDouble(), Is.EqualTo(1));
                Assert.That(Delta(changes, SimulationChangeKind.Producer).After.Count, Is.EqualTo(1));
                Assert.Throws<NotSupportedException>(() => ((IList<SimulationChange>)changes.Deltas).Clear());
            };
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void UpgradeAndCappedEffectiveRateAreJournaled()
        {
            var simulation = new Simulation(Economy(10));
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Execute(new BuyUpgradeCommand("boost"));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Upgrade).After.Enabled, Is.True);
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Resource).After.Rate.ToDouble(), Is.EqualTo(2));
            simulation.AdvanceTo(10);
            var delta = Delta(simulation.LastChanges, SimulationChangeKind.Resource);
            Assert.That(delta.Before.Rate.ToDouble(), Is.EqualTo(2));
            Assert.That(delta.After.Amount.ToDouble(), Is.EqualTo(10));
            Assert.That(delta.After.Rate, Is.EqualTo(BigDouble.Zero));
        }

        [Test]
        public void AdvanceCoalescesAllScheduledTransactionsAndKeepsPreviousJournalImmutable()
        {
            var simulation = new Simulation(Economy());
            simulation.Execute(new BuyProducerCommand("worker"));
            var previous = simulation.LastChanges;
            simulation.Schedule(new GrantResourceCommand("gold", 2), 1);
            simulation.Schedule(new GrantResourceCommand("gold", 3), 2);
            int calls = 0;
            simulation.Changed += _ => calls++;
            simulation.AdvanceTo(3);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(simulation.LastChanges.Deltas.Count, Is.EqualTo(1));
            var delta = Delta(simulation.LastChanges, SimulationChangeKind.Resource);
            Assert.That(delta.Before.Amount.ToDouble(), Is.EqualTo(8));
            Assert.That(delta.After.Amount.ToDouble(), Is.EqualTo(16));
            Assert.That(Delta(previous, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(8));
        }

        [Test]
        public void NoOpOrRejectedCommandsDoNotPublish()
        {
            var simulation = new Simulation(Economy());
            int calls = 0;
            simulation.Changed += _ => calls++;
            simulation.Execute(new GrantResourceCommand("gold", 0));
            simulation.Execute(new ToggleProducerCommand("worker", true));
            simulation.Execute(new GrantResourceCommand("gold", -1));
            simulation.Execute(new BuyProducerCommand("worker", 100));
            simulation.AdvanceTo(10);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ObserversCannotReenterAndTheirExceptionsNeverMisreportCommittedCommands()
        {
            var simulation = new Simulation(Economy());
            int healthy = 0, diagnostic = 0;
            simulation.ObserverException += _ => { diagnostic++; throw new Exception("diagnostic failure"); };
            simulation.Changed += _ =>
            {
                Assert.Throws<InvalidOperationException>(() => simulation.Execute(new GrantResourceCommand("gold", 100)));
                Assert.Throws<InvalidOperationException>(() => simulation.AdvanceTo(10));
                Assert.Throws<InvalidOperationException>(() => simulation.Schedule(new GrantResourceCommand("gold", 1), 0));
                Assert.Throws<InvalidOperationException>(() => simulation.NextRandomUInt64());
                Assert.Throws<InvalidOperationException>(() => simulation.Cancel(default));
                throw new InvalidOperationException("observer failure");
            };
            simulation.Changed += _ => healthy++;
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.True);
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(8));
            Assert.That(healthy, Is.EqualTo(1));
            Assert.That(diagnostic, Is.EqualTo(1));
            Assert.That(simulation.LastObserverException.Message, Is.EqualTo("observer failure"));
        }

        [Test]
        public void RuleEffectsPublishFlagsUnlocksResourcesAndModifiersTogether()
        {
            var modifier = new ModifierDefinition("boost", "locked", 2);
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("locked", null, null, initiallyUnlocked: false) },
                new[] { new UpgradeDefinition("locked-upgrade", null, null, initiallyUnlocked: false) },
                new[] { Rule("effects", RuleTriggerKind.Manual, new RuleAction[] {
                    new GrantResourceAction("gold", 2), new SetFlagAction("ready", true),
                    new UnlockProducerAction("locked"), new UnlockUpgradeAction("locked-upgrade"),
                    new SetModifierAction(modifier) }) },
                flags: new[] { new FlagDefinition("ready") });
            var simulation = new Simulation(content);
            simulation.Execute(new TriggerRuleCommand("effects"));
            simulation.AdvanceTo(0);
            var changes = simulation.LastChanges;
            Assert.That(changes.Deltas.Count, Is.EqualTo(5));
            Assert.That(Delta(changes, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(2));
            Assert.That(Delta(changes, SimulationChangeKind.Flag).After.Enabled, Is.True);
            Assert.That(Delta(changes, SimulationChangeKind.Producer).After.Unlocked, Is.True);
            Assert.That(Delta(changes, SimulationChangeKind.Upgrade).After.Unlocked, Is.True);
            Assert.That(Delta(changes, SimulationChangeKind.Modifier).After.Enabled, Is.True);
        }

        private sealed class ModuleCommand : ISimulationCommand { public bool Reject; }
        private sealed class ModuleHandler : ICommandHandler<ModuleCommand>
        {
            public readonly DenseStateStore<int> Store = new DenseStateStore<int>();
            public ModuleHandler() { Store.Register("counter", 0); }
            public CommandResult Build(ModuleCommand command, SimulationReadModel state, SimulationTransaction transaction)
            {
                transaction.Grant("gold", 2);
                transaction.Set(Store, 0, Store[0] + 1);
                return command.Reject ? CommandResult.Fail("reject") : CommandResult.Ok();
            }
        }

        [Test]
        public void ModuleTransactionsIncludeIndexedTypedStateAndRejectWithoutPublishing()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") }));
            var handler = new ModuleHandler();
            simulation.RegisterState("module", handler.Store);
            simulation.RegisterHandler(handler);
            int calls = 0;
            simulation.Changed += _ => calls++;
            simulation.Execute(new ModuleCommand { Reject = true });
            Assert.That(calls, Is.Zero);
            simulation.Execute(new ModuleCommand());
            var delta = Delta(simulation.LastChanges, SimulationChangeKind.Module);
            Assert.That(delta.ModuleId, Is.EqualTo("module"));
            Assert.That(delta.Before.ModuleValue, Is.EqualTo(0));
            Assert.That(delta.After.ModuleValue, Is.EqualTo(1));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ModuleTransactionUnregisteredStoreStillPublishesIndexedValueChanges()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") }));
            var handler = new ModuleHandler();
            simulation.RegisterHandler(handler);
            simulation.Execute(new ModuleCommand());
            var delta = Delta(simulation.LastChanges, SimulationChangeKind.Module);
            Assert.That(delta.ModuleId, Is.Null);
            Assert.That(delta.ModuleStore, Is.SameAs(handler.Store));
            Assert.That(delta.Index, Is.Zero);
            Assert.That(delta.After.ModuleValue, Is.EqualTo(1));
        }

        [Test]
        public void ResourceChangeTriggersIncludeModuleAndRuleTransactionsWithoutConditionDependencies()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                rules: new[] {
                    Rule("changed", RuleTriggerKind.ResourceChanged,
                        new RuleAction[] { new GrantResourceAction("gems", 1) }, "gold", true),
                    Rule("manual", RuleTriggerKind.Manual, new RuleAction[] { new GrantResourceAction("gold", 2) }) });
            var simulation = new Simulation(content);
            var handler = new ModuleHandler();
            simulation.RegisterState("module", handler.Store);
            simulation.RegisterHandler(handler);
            simulation.Execute(new ModuleCommand());
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            simulation.Execute(new TriggerRuleCommand("manual"));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(2));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(2));
        }

        private sealed class ResetCounter : IPrestigeResetParticipant
        {
            public ResetPolicy Policy => ResetPolicy.Reset;
            public readonly DenseStateStore<int> Store = new DenseStateStore<int>();
            public ResetCounter() { Store.Register("count", 5); }
            public CommandResult Prepare(SimulationReadModel state, StateTransaction transaction)
            { transaction.Set(Store, 0, 0); return CommandResult.Ok(); }
        }

        [Test]
        public void PrestigeIncludesTypedModuleResetInTheSameCommittedJournal()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold", 10) },
                prestige: new PrestigeDefinition("gold", 10, 2)));
            var reset = new ResetCounter();
            simulation.RegisterState("counter", reset.Store);
            simulation.RegisterResetParticipant(reset);
            simulation.Execute(new PrestigeCommand());
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Module).Before.ModuleValue, Is.EqualTo(5));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Module).After.ModuleValue, Is.EqualTo(0));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Prestige).After.Count, Is.EqualTo(1));
        }

        [Test]
        public void PrestigePublishesResetAndPrestigeDeltas()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") },
                prestige: new PrestigeDefinition("gold", 10, 2)));
            simulation.Execute(new GrantResourceCommand("gold", 10));
            simulation.Execute(new PrestigeCommand());
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Prestige).After.Count, Is.EqualTo(1));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Resource).After.Amount, Is.EqualTo(BigDouble.Zero));
        }

        [Test]
        public void ResourceChangedIsIndependentFromConditionAndNeverRunsOnInitRateOnlyOrNoOp()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }) },
                rules: new[] { Rule("changed", RuleTriggerKind.ResourceChanged,
                    new RuleAction[] { new GrantResourceAction("gems", 1) }, "gold", true, ConditionDefinition.Flag("ready")) },
                flags: new[] { new FlagDefinition("ready", true) });
            var simulation = new Simulation(content);
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Execute(new GrantResourceCommand("gold", 0));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            Assert.That(simulation.AdvanceTo(1).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(1));
        }

        [Test]
        public void PurchaseTriggerWithFlagConditionDoesNotFireAtInitialization()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, null) },
                rules: new[] { Rule("bought", RuleTriggerKind.ProducerPurchased,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, "worker", condition: ConditionDefinition.Flag("ready")) },
                flags: new[] { new FlagDefinition("ready", true) }));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            simulation.Execute(new BuyProducerCommand("worker"));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
        }

        [Test]
        public void ResourceChangedSeesTheCompletePurchaseTransaction()
        {
            var condition = ConditionDefinition.Require(new RequirementDefinition(RequirementKind.Producer, "worker", 1));
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold", 10), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 1) }, null) },
                rules: new[] { Rule("paid", RuleTriggerKind.ResourceChanged,
                    new RuleAction[] { new GrantResourceAction("gems", 1) }, "gold", condition: condition) }));
            simulation.Execute(new BuyProducerCommand("worker"));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(1));
        }

        [Test]
        public void ScheduledEventTriggerUsesTypedWhitelistAndSurvivesStateRestore()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                rules: new[] { Rule("scheduled", RuleTriggerKind.ScheduledEvent,
                    new RuleAction[] { new GrantResourceAction("gems", 1) }, repeat: true,
                    scheduled: ScheduledEventTriggerKind.GrantResource) },
                flags: new[] { new FlagDefinition("ready") });
            var simulation = new Simulation(content);
            simulation.Execute(new GrantResourceCommand("gold", 1));
            simulation.Schedule(new SetFlagCommand("ready", true), 1);
            simulation.Schedule(new GrantResourceCommand("gold", 1), 2);
            var restored = Simulation.FromState(content, simulation.ExportState());
            Assert.That(restored.AdvanceTo(1).ProcessedEvents, Is.EqualTo(1));
            Assert.That(restored.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(restored.AdvanceTo(2).ProcessedEvents, Is.EqualTo(2));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(1));
        }

        [Test]
        public void GlobalScheduledTriggerDoesNotTriggerOnRuleEventsOrPublicCommands()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                rules: new[] { Rule("scheduled", RuleTriggerKind.ScheduledEvent,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, repeat: true) });
            var simulation = new Simulation(content);
            simulation.Execute(new GrantResourceCommand("gold", 1));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            simulation.Schedule(new GrantResourceCommand("gold", 1), 1);
            Assert.That(simulation.AdvanceTo(1).ProcessedEvents, Is.EqualTo(2));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(3));
            Assert.That(simulation.PendingEventCount, Is.Zero);
        }

        [Test]
        public void ObserverFailuresDoNotTurnSuccessfulScheduledTransactionsIntoFailedEvents()
        {
            var simulation = new Simulation(Economy());
            simulation.Changed += _ => throw new ArithmeticException("bad view");
            simulation.Schedule(new BuyProducerCommand("worker"), 1);
            Assert.That(simulation.AdvanceTo(1).Completed, Is.True);
            Assert.That(simulation.LastEventResult.Success, Is.True);
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(1));
            Assert.That(simulation.LastObserverException.Message, Is.EqualTo("bad view"));
        }

        [TestCase(NegativeAmountPolicy.Reject)]
        [TestCase(NegativeAmountPolicy.ClampToZero)]
        [TestCase(NegativeAmountPolicy.Allow)]
        public void NegativePolicyControlsStartsGrantsSpendAndLifetime(NegativeAmountPolicy policy)
        {
            if (policy == NegativeAmountPolicy.Reject)
                Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceDefinition("invalid", -1));
            else
                Assert.That(new ResourceDefinition("start", -1, negativeAmountPolicy: policy).Initial.ToDouble(),
                    Is.EqualTo(policy == NegativeAmountPolicy.Allow ? -1 : 0));
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 2, 10, negativeAmountPolicy: policy) },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 5) }, null) });
            var simulation = new Simulation(content);
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", -5)).Success, Is.EqualTo(policy != NegativeAmountPolicy.Reject));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(),
                Is.EqualTo(policy == NegativeAmountPolicy.Reject ? 2 : policy == NegativeAmountPolicy.Allow ? -3 : 0));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.EqualTo(policy == NegativeAmountPolicy.Allow));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(),
                Is.EqualTo(policy == NegativeAmountPolicy.Reject ? 2 : policy == NegativeAmountPolicy.Allow ? -8 : 0));
            simulation.Execute(new GrantResourceCommand("gold", 100));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            var restored = Simulation.FromState(content, simulation.ExportState());
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceAmount("gold", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpendResourceAction("gold", -1));
        }

        [TestCase(NegativeAmountPolicy.Reject)]
        [TestCase(NegativeAmountPolicy.ClampToZero)]
        [TestCase(NegativeAmountPolicy.Allow)]
        public void SnapshotImportEnforcesNegativePolicyAndNonnegativeLifetime(NegativeAmountPolicy policy)
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", negativeAmountPolicy: policy) });
            var snapshot = new Simulation(content).ExportState();
            snapshot.Resources[0].Balance = -4;
            if (policy == NegativeAmountPolicy.Reject)
                Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.FromState(content, snapshot));
            else
                Assert.That(Simulation.FromState(content, snapshot).GetResource("gold").Balance.ToDouble(),
                    Is.EqualTo(policy == NegativeAmountPolicy.Allow ? -4 : 0));
            snapshot.Resources[0].LifetimeEarned = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.FromState(content, snapshot));
        }

        [Test]
        public void AllowDebtCoversRulesDelayedGrantsAndSnapshotEvents()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", negativeAmountPolicy: NegativeAmountPolicy.Allow) },
                rules: new[] { Rule("debt", RuleTriggerKind.Manual, new RuleAction[] {
                    new SpendResourceAction("gold", 3), new GrantResourceAction("gold", -2),
                    new ScheduleGrantResourceAction("gold", -1, 1) }) });
            var simulation = new Simulation(content);
            simulation.Execute(new TriggerRuleCommand("debt"));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(-5));
            var restored = Simulation.FromState(content, simulation.ExportState());
            restored.AdvanceTo(1);
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(-6));
            Assert.That(restored.GetResource("gold").LifetimeEarned, Is.EqualTo(BigDouble.Zero));
        }

        [Test]
        public void AllowNegativeStartProductionAndPrestigeTransformUseCheckedSignedMath()
        {
            var resource = new ResourceDefinition("gold", -4, cap: 10, prestigePolicy: ResetPolicy.Transform,
                prestigeTransform: 2, negativeAmountPolicy: NegativeAmountPolicy.Allow);
            var content = new ContentDatabase(new[] { resource, new ResourceDefinition("prestige", 1) },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }) },
                prestige: new PrestigeDefinition("prestige", 1, 2));
            var simulation = new Simulation(content);
            simulation.Execute(new PrestigeCommand());
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(-8));
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.AdvanceTo(1);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(-6));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(2));
            var restored = Simulation.FromState(content, simulation.ExportState());
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(-6));
        }

        [Test]
        public void BudgetDiagnosticKeepsPendingTimeAndEventsAndPublishesCommittedPrefix()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") });
            var simulation = new Simulation(content, eventBudget: 1);
            simulation.Schedule(new GrantResourceCommand("gold", 2), 1);
            simulation.Schedule(new GrantResourceCommand("gold", 3), 2);
            int calls = 0;
            simulation.Changed += _ => calls++;
            var result = simulation.AdvanceTo(10);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.Diagnostic, Is.EqualTo(AdvanceDiagnostic.EventBudgetExhausted));
            Assert.That(result.Time, Is.EqualTo(1));
            Assert.That(simulation.PendingTarget, Is.EqualTo(10));
            Assert.That(simulation.PendingEventCount, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            var restored = Simulation.FromState(content, simulation.ExportState(), eventBudget: 1);
            Assert.That(restored.AdvanceTo(10).Completed, Is.True);
            Assert.That(restored.Time, Is.EqualTo(10));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(5));
        }

        [Test]
        public void ResourceChangedSameTimeCascadeIsBoundedDiagnosedAndResumable()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                rules: new[] { Rule("loop", RuleTriggerKind.ResourceChanged,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, "gold", true) });
            var simulation = new Simulation(content, eventBudget: 3);
            simulation.Execute(new GrantResourceCommand("gold", 1));
            var result = simulation.AdvanceTo(10);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.Diagnostic, Is.EqualTo(AdvanceDiagnostic.SameTimeCascade));
            Assert.That(result.ProcessedEvents, Is.EqualTo(3));
            Assert.That(simulation.Time, Is.Zero);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(4));
            var restored = Simulation.FromState(content, simulation.ExportState(), eventBudget: 3);
            Assert.That(restored.AdvanceTo(10).Diagnostic, Is.EqualTo(AdvanceDiagnostic.SameTimeCascade));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
        }

        [Test]
        public void IdleAdvanceWithTenThousandProducersHasNoJournalAndLowAllocations()
        {
            var producers = new ProducerDefinition[10000];
            for (int i = 0; i < producers.Length; i++) producers[i] = new ProducerDefinition("p" + i, null, null);
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") }, producers));
            simulation.AdvanceTo(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 2; i < 102; i++) simulation.AdvanceTo(i);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024));
            Assert.That(simulation.LastChanges, Is.Null);
        }

        [Test]
        public void UnobservedProductionJournalReusesStorageAndLazySnapshotsRemainImmutable()
        {
            var simulation = new Simulation(Economy());
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.AdvanceTo(1);
            simulation.AdvanceTo(2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 3; i < 103; i++) simulation.AdvanceTo(i);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            simulation.Execute(new ToggleProducerCommand("worker", true));
            var previous = simulation.LastChanges;
            Assert.That(previous.Time, Is.EqualTo(102));
            Assert.That(Delta(previous, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(110));
            simulation.AdvanceTo(103);
            Assert.That(Delta(previous, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(110));
            Assert.That(Delta(simulation.LastChanges, SimulationChangeKind.Resource).After.Amount.ToDouble(), Is.EqualTo(111));
        }
    }
}
