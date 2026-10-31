using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework.Progression
{
    public sealed partial class Simulation
    {
        private bool[] flags;
        public bool GetFlag(string id) => flags[Content.FlagIndex(id)];
        private bool EvaluateCondition(IndexedCondition condition, double atTime)
        {
            switch (condition.Kind)
            {
                case ConditionKind.Requirement:
                    var requirement = condition.Requirement;
                    BigDouble value = RequirementValue(requirement);
                    if (value >= requirement.Amount) return true;
                    if (atTime > Time && (requirement.Kind == RequirementKind.Resource || requirement.Kind == RequirementKind.LifetimeResource))
                    {
                        int index = requirement.TargetIndex;
                        if (rates[index] <= BigDouble.Zero) return false;
                        BigDouble needed = Number.AddSigned(requirement.Amount, -value);
                        var cap = Content.Resources[index].Cap;
                        if (cap.HasValue && needed > Number.AddSigned(cap.Value, -balances[index])) return false;
                        double duration = Number.RatioToDouble(needed, rates[index]);
                        return atTime - Time >= duration;
                    }
                    return false;
                case ConditionKind.Flag: return flags[condition.FlagIndex] == condition.Expected;
                case ConditionKind.And:
                    foreach (var child in condition.Children) if (!EvaluateCondition(child, atTime)) return false;
                    return true;
                case ConditionKind.Or:
                    foreach (var child in condition.Children) if (EvaluateCondition(child, atTime)) return true;
                    return false;
                case ConditionKind.Not: return !EvaluateCondition(condition.Children[0], atTime);
                default: throw new InvalidOperationException("Unknown condition.");
            }
        }
        private bool TryConditionTime(int index, out double due)
        {
            due = Math.Max(Time, ruleEligible[index]);
            if (Content.Rules[index].Trigger.Kind == RuleTriggerKind.Time) due = Math.Max(due, Content.Rules[index].Trigger.Time);
            if (!Finite(due)) return false;
            var condition = Content.RuleCondition(index);
            if (EvaluateCondition(condition, due)) return true;
            var candidates = new SortedSet<double>();
            foreach (var requirement in Content.RuleConditions(index))
            {
                if (requirement.Kind != RequirementKind.Resource && requirement.Kind != RequirementKind.LifetimeResource) continue;
                int resource = requirement.TargetIndex;
                BigDouble value = RequirementValue(requirement);
                if (value >= requirement.Amount || rates[resource] <= BigDouble.Zero) continue;
                BigDouble needed = Number.AddSigned(requirement.Amount, -value);
                var cap = Content.Resources[resource].Cap;
                if (cap.HasValue && needed > Number.AddSigned(cap.Value, -balances[resource])) continue;
                double delta = Number.RatioToDouble(needed, rates[resource]);
                double crossing = Time + delta;
                if (crossing <= Time) crossing = NextTime(Time);
                if (Finite(crossing) && crossing >= due)
                {
                    candidates.Add(crossing);
                    double following = NextTime(crossing);
                    if (Finite(following)) candidates.Add(following);
                }
            }
            foreach (double candidate in candidates)
                if (EvaluateCondition(condition, candidate)) { due = candidate; return true; }
            return false;
        }
    }
}
