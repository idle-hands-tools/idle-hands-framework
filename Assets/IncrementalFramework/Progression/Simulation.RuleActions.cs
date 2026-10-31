using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework.Progression
{
    public sealed partial class Simulation
    {
        private void ProcessRule(int index)
        {
            ruleTokens[index] = default;
            ruleArmed[index] = false;
            if (!EvaluateCondition(Content.RuleCondition(index), Time)) { RefreshRule(index); return; }
            var transaction = new SimulationTransaction(Content);
            transaction.Actions.AddRange(Content.RuleActions(index));
            transaction.Seal();
            LastEventResult = CommitTransaction(transaction);
            if (!LastEventResult.Success) { RefreshRule(index); return; }
            var rule = Content.Rules[index];
            ruleFired[index] = true;
            double eligible = Time + rule.Cooldown;
            if (rule.Cooldown > 0 && eligible <= Time) eligible = NextTime(Time);
            if (!Finite(eligible)) { ruleDisabled[index] = true; eligible = Time; }
            ruleEligible[index] = eligible;
            RefreshRule(index);
        }

        // Shared by native rules and registered module commands. All preparation precedes writes.
        private CommandResult CommitTransaction(SimulationTransaction transaction)
        {
            var staged = new SortedDictionary<int, BigDouble>();
            var earned = new SortedDictionary<int, BigDouble>();
            var stagedFlags = new SortedDictionary<int, bool>();
            var proposedModifiers = (bool[])activeModifiers.Clone();
            bool changeModifiers = false;
            var scheduled = new List<ScheduledEvent<SimulationEvent>>();
            foreach (var cost in transaction.Costs)
            {
                if (balances[cost.Key] < cost.Value && Content.Resources[cost.Key].NegativeAmountPolicy != NegativeAmountPolicy.Allow)
                    return CommandResult.Fail("Insufficient resources for transaction.");
                staged[cost.Key] = Number.AddSigned(balances[cost.Key], -cost.Value);
            }
            foreach (var action in transaction.Actions)
                if (action.Kind == RuleActionKind.SpendResource)
                {
                    BigDouble balance = staged.TryGetValue(action.TargetIndex, out var previous) ? previous : balances[action.TargetIndex];
                    if (balance < action.Amount && Content.Resources[action.TargetIndex].NegativeAmountPolicy != NegativeAmountPolicy.Allow)
                        return CommandResult.Fail("Transaction cannot afford all costs.");
                    staged[action.TargetIndex] = Number.AddSigned(balance, -action.Amount);
                }
            void Grant(int target, BigDouble amount)
            {
                BigDouble before = staged.TryGetValue(target, out var previous) ? previous : balances[target];
                BigDouble after = GrantedBalance(target, before, amount);
                BigDouble oldEarned = earned.TryGetValue(target, out var e) ? e : lifetime[target];
                earned[target] = after > before ? Number.Add(oldEarned, Number.AddSigned(after, -before)) : oldEarned;
                staged[target] = after;
            }
            foreach (var grant in transaction.Grants) Grant(grant.Key, grant.Value);
            foreach (var action in transaction.Actions)
            {
                int target = action.TargetIndex;
                switch (action.Kind)
                {
                    case RuleActionKind.GrantResource: Grant(target, action.Amount); break;
                    case RuleActionKind.SetFlag: stagedFlags[target] = action.Enabled; break;
                    case RuleActionKind.SetModifier: proposedModifiers[target] = action.Enabled; changeModifiers = true; break;
                    case RuleActionKind.ScheduleGrantResource:
                        ValidateGrant(target, action.Amount);
                        double due = Time + action.Delay;
                        if (action.Delay > 0 && due <= Time) due = NextTime(Time);
                        if (!Finite(due)) throw new ArithmeticException("Scheduled action exceeds the finite time horizon.");
                        scheduled.Add(new ScheduledEvent<SimulationEvent>(due, action.Priority, 0,
                            new SimulationEvent(SimulationEventKind.GrantResource, target, action.Amount))); break;
                }
            }
            Dictionary<int, StatModifiers[]> outputs = null;
            SortedDictionary<int, StatModifiers> clicks = null;
            SortedSet<int> targets = null;
            if (changeModifiers && !PrepareModifierChange(proposedModifiers, out outputs, out clicks, out targets))
                throw new ArithmeticException("Transaction modifiers overflow production.");
            if (!transaction.Values.Validate()) return CommandResult.Fail("Transaction state is stale.");
            if (scheduled.Count > long.MaxValue - scheduler.NextSequence) return CommandResult.Fail("Event sequence exhausted.");
            foreach (var value in staged)
            {
                Mark(SimulationChangeKind.Resource, value.Key);
                if (balances[value.Key] != value.Value) changedResources.Add(value.Key);
                balances[value.Key] = value.Value;
            }
            foreach (var value in earned) lifetime[value.Key] = value.Value;
            var changedFlags = new List<int>();
            foreach (var value in stagedFlags)
            {
                Mark(SimulationChangeKind.Flag, value.Key);
                if (flags[value.Key] != value.Value) changedFlags.Add(value.Key);
                flags[value.Key] = value.Value;
            }
            foreach (var action in transaction.Actions)
            {
                if (action.Kind == RuleActionKind.UnlockProducer)
                { Mark(SimulationChangeKind.Producer, action.TargetIndex); producerUnlocked[action.TargetIndex] = true; }
                else if (action.Kind == RuleActionKind.UnlockUpgrade)
                { Mark(SimulationChangeKind.Upgrade, action.TargetIndex); upgradeUnlocked[action.TargetIndex] = true; }
            }
            foreach (var entry in scheduled) scheduler.Schedule(entry.Time, entry.Priority, entry.Payload);
            if (changeModifiers) CommitModifierChange(proposedModifiers, outputs, clicks, targets);
            transaction.Values.Commit(); CollectModuleChanges(transaction.Values);
            foreach (var value in staged) RefreshRules(value.Key);
            foreach (var value in stagedFlags) foreach (int dependent in Content.RulesForFlag(value.Key)) RefreshRule(dependent);
            foreach (int flag in changedFlags) NotifyTrigger(RuleTriggerKind.FlagChanged, flag);
            FlushResourceTriggers();
            return CommandResult.Ok();
        }
    }
}
