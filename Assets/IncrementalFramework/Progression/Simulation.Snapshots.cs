using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public sealed partial class Simulation
    {
        public SimulationState ExportState()
        {
            AssertWriter();
            var state = new SimulationState
            {
                Time = Time, RandomState = randomState, PrestigeCount = PrestigeCount,
                RandomAlgorithm = XorShift64.AlgorithmId,
                PrestigeCurrencyEarned = PrestigeCurrencyEarned,
                PermanentMultiplier = PermanentMultiplier, PendingTarget = pendingTarget,
                SystemsPreparedTarget = systemsPreparedTarget,
                NextEventSequence = scheduler.NextSequence
            };
            for (int i = 0; i < balances.Length; i++) state.Resources.Add(new ResourceState
            { Id = Content.Resources[i].Id, Balance = balances[i], LifetimeEarned = lifetime[i] });
            for (int i = 0; i < owned.Length; i++) state.Producers.Add(new ProducerState
            { Id = Content.Producers[i].Id, Owned = owned[i], Enabled = enabled[i], Unlocked = producerUnlocked[i] });
            for (int i = 0; i < purchased.Length; i++) state.Upgrades.Add(new UpgradeState
            { Id = Content.Upgrades[i].Id, Purchased = purchased[i], Unlocked = upgradeUnlocked[i] });
            for (int i = 0; i < ruleFired.Length; i++) state.Rules.Add(new RuleState
            { Id = Content.Rules[i].Id, Fired = ruleFired[i], Disabled = ruleDisabled[i], Armed = ruleArmed[i], NextEligibleTime = ruleEligible[i] });
            for (int i = 0; i < flags.Length; i++) state.Flags.Add(new FlagState { Id = Content.Flags[i].Id, Value = flags[i] });
            for (int i = 0; i < activeModifiers.Length; i++) state.Modifiers.Add(new ModifierState { Id = Content.Modifiers[i].Id, Enabled = activeModifiers[i] });
            foreach (var entry in scheduler.Export())
            {
                var payload = entry.Payload;
                string id = null;
                switch (payload.Kind)
                {
                    case SimulationEventKind.Rule: id = Content.Rules[payload.Index].Id; break;
                    case SimulationEventKind.TriggerRule: id = Content.Rules[payload.Index].Id; break;
                    case SimulationEventKind.GrantResource: id = Content.Resources[payload.Index].Id; break;
                    case SimulationEventKind.Click: id = Content.Resources[payload.Index].Id; break;
                    case SimulationEventKind.SetFlag: id = Content.Flags[payload.Index].Id; break;
                    case SimulationEventKind.SetModifier: id = Content.Modifiers[payload.Index].Id; break;
                    case SimulationEventKind.BuyProducer:
                    case SimulationEventKind.ToggleProducer: id = Content.Producers[payload.Index].Id; break;
                    case SimulationEventKind.BuyUpgrade: id = Content.Upgrades[payload.Index].Id; break;
                }
                state.Events.Add(new EventState
                {
                    Time = entry.Time, Priority = entry.Priority, Sequence = entry.Sequence, Kind = payload.Kind,
                    TokenVersion = entry.Version,
                    TargetId = id, Amount = payload.Amount, Count = payload.Count, Enabled = payload.Enabled
                });
            }
            return state;
        }

        public static Simulation FromState(ContentDatabase content, SimulationState state, int eventBudget = 10000)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var sim = new Simulation(content, state.RandomState, eventBudget);
            sim.Import(state);
            return sim;
        }

        private void Import(SimulationState state)
        {
            if (!Finite(state.Time) || state.Time < 0 || state.RandomState == 0 || state.RandomAlgorithm != XorShift64.AlgorithmId || state.PrestigeCount < 0 ||
                !Number.IsFinite(state.PermanentMultiplier) || state.PermanentMultiplier < BigDouble.One ||
                (state.PendingTarget.HasValue && (!Finite(state.PendingTarget.Value) || state.PendingTarget < state.Time)))
                throw new ArgumentException("Invalid simulation snapshot.");
            if (state.Resources == null || state.Resources.Count != balances.Length ||
                state.Producers == null || state.Producers.Count != owned.Length ||
                state.Upgrades == null || state.Upgrades.Count != purchased.Length ||
                state.Rules == null || state.Rules.Count != ruleFired.Length || state.Events == null)
                throw new ArgumentException("Snapshot content differs; migrate it before restoring.");
            Time = state.Time; randomState = state.RandomState; PrestigeCount = state.PrestigeCount;
            Number.NonNegative(state.PrestigeCurrencyEarned, "prestige currency earned");
            PrestigeCurrencyEarned = state.PrestigeCurrencyEarned;
            PermanentMultiplier = state.PermanentMultiplier; pendingTarget = state.PendingTarget;
            systemsPreparedTarget = state.SystemsPreparedTarget ?? pendingTarget;
            if (systemsPreparedTarget.HasValue && (!Finite(systemsPreparedTarget.Value) ||
                !pendingTarget.HasValue || systemsPreparedTarget < Time || systemsPreparedTarget > pendingTarget))
                throw new ArgumentException("Invalid prepared system horizon.");
            var seen = new HashSet<int>();
            foreach (var resource in state.Resources)
            {
                if (resource == null) throw new ArgumentException("Null resource state.");
                int i = Content.ResourceIndex(resource.Id);
                BigDouble balance = NormalizeBalance(i, resource.Balance);
                Number.NonNegative(resource.LifetimeEarned, "lifetime earned");
                if (!seen.Add(i) || (Content.Resources[i].Cap.HasValue && resource.Balance > Content.Resources[i].Cap.Value))
                    throw new ArgumentException("Duplicate or out-of-range resource state.");
                balances[i] = balance;
                if (resource.LifetimeEarned < balance) throw new ArgumentException("Lifetime earned is below balance.");
                lifetime[i] = resource.LifetimeEarned;
            }
            seen.Clear();
            foreach (var producer in state.Producers)
            {
                if (producer == null) throw new ArgumentException("Null producer state.");
                int i = Content.ProducerIndex(producer.Id);
                if (!seen.Add(i) || producer.Owned < 0 || (producer.Owned > 0 && !producer.Unlocked))
                    throw new ArgumentException("Invalid producer state.");
                owned[i] = producer.Owned; enabled[i] = producer.Enabled; producerUnlocked[i] = producer.Unlocked;
            }
            seen.Clear();
            foreach (var upgrade in state.Upgrades)
            {
                if (upgrade == null) throw new ArgumentException("Null upgrade state.");
                int i = Content.UpgradeIndex(upgrade.Id);
                if (!seen.Add(i) || (upgrade.Purchased && !upgrade.Unlocked)) throw new ArgumentException("Invalid upgrade state.");
                purchased[i] = upgrade.Purchased; upgradeUnlocked[i] = upgrade.Unlocked;
            }
            seen.Clear();
            if (state.Flags == null || state.Flags.Count != flags.Length) throw new ArgumentException("Snapshot flags differ from content.");
            foreach (var flag in state.Flags)
            {
                if (flag == null) throw new ArgumentException("Null flag state.");
                int i = Content.FlagIndex(flag.Id);
                if (!seen.Add(i)) throw new ArgumentException("Duplicate flag state.");
                flags[i] = flag.Value;
            }
            seen.Clear();
            if (state.Modifiers == null || state.Modifiers.Count != activeModifiers.Length) throw new ArgumentException("Snapshot modifiers differ from content.");
            foreach (var modifier in state.Modifiers)
            {
                if (modifier == null) throw new ArgumentException("Null modifier state.");
                int i = Content.ModifierIndex(modifier.Id);
                if (!seen.Add(i)) throw new ArgumentException("Duplicate modifier state.");
                activeModifiers[i] = modifier.Enabled;
            }
            seen.Clear();
            foreach (var rule in state.Rules)
            {
                if (rule == null || !ruleIndices.TryGetValue(rule.Id ?? "", out int i) || !seen.Add(i) ||
                    !Finite(rule.NextEligibleTime) || rule.NextEligibleTime < 0)
                    throw new ArgumentException("Invalid rule state.");
                ruleFired[i] = rule.Fired; ruleDisabled[i] = rule.Disabled; ruleArmed[i] = rule.Armed; ruleEligible[i] = rule.NextEligibleTime;
            }
            var entries = new List<ScheduledEvent<SimulationEvent>>();
            seen.Clear();
            Array.Clear(ruleTokens, 0, ruleTokens.Length);
            foreach (var entry in state.Events)
            {
                if (entry == null || !Finite(entry.Time) || entry.Time < Time) throw new ArgumentException("Invalid event time.");
                if (entry.Kind == SimulationEventKind.GrantResource) ValidateGrant(Content.ResourceIndex(entry.TargetId), entry.Amount);
                else Number.NonNegative(entry.Amount, "event amount");
                int index;
                switch (entry.Kind)
                {
                    case SimulationEventKind.Rule:
                        if (!ruleIndices.TryGetValue(entry.TargetId ?? "", out index) || !seen.Add(index) ||
                            ruleDisabled[index] || (ruleFired[index] && !Content.Rules[index].Repeat) || entry.Time < ruleEligible[index])
                            throw new ArgumentException("Invalid rule event.");
                        ruleTokens[index] = new EventToken(entry.Sequence, entry.TokenVersion); break;
                    case SimulationEventKind.GrantResource: index = Content.ResourceIndex(entry.TargetId); break;
                    case SimulationEventKind.Click: index = Content.ResourceIndex(entry.TargetId); break;
                    case SimulationEventKind.SetFlag: index = Content.FlagIndex(entry.TargetId); break;
                    case SimulationEventKind.SetModifier: index = Content.ModifierIndex(entry.TargetId); break;
                    case SimulationEventKind.TriggerRule:
                        if (!ruleIndices.TryGetValue(entry.TargetId ?? "", out index)) throw new ArgumentException("Unknown rule event target.");
                        break;
                    case SimulationEventKind.BuyProducer:
                        if (entry.Count < 1) throw new ArgumentException("Invalid purchase count.");
                        index = Content.ProducerIndex(entry.TargetId); break;
                    case SimulationEventKind.ToggleProducer: index = Content.ProducerIndex(entry.TargetId); break;
                    case SimulationEventKind.BuyUpgrade: index = Content.UpgradeIndex(entry.TargetId); break;
                    case SimulationEventKind.Prestige: index = 0; break;
                    default: throw new ArgumentException("Unknown event kind.");
                }
                entries.Add(new ScheduledEvent<SimulationEvent>(entry.Time, entry.Priority, entry.Sequence,
                    new SimulationEvent(entry.Kind, index, entry.Amount, entry.Count, entry.Enabled), entry.TokenVersion));
            }
            scheduler.Restore(entries, state.NextEventSequence);
            RebuildAllRates();
            for (int i = 0; i < ruleFired.Length; i++)
                if (TryRuleTime(i, out _) != scheduler.TryGet(ruleTokens[i], out _))
                    throw new ArgumentException("Snapshot is missing an active rule event or contains an inactive one.");
            changeBefore.Clear(); moduleChanges.Clear(); changedResources.Clear();
        }
    }
}
