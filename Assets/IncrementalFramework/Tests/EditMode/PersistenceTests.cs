using System;
using System.IO;
using BreakInfinity;
using IncrementalFramework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class PersistenceTests
    {
        private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
        }

        private sealed class GoldMigration : ISnapshotMigration
        {
            public int FromVersion => 1;
            public int ToVersion => 2;
            public JObject Migrate(JObject snapshot)
            {
                snapshot["state"]["resources"][0]["balance"] =
                    new JObject { ["mantissa"] = 7.0, ["exponent"] = 0 };
                return snapshot;
            }
        }

        private static ContentDatabase Content() => new ContentDatabase(
            new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
            new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }) },
            rules: new[] { new RuleDefinition("milestone", "gold", 100,
                new RuleAction[] { new GrantResourceAction("gems", 5) }) });

        private static string StateJson(Simulation simulation) =>
            SnapshotService.Serialize(SnapshotService.Capture(simulation, Epoch));

        [TestCase(1.2345678901234567, 1000L)]
        [TestCase(9.999999999999998, 9007199254740993L)]
        [TestCase(-3.141592653589793, -1000L)]
        [TestCase(0.0, 0L)]
        public void BigDoubleJsonPreservesMantissaBitsAndExactIntegerExponent(double mantissa, long exponent)
        {
            var expected = BigDouble.FromMantissaExponentNoNormalize(mantissa, exponent);
            var converter = new BigDoubleJsonConverter();
            var json = JsonConvert.SerializeObject(expected, converter);
            var actual = JsonConvert.DeserializeObject<BigDouble>(json, converter);
            Assert.That(BitConverter.DoubleToInt64Bits(actual.Mantissa),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.Mantissa)));
            Assert.That(actual.Exponent, Is.EqualTo(exponent));
            Assert.That(JObject.Parse(json)["exponent"].Type, Is.EqualTo(JTokenType.Integer));
        }

        [TestCase("{\"mantissa\":10,\"exponent\":5}")]
        [TestCase("{\"mantissa\":0.1,\"exponent\":5}")]
        [TestCase("{\"mantissa\":1,\"exponent\":5.5}")]
        [TestCase("{\"mantissa\":1}")]
        [TestCase("\"1e1000\"")]
        public void NumericConverterRejectsMalformedOrUnnormalizedRepresentations(string json)
        {
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<BigDouble>(json, new BigDoubleJsonConverter()));
        }

        [Test]
        public void SnapshotRoundtripPreservesHugeBalanceExactly()
        {
            var simulation = new Simulation(Content());
            var amount = BigDouble.FromMantissaExponentNoNormalize(1.2345678901234567, 1000);
            Assert.That(simulation.Execute(new GrantResourceCommand("gold", amount)).Success, Is.True);
            var json = StateJson(simulation);
            var restored = SnapshotService.Restore(simulation.Content, SnapshotService.Deserialize(json));
            var actual = restored.GetResource("gold").Balance;
            Assert.That(actual.Mantissa, Is.EqualTo(amount.Mantissa));
            Assert.That(actual.Exponent, Is.EqualTo(amount.Exponent));
            Assert.That(StateJson(restored), Is.EqualTo(json));
        }

        [Test]
        public void RestoredQueuedRulesAndCommandsMatchUninterruptedRunWithoutDuplicates()
        {
            var original = new Simulation(Content(), seed: 12345);
            original.Execute(new BuyProducerCommand("worker"));
            original.Schedule(new BuyProducerCommand("worker"), 100);
            var cancelled = original.Schedule(new GrantResourceCommand("gems", 999), 80);
            original.Cancel(cancelled);
            original.AdvanceTo(50);
            original.NextRandomUInt64();
            var saved = StateJson(original);
            var restored = SnapshotService.Restore(original.Content, SnapshotService.Deserialize(saved));
            Assert.That(StateJson(restored), Is.EqualTo(saved));
            Assert.That(restored.PendingEventCount, Is.EqualTo(original.PendingEventCount));
            Assert.That(restored.NextRandomUInt64(), Is.EqualTo(original.NextRandomUInt64()));
            original.AdvanceTo(200);
            restored.AdvanceTo(200);
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(300));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
            Assert.That(restored.PendingEventCount, Is.Zero);
            restored.AdvanceTo(200);
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
        }

        [Test]
        public void ExportedStateAndRestoredStateAreDetachedCopies()
        {
            var simulation = new Simulation(Content());
            simulation.Execute(new BuyProducerCommand("worker"));
            var state = simulation.ExportState();
            var restored = Simulation.FromState(simulation.Content, state);
            state.Resources[0].Balance = 999;
            state.Producers[0].Owned = 999;
            state.Events.Clear();
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(1));
            Assert.That(restored.GetProducer("worker").Owned, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(BigDouble.Zero));
            Assert.That(restored.PendingEventCount, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitStateImportResolvesQueuedStableIdsAfterContentReordering()
        {
            var original = new Simulation(Content());
            original.Execute(new BuyProducerCommand("worker"));
            original.Schedule(new GrantResourceCommand("gems", 7), 10);
            var source = original.Content;
            var reordered = new ContentDatabase(new[] { source.Resources[1], source.Resources[0] },
                new[] { source.Producers[0] }, rules: new[] { source.Rules[0] });
            var restored = Simulation.FromState(reordered, original.ExportState());
            original.AdvanceTo(200);
            restored.AdvanceTo(200);
            Assert.That(restored.GetResource("gold").Balance, Is.EqualTo(original.GetResource("gold").Balance));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(12));
            Assert.That(restored.PendingEventCount, Is.Zero);
        }

        [Test]
        public void PendingBudgetTargetSurvivesSaveAndResumesExactly()
        {
            var content = Content();
            var original = new Simulation(content, eventBudget: 1);
            original.Execute(new BuyProducerCommand("worker"));
            original.Schedule(new GrantResourceCommand("gold", 2), 1);
            original.Schedule(new GrantResourceCommand("gold", 3), 2);
            Assert.That(original.AdvanceTo(10).Completed, Is.False);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(StateJson(original)), eventBudget: 1);
            Assert.That(restored.PendingTarget, Is.EqualTo(10));
            while (!original.AdvanceTo(10).Completed) { }
            while (!restored.AdvanceTo(10).Completed) { }
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(15));
        }

        [Test]
        public void VersionOneSnapshotMigratesAndFutureVersionIsRejected()
        {
            var simulation = new Simulation(Content());
            var json = JObject.Parse(StateJson(simulation));
            json["version"] = 1;
            json["simulation"] = json["state"];
            json.Remove("state");
            var migrated = SnapshotService.Deserialize(json.ToString());
            Assert.That(migrated.Version, Is.EqualTo(SimulationSnapshot.CurrentVersion));
            Assert.That(StateJson(SnapshotService.Restore(simulation.Content, migrated)), Is.EqualTo(StateJson(simulation)));
            json["version"] = 999;
            Assert.Throws<JsonSerializationException>(() => SnapshotService.Deserialize(json.ToString()));
        }

        [Test]
        public void RestoreRejectsInvalidBalancesAndDuplicateQueuedSequences()
        {
            var simulation = new Simulation(Content());
            var state = simulation.ExportState();
            state.Resources[0].Balance = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.FromState(simulation.Content, state));
            state = simulation.ExportState();
            state.Events.Add(new EventState { Time = 1, Sequence = 1, Kind = SimulationEventKind.GrantResource, TargetId = "gold", Amount = 1 });
            state.Events.Add(new EventState { Time = 2, Sequence = 1, Kind = SimulationEventKind.GrantResource, TargetId = "gold", Amount = 2 });
            state.NextEventSequence = 2;
            Assert.Throws<ArgumentException>(() => Simulation.FromState(simulation.Content, state));
        }

        [Test]
        public void AtomicSaveBackupRecoversCorruptionAndOfflineLoadCapsAtTwentyFourHours()
        {
            var directory = "snapshot-test-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory, "save.json");
            try
            {
                var content = Content();
                var simulation = new Simulation(content);
                simulation.Execute(new BuyProducerCommand("worker"));
                SnapshotService.Save(path, simulation, Epoch);
                simulation.Execute(new GrantResourceCommand("gems", 10));
                SnapshotService.Save(path, simulation, Epoch);
                Assert.That(File.Exists(path + ".bak"), Is.True);
                File.WriteAllText(path, "{corrupt");
                var restored = SnapshotService.Load(path, content, Epoch.AddDays(3), 86400);
                Assert.That(restored.Time, Is.EqualTo(86400));
                Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(86400));
                Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
                Assert.That(restored.PendingEventCount, Is.Zero);
                Assert.That(Directory.GetFiles(directory, "*.pending-*"), Is.Empty);
                var backwards = SnapshotService.Load(path, content, Epoch.AddDays(-1), 86400);
                Assert.That(backwards.Time, Is.Zero);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ClockDrivenOfflineSummaryResumesBudgetSlicesWithoutDoubleCounting()
        {
            var content = Content();
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Schedule(new GrantResourceCommand("gems", 2), 10);
            simulation.Schedule(new GrantResourceCommand("gems", 3), 20);
            var clock = new FixedClock { UtcNow = Epoch };
            var snapshot = SnapshotService.Capture(simulation, clock);
            clock.UtcNow = Epoch.AddDays(3);
            var result = SnapshotService.Resume(content, snapshot, clock, 86400, eventBudget: 1);
            Assert.That(result.OfflineSeconds, Is.EqualTo(86400));
            Assert.That(result.TargetTime, Is.EqualTo(86400));
            Assert.That(result.Completed, Is.False);
            Assert.That(result.Advance.ProcessedEvents, Is.EqualTo(1));
            for (int i = 0; i < 5 && !result.Completed; i++) result.Continue();
            Assert.That(result.Completed, Is.True);
            Assert.That(result.RemainingSeconds, Is.Zero);
            Assert.That(result.Gains[0].ResourceId, Is.EqualTo("gold"));
            Assert.That(result.Gains[0].NetGain.ToDouble(), Is.EqualTo(86400));
            Assert.That(result.Gains[1].NetGain.ToDouble(), Is.EqualTo(10));
            result.Continue();
            Assert.That(result.Gains[0].NetGain.ToDouble(), Is.EqualTo(86400));
            Assert.That(result.Gains[1].NetGain.ToDouble(), Is.EqualTo(10));
        }

        [Test]
        public void SnapshotRejectsChangedContentEvenWhenIdsAreUnchanged()
        {
            var simulation = new Simulation(Content());
            var original = simulation.Content;
            var changed = new ContentDatabase(new[] { original.Resources[0], original.Resources[1] },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 2) }) },
                rules: new[] { original.Rules[0] });
            Assert.Throws<InvalidOperationException>(() =>
                SnapshotService.Restore(changed, SnapshotService.Capture(simulation, Epoch)));
        }

        [Test]
        public void DeserializeRejectsDuplicatePropertiesAndTrailingDocuments()
        {
            var json = StateJson(new Simulation(Content()));
            Assert.Throws<JsonReaderException>(() =>
                SnapshotService.Deserialize(json.Replace("\"version\": 2", "\"version\": 2, \"version\": 2")));
            Assert.Throws<JsonReaderException>(() => SnapshotService.Deserialize(json + "{}"));
        }

        [TestCase("cost")]
        [TestCase("rate")]
        [TestCase("modifier")]
        [TestCase("threshold")]
        [TestCase("action")]
        [TestCase("cap")]
        [TestCase("prestige")]
        public void FingerprintDetectsEveryEconomyDefinitionChange(string changed)
        {
            var baseline = FingerprintContent(null);
            var snapshot = SnapshotService.Capture(new Simulation(baseline), Epoch);
            var altered = FingerprintContent(changed);
            var changedSnapshot = SnapshotService.Capture(new Simulation(altered), Epoch);
            Assert.That(changedSnapshot.ContentHash, Is.Not.EqualTo(snapshot.ContentHash));
            Assert.Throws<InvalidOperationException>(() => SnapshotService.Restore(altered, snapshot));
        }

        private static ContentDatabase FingerprintContent(string changed) =>
            new ContentDatabase(new[] { new ResourceDefinition("gold", cap: changed == "cap" ? 1001 : 1000),
                    new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", changed == "cost" ? 2 : 1) },
                    new[] { new ResourceAmount("gold", changed == "rate" ? 3 : 2) }) },
                new[] { new UpgradeDefinition("upgrade", null,
                    new[] { new ModifierDefinition("modifier", "worker", changed == "modifier" ? 3 : 2) }) },
                new[] { new RuleDefinition("rule", "gold", changed == "threshold" ? 11 : 10,
                    new RuleAction[] { new GrantResourceAction("gems", changed == "action" ? 2 : 1) }) },
                new PrestigeDefinition("gold", 100, changed == "prestige" ? 3 : 2));

        [Test]
        public void ClockRollbackEarnsNothingAndOfflineGainRespectsResourceCap()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", cap: 10) },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 3) }) });
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            var snapshot = SnapshotService.Capture(simulation, Epoch);
            var clock = new FixedClock { UtcNow = Epoch.AddDays(-1) };
            var rollback = SnapshotService.Resume(content, snapshot, clock, 1000);
            Assert.That(rollback.OfflineSeconds, Is.Zero);
            Assert.That(rollback.Simulation.Time, Is.Zero);
            Assert.That(rollback.Gains[0].NetGain, Is.EqualTo(BigDouble.Zero));
            clock.UtcNow = Epoch.AddDays(1);
            var capped = SnapshotService.Resume(content, snapshot, clock, 1000);
            Assert.That(capped.OfflineSeconds, Is.EqualTo(1000));
            Assert.That(capped.Advance.ProcessedEvents, Is.Zero);
            Assert.That(capped.Gains[0].NetGain.ToDouble(), Is.EqualTo(10));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void OfflineResumeRejectsInvalidDurationCap(double seconds)
        {
            var simulation = new Simulation(Content());
            Assert.Throws<ArgumentOutOfRangeException>(() => SnapshotService.Resume(simulation.Content,
                SnapshotService.Capture(simulation, Epoch), new FixedClock { UtcNow = Epoch }, seconds));
        }

        [Test]
        public void MidOfflineSaveRetainsUnprocessedTargetWithoutReearningElapsedTime()
        {
            var content = Content();
            var simulation = new Simulation(content);
            simulation.Execute(new BuyProducerCommand("worker"));
            simulation.Schedule(new GrantResourceCommand("gems", 2), 10);
            simulation.Schedule(new GrantResourceCommand("gems", 3), 20);
            var clock = new FixedClock { UtcNow = Epoch.AddSeconds(200) };
            var first = SnapshotService.Resume(content, SnapshotService.Capture(simulation, Epoch), clock, 1000, 1);
            Assert.That(first.Completed, Is.False);
            var saved = SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(first.Simulation, clock)));
            var second = SnapshotService.Resume(content, saved, clock, 1000, 1);
            Assert.That(second.OfflineSeconds, Is.Zero);
            Assert.That(second.TargetTime, Is.EqualTo(200));
            for (int i = 0; i < 5 && !second.Completed; i++) second.Continue();
            Assert.That(second.Completed, Is.True);
            Assert.That(second.Simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(200));
            Assert.That(second.Simulation.GetResource("gems").Balance.ToDouble(), Is.EqualTo(10));
        }

        [Test]
        public void CustomMigrationTransformsOldStateAndDoesNotMutateInputJson()
        {
            var content = Content();
            var json = JObject.Parse(StateJson(new Simulation(content)));
            json["version"] = 1;
            var migrated = SnapshotService.Deserialize(json.ToString(), new[] { new GoldMigration() });
            Assert.That(migrated.Version, Is.EqualTo(2));
            var restored = SnapshotService.Restore(content, migrated);
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(7));
            Assert.That(json["state"]["resources"][0]["balance"]["mantissa"].Value<double>(), Is.Zero);
        }

        [Test]
        public void SnapshotRejectsOversizedPayloadAndTypeMetadata()
        {
            Assert.Throws<JsonSerializationException>(() =>
                SnapshotService.Deserialize(new string('x', SnapshotService.MaximumSnapshotBytes + 1)));
            var json = JObject.Parse(StateJson(new Simulation(Content())));
            json["state"]["$type"] = "Untrusted.Type, Assembly";
            Assert.Throws<JsonSerializationException>(() => SnapshotService.Deserialize(json.ToString()));
        }

        [Test]
        public void SnapshotAndResumeRejectNonUtcWallClockTimes()
        {
            var simulation = new Simulation(Content());
            var unspecified = DateTime.SpecifyKind(Epoch, DateTimeKind.Unspecified);
            Assert.Throws<ArgumentException>(() => SnapshotService.Capture(simulation, unspecified));
            var snapshot = SnapshotService.Capture(simulation, Epoch);
            Assert.Throws<ArgumentException>(() => SnapshotService.Resume(simulation.Content, snapshot,
                new FixedClock { UtcNow = unspecified }, 100));
            var json = JObject.Parse(SnapshotService.Serialize(snapshot));
            json["savedAtUtc"] = "2026-01-01T00:00:00";
            Assert.Throws<ArgumentException>(() => SnapshotService.Deserialize(json.ToString()));
        }
    }
}
