using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BreakInfinity;
using IncrementalFramework;

namespace IdleHands.UnityIntegration
{
    public sealed class ContentValidationException : ArgumentException
    {
        public IReadOnlyList<string> Errors { get; }
        public ContentValidationException(List<string> errors)
            : base("Content validation failed:\n" + string.Join("\n", errors))
        { Errors = errors.AsReadOnly(); }
    }

    public sealed class CompiledGame
    {
        public ContentDatabase Content { get; }
        public string ClickResourceId { get; }
        public BigDouble ClickAmount { get; }
        public double MaximumOfflineSeconds { get; }
        internal CompiledGame(ContentDatabase content, GameContentAsset source)
        {
            Content = content;
            ClickResourceId = source.clickResource.id;
            ClickAmount = ContentCompiler.ReadNumber(source.clickAmount);
            MaximumOfflineSeconds = source.maximumOfflineSeconds;
        }
    }

    public static class ContentCompiler
    {
        public static CompiledGame Compile(GameContentAsset source)
        {
            var errors = Validate(source);
            if (errors.Count != 0) throw new ContentValidationException(errors);
            var resources = source.resources.Select(x => new ResourceDefinition(
                x.id, ReadNumber(x.startingAmount), resetOnPrestige: x.resetOnPrestige)).ToArray();
            var producers = source.producers.Select(x => new ProducerDefinition(
                x.id, Amounts(x.costs), Amounts(x.productionPerSecond),
                double.Parse(x.costGrowth, CultureInfo.InvariantCulture), initiallyUnlocked: x.initiallyUnlocked,
                prerequisites: Conditions(x.prerequisites))).ToArray();
            var upgrades = source.upgrades.Select(x => new UpgradeDefinition(
                x.id, Amounts(x.costs), x.modifiers.Select((m, i) => new ModifierDefinition(
                    x.id + ":modifier:" + i, m.producerId, ReadNumber(m.value),
                    m.additive ? ModifierKind.Add : ModifierKind.Multiply)).ToArray(),
                permanent: x.permanent, initiallyUnlocked: x.initiallyUnlocked,
                prerequisites: Conditions(x.prerequisites))).ToArray();
            var rules = source.rules.Select(x => new RuleDefinition(
                x.id, (x.triggerResource != null ? x.triggerResource : source.clickResource).id,
                ReadNumber(x.threshold), x.actions.Select(Action).ToArray(),
                repeat: x.repeat, cooldown: x.cooldownSeconds, resetOnPrestige: x.resetOnPrestige,
                conditions: Conditions(x.conditions))).ToArray();
            var prestige = source.prestigeResource == null ? null : new PrestigeDefinition(
                source.prestigeResource.id, ReadNumber(source.prestigeThreshold),
                BigDouble.One + ReadNumber(source.prestigeProductionBonus));
            return new CompiledGame(new ContentDatabase(resources, producers, upgrades, rules, prestige), source);
        }

        public static List<string> Validate(GameContentAsset source)
        {
            var errors = new List<string>();
            if (source == null) { errors.Add("Game content asset is missing."); return errors; }
            var resources = Index(source.resources, x => x.id, "resources", errors);
            var producers = Index(source.producers, x => x.id, "producers", errors);
            var upgrades = Index(source.upgrades, x => x.id, "upgrades", errors);
            Index(source.rules, x => x.id, "rules", errors);
            if (resources.Count == 0) errors.Add("At least one resource is required.");
            CheckResource(source.clickResource, resources, "clickResource", errors);
            Parse(source.clickAmount, "clickAmount", errors, true);
            if (float.IsNaN(source.maximumOfflineSeconds) || float.IsInfinity(source.maximumOfflineSeconds)
                || source.maximumOfflineSeconds < 0) errors.Add("maximumOfflineSeconds must be finite and non-negative.");
            if (source.prestigeResource != null)
            {
                CheckResource(source.prestigeResource, resources, "prestigeResource", errors);
                Parse(source.prestigeThreshold, "prestigeThreshold", errors, true);
                Parse(source.prestigeProductionBonus, "prestigeProductionBonus", errors);
            }
            foreach (var x in source.resources ?? Array.Empty<ResourceAsset>())
                if (x != null) Parse(x.startingAmount, x.name + ".startingAmount", errors);
            foreach (var x in source.producers ?? Array.Empty<ProducerAsset>())
            {
                if (x == null) continue;
                CheckAmounts(x.costs, resources, x.name + ".costs", errors);
                CheckAmounts(x.productionPerSecond, resources, x.name + ".productionPerSecond", errors);
                double growth;
                if (!double.TryParse(x.costGrowth, NumberStyles.Float, CultureInfo.InvariantCulture, out growth)
                    || double.IsNaN(growth) || double.IsInfinity(growth) || growth < 1)
                    errors.Add(x.name + ".costGrowth must be a finite number >= 1.");
                CheckConditions(x.prerequisites, resources, producers, upgrades, x.name + ".prerequisites", errors);
            }
            foreach (var x in source.upgrades ?? Array.Empty<UpgradeAsset>())
            {
                if (x == null) continue;
                CheckAmounts(x.costs, resources, x.name + ".costs", errors);
                CheckConditions(x.prerequisites, resources, producers, upgrades, x.name + ".prerequisites", errors);
                if (x.modifiers == null) { errors.Add(x.name + ".modifiers cannot be null."); continue; }
                for (int i = 0; i < x.modifiers.Length; i++)
                {
                    var modifier = x.modifiers[i];
                    string path = x.name + ".modifiers[" + i + "]";
                    if (modifier == null) { errors.Add(path + " is missing."); continue; }
                    if (string.IsNullOrEmpty(modifier.producerId) || !producers.ContainsKey(modifier.producerId))
                        errors.Add(path + " references unknown producer '" + modifier.producerId + "'.");
                    Parse(modifier.value, path + ".value", errors);
                }
            }
            foreach (var x in source.rules ?? Array.Empty<RuleAsset>())
            {
                if (x == null) continue;
                if (x.triggerResource != null) CheckResource(x.triggerResource, resources, x.name + ".triggerResource", errors);
                Parse(x.threshold, x.name + ".threshold", errors);
                CheckConditions(x.conditions, resources, producers, upgrades, x.name + ".conditions", errors);
                if (float.IsNaN(x.cooldownSeconds) || float.IsInfinity(x.cooldownSeconds) || x.cooldownSeconds < 0
                    || (x.repeat && x.cooldownSeconds <= 0))
                    errors.Add(x.name + ".cooldownSeconds must be finite, non-negative, and positive for repeating rules.");
                if (x.actions == null) { errors.Add(x.name + ".actions cannot be null."); continue; }
                for (int i = 0; i < x.actions.Length; i++)
                {
                    var action = x.actions[i];
                    string path = x.name + ".actions[" + i + "]";
                    if (action == null) { errors.Add(path + " is missing."); continue; }
                    bool exists = false;
                    if (!string.IsNullOrEmpty(action.targetId))
                    {
                        switch (action.kind)
                        {
                            case RuleActionKind.GrantResource:
                                exists = resources.ContainsKey(action.targetId);
                                Parse(action.amount, path + ".amount", errors);
                                break;
                            case RuleActionKind.UnlockProducer: exists = producers.ContainsKey(action.targetId); break;
                            case RuleActionKind.UnlockUpgrade: exists = upgrades.ContainsKey(action.targetId); break;
                        }
                    }
                    if (!exists) errors.Add(path + " has an unknown action kind or target ID '" + action.targetId + "'.");
                }
            }
            return errors;
        }

        private static Dictionary<string, T> Index<T>(T[] values, Func<T, string> getId, string path, List<string> errors)
            where T : UnityEngine.Object
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            if (values == null) { errors.Add(path + " cannot be null."); return result; }
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null) { errors.Add(path + "[" + i + "] is missing."); continue; }
                string id = getId(values[i]);
                if (string.IsNullOrWhiteSpace(id)) errors.Add(path + "[" + i + "] requires a nonempty ID.");
                else if (id != id.Trim()) errors.Add(path + "[" + i + "] ID has surrounding whitespace.");
                else if (result.ContainsKey(id)) errors.Add(path + " has duplicate ID '" + id + "'.");
                else result.Add(id, values[i]);
            }
            return result;
        }

        private static void CheckResource(ResourceAsset resource, Dictionary<string, ResourceAsset> resources,
            string path, List<string> errors)
        {
            ResourceAsset registered;
            if (resource == null || string.IsNullOrEmpty(resource.id)
                || !resources.TryGetValue(resource.id, out registered) || registered != resource)
                errors.Add(path + " must reference a resource registered in this game.");
        }

        private static void CheckAmounts(ResourceAmount[] amounts, Dictionary<string, ResourceAsset> resources,
            string path, List<string> errors)
        {
            if (amounts == null) { errors.Add(path + " cannot be null."); return; }
            var seen = new HashSet<ResourceAsset>();
            for (int i = 0; i < amounts.Length; i++)
            {
                string item = path + "[" + i + "]";
                if (amounts[i] == null) { errors.Add(item + " is missing."); continue; }
                CheckResource(amounts[i].resource, resources, item, errors);
                if (amounts[i].resource != null && !seen.Add(amounts[i].resource))
                    errors.Add(item + " repeats a resource; combine amounts into one entry.");
                Parse(amounts[i].amount, item + ".amount", errors);
            }
        }

        private static void CheckConditions(Requirement[] values, Dictionary<string, ResourceAsset> resources,
            Dictionary<string, ProducerAsset> producers, Dictionary<string, UpgradeAsset> upgrades,
            string path, List<string> errors)
        {
            if (values == null) { errors.Add(path + " cannot be null."); return; }
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                string item = path + "[" + i + "]";
                if (value == null) { errors.Add(item + " is missing."); continue; }
                Parse(value.amount, item + ".amount", errors);
                bool exists;
                switch (value.kind)
                {
                    case RequirementKind.Resource:
                    case RequirementKind.LifetimeResource: exists = value.targetId != null && resources.ContainsKey(value.targetId); break;
                    case RequirementKind.Producer: exists = value.targetId != null && producers.ContainsKey(value.targetId); break;
                    case RequirementKind.Upgrade: exists = value.targetId != null && upgrades.ContainsKey(value.targetId); break;
                    case RequirementKind.Prestige: exists = true; break;
                    default: exists = false; break;
                }
                if (!exists) errors.Add(item + " has an unknown kind or target ID '" + value.targetId + "'.");
            }
        }

        private static void Parse(string text, string path, List<string> errors, bool positive = false)
        {
            try
            {
                if (text == null || !Regex.IsMatch(text, @"^[+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$"))
                    throw new FormatException();
                var value = ReadNumber(text);
                if (!Number.IsFinite(value) || value < BigDouble.Zero || (positive && value <= BigDouble.Zero))
                    throw new FormatException();
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            { errors.Add(path + " must be a finite " + (positive ? "positive" : "non-negative") + " invariant number (e.g. 1.25e30)."); }
        }

        private static IncrementalFramework.ResourceAmount[] Amounts(ResourceAmount[] values)
        {
            return values.Select(x => new IncrementalFramework.ResourceAmount(x.resource.id, ReadNumber(x.amount))).ToArray();
        }

        private static ConditionDefinition[] Conditions(Requirement[] values)
        {
            return values.Select(x => new ConditionDefinition((ConditionKind)(int)x.kind, x.targetId, ReadNumber(x.amount))).ToArray();
        }

        internal static BigDouble ReadNumber(string text) { return BigDouble.Parse(text.Replace('E', 'e')); }

        private static RuleAction Action(RuleActionEntry entry)
        {
            switch (entry.kind)
            {
                case RuleActionKind.GrantResource: return new GrantResourceAction(entry.targetId, ReadNumber(entry.amount));
                case RuleActionKind.UnlockProducer: return new UnlockProducerAction(entry.targetId);
                case RuleActionKind.UnlockUpgrade: return new UnlockUpgradeAction(entry.targetId);
                default: throw new ArgumentOutOfRangeException(nameof(entry.kind));
            }
        }
    }
}
