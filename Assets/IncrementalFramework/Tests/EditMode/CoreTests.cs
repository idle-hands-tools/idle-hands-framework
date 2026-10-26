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
        public void SchedulerOrdersByTimeThenPriorityThenInsertionAndNeverExecutesCancelledEntries()
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

        [Test]
        public void ReschedulePreservesSequenceIncrementsVersionAndInvalidatesEveryStaleToken()
        {
            var scheduler = new EventScheduler<string>();
            var first = scheduler.Schedule(5, 0, "old");
            scheduler.Schedule(5, 0, "second");
            var current = scheduler.Reschedule(first, 5, 0, "replacement");
            Assert.That(current.Value, Is.EqualTo(first.Value));
            Assert.That(current.Version, Is.EqualTo(first.Version + 1));
            Assert.That(current, Is.Not.EqualTo(first));
            Assert.That(scheduler.TryGet(first, out _), Is.False);
            Assert.That(scheduler.Cancel(first), Is.False);
            Assert.Throws<ArgumentException>(() => scheduler.Reschedule(first, 5, 0, "stale"));
            Assert.That(scheduler.Export()[0].Payload, Is.EqualTo("replacement"));
            Assert.That(scheduler.Export()[0].Token, Is.EqualTo(current));
            Assert.That(scheduler.Cancel(current), Is.True);
            Assert.That(scheduler.TryDequeue(out var entry), Is.True);
            Assert.That(entry.Payload, Is.EqualTo("second"));
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }

        [Test]
        public void SchedulerExportRestoreKeepsOnlyLiveVersionsAndNeverRevivesTombstones()
        {
            var scheduler = new EventScheduler<int>();
            var original = scheduler.Schedule(10, 0, 1);
            var current = scheduler.Reschedule(original, 20, 0, 2);
            var cancelled = scheduler.Schedule(5, 0, 99);
            scheduler.Cancel(cancelled);
            var entries = scheduler.Export();
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Version, Is.EqualTo(2));
            var restored = new EventScheduler<int>();
            restored.Restore(entries, scheduler.NextSequence);
            Assert.That(restored.Count, Is.EqualTo(1));
            Assert.That(restored.TryGet(original, out _), Is.False);
            Assert.That(restored.TryGet(current, out var live), Is.True);
            Assert.That(live.Payload, Is.EqualTo(2));
            Assert.That(restored.Cancel(cancelled), Is.False);
            Assert.That(restored.Cancel(current), Is.True);
            Assert.That(restored.TryDequeue(out _), Is.False);
        }

        [Test]
        public void LazyCancellationPruningHonorsExplicitBudget()
        {
            var scheduler = new EventScheduler<int>();
            var tokens = new List<EventToken>();
            for (int i = 0; i < 10; i++) tokens.Add(scheduler.Schedule(0, 0, i));
            scheduler.Schedule(100, 0, 100);
            foreach (var token in tokens) scheduler.Cancel(token);
            Assert.That(scheduler.Count, Is.EqualTo(1));
            Assert.That(scheduler.HasCancelledHead, Is.True);
            Assert.That(scheduler.PruneCancelled(3), Is.EqualTo(3));
            Assert.That(scheduler.StorageCount, Is.EqualTo(8));
            Assert.That(scheduler.HasCancelledHead, Is.True);
            Assert.That(scheduler.PruneCancelled(0), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.PruneCancelled(-1));
            Assert.That(scheduler.PruneCancelled(20), Is.EqualTo(7));
            Assert.That(scheduler.TryPeek(out var entry), Is.True);
            Assert.That(entry.Payload, Is.EqualTo(100));
        }

        [Test]
        public void RepeatedReschedulingCompactsTombstonesAndKeepsStorageBounded()
        {
            var scheduler = new EventScheduler<int>();
            var token = scheduler.Schedule(1, 0, 0);
            for (int i = 1; i <= 5000; i++) token = scheduler.Reschedule(token, i + 1, 0, i);
            Assert.That(scheduler.Count, Is.EqualTo(1));
            Assert.That(scheduler.StorageCount, Is.LessThanOrEqualTo(128));
            Assert.That(token.Version, Is.EqualTo(5001));
            Assert.That(scheduler.TryDequeue(out var entry), Is.True);
            Assert.That(entry.Payload, Is.EqualTo(5000));
            Assert.That(scheduler.TryDequeue(out _), Is.False);
        }
    }
}
