using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public sealed partial class Simulation
    {
        private void ProcessRule(int index)
        {
            ruleTokens[index] = default;
            ruleArmed[index] = false;
            if (!EvaluateCondition(Content.RuleCondition(index), Time)) { RefreshRule(index); return; }
            var actions = Content.RuleActions(index);
            var staged = new SortedDictionary<int, BigDouble>();
            var earned = new SortedDictionary<int, BigDouble>();
            var stagedFlags = new SortedDictionary<int, bool>();
            var proposedModifiers = (bool[])activeModifiers.Clone();
            bool changeModifiers = false;
            var scheduled = new List<ScheduledEvent<SimulationEvent>>();
            foreach (var action in actions)
                if (action.Kind == RuleActionKind.SpendResource)
                {
                    BigDouble balance = staged.TryGetValue(action.TargetIndex, out var previous) ? previous : balances[action.TargetIndex];
                    if (balance < action.Amount) { LastEventResult = CommandResult.Fail("Rule cannot afford its complete transaction."); RefreshRule(index); return; }
                    staged[action.TargetIndex] = Number.AddSigned(balance, -action.Amount);
                }
            foreach (var action in actions)
            {
                int target = action.TargetIndex;
                switch (action.Kind)
                {
                    case RuleActionKind.GrantResource:
                        BigDouble before = staged.TryGetValue(target, out var previous) ? previous : balances[target];
                        BigDouble after = GrantedBalance(target, before, action.Amount);
                        BigDouble oldEarned = earned.TryGetValue(target, out var e) ? e : lifetime[target];
                        earned[target] = Number.Add(oldEarned, Number.AddSigned(after, -before)); staged[target] = after; break;
                    case RuleActionKind.SetFlag: stagedFlags[target] = action.Enabled; break;
                    case RuleActionKind.SetModifier: proposedModifiers[target] = action.Enabled; changeModifiers = true; break;
                    case RuleActionKind.ScheduleGrantResource:
                        double due = Time + action.Delay;
                        if (action.Delay > 0 && due <= Time) due = NextTime(Time);
                        if (!Finite(due)) throw new ArithmeticException("Scheduled rule action exceeds the finite time horizon.");
                        scheduled.Add(new ScheduledEvent<SimulationEvent>(due, action.Priority, 0,
                            new SimulationEvent(SimulationEventKind.GrantResource, target, action.Amount))); break;
                }
            }
            Dictionary<int, StatModifiers[]> outputs = null;
            SortedDictionary<int, StatModifiers> clicks = null;
            SortedSet<int> targets = null;
            if (changeModifiers && !PrepareModifierChange(proposedModifiers, out outputs, out clicks, out targets))
                throw new ArithmeticException("Rule modifiers overflow production.");
            var rule = Content.Rules[index];
            ruleFired[index] = true;
            double eligible = Time + rule.Cooldown;
            if (rule.Cooldown > 0 && eligible <= Time) eligible = NextTime(Time);
            if (!Finite(eligible)) { ruleDisabled[index] = true; eligible = Time; }
            ruleEligible[index] = eligible;
            foreach (var value in staged) balances[value.Key] = value.Value;
            foreach (var value in earned) lifetime[value.Key] = value.Value;
            var changedFlags = new List<int>();
            foreach (var value in stagedFlags)
            { if (flags[value.Key] != value.Value) changedFlags.Add(value.Key); flags[value.Key] = value.Value; }
            foreach (var action in actions)
            {
                if (action.Kind == RuleActionKind.UnlockProducer) producerUnlocked[action.TargetIndex] = true;
                else if (action.Kind == RuleActionKind.UnlockUpgrade) upgradeUnlocked[action.TargetIndex] = true;
            }
            foreach (var entry in scheduled) scheduler.Schedule(entry.Time, entry.Priority, entry.Payload);
            if (changeModifiers) CommitModifierChange(proposedModifiers, outputs, clicks, targets);
            foreach (var value in staged) RefreshRules(value.Key);
            foreach (var value in stagedFlags) foreach (int dependent in Content.RulesForFlag(value.Key)) RefreshRule(dependent);
            foreach (int flag in changedFlags) NotifyTrigger(RuleTriggerKind.FlagChanged, flag);
            RefreshRule(index); LastEventResult = CommandResult.Ok();
        }
    }
}
