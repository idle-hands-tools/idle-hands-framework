using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    /// <summary>Validated, immutable definitions with dense ordinal indices and reverse dependency indices.</summary>
    public sealed class ContentDatabase
    {
        public IReadOnlyList<ResourceDefinition> Resources { get; }
        public IReadOnlyList<ProducerDefinition> Producers { get; }
        public IReadOnlyList<UpgradeDefinition> Upgrades { get; }
        public IReadOnlyList<RuleDefinition> Rules { get; }
        public PrestigeDefinition Prestige { get; }
        public IReadOnlyList<FlagDefinition> Flags { get; }
        public IReadOnlyList<ModifierDefinition> Modifiers { get; }
        private readonly Dictionary<string, int> resources;
        private readonly Dictionary<string, int> producers;
        private readonly Dictionary<string, int> upgrades;
        private readonly Dictionary<string, int> flags;
        private readonly Dictionary<string, int> modifiers;
        private readonly IReadOnlyList<IndexedModifier> indexedModifiers;
        private readonly IReadOnlyList<int>[] modifierProducers;
        private readonly IReadOnlyList<int>[] resourceRules;
        private readonly IReadOnlyList<int>[] producerUpgrades;
        private readonly IReadOnlyList<int>[] resourceProducers;
        private readonly IReadOnlyList<IndexedAmount>[] producerCosts;
        private readonly IReadOnlyList<IndexedAmount>[] producerOutputs;
        private readonly IReadOnlyList<IndexedAmount>[] upgradeCosts;
        private readonly IReadOnlyList<IndexedModifier>[] upgradeModifiers;
        private readonly int[] ruleResources;
        private readonly IReadOnlyList<IndexedRuleAction>[] ruleActions;
        private readonly IReadOnlyList<IndexedRequirement>[] producerPrerequisites;
        private readonly IReadOnlyList<IndexedRequirement>[] upgradePrerequisites;
        private readonly IndexedCondition[] producerPrerequisiteConditions;
        private readonly IndexedCondition[] upgradePrerequisiteConditions;
        private readonly IReadOnlyList<IndexedRequirement>[] ruleConditions;
        private readonly IReadOnlyList<int>[] producerRules;
        private readonly IReadOnlyList<int>[] upgradeRules;
        private readonly IReadOnlyList<int> prestigeRules;
        private readonly IReadOnlyList<int>[] resourceUpgrades;
        private readonly IReadOnlyList<int>[] clickUpgrades;
        private readonly IReadOnlyList<int>[] affectedProducers;
        private readonly IndexedCondition[] conditions;
        private readonly IReadOnlyList<int>[] flagRules;
        private readonly Dictionary<(RuleTriggerKind, int), IReadOnlyList<int>> triggerRules = new Dictionary<(RuleTriggerKind, int), IReadOnlyList<int>>();
        public ContentDatabase(ResourceDefinition[] resources, ProducerDefinition[] producers = null,
            UpgradeDefinition[] upgrades = null, RuleDefinition[] rules = null, PrestigeDefinition prestige = null, FlagDefinition[] flags = null,
            ModifierDefinition[] modifiers = null)
        {
            Resources = Number.Copy(resources); Producers = Number.Copy(producers);
            Upgrades = Number.Copy(upgrades); Rules = Number.Copy(rules); Prestige = prestige;
            Flags = Number.Copy(flags); this.flags = Index(Flags, x => x.Id);
            this.resources = Index(Resources, x => x.Id); this.producers = Index(Producers, x => x.Id);
            this.upgrades = Index(Upgrades, x => x.Id); Index(Rules, x => x.Id);
            var modifierDefinitions = new List<ModifierDefinition>(modifiers ?? Array.Empty<ModifierDefinition>());
            var dynamicDefinitions = new Dictionary<string, ModifierDefinition>(StringComparer.Ordinal);
            foreach (var modifier in modifierDefinitions)
            {
                if (modifier == null || dynamicDefinitions.ContainsKey(modifier.Id)) throw new ArgumentException("Null or duplicate modifier.");
                dynamicDefinitions.Add(modifier.Id, modifier);
            }
            foreach (var rule in Rules)
                foreach (var action in rule.Actions)
                    if (action is SetModifierAction set && set.Definition != null)
                    {
                        if (dynamicDefinitions.TryGetValue(set.ModifierId, out var existing))
                        {
                            if (existing.Target != set.Definition.Target || existing.Kind != set.Definition.Kind ||
                                existing.Value != set.Definition.Value || existing.Priority != set.Definition.Priority ||
                                !string.Equals(existing.TargetId, set.Definition.TargetId, StringComparison.Ordinal))
                                throw new ArgumentException("Conflicting dynamic modifier definitions.");
                        }
                        else { dynamicDefinitions.Add(set.ModifierId, set.Definition); modifierDefinitions.Add(set.Definition); }
                    }
            Modifiers = modifierDefinitions.AsReadOnly(); this.modifiers = Index(Modifiers, x => x.Id);
            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            var reverseRules = Lists(Resources.Count);
            var reverseUpgrades = Lists(Producers.Count);
            var reverseProducers = Lists(Resources.Count);
            var reverseProducerRules = Lists(Producers.Count);
            var reverseUpgradeRules = Lists(Upgrades.Count);
            var prestigeRuleList = new List<int>();
            var reverseResourceUpgrades = Lists(Resources.Count);
            var reverseClickUpgrades = Lists(Resources.Count);
            var upgradeProducers = Lists(Upgrades.Count);
            var reverseFlagRules = Lists(Flags.Count); conditions = new IndexedCondition[Rules.Count];
            var triggers = new Dictionary<(RuleTriggerKind, int), List<int>>();
            producerPrerequisites = new IReadOnlyList<IndexedRequirement>[Producers.Count];
            upgradePrerequisites = new IReadOnlyList<IndexedRequirement>[Upgrades.Count];
            producerPrerequisiteConditions = new IndexedCondition[Producers.Count];
            upgradePrerequisiteConditions = new IndexedCondition[Upgrades.Count];
            ruleConditions = new IReadOnlyList<IndexedRequirement>[Rules.Count];
            producerCosts = new IReadOnlyList<IndexedAmount>[Producers.Count];
            producerOutputs = new IReadOnlyList<IndexedAmount>[Producers.Count];
            upgradeCosts = new IReadOnlyList<IndexedAmount>[Upgrades.Count];
            upgradeModifiers = new IReadOnlyList<IndexedModifier>[Upgrades.Count];
            ruleResources = new int[Rules.Count]; ruleActions = new IReadOnlyList<IndexedRuleAction>[Rules.Count];
            for (int i = 0; i < Producers.Count; i++)
            {
                producerCosts[i] = Compile(Producers[i].Costs); producerOutputs[i] = Compile(Producers[i].Production);
                producerPrerequisites[i] = CompileRequirements(Producers[i].Prerequisites);
                producerPrerequisiteConditions[i] = CompileCondition(Producers[i].PrerequisiteCondition, new List<IndexedRequirement>(), new List<int>(), 0);
                foreach (var output in producerOutputs[i]) reverseProducers[output.ResourceIndex].Add(i);
            }
            for (int i = 0; i < Upgrades.Count; i++)
            {
                upgradeCosts[i] = Compile(Upgrades[i].Costs);
                upgradePrerequisites[i] = CompileRequirements(Upgrades[i].Prerequisites);
                upgradePrerequisiteConditions[i] = CompileCondition(Upgrades[i].PrerequisiteCondition, new List<IndexedRequirement>(), new List<int>(), 0);
                var compiledModifiers = new List<IndexedModifier>();
                foreach (var modifier in Upgrades[i].Modifiers)
                {
                    if (modifier == null || !modifierIds.Add(modifier.Id)) throw new ArgumentException("Null or duplicate modifier.");
                    int target = -1;
                    if (modifier.Target == ModifierTarget.ProducerProduction)
                    {
                        target = ProducerIndex(modifier.TargetId);
                        AddUnique(reverseUpgrades[target], i); AddUnique(upgradeProducers[i], target);
                    }
                    else if (modifier.Target == ModifierTarget.GlobalProduction)
                        for (int p = 0; p < Producers.Count; p++)
                        { AddUnique(reverseUpgrades[p], i); AddUnique(upgradeProducers[i], p); }
                    else if (modifier.Target == ModifierTarget.ResourceProduction)
                    {
                        target = ResourceIndex(modifier.TargetId); AddUnique(reverseResourceUpgrades[target], i);
                        foreach (int p in reverseProducers[target]) { AddUnique(upgradeProducers[i], p); AddUnique(reverseUpgrades[p], i); }
                    }
                    else if (modifier.Target == ModifierTarget.ResourceCategoryProduction)
                    {
                        bool found = false;
                        for (int r = 0; r < Resources.Count; r++)
                            if (string.Equals(Resources[r].Category, modifier.TargetId, StringComparison.Ordinal))
                            {
                                found = true; AddUnique(reverseResourceUpgrades[r], i);
                                foreach (int p in reverseProducers[r]) { AddUnique(upgradeProducers[i], p); AddUnique(reverseUpgrades[p], i); }
                            }
                        if (!found) throw new ArgumentException("Unknown resource category: " + modifier.TargetId);
                    }
                    else if (modifier.Target == ModifierTarget.ProducerCategoryProduction)
                    {
                        bool found = false;
                        for (int p = 0; p < Producers.Count; p++)
                            if (string.Equals(Producers[p].Category, modifier.TargetId, StringComparison.Ordinal))
                            { found = true; AddUnique(upgradeProducers[i], p); AddUnique(reverseUpgrades[p], i); }
                        if (!found) throw new ArgumentException("Unknown producer category: " + modifier.TargetId);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(modifier.TargetId))
                        { target = ResourceIndex(modifier.TargetId); AddUnique(reverseClickUpgrades[target], i); }
                        else for (int r = 0; r < Resources.Count; r++) AddUnique(reverseClickUpgrades[r], i);
                    }
                    compiledModifiers.Add(new IndexedModifier(target, modifier.Kind, modifier.Value, modifier.Target, modifier.Id, modifier.TargetId, modifier.Priority));
                }
                upgradeModifiers[i] = compiledModifiers.AsReadOnly();
            }
            for (int i = 0; i < Rules.Count; i++)
            {
                var trigger = Rules[i].Trigger;
                int triggerTarget = -1;
                if (!string.IsNullOrEmpty(trigger.TargetId))
                {
                    if (trigger.Kind == RuleTriggerKind.ProducerPurchased) triggerTarget = ProducerIndex(trigger.TargetId);
                    else if (trigger.Kind == RuleTriggerKind.UpgradePurchased) triggerTarget = UpgradeIndex(trigger.TargetId);
                    else if (trigger.Kind == RuleTriggerKind.FlagChanged) triggerTarget = FlagIndex(trigger.TargetId);
                    else if (trigger.Kind == RuleTriggerKind.ResourceThreshold) triggerTarget = ResourceIndex(trigger.TargetId);
                    else throw new ArgumentException("This trigger has no content target.");
                }
                var triggerKey = (trigger.Kind, triggerTarget);
                if (!triggers.TryGetValue(triggerKey, out var triggered)) { triggered = new List<int>(); triggers.Add(triggerKey, triggered); }
                triggered.Add(i);
                var leaves = new List<IndexedRequirement>();
                var usedFlags = new List<int>();
                conditions[i] = CompileCondition(Rules[i].Condition, leaves, usedFlags, 0);
                ruleConditions[i] = leaves.AsReadOnly();
                foreach (int flag in usedFlags) AddUnique(reverseFlagRules[flag], i);
                ruleResources[i] = -1;
                foreach (var condition in ruleConditions[i])
                {
                    if (condition.Kind == RequirementKind.Resource || condition.Kind == RequirementKind.LifetimeResource)
                    {
                        AddUnique(reverseRules[condition.TargetIndex], i);
                        if (ruleResources[i] < 0) ruleResources[i] = condition.TargetIndex;
                    }
                    else if (condition.Kind == RequirementKind.Producer) AddUnique(reverseProducerRules[condition.TargetIndex], i);
                    else if (condition.Kind == RequirementKind.Upgrade) AddUnique(reverseUpgradeRules[condition.TargetIndex], i);
                    else AddUnique(prestigeRuleList, i);
                }
                var actions = new List<IndexedRuleAction>();
                var spendTotals = new SortedDictionary<int, BigDouble>();
                foreach (var action in Rules[i].Actions)
                {
                    if (action is GrantResourceAction grant) actions.Add(new IndexedRuleAction(RuleActionKind.GrantResource, ResourceIndex(grant.ResourceId), grant.Amount));
                    else if (action is UnlockProducerAction producer) actions.Add(new IndexedRuleAction(RuleActionKind.UnlockProducer, ProducerIndex(producer.ProducerId)));
                    else if (action is UnlockUpgradeAction upgrade) actions.Add(new IndexedRuleAction(RuleActionKind.UnlockUpgrade, UpgradeIndex(upgrade.UpgradeId)));
                    else if (action is SpendResourceAction spend)
                    {
                        int resource = ResourceIndex(spend.ResourceId); AddUnique(reverseRules[resource], i);
                        spendTotals.TryGetValue(resource, out var priorCost); spendTotals[resource] = Number.Add(priorCost, spend.Amount);
                        actions.Add(new IndexedRuleAction(RuleActionKind.SpendResource, resource, spend.Amount));
                    }
                    else if (action is SetFlagAction flag) actions.Add(new IndexedRuleAction(RuleActionKind.SetFlag, FlagIndex(flag.FlagId), enabled: flag.Value));
                    else if (action is ScheduleGrantResourceAction schedule) actions.Add(new IndexedRuleAction(RuleActionKind.ScheduleGrantResource,
                        ResourceIndex(schedule.ResourceId), schedule.Amount, delay: schedule.Delay, priority: schedule.Priority));
                    else if (action is SetModifierAction modifier) actions.Add(new IndexedRuleAction(RuleActionKind.SetModifier, ModifierIndex(modifier.ModifierId), enabled: modifier.Enabled));
                    else throw new ArgumentException("Unknown rule action.");
                }
                if (spendTotals.Count > 0)
                {
                    var combined = new List<IndexedCondition> { conditions[i] };
                    foreach (var cost in spendTotals)
                    {
                        var requirement = new IndexedRequirement(RequirementKind.Resource, cost.Key, cost.Value);
                        leaves.Add(requirement);
                        combined.Add(new IndexedCondition(ConditionKind.Requirement, requirement, -1, false, null));
                    }
                    conditions[i] = new IndexedCondition(ConditionKind.And, default, -1, false, combined.ToArray());
                }
                ruleActions[i] = actions.AsReadOnly();
            }
            if (Prestige != null)
            {
                ResourceIndex(Prestige.ResourceId);
                if (Prestige.UsesLifetimeReward) ResourceIndex(Prestige.RewardResourceId);
            }
            resourceRules = Freeze(reverseRules); producerUpgrades = Freeze(reverseUpgrades);
            resourceProducers = Freeze(reverseProducers);
            producerRules = Freeze(reverseProducerRules); upgradeRules = Freeze(reverseUpgradeRules);
            prestigeRules = prestigeRuleList.AsReadOnly();
            resourceUpgrades = Freeze(reverseResourceUpgrades); clickUpgrades = Freeze(reverseClickUpgrades);
            affectedProducers = Freeze(upgradeProducers);
            flagRules = Freeze(reverseFlagRules);
            var compiledDynamic = new List<IndexedModifier>();
            var dynamicProducers = Lists(Modifiers.Count);
            for (int i = 0; i < Modifiers.Count; i++)
            {
                var definition = Modifiers[i];
                if (!modifierIds.Add(definition.Id)) throw new ArgumentException("Duplicate modifier id.");
                int target = -1;
                if (definition.Target == ModifierTarget.ProducerProduction) target = ProducerIndex(definition.TargetId);
                else if (definition.Target == ModifierTarget.ResourceProduction || (definition.Target == ModifierTarget.Click && !string.IsNullOrEmpty(definition.TargetId)))
                    target = ResourceIndex(definition.TargetId);
                var modifier = new IndexedModifier(target, definition.Kind, definition.Value, definition.Target, definition.Id, definition.TargetId, definition.Priority);
                compiledDynamic.Add(modifier);
                for (int p = 0; p < Producers.Count; p++)
                    foreach (var output in ProducerOutputs(p))
                        if (Matches(modifier, p, output.ResourceIndex)) { AddUnique(dynamicProducers[i], p); break; }
                if ((definition.Target == ModifierTarget.ProducerCategoryProduction || definition.Target == ModifierTarget.ResourceCategoryProduction) && dynamicProducers[i].Count == 0)
                    throw new ArgumentException("Unknown or unused modifier category.");
            }
            indexedModifiers = compiledDynamic.AsReadOnly(); modifierProducers = Freeze(dynamicProducers);
            foreach (var trigger in triggers) triggerRules.Add(trigger.Key, trigger.Value.AsReadOnly());
        }
        public int ResourceIndex(string id) => Lookup(resources, id);
        public int ProducerIndex(string id) => Lookup(producers, id);
        public int UpgradeIndex(string id) => Lookup(upgrades, id);
        public int FlagIndex(string id) => Lookup(flags, id);
        public int ModifierIndex(string id) => Lookup(modifiers, id);
        public IndexedModifier Modifier(int index) => indexedModifiers[index];
        public IReadOnlyList<int> ModifierAffectedProducers(int index) => modifierProducers[index];
        private bool Matches(IndexedModifier modifier, int producer, int resource)
        {
            switch (modifier.Target)
            {
                case ModifierTarget.GlobalProduction: return true;
                case ModifierTarget.ProducerProduction: return modifier.TargetIndex == producer;
                case ModifierTarget.ResourceProduction: return modifier.TargetIndex == resource;
                case ModifierTarget.ProducerCategoryProduction: return string.Equals(modifier.Category, Producers[producer].Category, StringComparison.Ordinal);
                case ModifierTarget.ResourceCategoryProduction: return string.Equals(modifier.Category, Resources[resource].Category, StringComparison.Ordinal);
                default: return false;
            }
        }
        public IReadOnlyList<int> RulesForResource(int index) => resourceRules[index];
        public IReadOnlyList<int> UpgradesForProducer(int index) => producerUpgrades[index];
        public IReadOnlyList<int> ProducersForResource(int index) => resourceProducers[index];
        public IReadOnlyList<IndexedAmount> ProducerCosts(int index) => producerCosts[index];
        public IReadOnlyList<IndexedAmount> ProducerOutputs(int index) => producerOutputs[index];
        public IReadOnlyList<IndexedAmount> UpgradeCosts(int index) => upgradeCosts[index];
        public IReadOnlyList<IndexedModifier> UpgradeModifiers(int index) => upgradeModifiers[index];
        public int RuleResource(int index) => ruleResources[index];
        public IReadOnlyList<IndexedRuleAction> RuleActions(int index) => ruleActions[index];
        public IReadOnlyList<IndexedRequirement> ProducerPrerequisites(int index) => producerPrerequisites[index];
        public IReadOnlyList<IndexedRequirement> UpgradePrerequisites(int index) => upgradePrerequisites[index];
        public IndexedCondition ProducerPrerequisiteCondition(int index) => producerPrerequisiteConditions[index];
        public IndexedCondition UpgradePrerequisiteCondition(int index) => upgradePrerequisiteConditions[index];
        public IReadOnlyList<IndexedRequirement> RuleConditions(int index) => ruleConditions[index];
        public IReadOnlyList<int> RulesForProducer(int index) => producerRules[index];
        public IReadOnlyList<int> RulesForUpgrade(int index) => upgradeRules[index];
        public IReadOnlyList<int> RulesForPrestige() => prestigeRules;
        public IReadOnlyList<int> UpgradesForResource(int index) => resourceUpgrades[index];
        public IReadOnlyList<int> ClickUpgradesForResource(int index) => clickUpgrades[index];
        public IReadOnlyList<int> AffectedProducers(int upgradeIndex) => affectedProducers[upgradeIndex];
        public IndexedCondition RuleCondition(int index) => conditions[index];
        public IReadOnlyList<int> RulesForFlag(int index) => flagRules[index];
        public IReadOnlyList<int> RulesForTrigger(RuleTriggerKind kind, int target = -1) =>
            triggerRules.TryGetValue((kind, target), out var rules) ? rules : Array.Empty<int>();
        private IndexedCondition CompileCondition(ConditionDefinition condition, List<IndexedRequirement> leaves, List<int> usedFlags, int depth)
        {
            if (condition == null || depth > 64) throw new ArgumentException("Invalid or excessively deep condition.");
            if (condition.Kind == ConditionKind.Requirement)
            {
                var requirement = CompileRequirements(new[] { condition.Requirement })[0];
                leaves.Add(requirement);
                return new IndexedCondition(condition.Kind, requirement, -1, false, null);
            }
            if (condition.Kind == ConditionKind.Flag)
            {
                int index = FlagIndex(condition.FlagId); AddUnique(usedFlags, index);
                return new IndexedCondition(condition.Kind, default, index, condition.Expected, null);
            }
            var children = new IndexedCondition[condition.Children.Count];
            for (int i = 0; i < children.Length; i++) children[i] = CompileCondition(condition.Children[i], leaves, usedFlags, depth + 1);
            return new IndexedCondition(condition.Kind, default, -1, false, children);
        }
        private static void AddUnique(List<int> values, int value) { if (!values.Contains(value)) values.Add(value); }
        private IReadOnlyList<IndexedRequirement> CompileRequirements(IReadOnlyList<RequirementDefinition> requirements)
        {
            var result = new List<IndexedRequirement>();
            foreach (var requirement in requirements)
            {
                if (requirement == null) throw new ArgumentException("Null requirement.");
                int index = -1;
                if (requirement.Kind == RequirementKind.Resource || requirement.Kind == RequirementKind.LifetimeResource) index = ResourceIndex(requirement.TargetId);
                else if (requirement.Kind == RequirementKind.Producer) index = ProducerIndex(requirement.TargetId);
                else if (requirement.Kind == RequirementKind.Upgrade) index = UpgradeIndex(requirement.TargetId);
                result.Add(new IndexedRequirement(requirement.Kind, index, requirement.Amount));
            }
            return result.AsReadOnly();
        }
        private static int Lookup(Dictionary<string, int> index, string id)
        {
            if (id == null || !index.TryGetValue(id, out var value)) throw new ArgumentException("Unknown content id: " + id);
            return value;
        }
        private IReadOnlyList<IndexedAmount> Compile(IReadOnlyList<ResourceAmount> amounts)
        {
            var totals = new SortedDictionary<int, BigDouble>();
            foreach (var amount in amounts)
            {
                if (amount == null) throw new ArgumentException("Null resource amount.");
                int index = ResourceIndex(amount.ResourceId);
                totals.TryGetValue(index, out var total);
                total = Number.Add(total, amount.Amount);
                Number.NonNegative(total, "compiled amount");
                totals[index] = total;
            }
            var result = new List<IndexedAmount>();
            foreach (var pair in totals) result.Add(new IndexedAmount(pair.Key, pair.Value));
            return result.AsReadOnly();
        }
        private static Dictionary<string, int> Index<T>(IReadOnlyList<T> values, Func<T, string> id)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || index.ContainsKey(id(values[i]))) throw new ArgumentException("Null or duplicate definition.");
                index.Add(id(values[i]), i);
            }
            return index;
        }
        private static List<int>[] Lists(int count)
        {
            var result = new List<int>[count]; for (int i = 0; i < count; i++) result[i] = new List<int>(); return result;
        }
        private static IReadOnlyList<int>[] Freeze(List<int>[] lists)
        {
            var result = new IReadOnlyList<int>[lists.Length];
            for (int i = 0; i < lists.Length; i++) result[i] = lists[i].AsReadOnly(); return result;
        }

    }

    public readonly struct IndexedAmount
    {
        public int ResourceIndex { get; }
        public BigDouble Amount { get; }
        public IndexedAmount(int resourceIndex, BigDouble amount) { ResourceIndex = resourceIndex; Amount = amount; }
    }

    public readonly struct IndexedModifier
    {
        public int ProducerIndex { get; }
        public ModifierKind Kind { get; }
        public BigDouble Value { get; }
        public ModifierTarget Target { get; }
        public int TargetIndex => ProducerIndex;
        public string Id { get; }
        public string Category { get; }
        public int Priority { get; }
        public IndexedModifier(int producerIndex, ModifierKind kind, BigDouble value, ModifierTarget target = ModifierTarget.ProducerProduction,
            string id = null, string category = null, int priority = 0)
        { ProducerIndex = producerIndex; Kind = kind; Value = value; Target = target; Id = id; Category = category; Priority = priority; }
    }

    public enum RuleActionKind { GrantResource, UnlockProducer, UnlockUpgrade, SpendResource, SetFlag, ScheduleGrantResource, SetModifier }
    public readonly struct IndexedRuleAction
    {
        public RuleActionKind Kind { get; }
        public int TargetIndex { get; }
        public BigDouble Amount { get; }
        public bool Enabled { get; }
        public double Delay { get; }
        public int Priority { get; }
        public IndexedRuleAction(RuleActionKind kind, int targetIndex, BigDouble amount = default, bool enabled = true, double delay = 0, int priority = 0)
        { Kind = kind; TargetIndex = targetIndex; Amount = amount; Enabled = enabled; Delay = delay; Priority = priority; }
    }
    public readonly struct IndexedRequirement
    {
        public RequirementKind Kind { get; }
        public int TargetIndex { get; }
        public BigDouble Amount { get; }
        public IndexedRequirement(RequirementKind kind, int targetIndex, BigDouble amount)
        { Kind = kind; TargetIndex = targetIndex; Amount = amount; }
    }
    public sealed class IndexedCondition
    {
        public ConditionKind Kind { get; }
        public IndexedRequirement Requirement { get; }
        public int FlagIndex { get; }
        public bool Expected { get; }
        public IReadOnlyList<IndexedCondition> Children { get; }
        public IndexedCondition(ConditionKind kind, IndexedRequirement requirement, int flagIndex, bool expected, IndexedCondition[] children)
        { Kind = kind; Requirement = requirement; FlagIndex = flagIndex; Expected = expected; Children = Array.AsReadOnly((IndexedCondition[])(children ?? Array.Empty<IndexedCondition>()).Clone()); }
    }
}
