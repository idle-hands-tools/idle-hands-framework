using System;

namespace IncrementalFramework
{
    public sealed partial class Simulation
    {
        private bool[] ruleArmed;
        private static bool ContinuousTrigger(RuleTriggerKind kind) =>
            kind == RuleTriggerKind.Automatic || kind == RuleTriggerKind.ResourceThreshold || kind == RuleTriggerKind.Time;
        private void NotifyTrigger(RuleTriggerKind kind, int target = -1)
        {
            foreach (int rule in Content.RulesForTrigger(kind, target)) ArmRule(rule);
            if (target >= 0) foreach (int rule in Content.RulesForTrigger(kind)) ArmRule(rule);
        }
        private void ArmRule(int index)
        {
            if (ruleDisabled[index] || (ruleFired[index] && !Content.Rules[index].Repeat)) return;
            if (!EvaluateCondition(Content.RuleCondition(index), Time)) return;
            ruleArmed[index] = true; RefreshRule(index);
        }
        private CommandResult TriggerManualRule(string id)
        {
            if (id == null || !ruleIndices.TryGetValue(id, out int index)) return CommandResult.Fail("Unknown rule id.");
            if (Content.Rules[index].Trigger.Kind != RuleTriggerKind.Manual) return CommandResult.Fail("Rule does not use a manual trigger.");
            ArmRule(index); return CommandResult.Ok();
        }
    }
}
