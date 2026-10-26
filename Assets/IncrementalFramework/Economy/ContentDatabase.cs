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
        private readonly Dictionary<string, int> resources;
        private readonly Dictionary<string, int> producers;
        private readonly Dictionary<string, int> upgrades;
        private readonly IReadOnlyList<int>[] resourceRules;
        private readonly IReadOnlyList<int>[] producerUpgrades;
        private readonly IReadOnlyList<int>[] resourceProducers;
        private readonly IReadOnlyList<IndexedAmount>[] producerCosts;
        private readonly IReadOnlyList<IndexedAmount>[] producerOutputs;
        private readonly IReadOnlyList<IndexedAmount>[] upgradeCosts;
        private readonly IReadOnlyList<IndexedModifier>[] upgradeModifiers;
        private readonly int[] ruleResources;
        private readonly IReadOnlyList<IndexedRuleAction>[] ruleActions;
        public ContentDatabase(ResourceDefinition[] resources, ProducerDefinition[] producers = null,
            UpgradeDefinition[] upgrades = null, RuleDefinition[] rules = null, PrestigeDefinition prestige = null)
        {
            Resources = Number.Copy(resources); Producers = Number.Copy(producers);
            Upgrades = Number.Copy(upgrades); Rules = Number.Copy(rules); Prestige = prestige;
            this.resources = Index(Resources, x => x.Id); this.producers = Index(Producers, x => x.Id);
            this.upgrades = Index(Upgrades, x => x.Id); Index(Rules, x => x.Id);
            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            var reverseRules = Lists(Resources.Count);
            var reverseUpgrades = Lists(Producers.Count);
            var reverseProducers = Lists(Resources.Count);
            producerCosts = new IReadOnlyList<IndexedAmount>[Producers.Count];
            producerOutputs = new IReadOnlyList<IndexedAmount>[Producers.Count];
            upgradeCosts = new IReadOnlyList<IndexedAmount>[Upgrades.Count];
            upgradeModifiers = new IReadOnlyList<IndexedModifier>[Upgrades.Count];
            ruleResources = new int[Rules.Count]; ruleActions = new IReadOnlyList<IndexedRuleAction>[Rules.Count];
            for (int i = 0; i < Producers.Count; i++)
            {
                producerCosts[i] = Compile(Producers[i].Costs); producerOutputs[i] = Compile(Producers[i].Production);
                foreach (var output in producerOutputs[i]) reverseProducers[output.ResourceIndex].Add(i);
            }
            for (int i = 0; i < Upgrades.Count; i++)
            {
                upgradeCosts[i] = Compile(Upgrades[i].Costs);
                var compiledModifiers = new List<IndexedModifier>();
                foreach (var modifier in Upgrades[i].Modifiers)
                {
                    if (modifier == null || !modifierIds.Add(modifier.Id)) throw new ArgumentException("Null or duplicate modifier.");
                    int target = ProducerIndex(modifier.TargetProducerId);
                    compiledModifiers.Add(new IndexedModifier(target, modifier.Kind, modifier.Value));
                    if (!reverseUpgrades[target].Contains(i)) reverseUpgrades[target].Add(i);
                }
                upgradeModifiers[i] = compiledModifiers.AsReadOnly();
            }
            for (int i = 0; i < Rules.Count; i++)
            {
                ruleResources[i] = ResourceIndex(Rules[i].ResourceId);
                reverseRules[ruleResources[i]].Add(i);
                var actions = new List<IndexedRuleAction>();
                foreach (var action in Rules[i].Actions)
                {
                    if (action is GrantResourceAction grant) actions.Add(new IndexedRuleAction(RuleActionKind.GrantResource, ResourceIndex(grant.ResourceId), grant.Amount));
                    else if (action is UnlockProducerAction producer) actions.Add(new IndexedRuleAction(RuleActionKind.UnlockProducer, ProducerIndex(producer.ProducerId)));
                    else if (action is UnlockUpgradeAction upgrade) actions.Add(new IndexedRuleAction(RuleActionKind.UnlockUpgrade, UpgradeIndex(upgrade.UpgradeId)));
                    else throw new ArgumentException("Unknown rule action.");
                }
                ruleActions[i] = actions.AsReadOnly();
            }
            if (Prestige != null) ResourceIndex(Prestige.ResourceId);
            resourceRules = Freeze(reverseRules); producerUpgrades = Freeze(reverseUpgrades);
            resourceProducers = Freeze(reverseProducers);
        }
        public int ResourceIndex(string id) => Lookup(resources, id);
        public int ProducerIndex(string id) => Lookup(producers, id);
        public int UpgradeIndex(string id) => Lookup(upgrades, id);
        public IReadOnlyList<int> RulesForResource(int index) => resourceRules[index];
        public IReadOnlyList<int> UpgradesForProducer(int index) => producerUpgrades[index];
        public IReadOnlyList<int> ProducersForResource(int index) => resourceProducers[index];
        public IReadOnlyList<IndexedAmount> ProducerCosts(int index) => producerCosts[index];
        public IReadOnlyList<IndexedAmount> ProducerOutputs(int index) => producerOutputs[index];
        public IReadOnlyList<IndexedAmount> UpgradeCosts(int index) => upgradeCosts[index];
        public IReadOnlyList<IndexedModifier> UpgradeModifiers(int index) => upgradeModifiers[index];
        public int RuleResource(int index) => ruleResources[index];
        public IReadOnlyList<IndexedRuleAction> RuleActions(int index) => ruleActions[index];
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
                total += amount.Amount;
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
        public IndexedModifier(int producerIndex, ModifierKind kind, BigDouble value)
        { ProducerIndex = producerIndex; Kind = kind; Value = value; }
    }

    public enum RuleActionKind { GrantResource, UnlockProducer, UnlockUpgrade }
    public readonly struct IndexedRuleAction
    {
        public RuleActionKind Kind { get; }
        public int TargetIndex { get; }
        public BigDouble Amount { get; }
        public IndexedRuleAction(RuleActionKind kind, int targetIndex, BigDouble amount = default)
        { Kind = kind; TargetIndex = targetIndex; Amount = amount; }
    }
}
