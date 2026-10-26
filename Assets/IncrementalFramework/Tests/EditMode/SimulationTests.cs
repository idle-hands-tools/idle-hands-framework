using System;
using System.Threading;
using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class SimulationTests
    {
        private static ResourceAmount[] Amount(string id, double value) =>
            new[] { new ResourceAmount(id, value) };

        private static ContentDatabase WorkerContent(BigDouble initial = default, BigDouble? cap = null) =>
            new ContentDatabase(new[] { new ResourceDefinition("gold", initial, cap) },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)) });

        private static void Succeeds(Simulation simulation, ISimulationCommand command) =>
            Assert.That(simulation.Execute(command).Success, Is.True);

        private static void Balance(Simulation simulation, string id, double expected) =>
            Assert.That(simulation.GetResource(id).Balance.ToDouble(), Is.EqualTo(expected).Within(1e-8));

        [Test]
        public void InvalidNegativeAndNonfiniteCommandsCannotCreateMoney()
        {
            var simulation = new Simulation(WorkerContent(10));
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", -5)).Success, Is.False);
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", BigDouble.NaN)).Success, Is.False);
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", BigDouble.PositiveInfinity)).Success, Is.False);
            Assert.That(simulation.Execute(new BuyProducerCommand("worker", -1)).Success, Is.False);
            Assert.That(simulation.Execute(new BuyProducerCommand("worker", 0)).Success, Is.False);
            Balance(simulation, "gold", 10);
            Assert.That(simulation.GetProducer("worker").Owned, Is.Zero);
        }

        [Test]
        public void MultiResourcePurchaseFailureIsAtomicAndDuplicateCostsAreAggregated()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 10), new ResourceDefinition("wood", 1) },
                new[] { new ProducerDefinition("worker", new[] {
                    new ResourceAmount("gold", 6), new ResourceAmount("wood", 2) }, Amount("gold", 1)),
                    new ProducerDefinition("duplicate", new[] {
                    new ResourceAmount("gold", 6), new ResourceAmount("gold", 6) }, null) });
            var simulation = new Simulation(content);
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.False);
            Assert.That(simulation.Execute(new BuyProducerCommand("duplicate")).Success, Is.False);
            Balance(simulation, "gold", 10);
            Balance(simulation, "wood", 1);
            Assert.That(simulation.GetProducer("worker").Owned, Is.Zero);
            Assert.That(simulation.GetResource("gold").Rate, Is.EqualTo(BigDouble.Zero));
        }

        [Test]
        public void UnaffordableUpgradeCannotPartiallySpendOrApplyModifiers()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 10), new ResourceDefinition("wood", 1) },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)) },
                new[] { new UpgradeDefinition("upgrade", new[] {
                    new ResourceAmount("gold", 5), new ResourceAmount("wood", 2) },
                    new[] { new ModifierDefinition("m", "worker", 100) }) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            Assert.That(simulation.Execute(new BuyUpgradeCommand("upgrade")).Success, Is.False);
            Balance(simulation, "gold", 10);
            Balance(simulation, "wood", 1);
            Assert.That(simulation.GetUpgrade("upgrade").Purchased, Is.False);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(1));
        }

        [Test]
        public void AggregateRateExponentOverflowRejectsPurchaseAtomically()
        {
            var huge = BigDouble.FromMantissaExponentNoNormalize(6, long.MaxValue);
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 10), new ResourceDefinition("energy") },
                new[] { new ProducerDefinition("first", Amount("gold", 1), new[] { new ResourceAmount("energy", huge) }),
                    new ProducerDefinition("second", Amount("gold", 1), new[] { new ResourceAmount("energy", huge) }) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("first"));
            var before = simulation.ExportState();
            Assert.That(simulation.Execute(new BuyProducerCommand("second")).Success, Is.False);
            Balance(simulation, "gold", 9);
            Assert.That(simulation.GetProducer("second").Owned, Is.Zero);
            Assert.That(simulation.GetResource("energy").Rate.Mantissa, Is.EqualTo(huge.Mantissa));
            Assert.That(simulation.GetResource("energy").Rate.Exponent, Is.EqualTo(huge.Exponent));
            Assert.That(simulation.PendingEventCount, Is.EqualTo(before.Events.Count));
        }

        [Test]
        public void WriterThreadAffinityRejectsConcurrentMutation()
        {
            var simulation = new Simulation(WorkerContent());
            Exception actual = null;
            var thread = new Thread(() =>
            {
                try { simulation.Execute(new GrantResourceCommand("gold", 10)); }
                catch (Exception error) { actual = error; }
            });
            thread.Start();
            Assert.That(thread.Join(5000), Is.True);
            Assert.That(actual, Is.TypeOf<InvalidOperationException>());
            Balance(simulation, "gold", 0);
        }

        [TestCase(1, 3, 30)]
        [TestCase(2, 3, 70)]
        [TestCase(1.5, 4, 81.25)]
        public void BulkCostsFollowGeometricSeries(double growth, int count, double expected)
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 1000) },
                new[] { new ProducerDefinition("worker", Amount("gold", 10), null, growth) });
            var simulation = new Simulation(content);
            Assert.That(simulation.GetProducerCost("worker", count)[0].Amount.ToDouble(),
                Is.EqualTo(expected).Within(1e-8));
            Succeeds(simulation, new BuyProducerCommand("worker", count));
            Balance(simulation, "gold", 1000 - expected);
            Assert.That(simulation.GetProducerCost("worker")[0].Amount.ToDouble(),
                Is.EqualTo(10 * Math.Pow(growth, count)).Within(1e-8));
        }

        [Test]
        public void AdditiveModifiersPrecedeMultipliersRegardlessOfPurchaseOrder()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 2)) },
                new[] {
                    new UpgradeDefinition("multiply", null, new[] { new ModifierDefinition("m", "worker", 3) }),
                    new UpgradeDefinition("add", null, new[] { new ModifierDefinition("a", "worker", 2, ModifierKind.Add) }) });
            var first = new Simulation(content);
            var second = new Simulation(content);
            foreach (var simulation in new[] { first, second }) Succeeds(simulation, new BuyProducerCommand("worker", 2));
            Succeeds(first, new BuyUpgradeCommand("multiply"));
            Succeeds(first, new BuyUpgradeCommand("add"));
            Succeeds(second, new BuyUpgradeCommand("add"));
            Succeeds(second, new BuyUpgradeCommand("multiply"));
            Assert.That(first.GetResource("gold").Rate.ToDouble(), Is.EqualTo(36).Within(1e-8));
            Assert.That(second.GetResource("gold").Rate, Is.EqualTo(first.GetResource("gold").Rate));
            Assert.That(first.Execute(new BuyUpgradeCommand("add")).Success, Is.False);
        }

        [Test]
        public void UpgradeAndToggleOnlyAffectTheirResourceDependencies()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("wood") },
                new[] { new ProducerDefinition("miner", null, Amount("gold", 2)),
                    new ProducerDefinition("logger", null, Amount("wood", 7)) },
                new[] { new UpgradeDefinition("pick", null, new[] { new ModifierDefinition("m", "miner", 3) }) },
                new[] { new RuleDefinition("gold-rule", "gold", 1000, null),
                    new RuleDefinition("wood-rule", "wood", 1000, null) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("miner"));
            Succeeds(simulation, new BuyProducerCommand("logger"));
            var before = simulation.ExportState();
            var woodEvent = before.Events.Find(x => x.TargetId == "wood-rule");
            var goldEvent = before.Events.Find(x => x.TargetId == "gold-rule");
            long goldRebuilds = simulation.RateRebuildCount("gold");
            long woodRebuilds = simulation.RateRebuildCount("wood");
            Succeeds(simulation, new BuyUpgradeCommand("pick"));
            Assert.That(simulation.RateRebuildCount("gold"), Is.EqualTo(goldRebuilds + 1));
            Assert.That(simulation.RateRebuildCount("wood"), Is.EqualTo(woodRebuilds));
            var after = simulation.ExportState();
            Assert.That(after.Events.Find(x => x.TargetId == "wood-rule").Sequence, Is.EqualTo(woodEvent.Sequence),
                "Unrelated rate dependencies must not be invalidated or rescheduled.");
            Assert.That(after.Events.Find(x => x.TargetId == "wood-rule").TokenVersion, Is.EqualTo(woodEvent.TokenVersion));
            Assert.That(after.Events.Find(x => x.TargetId == "wood-rule").Time, Is.EqualTo(woodEvent.Time));
            Assert.That(after.Events.Find(x => x.TargetId == "gold-rule").TokenVersion, Is.GreaterThan(goldEvent.TokenVersion));
            Assert.That(after.Events.Find(x => x.TargetId == "gold-rule").Time, Is.LessThan(goldEvent.Time));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(6));
            Assert.That(simulation.GetResource("wood").Rate.ToDouble(), Is.EqualTo(7));
            Succeeds(simulation, new ToggleProducerCommand("miner", false));
            simulation.AdvanceTo(10);
            Balance(simulation, "gold", 0);
            Balance(simulation, "wood", 70);
            Succeeds(simulation, new ToggleProducerCommand("miner", true));
            simulation.AdvanceTo(20);
            Balance(simulation, "gold", 60);
            Balance(simulation, "wood", 140);
        }

        [Test]
        public void ScheduledProductionChangeAt100SplitsAdvanceTo200ExactlyOnce()
        {
            var simulation = new Simulation(WorkerContent());
            Succeeds(simulation, new BuyProducerCommand("worker"));
            simulation.Schedule(new BuyProducerCommand("worker"), 100);
            var result = simulation.AdvanceTo(200);
            Assert.That(result.Completed, Is.True);
            Assert.That(result.ProcessedEvents, Is.EqualTo(1));
            Balance(simulation, "gold", 300);
            simulation.AdvanceTo(200);
            Balance(simulation, "gold", 300);
            simulation.AdvanceTo(250);
            Balance(simulation, "gold", 400);
            Assert.That(simulation.PendingEventCount, Is.Zero);
        }

        [Test]
        public void ThresholdCrossingsUnlockAndGrantAtExactTimeWithoutDoubleCounting()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)),
                    new ProducerDefinition("locked", null, Amount("gems", 1), initiallyUnlocked: false) },
                rules: new[] { new RuleDefinition("threshold", "gold", 100,
                    new RuleAction[] { new GrantResourceAction("gems", 5), new UnlockProducerAction("locked") }) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            simulation.Schedule(new BuyProducerCommand("locked"), 100, 200);
            var result = simulation.AdvanceTo(200);
            Assert.That(result.Completed, Is.True);
            Balance(simulation, "gold", 200);
            Balance(simulation, "gems", 105);
            simulation.AdvanceTo(200);
            Balance(simulation, "gems", 105);
        }

        [Test]
        public void CapStopsEffectiveProductionAndSpendingResumesIt()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", cap: 100) },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 10)),
                    new ProducerDefinition("sink", Amount("gold", 40), null) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            simulation.AdvanceTo(1000);
            Balance(simulation, "gold", 100);
            Assert.That(simulation.GetResource("gold").Rate, Is.EqualTo(BigDouble.Zero));
            Succeeds(simulation, new BuyProducerCommand("sink"));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(10));
            simulation.AdvanceTo(1002);
            Balance(simulation, "gold", 80);
            Succeeds(simulation, new GrantResourceCommand("gold", 1000));
            Balance(simulation, "gold", 100);
        }

        [Test]
        public void BudgetedAdvanceResumesWithoutSkippingEventsOrDoubleCounting()
        {
            var simulation = new Simulation(WorkerContent(), eventBudget: 2);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            for (int i = 1; i <= 5; i++) simulation.Schedule(new GrantResourceCommand("gold", 10), i);
            var first = simulation.AdvanceTo(10);
            Assert.That(first.Completed, Is.False);
            Assert.That(first.ProcessedEvents, Is.EqualTo(2));
            Assert.That(simulation.Time, Is.EqualTo(2));
            Balance(simulation, "gold", 22);
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", 1)).Success, Is.False);
            Assert.That(simulation.AdvanceTo(10).Completed, Is.False);
            Assert.That(simulation.AdvanceTo(10).Completed, Is.True);
            Balance(simulation, "gold", 60);
            Assert.That(simulation.PendingTarget, Is.Null);
        }

        [Test]
        public void CancelledTombstonePruningConsumesWorkBudgetWithoutExecutingOrIntegratingTwice()
        {
            var simulation = new Simulation(WorkerContent(), eventBudget: 3);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            for (int i = 1; i <= 10; i++)
            {
                var token = simulation.Schedule(new GrantResourceCommand("gold", 999), i);
                Assert.That(simulation.Cancel(token), Is.True);
            }
            simulation.Schedule(new GrantResourceCommand("gold", 1), 20);
            var result = simulation.AdvanceTo(30);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.ProcessedEvents, Is.Zero);
            Assert.That(result.ProcessedWork, Is.EqualTo(3));
            Assert.That(simulation.Time, Is.Zero);
            Balance(simulation, "gold", 0);
            for (int i = 0; i < 5 && !result.Completed; i++)
            {
                result = simulation.AdvanceTo(30);
                Assert.That(result.ProcessedWork, Is.LessThanOrEqualTo(3));
            }
            Assert.That(result.Completed, Is.True);
            Balance(simulation, "gold", 31);
            Assert.That(simulation.PendingEventCount, Is.Zero);
        }

        [Test]
        public void SameTimeZeroCooldownCascadeIsBoundedByEventBudget()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") }, rules:
                new[] { new RuleDefinition("repeat", "gold", 0,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, repeat: true) });
            var simulation = new Simulation(content, eventBudget: 7);
            var result = simulation.AdvanceTo(100);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.ProcessedEvents, Is.EqualTo(7));
            Assert.That(simulation.Time, Is.Zero);
            Balance(simulation, "gold", 7);
            Assert.That(simulation.PendingEventCount, Is.EqualTo(1));
        }

        [Test]
        public void CancelledCommandsNeverExecute()
        {
            var simulation = new Simulation(WorkerContent());
            var token = simulation.Schedule(new GrantResourceCommand("gold", 100), 10);
            Assert.That(simulation.Cancel(token), Is.True);
            Assert.That(simulation.Cancel(token), Is.False);
            Assert.That(simulation.AdvanceTo(20).ProcessedEvents, Is.Zero);
            Balance(simulation, "gold", 0);
        }

        [Test]
        public void SimulationCommandRescheduleInvalidatesOldTokenAndExecutesReplacementOnlyOnce()
        {
            var simulation = new Simulation(WorkerContent());
            var original = simulation.Schedule(new GrantResourceCommand("gold", 7), 10);
            var replacement = simulation.Reschedule(original, 20, -1);
            Assert.That(replacement.Value, Is.EqualTo(original.Value));
            Assert.That(replacement.Version, Is.EqualTo(original.Version + 1));
            Assert.That(simulation.Cancel(original), Is.False);
            Assert.Throws<ArgumentException>(() => simulation.Reschedule(original, 15));
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.Zero);
            Balance(simulation, "gold", 0);
            Assert.That(simulation.AdvanceTo(20).ProcessedEvents, Is.EqualTo(1));
            Balance(simulation, "gold", 7);
            Assert.That(simulation.AdvanceTo(30).ProcessedEvents, Is.Zero);
            Balance(simulation, "gold", 7);
            var cancelled = simulation.Schedule(new GrantResourceCommand("gold", 99), 40);
            var current = simulation.Reschedule(cancelled, 50);
            Assert.That(simulation.Cancel(cancelled), Is.False);
            Assert.That(simulation.Cancel(current), Is.True);
            simulation.AdvanceTo(60);
            Balance(simulation, "gold", 7);
        }

        [Test]
        public void ScheduledCommandsRespectPriorityAndStableInsertionOrder()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", Amount("gold", 10), Amount("gold", 1), 1) });
            var simulation = new Simulation(content);
            simulation.Schedule(new BuyProducerCommand("worker"), 5, 1);
            simulation.Schedule(new GrantResourceCommand("gold", 10), 5, 0);
            simulation.Schedule(new GrantResourceCommand("gold", 10), 5, 2);
            simulation.Schedule(new BuyProducerCommand("worker"), 5, 2);
            Assert.That(simulation.AdvanceTo(10).ProcessedEvents, Is.EqualTo(4));
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(2));
            Balance(simulation, "gold", 10);
        }

        [Test]
        public void MonotonicClockRejectsPastAndNonfiniteAdvancesAndEvents()
        {
            var simulation = new Simulation(WorkerContent());
            simulation.AdvanceTo(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.AdvanceTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.AdvanceTo(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.AdvanceTo(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                simulation.Schedule(new GrantResourceCommand("gold", 1), 4));
            Assert.That(simulation.Time, Is.EqualTo(5));
            Balance(simulation, "gold", 0);
        }

        [Test]
        public void UnreachableThresholdAboveCapNeverQueuesFalseEvent()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", cap: 10), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)) },
                rules: new[] { new RuleDefinition("impossible", "gold", 11,
                    new RuleAction[] { new GrantResourceAction("gems", 100) }) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            Assert.That(simulation.AdvanceTo(86400).ProcessedEvents, Is.Zero);
            Balance(simulation, "gold", 10);
            Balance(simulation, "gems", 0);
        }

        [Test]
        public void RateChangesCancelStaleThresholdAndRescheduleFromCurrentBalance()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)) },
                rules: new[] { new RuleDefinition("threshold", "gold", 100,
                    new RuleAction[] { new GrantResourceAction("gems", 5) }) });
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            simulation.Schedule(new ToggleProducerCommand("worker", false), 50);
            Assert.That(simulation.AdvanceTo(200).ProcessedEvents, Is.EqualTo(1));
            Balance(simulation, "gold", 50);
            Balance(simulation, "gems", 0);
            Assert.That(simulation.PendingEventCount, Is.Zero);
            Succeeds(simulation, new ToggleProducerCommand("worker", true));
            Assert.That(simulation.AdvanceTo(250).ProcessedEvents, Is.EqualTo(1));
            Balance(simulation, "gold", 100);
            Balance(simulation, "gems", 5);
        }

        [Test]
        public void PrestigeCanPreserveProducersAndUpgradesWithoutCompoundingTheirBonus()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 2)) },
                new[] { new UpgradeDefinition("upgrade", null, new[] { new ModifierDefinition("m", "worker", 3) }) },
                prestige: new PrestigeDefinition("gold", 100, 2, resetProducers: false, resetUpgrades: false));
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker", 2));
            Succeeds(simulation, new BuyUpgradeCommand("upgrade"));
            Succeeds(simulation, new GrantResourceCommand("gold", 100));
            Succeeds(simulation, new PrestigeCommand());
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(2));
            Assert.That(simulation.GetUpgrade("upgrade").Purchased, Is.True);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(24));
            Succeeds(simulation, new ToggleProducerCommand("worker", false));
            Succeeds(simulation, new ToggleProducerCommand("worker", true));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(24));
        }

        [Test]
        public void TwentyFourHoursOfflineUsesAnalyticAdvanceWithoutPerSecondEvents()
        {
            var simulation = new Simulation(WorkerContent(), eventBudget: 1);
            Succeeds(simulation, new BuyProducerCommand("worker", 123));
            var result = simulation.AdvanceTo(24 * 60 * 60);
            Assert.That(result.Completed, Is.True);
            Assert.That(result.ProcessedEvents, Is.Zero);
            Balance(simulation, "gold", 123 * 86400);
        }

        [Test]
        public void SeededRandomAndCommandReplayAreDeterministic()
        {
            var first = new Simulation(WorkerContent(), 123456);
            var second = new Simulation(WorkerContent(), 123456);
            var different = new Simulation(WorkerContent(), 654321);
            Assert.That(first.NextRandomUInt64(), Is.Not.EqualTo(different.NextRandomUInt64()));
            second.NextRandomUInt64();
            for (int i = 0; i < 100; i++)
            {
                var draw = first.NextRandomUInt64();
                Assert.That(second.NextRandomUInt64(), Is.EqualTo(draw));
                int count = (int)(draw % 5) + 1;
                foreach (var simulation in new[] { first, second })
                {
                    Succeeds(simulation, new BuyProducerCommand("worker", count));
                    simulation.AdvanceTo((i + 1) * 10);
                }
            }
            Assert.That(first.GetResource("gold").Balance, Is.EqualTo(second.GetResource("gold").Balance));
            Assert.That(first.GetProducer("worker").Owned, Is.EqualTo(second.GetProducer("worker").Owned));
        }

        [Test]
        public void PrestigeResetsTransientStatePreservesPermanentStateAndAppliesBonusOnce()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems", 7, resetOnPrestige: false) },
                new[] { new ProducerDefinition("worker", null, Amount("gold", 1)) },
                new[] {
                    new UpgradeDefinition("permanent", null, new[] { new ModifierDefinition("permanentM", "worker", 3) }, permanent: true),
                    new UpgradeDefinition("temporary", null, new[] { new ModifierDefinition("temporaryM", "worker", 5) }) },
                prestige: new PrestigeDefinition("gold", 100, 2));
            var simulation = new Simulation(content);
            Succeeds(simulation, new BuyProducerCommand("worker"));
            Succeeds(simulation, new BuyUpgradeCommand("permanent"));
            Succeeds(simulation, new BuyUpgradeCommand("temporary"));
            Succeeds(simulation, new GrantResourceCommand("gold", 100));
            Succeeds(simulation, new PrestigeCommand());
            Balance(simulation, "gold", 0);
            Balance(simulation, "gems", 7);
            Assert.That(simulation.GetProducer("worker").Owned, Is.Zero);
            Assert.That(simulation.GetUpgrade("permanent").Purchased, Is.True);
            Assert.That(simulation.GetUpgrade("temporary").Purchased, Is.False);
            Assert.That(simulation.PermanentMultiplier.ToDouble(), Is.EqualTo(2));
            Succeeds(simulation, new BuyProducerCommand("worker"));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(6));
            simulation.AdvanceTo(10);
            Balance(simulation, "gold", 60);
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.False);
            Succeeds(simulation, new GrantResourceCommand("gold", 40));
            Succeeds(simulation, new PrestigeCommand());
            Succeeds(simulation, new BuyProducerCommand("worker"));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(12));
            Assert.That(simulation.PrestigeCount, Is.EqualTo(2));
        }
    }
}
