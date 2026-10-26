using System;
using System.Collections.Generic;
using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class CoreTests
    {
        [Test]
        public void BigNumbersRemainFiniteBeyondDoubleRange()
        {
            var huge = new BigDouble(1.23456789012345, 1000);
            var product = huge * 100;
            Assert.That(BigDouble.IsInfinity(product), Is.False);
            Assert.That(product.Exponent, Is.EqualTo(1002));
            Assert.That(product.Mantissa, Is.EqualTo(huge.Mantissa).Within(1e-14));
            Assert.That(product / huge, Is.EqualTo(new BigDouble(100)));
        }

        [Test]
        public void SchedulerOrdersByTimeThenPriorityThenInsertionAndCancelsEagerly()
        {
            var scheduler = new EventScheduler<string>();
            scheduler.Schedule(20, -10, "last");
            scheduler.Schedule(10, 2, "third");
            scheduler.Schedule(10, 1, "first");
            scheduler.Schedule(10, 1, "second");
            var cancelled = scheduler.Schedule(0, 0, "cancelled");
            Assert.That(scheduler.Cancel(cancelled), Is.True);
            Assert.That(scheduler.Cancel(cancelled), Is.False);
            Assert.That(scheduler.Count, Is.EqualTo(4));
            var actual = new List<string>();
            while (scheduler.TryDequeue(out var entry)) actual.Add(entry.Payload);
            Assert.That(actual, Is.EqualTo(new[] { "first", "second", "third", "last" }));
            Assert.That(scheduler.TryPeek(out _), Is.False);
        }

        [Test]
        public void SchedulerRestoresStableSequenceAndRejectsInvalidStateAtomically()
        {
            var scheduler = new EventScheduler<string>();
            scheduler.Schedule(5, 0, "one");
            scheduler.Schedule(5, 0, "two");
            var restored = new EventScheduler<string>();
            restored.Restore(scheduler.Export(), scheduler.NextSequence);
            restored.Schedule(5, 0, "three");
            Assert.That(restored.Export()[2].Payload, Is.EqualTo("three"));
            Assert.Throws<ArgumentException>(() => restored.Restore(
                new[] { new ScheduledEvent<string>(0, 0, 1, "a"), new ScheduledEvent<string>(0, 0, 1, "b") }, 2));
            Assert.That(restored.Count, Is.EqualTo(3));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void SchedulerRejectsInvalidTime(double time)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EventScheduler<int>().Schedule(time, 0, 1));
        }

        [Test]
        public void ContentCopiesInputCollectionsAndExposesReadOnlyViews()
        {
            var costs = new[] { new ResourceAmount("gold", 1) };
            var producer = new ProducerDefinition("worker", costs, new[] { new ResourceAmount("gold", 2) });
            var resources = new[] { new ResourceDefinition("gold") };
            var content = new ContentDatabase(resources, new[] { producer });
            costs[0] = new ResourceAmount("missing", 999);
            resources[0] = new ResourceDefinition("replacement");
            Assert.That(content.Resources[0].Id, Is.EqualTo("gold"));
            Assert.That(producer.Costs[0].ResourceId, Is.EqualTo("gold"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ResourceDefinition>)content.Resources)[0] = new ResourceDefinition("changed"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ResourceAmount>)producer.Costs)[0] = new ResourceAmount("gold", 3));
        }

        [Test]
        public void ContentRejectsDuplicateIdsAndDanglingReferences()
        {
            var gold = new ResourceDefinition("gold");
            Assert.Throws<ArgumentException>(() => new ContentDatabase(new[] { gold, gold }));
            Assert.Throws<ArgumentException>(() => new ContentDatabase(new[] { gold },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("missing", 1) }) }));
            Assert.Throws<ArgumentException>(() => new ContentDatabase(new[] { gold }, upgrades:
                new[] { new UpgradeDefinition("upgrade", null, new[] { new ModifierDefinition("mod", "missing", 2) }) }));
            Assert.Throws<ArgumentException>(() => new ContentDatabase(new[] { gold }, rules:
                new[] { new RuleDefinition("rule", "gold", 1, new RuleAction[] { new GrantResourceAction("missing", 1) }) }));
            Assert.Throws<ArgumentException>(() => new ContentDatabase(new[] { gold }, prestige:
                new PrestigeDefinition("missing", 1, 2)));
        }

        [Test]
        public void DefinitionsRejectInvalidAmountsGrowthAndCaps()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceAmount("gold", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceAmount("gold", BigDouble.NaN));
            Assert.Throws<ArgumentException>(() => new ResourceDefinition("gold", 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProducerDefinition("worker", null, null, 0.9));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProducerDefinition("worker", null, null, double.NaN));
        }
    }
}
