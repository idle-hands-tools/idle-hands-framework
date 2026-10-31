using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.IO;
using BreakInfinity;
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
                snapshot["state"]["resources"][0]["lifetimeEarned"] =
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
        public void SnapshotRestoresSpentLifetimePermanentUpgradeAndDisabledProducer()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold", 100) },
                new[] { new ProducerDefinition("worker", new[] { new ResourceAmount("gold", 20) },
                    new[] { new ResourceAmount("gold", 2) }) },
                new[] { new UpgradeDefinition("upgrade", new[] { new ResourceAmount("gold", 5) },
                    new[] { new ModifierDefinition("permanent", "worker", 3) }, permanent: true) });
            var original = new Simulation(content, 789);
            original.Execute(new BuyProducerCommand("worker"));
            original.Execute(new BuyUpgradeCommand("upgrade"));
            original.Execute(new ToggleProducerCommand("worker", false));
            original.Schedule(new ToggleProducerCommand("worker", true), 100);
            original.AdvanceTo(50);
            var json = StateJson(original);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(json));
            Assert.That(StateJson(restored), Is.EqualTo(json));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(75));
            Assert.That(restored.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(100));
            Assert.That(restored.GetUpgrade("upgrade").Purchased, Is.True);
            Assert.That(restored.GetProducer("worker").Enabled, Is.False);
            original.AdvanceTo(200);
            restored.AdvanceTo(200);
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(675));
            Assert.That(restored.GetResource("gold").LifetimeEarned.ToDouble(), Is.EqualTo(700));
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
        public void ZeroCooldownCascadeSnapshotRetainsExactQueuedSequenceOnBudgetResume()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                rules: new[] { new RuleDefinition("repeat", "gold", 0,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, repeat: true) });
            var original = new Simulation(content, eventBudget: 3);
            Assert.That(original.AdvanceTo(100).Completed, Is.False);
            var saved = StateJson(original);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(saved), eventBudget: 3);
            Assert.That(StateJson(restored), Is.EqualTo(saved));
            Assert.That(restored.ExportState().Events[0].Sequence, Is.EqualTo(original.ExportState().Events[0].Sequence));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(original.AdvanceTo(100).Completed, Is.False);
                Assert.That(restored.AdvanceTo(100).Completed, Is.False);
                Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            }
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(12));
            Assert.That(restored.Time, Is.Zero);
            Assert.That(restored.PendingEventCount, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotPreservesRescheduledRuleTokenVersionAndExcludesCancelledEntries()
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }) },
                new[] { new UpgradeDefinition("upgrade", null, new[] { new ModifierDefinition("m", "worker", 2) }) },
                new[] { new RuleDefinition("rule", "gold", 100, new RuleAction[] { new GrantResourceAction("gems", 5) }) });
            var original = new Simulation(content);
            original.Execute(new BuyProducerCommand("worker"));
            original.Schedule(new BuyUpgradeCommand("upgrade"), 5);
            var cancelled = original.Schedule(new GrantResourceCommand("gems", 999), 3);
            original.Cancel(cancelled);
            original.AdvanceTo(10);
            var state = original.ExportState();
            Assert.That(state.Events.Count, Is.EqualTo(1));
            Assert.That(state.Events[0].Kind, Is.EqualTo(SimulationEventKind.Rule));
            Assert.That(state.Events[0].TokenVersion, Is.GreaterThan(1));
            var json = StateJson(original);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(json));
            Assert.That(StateJson(restored), Is.EqualTo(json));
            original.AdvanceTo(100);
            restored.AdvanceTo(100);
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            Assert.That(restored.GetResource("gold").Balance.ToDouble(), Is.EqualTo(195));
            Assert.That(restored.GetResource("gems").Balance.ToDouble(), Is.EqualTo(5));
        }

        [TestCase(1e20)]
        [TestCase(double.MaxValue)]
        public void PositiveCooldownAtHugeTimeNeverAccidentallyRepeatsAtSameTimestamp(double time)
        {
            var content = new ContentDatabase(new[] { new ResourceDefinition("gold") },
                rules: new[] { new RuleDefinition("repeat", "gold", 0,
                    new RuleAction[] { new GrantResourceAction("gold", 1) }, repeat: true, cooldown: 1) });
            var initial = new Simulation(content);
            var state = initial.ExportState();
            state.Time = time;
            state.Events[0].Time = time;
            var simulation = Simulation.FromState(content, state, eventBudget: 3);
            var result = simulation.AdvanceTo(time);
            Assert.That(result.Completed, Is.True);
            Assert.That(result.ProcessedEvents, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance.ToDouble(), Is.EqualTo(1));
            var after = simulation.ExportState();
            if (time == double.MaxValue)
            {
                Assert.That(after.Events, Is.Empty);
                Assert.That(after.Rules[0].Disabled, Is.True);
            }
            else
            {
                Assert.That(after.Events.Count, Is.EqualTo(1));
                Assert.That(after.Events[0].Time, Is.GreaterThan(time));
            }
            var json = StateJson(simulation);
            var restored = SnapshotService.Restore(content, SnapshotService.Deserialize(json));
            Assert.That(StateJson(restored), Is.EqualTo(json));
            Assert.That(restored.AdvanceTo(time).ProcessedEvents, Is.Zero);
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

        [TestCase(1)]
        [TestCase(2)]
        public void HistoricalSnapshotsWithoutFingerprintRequireExplicitLegacyContentMigration(int version)
        {
            var content = Content();
            var original = new Simulation(content);
            original.Execute(new GrantResourceCommand("gold", 10));
            original.Execute(new BuyProducerCommand("worker"));
            var root = JObject.Parse(StateJson(original));
            root["version"] = version;
            root.Remove("contentHash");
            root.Remove("contentVersion");
            foreach (var resource in (JArray)root["state"]["resources"])
                ((JObject)resource).Remove("lifetimeEarned");
            if (version == 1)
            {
                root["simulation"] = root["state"];
                root.Remove("state");
            }
            var legacyJson = root.ToString();
            Assert.Throws<JsonSerializationException>(() => SnapshotService.Deserialize(legacyJson));
            var migrated = SnapshotService.Deserialize(legacyJson, new[] { new VersionTwoSnapshotMigration(content) });
            Assert.That(migrated.Version, Is.EqualTo(SimulationSnapshot.CurrentVersion));
            Assert.That(StateJson(SnapshotService.Restore(content, migrated)), Is.EqualTo(StateJson(original)));
        }

        [TestCase("contentHash")]
        [TestCase("contentVersion")]
        public void ExplicitLegacyMigrationCannotOverrideExistingMismatchedFingerprint(string property)
        {
            var content = Content();
            var root = JObject.Parse(StateJson(new Simulation(content)));
            root["version"] = 2;
            root[property] = property == "contentHash" ? new string('0', 64) : "different-version";
            Assert.Throws<JsonSerializationException>(() => SnapshotService.Deserialize(
                root.ToString(), new[] { new VersionTwoSnapshotMigration(content) }));
        }

        [Test]
        public void HistoricalDebtMigrationPreservesHugeSignedBalancesAndQueuedGrantsWithZeroLifetime()
        {
            var balance = BigDouble.FromMantissaExponentNoNormalize(-1.2345678901234567, 1000);
            var amount = BigDouble.FromMantissaExponentNoNormalize(-2.345678901234567, 1001);
            var content = new ContentDatabase(new[] {
                new ResourceDefinition("debt", balance, negativeAmountPolicy: NegativeAmountPolicy.Allow) });
            var original = new Simulation(content);
            original.Schedule(new GrantResourceCommand("debt", amount), 5);
            var root = JObject.Parse(StateJson(original));
            root["version"] = 2;
            root.Remove("contentHash");
            root.Remove("contentVersion");
            ((JObject)root["state"]["resources"][0]).Remove("lifetimeEarned");
            var migrated = SnapshotService.Deserialize(root.ToString(), new[] { new VersionTwoSnapshotMigration(content) });
            var restored = SnapshotService.Restore(content, migrated);
            Assert.That(restored.GetResource("debt").Balance.Mantissa, Is.EqualTo(balance.Mantissa));
            Assert.That(restored.GetResource("debt").Balance.Exponent, Is.EqualTo(balance.Exponent));
            Assert.That(restored.GetResource("debt").LifetimeEarned, Is.EqualTo(BigDouble.Zero));
            var queued = restored.ExportState().Events[0].Amount;
            Assert.That(queued.Mantissa, Is.EqualTo(amount.Mantissa));
            Assert.That(queued.Exponent, Is.EqualTo(amount.Exponent));
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            original.AdvanceTo(5);
            restored.AdvanceTo(5);
            Assert.That(StateJson(restored), Is.EqualTo(StateJson(original)));
            Assert.That(restored.GetResource("debt").LifetimeEarned, Is.EqualTo(BigDouble.Zero));
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
            var version = "\"version\": " + SimulationSnapshot.CurrentVersion;
            Assert.Throws<JsonReaderException>(() =>
                SnapshotService.Deserialize(json.Replace(version, version + ", " + version)));
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

        [TestCase("resources")]
        [TestCase("producers")]
        [TestCase("upgrades")]
        [TestCase("rules")]
        public void FingerprintPreservesDefinitionArrayOrderAndRejectsImplicitReordering(string reordered)
        {
            var original = new Simulation(OrderedContent(null));
            var snapshot = SnapshotService.Capture(original, Epoch);
            var changed = OrderedContent(reordered);
            Assert.That(ContentFingerprint.Compute(changed), Is.Not.EqualTo(snapshot.ContentHash));
            Assert.Throws<InvalidOperationException>(() => SnapshotService.Restore(changed, snapshot));
        }

        private static ContentDatabase OrderedContent(string reordered)
        {
            var resources = new[] { new ResourceDefinition("gold"), new ResourceDefinition("gems") };
            var producers = new[] {
                new ProducerDefinition("miner", null, new[] { new ResourceAmount("gold", 1) }),
                new ProducerDefinition("jeweler", null, new[] { new ResourceAmount("gems", 2) }) };
            var upgrades = new[] {
                new UpgradeDefinition("pick", null, new[] { new ModifierDefinition("pick-m", "miner", 2) }),
                new UpgradeDefinition("lens", null, new[] { new ModifierDefinition("lens-m", "jeweler", 3) }) };
            var rules = new[] {
                new RuleDefinition("gold-rule", "gold", 10, new RuleAction[] { new GrantResourceAction("gems", 1) }),
                new RuleDefinition("gems-rule", "gems", 20, new RuleAction[] { new GrantResourceAction("gold", 2) }) };
            if (reordered == "resources") Array.Reverse(resources);
            if (reordered == "producers") Array.Reverse(producers);
            if (reordered == "upgrades") Array.Reverse(upgrades);
            if (reordered == "rules") Array.Reverse(rules);
            return new ContentDatabase(resources, producers, upgrades, rules);
        }

        [TestCase("grant-vs-spend")]
        [TestCase("grant-amount")]
        [TestCase("spend-amount")]
        [TestCase("set-flag")]
        [TestCase("schedule-delay")]
        [TestCase("schedule-priority")]
        [TestCase("set-modifier")]
        [TestCase("flag-initial")]
        [TestCase("flag-condition")]
        [TestCase("modifier-priority")]
        [TestCase("modifier-category")]
        public void AdvancedRuleAndModifierFingerprintIncludesDiscriminatorsAndEveryPayload(string changed)
        {
            var baseline = AdvancedFingerprintContent(null);
            var altered = AdvancedFingerprintContent(changed);
            var snapshot = SnapshotService.Capture(new Simulation(baseline), Epoch);
            Assert.That(ContentFingerprint.Compute(altered), Is.Not.EqualTo(snapshot.ContentHash));
            Assert.Throws<InvalidOperationException>(() => SnapshotService.Restore(altered, snapshot));
        }

        private static ContentDatabase AdvancedFingerprintContent(string changed)
        {
            RuleAction first = changed == "grant-vs-spend"
                ? (RuleAction)new SpendResourceAction("gold", 1)
                : new GrantResourceAction("gold", changed == "grant-amount" ? 2 : 1);
            var actions = new[] { first,
                new SpendResourceAction("gold", changed == "spend-amount" ? 2 : 1),
                new SetFlagAction("ready", changed != "set-flag"),
                new ScheduleGrantResourceAction("gold", 2, changed == "schedule-delay" ? 6 : 5,
                    changed == "schedule-priority" ? 2 : 1),
                new SetModifierAction("dynamic", changed != "set-modifier") };
            return new ContentDatabase(new[] { new ResourceDefinition("gold", 10, category: "ore"),
                    new ResourceDefinition("wood", category: "forest") },
                new[] { new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1), new ResourceAmount("wood", 1) }) },
                rules: new[] { new RuleDefinition("rule", ConditionDefinition.Flag("ready", changed != "flag-condition"), actions) },
                flags: new[] { new FlagDefinition("ready", changed == "flag-initial") },
                modifiers: new[] { new ModifierDefinition("dynamic", ModifierTarget.ResourceCategoryProduction,
                    changed == "modifier-category" ? "forest" : "ore", 2, ModifierKind.Override,
                    changed == "modifier-priority" ? 2 : 1) });
        }

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
            Assert.That(migrated.Version, Is.EqualTo(SimulationSnapshot.CurrentVersion));
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

        [TestCase("json")]
        [TestCase("utf8")]
        [TestCase("header")]
        public void ReadSnapshotFallsBackToBackupForMalformedMainFile(string corruption)
        {
            var directory = "snapshot-test-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory, "save.json");
            try
            {
                var simulation = new Simulation(Content());
                simulation.Execute(new BuyProducerCommand("worker"));
                SnapshotService.Save(path, simulation, Epoch);
                var expected = StateJson(simulation);
                simulation.Execute(new GrantResourceCommand("gems", 10));
                SnapshotService.Save(path, simulation, Epoch);
                if (corruption == "json") File.WriteAllText(path, "{broken");
                else if (corruption == "utf8") File.WriteAllBytes(path, new byte[] { 0xc3, 0x28 });
                else
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    json["version"] = 999;
                    File.WriteAllText(path, json.ToString());
                }
                var snapshot = SnapshotService.ReadSnapshot(path);
                Assert.That(SnapshotService.Serialize(snapshot), Is.EqualTo(expected));
                Assert.That(Directory.GetFiles(directory, "*.pending-*"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ReadSnapshotRejectsOversizedSparseFileWithoutBackup()
        {
            var directory = "snapshot-test-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(directory, "oversized.json");
            try
            {
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.SetLength((long)SnapshotService.MaximumSnapshotBytes + 1);
                Assert.That(File.Exists(path + ".bak"), Is.False);
                Assert.Throws<JsonSerializationException>(() => SnapshotService.ReadSnapshot(path));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
