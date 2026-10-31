using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using BreakInfinity;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class AdvancedProgressionTests
    {
        [Test]
        public void FlatPercentMultiplyAndPriorityOverrideHaveDeterministicOrder()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 2) }) },
                new[] {
                    new UpgradeDefinition("multiply", null, new[] { new ModifierDefinition("m", "worker", 2) }),
                    new UpgradeDefinition("percent", null, new[] { new ModifierDefinition("p", "worker", 0.5, ModifierKind.AddPercent) }),
                    new UpgradeDefinition("flat", null, new[] { new ModifierDefinition("f", "worker", 4, ModifierKind.Flat) }),
                    new UpgradeDefinition("override-a", null, new[] { new ModifierDefinition("a", "worker", 11, ModifierKind.Override, 1) }),
                    new UpgradeDefinition("override-z", null, new[] { new ModifierDefinition("z", "worker", 13, ModifierKind.Override, 1) }),
                    new UpgradeDefinition("override-low", null, new[] { new ModifierDefinition("low", "worker", 99, ModifierKind.Override, 0) }),
                    new UpgradeDefinition("override-high", null, new[] { new ModifierDefinition("high", "worker", 17, ModifierKind.Override, 2) }) });
            var first = new Simulation(content);
            var second = new Simulation(content);
            first.Execute(new BuyProducerCommand("worker", 3));
            second.Execute(new BuyProducerCommand("worker", 3));
            foreach (var id in new[] { "multiply", "flat", "percent" }) first.Execute(new BuyUpgradeCommand(id));
            foreach (var id in new[] { "percent", "flat", "multiply" }) second.Execute(new BuyUpgradeCommand(id));
            Assert.That(first.GetResource("gold").Rate.ToDouble(), Is.EqualTo(30));
            Assert.That(second.GetResource("gold").Rate, Is.EqualTo(first.GetResource("gold").Rate));
            foreach (var simulation in new[] { first, second })
            {
                foreach (var id in new[] { "override-z", "override-low", "override-a" })
                    Assert.That(simulation.Execute(new BuyUpgradeCommand(id)).Success, Is.True);
                Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(13));
                simulation.Execute(new BuyUpgradeCommand("override-high"));
                Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(17));
            }
        }

        [Test]
        public void ResourceAndProducerCategoryModifiersAffectOnlyMatchingDependencies()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", category: "ore"), new ResourceDefinition("wood", category: "forest") },
                new[] { new ProducerDefinition("miner", null, new[] { new ResourceAmount("gold", 1), new ResourceAmount("wood", 1) }, category: "industrial"),
                    new ProducerDefinition("logger", null, new[] { new ResourceAmount("wood", 1) }, category: "natural") },
                modifiers: new[] {
                    new ModifierDefinition("ore", ModifierTarget.ResourceCategoryProduction, "ore", 3),
                    new ModifierDefinition("industrial", ModifierTarget.ProducerCategoryProduction, "industrial", 2) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("miner"));
            simulation.Execute(new BuyProducerCommand("logger"));
            Assert.That(simulation.Execute(new SetModifierCommand("ore", true)).Success, Is.True);
            Assert.That(simulation.Execute(new SetModifierCommand("industrial", true)).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(6));
            Assert.That(simulation.GetResource("wood").Rate.ToDouble(), Is.EqualTo(3));
            simulation.Execute(new SetModifierCommand("ore", false));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.GetResource("wood").Rate.ToDouble(), Is.EqualTo(3));
        }

        [Test]
        public void LifetimePrestigeRewardsRemainClaimedAfterSpendingCurrencyAndSaveRestore()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"),
                    new ResourceDefinition("gems", resetOnPrestige: false) },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }),
                    new ProducerDefinition("sink", new[] { new ResourceAmount("gold", 350) }, null, 1),
                    new ProducerDefinition("gem-sink", new[] { new ResourceAmount("gems", 1) }, null, 1) },
                new[] {
                    new UpgradeDefinition("permanent", null, new[] { new ModifierDefinition("p", "worker", 2) }, permanent: true),
                    new UpgradeDefinition("temporary", null, new[] { new ModifierDefinition("t", "worker", 3) }) },
                prestige: new PrestigeDefinition("gold", "gems", 100));
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Execute(new BuyUpgradeCommand("permanent"));
            simulation.Execute(new BuyUpgradeCommand("temporary"));
            simulation.Execute(new GrantResourceCommand("gold", 400));
            simulation.Execute(new BuyProducerCommand("sink"));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(50));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(400));
            Assert.That(simulation.GetPrestigeReward().ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(400));
            Assert.That(simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.PrestigeCurrencyEarned.ToDouble(), Is.EqualTo(2));
            Assert.That(simulation.PermanentMultiplier.ToDouble(), Is.EqualTo(1.2).Within(1e-12));
            Assert.That(simulation.GetUpgrade("permanent").Purchased, Is.True);
            Assert.That(simulation.GetUpgrade("temporary").Purchased, Is.False);
            simulation.Execute(new BuyProducerCommand("worker"));
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(2.4).Within(1e-12));
            Assert.That(simulation.Execute(new BuyProducerCommand("gem-sink")).Success, Is.True);
            Assert.That(simulation.GetPrestigeReward(), Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.False);
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var snapshot = SnapshotService.Capture(simulation, clock);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(SnapshotService.Serialize(snapshot)));
            Assert.That(restored.GetPrestigeReward(), Is.EqualTo(BigDouble.Zero));
            Assert.That(restored.Execute(new PrestigeCommand()).Success, Is.False);
            restored.Execute(new GrantResourceCommand("gold", 500));
            Assert.That(restored.GetPrestigeReward().ToDouble(), Is.EqualTo(1));
            Assert.That(restored.Execute(new PrestigeCommand()).Success, Is.True);
            Assert.That(restored.PrestigeCurrencyEarned.ToDouble(), Is.EqualTo(3));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(2));
            Assert.That(restored.PermanentMultiplier.ToDouble(), Is.EqualTo(1.3).Within(1e-12));
            restored.Execute(new BuyProducerCommand("worker"));
            Assert.That(restored.GetResource("gold").Rate.ToDouble(), Is.EqualTo(2.6).Within(1e-12));
        }

        [Test]
        public void PrestigeResetPreserveTransformRecalculatePoliciesAndFlagsApplyExactlyOnce()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"),
                    new ResourceDefinition("reset", 7), new ResourceDefinition("preserve", 7, prestigePolicy: ResetPolicy.Preserve),
                    new ResourceDefinition("transform", 100, prestigePolicy: ResetPolicy.Transform, prestigeTransform: 0.25),
                    new ResourceDefinition("recalculate", 3, prestigePolicy: ResetPolicy.Recalculate) },
                new[] {
                    new ProducerDefinition("reset", null, new[] { new ResourceAmount("gold", 1) }, prestigePolicy: ResetPolicy.Reset),
                    new ProducerDefinition("preserve", null, new[] { new ResourceAmount("gold", 1) }, prestigePolicy: ResetPolicy.Preserve),
                    new ProducerDefinition("transform", null, new[] { new ResourceAmount("gold", 1) }, prestigePolicy: ResetPolicy.Transform, prestigeTransform: 0.5),
                    new ProducerDefinition("recalculate", null, new[] { new ResourceAmount("gold", 1) }, prestigePolicy: ResetPolicy.Recalculate) },
                new[] {
                    new UpgradeDefinition("permanent", null, new[] { new ModifierDefinition("permanent-m", ModifierTarget.GlobalProduction, null, 3) }, permanent: true),
                    new UpgradeDefinition("temporary", null, new[] { new ModifierDefinition("temporary-m", ModifierTarget.GlobalProduction, null, 5) }) },
                prestige: new PrestigeDefinition("gold", 100, 2),
                flags: new[] { new FlagDefinition("reset"), new FlagDefinition("preserve", resetOnPrestige: false) },
                modifiers: new[] { new ModifierDefinition("dynamic", ModifierTarget.GlobalProduction, null, 2) });
            var simulation = new Simulation(content);
            foreach (string id in new[] { "reset", "preserve", "transform", "recalculate" })
                simulation.Execute(new BuyProducerCommand(id, 3));
            simulation.Execute(new BuyUpgradeCommand("permanent"));
            simulation.Execute(new BuyUpgradeCommand("temporary"));
            simulation.Execute(new SetModifierCommand("dynamic", true));
            simulation.Execute(new GrantResourceCommand("gold", 100));
            simulation.Execute(new GrantResourceCommand("reset", 13));
            simulation.Execute(new GrantResourceCommand("preserve", 13));
            simulation.Execute(new GrantResourceCommand("transform", 100));
            simulation.Execute(new GrantResourceCommand("recalculate", 17));
            simulation.Execute(new SetFlagCommand("reset", true));
            simulation.Execute(new SetFlagCommand("preserve", true));
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(simulation.GetResource("reset").Balance.ToDouble(), Is.EqualTo(7));
            Assert.That(simulation.GetResource("preserve").Balance.ToDouble(), Is.EqualTo(20));
            Assert.That(simulation.GetResource("transform").Balance.ToDouble(), Is.EqualTo(50));
            Assert.That(simulation.GetResource("transform").LifetimeEarned.ToDouble(), Is.EqualTo(200));
            Assert.That(simulation.GetResource("recalculate").Balance.ToDouble(), Is.EqualTo(3));
            Assert.That(simulation.GetProducer("reset").Owned, Is.Zero);
            Assert.That(simulation.GetProducer("preserve").Owned, Is.EqualTo(3));
            Assert.That(simulation.GetProducer("transform").Owned, Is.EqualTo(1));
            Assert.That(simulation.GetProducer("recalculate").Owned, Is.Zero);
            Assert.That(simulation.GetFlag("reset"), Is.False);
            Assert.That(simulation.GetFlag("preserve"), Is.True);
            Assert.That(simulation.IsModifierEnabled("dynamic"), Is.False);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(24));
        }

        [TestCase(-10, 0, 0)]
        [TestCase(0, -2, 0)]
        [TestCase(-2, -0.25, 6)]
        [TestCase(2, -0.5, 8)]
        public void SignedFlatAndPercentDebuffsClampEachNonnegativePipelineStage(double flat, double percent, double expected)
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 2) }) },
                new[] { new UpgradeDefinition("debuff", null, new[] {
                    new ModifierDefinition("flat", "worker", flat, ModifierKind.Flat),
                    new ModifierDefinition("percent", "worker", percent, ModifierKind.AddPercent),
                    new ModifierDefinition("multiply", "worker", 2) }) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker", 3));
            Assert.That(simulation.Execute(new BuyUpgradeCommand("debuff")).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Rate.ToDouble(), Is.EqualTo(expected).Within(1e-12));
            simulation.AdvanceTo(10);
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(expected * 10).Within(1e-12));
        }

        [TestCase(ModifierKind.Multiply)]
        [TestCase(ModifierKind.Override)]
        public void NegativeMultipliersAndOverridesRemainInvalid(ModifierKind kind)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModifierDefinition("invalid", "worker", -1, kind));
        }
    }
}
