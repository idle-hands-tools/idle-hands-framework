using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using BreakInfinity;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class DeclarativeRuleTests
    {
        [Test]
        public void AndOrNotAndFlagsReactOnlyWhenCompleteConditionBecomesTrue()
        {
            var condition = ConditionDefinition.All(ConditionDefinition.Flag("ready"),
                ConditionDefinition.Any(
                    ConditionDefinition.Require(new RequirementDefinition(RequirementKind.Resource, "gold", 5)),
                    ConditionDefinition.Require(new RequirementDefinition(RequirementKind.Resource, "wood", 5))),
                ConditionDefinition.Not(ConditionDefinition.Flag("blocked")));
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("wood"), new ResourceDefinition("gems") },
                rules: new[] { new RuleDefinition("logic", condition, new RuleAction[] { new GrantResourceAction("gems", 2) }) },
                flags: new[] { new FlagDefinition("ready"), new FlagDefinition("blocked") });
            var simulation = new Simulation(content);
            simulation.Execute(new GrantResourceCommand("gold", 5));
            simulation.Execute(new SetFlagCommand("ready", true));
            simulation.Execute(new SetFlagCommand("blocked", true));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            simulation.Execute(new SetFlagCommand("blocked", false));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
        }

        private static ContentDatabase SpendingContent(int wood) => new ContentDatabase(
            new[] { new ResourceDefinition("gold", 10), new ResourceDefinition("wood", wood), new ResourceDefinition("gems") },
            new[] { new ProducerDefinition("locked", null, null, initiallyUnlocked: false) },
            rules: new[] { new RuleDefinition("trade", ConditionDefinition.All(), new RuleAction[] {
                new GrantResourceAction("gems", 9), new SpendResourceAction("gold", 6),
                new SpendResourceAction("wood", 2), new SetFlagAction("done", true),
                new UnlockProducerAction("locked"), new ScheduleGrantResourceAction("gems", 3, 5) }) },
            flags: new[] { new FlagDefinition("done") });

        [Test]
        public void UnaffordableRuleRollsBackSpendsGrantsFlagsUnlocksAndSchedulesTogether()
        {
            var simulation = new Simulation(SpendingContent(1), eventBudget: 1);
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gems").LifetimeEarned, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetFlag("done"), Is.False);
            Assert.That(simulation.GetProducer("locked").Unlocked, Is.False);
            Assert.That(simulation.ExportState().Events.Exists(x => x.Kind == SimulationEventKind.GrantResource), Is.False);
            Assert.That(simulation.ExportState().Rules[0].Fired, Is.False);
            Assert.That(simulation.Execute(new GrantResourceCommand("wood", 1)).Success, Is.True);
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(9));
            Assert.That(simulation.GetFlag("done"), Is.True);
        }

        [Test]
        public void AffordableRuleCommitsCompleteTransactionAndDelayedActionExactlyOnce()
        {
            var simulation = new Simulation(SpendingContent(2));
            Assert.That(simulation.AdvanceTo(0).Completed, Is.True);
            Assert.That(simulation.LastEventResult.Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(4));
            Assert.That(simulation.GetResource("wood").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.GetFlag("done"), Is.True);
            Assert.That(simulation.GetProducer("locked").Unlocked, Is.True);
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(9));
            Assert.That(simulation.PendingEventCount, Is.EqualTo(1));
            Assert.That(simulation.AdvanceTo(5).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(12));
            Assert.That(simulation.GetResource("gems").LifetimeEarned.ToDouble(), Is.EqualTo(12));
            Assert.That(simulation.AdvanceTo(5).ProcessedEvents, Is.Zero);
        }

        [Test]
        public void FlagTriggeredDynamicModifierAt100SplitsProductionAndSurvivesQueuedSave()
        {
            var modifier = new ModifierDefinition("boost", "worker", 2);
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }) },
                rules: new[] { new RuleDefinition("boost-rule", ConditionDefinition.Flag("boost"),
                    new RuleAction[] { new SetModifierAction(modifier) },
                    trigger: new RuleTriggerDefinition(RuleTriggerKind.FlagChanged, "boost")) },
                flags: new[] { new FlagDefinition("boost") });
            var original = new Simulation(content);
            original.Execute(new BuyProducerCommand("worker"));
            original.Schedule(new SetFlagCommand("boost", true), 100);
            original.AdvanceTo(50);
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var restored = SnapshotService.Restore(content,
                SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(original, clock))));
            original.AdvanceTo(200);
            restored.AdvanceTo(200);
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(300));
            Assert.That(restored.GetFlag("boost"), Is.True);
            Assert.That(restored.IsModifierEnabled("boost"), Is.True);
            Assert.That(SnapshotService.Serialize(SnapshotService.Capture(restored, clock)),
                Is.EqualTo(SnapshotService.Serialize(SnapshotService.Capture(original, clock))));
            restored.Execute(new SetModifierCommand("boost", false));
            Assert.That(restored.GetResource("gold").Rate.ToDouble(), Is.EqualTo(1));
        }

        [Test]
        public void TimeAndManualTriggersDoNotFirePrematurelyAndManualRuleFiresOnlyOnce()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") }, rules: new[] {
                new RuleDefinition("time", ConditionDefinition.All(), new RuleAction[] { new GrantResourceAction("gold", 2) },
                    trigger: new RuleTriggerDefinition(RuleTriggerKind.Time, time: 10)),
                new RuleDefinition("manual", ConditionDefinition.All(), new RuleAction[] { new GrantResourceAction("gold", 3) },
                    trigger: new RuleTriggerDefinition(RuleTriggerKind.Manual)) });
            var simulation = new Simulation(content);
            Assert.That(simulation.AdvanceTo(9).ProcessedEvents, Is.Zero);
            Assert.That(simulation.Execute(new TriggerRuleCommand("manual")).Success, Is.True);
            Assert.That(simulation.AdvanceTo(9).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(3));
            simulation.Execute(new TriggerRuleCommand("manual"));
            Assert.That(simulation.AdvanceTo(9).ProcessedEvents, Is.Zero);
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(5));
        }

        [TestCase(RuleTriggerKind.ProducerPurchased)]
        [TestCase(RuleTriggerKind.UpgradePurchased)]
        [TestCase(RuleTriggerKind.Prestige)]
        [TestCase(RuleTriggerKind.ResourceThreshold)]
        public void ExplicitEventTriggersArmOnlyTheirMatchingRule(RuleTriggerKind kind)
        {
            string target = kind == RuleTriggerKind.ProducerPurchased ? "worker"
                : kind == RuleTriggerKind.UpgradePurchased ? "upgrade"
                : kind == RuleTriggerKind.ResourceThreshold ? "gold" : null;
            var condition = kind == RuleTriggerKind.ResourceThreshold
                ? ConditionDefinition.Require(new RequirementDefinition(RequirementKind.Resource, "gold", 1))
                : ConditionDefinition.All();
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, null) },
                new[] { new UpgradeDefinition("upgrade", null, null) },
                new[] { new RuleDefinition("triggered", condition, new RuleAction[] { new GrantResourceAction("gems", 1) },
                    resetOnPrestige: false, trigger: new RuleTriggerDefinition(kind, target)) },
                new PrestigeDefinition("gold", 10, 2));
            var simulation = new Simulation(content);
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            if (kind == RuleTriggerKind.ProducerPurchased) simulation.Execute(new BuyProducerCommand("worker"));
            else if (kind == RuleTriggerKind.UpgradePurchased) simulation.Execute(new BuyUpgradeCommand("upgrade"));
            else if (kind == RuleTriggerKind.Prestige)
            {
                simulation.Execute(new GrantResourceCommand("gold", 10));
                Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.True);
            }
            else simulation.Execute(new GrantResourceCommand("gold", 1));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.One));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
        }

        [Test]
        public void DuplicateRuleSpendActionsAreAggregatedBeforeAnyGrant()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 6), new ResourceDefinition("gems") },
                rules: new[] { new RuleDefinition("trade", ConditionDefinition.All(), new RuleAction[] {
                    new GrantResourceAction("gems", 1), new SpendResourceAction("gold", 4), new SpendResourceAction("gold", 4) }) });
            var simulation = new Simulation(content);
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.Zero);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(6));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            simulation.Execute(new GrantResourceCommand("gold", 2));
            Assert.That(simulation.AdvanceTo(0).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.One));
        }
    }
}
