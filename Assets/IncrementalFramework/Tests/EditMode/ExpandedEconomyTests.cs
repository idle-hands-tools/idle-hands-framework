using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using BreakInfinity;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class ExpandedEconomyTests
    {
        [Test]
        public void ScopedProducerResourceGlobalAndClickModifiersStayIndependent()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("wood") },
                new[] { new ProducerDefinition("worker", null,
                    new[] { new ResourceAmount("gold", 2), new ResourceAmount("wood", 1) }) },
                new[] {
                    new UpgradeDefinition("producer", null, new[] { new ModifierDefinition("p", "worker", 2) }),
                    new UpgradeDefinition("resource", null, new[] { new ModifierDefinition("r", ModifierTarget.ResourceProduction, "gold", 3) }),
                    new UpgradeDefinition("global", null, new[] { new ModifierDefinition("g", ModifierTarget.GlobalProduction, null, 5) }),
                    new UpgradeDefinition("click", null, new[] { new ModifierDefinition("c", ModifierTarget.Click, "gold", 7) }) });
            var simulation = new Simulation(content);
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.True);
            foreach (string id in new[] { "click", "global", "resource", "producer" })
                Assert.That(simulation.Execute(new BuyUpgradeCommand(id)).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(60));
            Assert.That(simulation.GetResource("wood").Rate.ToDouble(), Is.EqualTo(10));
            Assert.That(simulation.Execute(new ClickCommand("gold", 2)).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(14));
            Assert.That(simulation.Execute(new ClickCommand("wood", 2)).Success, Is.True);
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.EqualTo(2));
            simulation.AdvanceTo(10);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(614));
            Assert.That(simulation.GetResource("wood").Balance.ToDouble(), Is.EqualTo(102));
        }

        [Test]
        public void LifetimeTracksOnlyRetainedEarningsNeverSpendingOrCappedPhantomProduction()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 50, 100) },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 10) }),
                    new ProducerDefinition("sink", new[] { new ResourceAmount("gold", 50) }, null, 1) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Execute(new BuyProducerCommand("sink"));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(50));
            simulation.AdvanceTo(10);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(100));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(150));
            simulation.AdvanceTo(20);
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(150));
            simulation.Execute(new BuyProducerCommand("sink"));
            simulation.Execute(new ClickCommand("gold", 5));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(55));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(155));
        }

        [Test]
        public void LifetimePrerequisiteSurvivesSpendingAndCompositeRuleWaitsForAllConditions()
        {
            var lifetime = new RequirementDefinition(RequirementKind.LifetimeResource, "gold", 10);
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 5) },
                        new[] { new ResourceAmount("gold", 1) }, 1),
                    new ProducerDefinition("advanced", null, null, prerequisites: new[] { lifetime }) },
                new[] { new UpgradeDefinition("upgrade", null, null) },
                new[] { new RuleDefinition("composite", new[] { lifetime,
                        new RequirementDefinition(RequirementKind.Producer, "worker", 2),
                        new RequirementDefinition(RequirementKind.Upgrade, "upgrade", 1) },
                    new RuleAction[] { new GrantResourceAction("gems", 3) }) });
            var simulation = new Simulation(content);
            Assert.That(simulation.Execute(new BuyProducerCommand("advanced")).Success, Is.False);
            simulation.Execute(new GrantResourceCommand("gold", 10));
            Assert.That(simulation.Execute(new BuyProducerCommand("worker", 2)).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.Execute(new BuyProducerCommand("advanced")).Success, Is.True);
            simulation.AdvanceTo(0);
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.Execute(new BuyUpgradeCommand("upgrade")).Success, Is.True);
            simulation.AdvanceTo(0);
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(3));
            simulation.AdvanceTo(0);
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(3));
        }

        [Test]
        public void CompositeResourceThresholdSchedulesAtLatestAnalyticCrossing()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("wood"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null,
                    new[] { new ResourceAmount("gold", 1), new ResourceAmount("wood", 2) }) },
                rules: new[] { new RuleDefinition("both", new[] {
                    new RequirementDefinition(RequirementKind.Resource, "gold", 10),
                    new RequirementDefinition(RequirementKind.Resource, "wood", 30) },
                    new RuleAction[] { new GrantResourceAction("gems", 1) }) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.Zero);
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.AdvanceTo(15).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.One));
            simulation.AdvanceTo(100);
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.One));
        }

        [Test]
        public void RepeatingRuleCooldownUsesExactSimulationTimesAndNeverRefiresAtSameBoundary()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                rules: new[] { new RuleDefinition("periodic", "gold", 0,
                    new RuleAction[] { new GrantResourceAction("gems", 1) }, repeat: true, cooldown: 60) });
            var simulation = new Simulation(content);
            Assert.That(simulation.AdvanceTo(180).ProcessedEvents, Is.EqualTo(4));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(4));
            Assert.That(simulation.AdvanceTo(180).ProcessedEvents, Is.Zero);
            Assert.That(simulation.AdvanceTo(240).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
            Assert.That(simulation.PendingEventCount, Is.EqualTo(1));
        }

        [TestCase(0.1, 0.3, 3)]
        [TestCase(3, 1, 1.0 / 3)]
        public void FractionalAnalyticThresholdDoesNotSpinOrMissItsBoundary(double rate, double threshold, double crossing)
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", rate) }) },
                rules: new[] { new RuleDefinition("fractional", "gold", threshold,
                    new RuleAction[] { new GrantResourceAction("gems", 1) }) });
            var simulation = new Simulation(content, eventBudget: 8);
            simulation.Execute(new BuyProducerCommand("worker"));
            var result = simulation.AdvanceTo(crossing);
            Assert.That(result.Completed, Is.True);
            Assert.That(result.ProcessedEvents, Is.LessThanOrEqualTo(2));
            Assert.That(simulation.GetResource("gems").Balance, Is.EqualTo(BigDouble.One));
            Assert.That(simulation.Time, Is.EqualTo(crossing));
        }

        [Test]
        public void ScheduledUpgradeAtFiveIntegratesOldAndNewRatesExactlyOnce()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 2) }) },
                new[] { new UpgradeDefinition("upgrade", null, new[] { new ModifierDefinition("m", "worker", 3) }) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Schedule(new BuyUpgradeCommand("upgrade"), 5);
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(40));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(6));
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.Zero);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(40));
        }

        [TestCase(1.000000000001)]
        [TestCase(1.0000000000000002)]
        public void GeometricBulkCostIsStableWhenGrowthIsAlmostOne(double growth)
        {
            const int count = 10000;
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 1000000) },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 10) }, null, growth) });
            var simulation = new Simulation(content);
            double expected = 10 * (count + count * (count - 1.0) * (growth - 1) / 2);
            double actual = simulation.GetProducerCost("worker", count)[0].Amount.ToDouble();
            Assert.That(actual, Is.EqualTo(expected).Within(1e-7));
            Assert.That(simulation.Execute(new BuyProducerCommand("worker", count)).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(1000000 - expected).Within(1e-7));
        }

        [Test]
        public void SelfRefreshingRepeaterCannotStarveAnotherSameTimeRule()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                rules: new[] {
                    new RuleDefinition("repeat", "gold", 0,
                        new RuleAction[] { new GrantResourceAction("gold", 1) }, repeat: true),
                    new RuleDefinition("once", "gold", 0,
                        new RuleAction[] { new GrantResourceAction("gems", 1) }) });
            var simulation = new Simulation(content, eventBudget: 2);
            var onceSequence = simulation.ExportState().Events.Find(x => x.TargetId == "once").Sequence;
            var result = simulation.AdvanceTo(0);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.ProcessedEvents, Is.EqualTo(2));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(1));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(1));
            var events = simulation.ExportState().Events;
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].TargetId, Is.EqualTo("repeat"));
            Assert.That(events[0].Sequence, Is.GreaterThan(onceSequence));
            simulation.AdvanceTo(0);
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(1));
        }

        [TestCase(RequirementKind.Resource)]
        [TestCase(RequirementKind.LifetimeResource)]
        [TestCase(RequirementKind.Producer)]
        [TestCase(RequirementKind.Upgrade)]
        [TestCase(RequirementKind.Prestige)]
        public void UpgradePrerequisitesEvaluateEverySupportedRequirementKind(RequirementKind kind)
        {
            string target = kind == RequirementKind.Producer ? "worker"
                : kind == RequirementKind.Upgrade ? "basic" : kind == RequirementKind.Prestige ? null : "gold";
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, null) },
                new[] { new UpgradeDefinition("basic", null, null),
                    new UpgradeDefinition("gated", null, null, prerequisites:
                        new[] { new RequirementDefinition(kind, target, 1) }) },
                prestige: new PrestigeDefinition("gold", 10, 2));
            var simulation = new Simulation(content);
            Assert.That(simulation.Execute(new BuyUpgradeCommand("gated")).Success, Is.False);
            Assert.That(simulation.GetUpgrade("gated").Purchased, Is.False);
            if (kind == RequirementKind.Producer) simulation.Execute(new BuyProducerCommand("worker"));
            else if (kind == RequirementKind.Upgrade) simulation.Execute(new BuyUpgradeCommand("basic"));
            else if (kind == RequirementKind.Prestige)
            {
                simulation.Execute(new GrantResourceCommand("gold", 10));
                Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.True);
            }
            else simulation.Execute(new GrantResourceCommand("gold", 1));
            Assert.That(simulation.Execute(new BuyUpgradeCommand("gated")).Success, Is.True);
            Assert.That(simulation.GetUpgrade("gated").Purchased, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BooleanPurchasePrerequisitesAreAndedWithLegacyRequirementArrays(bool producer)
        {
            var legacy = new[] { new RequirementDefinition(RequirementKind.Resource, "gold", 1) };
            var condition = ConditionDefinition.All(ConditionDefinition.Flag("ready"),
                ConditionDefinition.Not(ConditionDefinition.Flag("blocked")));
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("gated", null, null, prerequisites: legacy, prerequisiteCondition: condition) },
                new[] { new UpgradeDefinition("gated", null, null, prerequisites: legacy, prerequisiteCondition: condition) },
                flags: new[] { new FlagDefinition("ready"), new FlagDefinition("blocked") });
            var simulation = new Simulation(content);
            ISimulationCommand command = producer ? (ISimulationCommand)new BuyProducerCommand("gated") : new BuyUpgradeCommand("gated");
            Assert.That(simulation.Execute(command).Success, Is.False);
            simulation.Execute(new SetFlagCommand("ready", true));
            Assert.That(simulation.Execute(command).Success, Is.False);
            simulation.Execute(new GrantResourceCommand("gold", 1));
            simulation.Execute(new SetFlagCommand("blocked", true));
            Assert.That(simulation.Execute(command).Success, Is.False);
            simulation.Execute(new SetFlagCommand("blocked", false));
            Assert.That(simulation.Execute(command).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.One));
        }
    }
}
