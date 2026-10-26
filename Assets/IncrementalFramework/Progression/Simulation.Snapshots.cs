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
                PermanentMultiplier = PermanentMultiplier, PendingTarget = pendingTarget,
                NextEventSequence = scheduler.NextSequence
            };
            for (int i = 0; i < balances.Length; i++) state.Resources.Add(new ResourceState { Id = Content.Resources[i].Id, Balance = balances[i] });
            for (int i = 0; i < owned.Length; i++) state.Producers.Add(new ProducerState
            { Id = Content.Producers[i].Id, Owned = owned[i], Enabled = enabled[i], Unlocked = producerUnlocked[i] });
            for (int i = 0; i < purchased.Length; i++) state.Upgrades.Add(new UpgradeState
            { Id = Content.Upgrades[i].Id, Purchased = purchased[i], Unlocked = upgradeUnlocked[i] });
            for (int i = 0; i < ruleFired.Length; i++) state.Rules.Add(new RuleState
            { Id = Content.Rules[i].Id, Fired = ruleFired[i], Disabled = ruleDisabled[i], NextEligibleTime = ruleEligible[i] });
            foreach (var entry in scheduler.Export())
            {
                var payload = entry.Payload;
                string id = null;
                switch (payload.Kind)
                {
                    case SimulationEventKind.Rule: id = Content.Rules[payload.Index].Id; break;
                    case SimulationEventKind.GrantResource: id = Content.Resources[payload.Index].Id; break;
                    case SimulationEventKind.BuyProducer:
                    case SimulationEventKind.ToggleProducer: id = Content.Producers[payload.Index].Id; break;
                    case SimulationEventKind.BuyUpgrade: id = Content.Upgrades[payload.Index].Id; break;
                }
                state.Events.Add(new EventState
                {
                    Time = entry.Time, Priority = entry.Priority, Sequence = entry.Sequence, Kind = payload.Kind,
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
            if (!Finite(state.Time) || state.Time < 0 || state.RandomState == 0 || state.PrestigeCount < 0 ||
                !Number.IsFinite(state.PermanentMultiplier) || state.PermanentMultiplier < BigDouble.One ||
                (state.PendingTarget.HasValue && (!Finite(state.PendingTarget.Value) || state.PendingTarget < state.Time)))
                throw new ArgumentException("Invalid simulation snapshot.");
            if (state.Resources == null || state.Resources.Count != balances.Length ||
                state.Producers == null || state.Producers.Count != owned.Length ||
                state.Upgrades == null || state.Upgrades.Count != purchased.Length ||
                state.Rules == null || state.Rules.Count != ruleFired.Length || state.Events == null)
                throw new ArgumentException("Snapshot content differs; migrate it before restoring.");
            Time = state.Time; randomState = state.RandomState; PrestigeCount = state.PrestigeCount;
            PermanentMultiplier = state.PermanentMultiplier; pendingTarget = state.PendingTarget;
            var seen = new HashSet<int>();
            foreach (var resource in state.Resources)
            {
                if (resource == null) throw new ArgumentException("Null resource state.");
                int i = Content.ResourceIndex(resource.Id);
                Number.NonNegative(resource.Balance, "balance");
                if (!seen.Add(i) || (Content.Resources[i].Cap.HasValue && resource.Balance > Content.Resources[i].Cap.Value))
                    throw new ArgumentException("Duplicate or out-of-range resource state.");
                balances[i] = resource.Balance;
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
            foreach (var rule in state.Rules)
            {
                if (rule == null || !ruleIndices.TryGetValue(rule.Id ?? "", out int i) || !seen.Add(i) ||
                    !Finite(rule.NextEligibleTime) || rule.NextEligibleTime < 0)
                    throw new ArgumentException("Invalid rule state.");
                ruleFired[i] = rule.Fired; ruleDisabled[i] = rule.Disabled; ruleEligible[i] = rule.NextEligibleTime;
            }
            var entries = new List<ScheduledEvent<SimulationEvent>>();
            seen.Clear();
            Array.Clear(ruleTokens, 0, ruleTokens.Length);
            foreach (var entry in state.Events)
            {
                if (entry == null || !Finite(entry.Time) || entry.Time < Time) throw new ArgumentException("Invalid event time.");
                Number.NonNegative(entry.Amount, "event amount");
                int index;
                switch (entry.Kind)
                {
                    case SimulationEventKind.Rule:
                        if (!ruleIndices.TryGetValue(entry.TargetId ?? "", out index) || !seen.Add(index) ||
                            ruleDisabled[index] || (ruleFired[index] && !Content.Rules[index].Repeat) || entry.Time < ruleEligible[index])
                            throw new ArgumentException("Invalid rule event.");
                        ruleTokens[index] = new EventToken(entry.Sequence); break;
                    case SimulationEventKind.GrantResource: index = Content.ResourceIndex(entry.TargetId); break;
                    case SimulationEventKind.BuyProducer:
                        if (entry.Count < 1) throw new ArgumentException("Invalid purchase count.");
                        index = Content.ProducerIndex(entry.TargetId); break;
                    case SimulationEventKind.ToggleProducer: index = Content.ProducerIndex(entry.TargetId); break;
                    case SimulationEventKind.BuyUpgrade: index = Content.UpgradeIndex(entry.TargetId); break;
                    case SimulationEventKind.Prestige: index = 0; break;
                    default: throw new ArgumentException("Unknown event kind.");
                }
                entries.Add(new ScheduledEvent<SimulationEvent>(entry.Time, entry.Priority, entry.Sequence,
                    new SimulationEvent(entry.Kind, index, entry.Amount, entry.Count, entry.Enabled)));
            }
            scheduler.Restore(entries, state.NextEventSequence);
            RebuildAllRates();
            for (int i = 0; i < ruleFired.Length; i++)
                if (TryRuleTime(i, out _) != scheduler.TryGet(ruleTokens[i], out _))
                    throw new ArgumentException("Snapshot is missing an active rule event or contains an inactive one.");
        }
    }
}
