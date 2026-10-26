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
        private readonly IReadOnlyDictionary<string, ResourceNumberFormatter> formatters;
        public string FormatResource(string resourceId, BigDouble value) { return formatters[resourceId].Format(value); }
        internal CompiledGame(ContentDatabase content, GameContentAsset source)
        {
            Content = content;
            ClickResourceId = source.clickResource.id;
            ClickAmount = ContentCompiler.ReadNumber(source.clickAmount);
            MaximumOfflineSeconds = source.maximumOfflineSeconds;
            formatters = source.resources.ToDictionary(x => x.id,
                x => new ResourceNumberFormatter(x.numberFormat, x.decimalPlaces, x.useGrouping));
        }
    }

    public static class ContentCompiler
    {
        public static CompiledGame Compile(GameContentAsset source)
        {
            var errors = Validate(source);
            if (errors.Count != 0) throw new ContentValidationException(errors);
            var resources = source.resources.Select(x => new ResourceDefinition(
                x.id, ReadNumber(x.startingAmount), x.hasCap ? (BigDouble?)ReadNumber(x.cap) : null,
                resetOnPrestige: x.resetOnPrestige, category: x.category,
                prestigePolicy: x.usePrestigePolicy ? (ResetPolicy?)x.prestigePolicy : null,
                prestigeTransform: ReadNumber(x.prestigeTransform), negativeAmountPolicy: x.negativePolicy)).ToArray();
            var producers = source.producers.Select(x => new ProducerDefinition(
                x.id, Amounts(x.costs), Amounts(x.productionPerSecond),
                double.Parse(x.costGrowth, CultureInfo.InvariantCulture), initiallyUnlocked: x.initiallyUnlocked,
                prerequisites: Conditions(x.prerequisites), category: x.category,
                prestigePolicy: x.usePrestigePolicy ? (ResetPolicy?)x.prestigePolicy : null,
                prestigeTransform: ReadNumber(x.prestigeTransform),
                prerequisiteCondition: OptionalBooleanTree(x.booleanNodes, x.rootConditionIndex))).ToArray();
            var upgrades = source.upgrades.Select(x => new UpgradeDefinition(
                x.id, Amounts(x.costs), x.modifiers.Select((m, i) => CompileModifier(x.id, i, m)).ToArray(),
                permanent: x.permanent, initiallyUnlocked: x.initiallyUnlocked,
                prerequisites: Conditions(x.prerequisites),
                prerequisiteCondition: OptionalBooleanTree(x.booleanNodes, x.rootConditionIndex))).ToArray();
            var rules = source.rules.Select(x => new RuleDefinition(
                x.id, RuleConditions(x, source.clickResource), x.actions.Select(CompileAction).ToArray(),
                repeat: x.repeat, cooldown: x.cooldownSeconds, resetOnPrestige: x.resetOnPrestige,
                trigger: new RuleTriggerDefinition(x.triggerKind, x.triggerTargetId, x.triggerTime,
                    x.filterScheduledEvent ? (ScheduledEventTriggerKind?)x.scheduledEventKind : null))).ToArray();
            PrestigeDefinition prestige = null;
            if (source.prestigeSourceResource != null)
            {
                prestige = source.prestigeMode == PrestigeMode.LifetimeCurrency
                    ? new PrestigeDefinition(source.prestigeSourceResource.id, source.prestigeRewardResource.id,
                        ReadNumber(source.prestigeThreshold), source.prestigeRewardExponent, ReadNumber(source.prestigeRewardMultiplier),
                        ReadNumber(source.prestigeProductionBonus), source.prestigeResetProducers, source.prestigeResetUpgrades)
                    : new PrestigeDefinition(source.prestigeSourceResource.id, ReadNumber(source.prestigeThreshold),
                        BigDouble.One + ReadNumber(source.prestigeProductionBonus), source.prestigeResetProducers, source.prestigeResetUpgrades);
            }
            var flags = source.flags.Select(x => new FlagDefinition(x.id, x.initial, x.resetOnPrestige)).ToArray();
            return new CompiledGame(new ContentDatabase(resources, producers, upgrades, rules, prestige, flags), source);
        }

        public static List<string> Validate(GameContentAsset source)
        {
            var errors = new List<string>();
            if (source == null) { errors.Add("Game content asset is missing."); return errors; }
            var resources = Index(source.resources, x => x.id, "resources", errors);
            var producers = Index(source.producers, x => x.id, "producers", errors);
            var upgrades = Index(source.upgrades, x => x.id, "upgrades", errors);
            var flags = Index(source.flags, x => x.id, "flags", errors);
            Index(source.rules, x => x.id, "rules", errors);
            if (resources.Count == 0) errors.Add("At least one resource is required.");
            CheckResource(source.clickResource, resources, "clickResource", errors);
            Parse(source.clickAmount, "clickAmount", errors, true);
            if (float.IsNaN(source.maximumOfflineSeconds) || float.IsInfinity(source.maximumOfflineSeconds)
                || source.maximumOfflineSeconds < 0) errors.Add("maximumOfflineSeconds must be finite and non-negative.");
            if (source.prestigeSourceResource != null)
            {
                if (!Enum.IsDefined(typeof(PrestigeMode), source.prestigeMode)) errors.Add("prestigeMode is invalid.");
                CheckResource(source.prestigeSourceResource, resources, "prestigeSourceResource", errors);
                Parse(source.prestigeThreshold, "prestigeThreshold", errors, true);
                Parse(source.prestigeProductionBonus, "prestigeProductionBonus", errors);
                if (source.prestigeMode == PrestigeMode.LifetimeCurrency)
                {
                    CheckResource(source.prestigeRewardResource, resources, "prestigeRewardResource", errors);
                    if (source.prestigeRewardResource == source.prestigeSourceResource)
                        errors.Add("prestigeRewardResource must differ from prestigeSourceResource.");
                    if (source.prestigeRewardResource != null && (source.prestigeRewardResource.usePrestigePolicy
                        ? source.prestigeRewardResource.prestigePolicy != ResetPolicy.Preserve : source.prestigeRewardResource.resetOnPrestige))
                        errors.Add("prestigeRewardResource must be preserved on prestige.");
                    if (double.IsNaN(source.prestigeRewardExponent) || double.IsInfinity(source.prestigeRewardExponent)
                        || source.prestigeRewardExponent <= 0) errors.Add("prestigeRewardExponent must be finite and positive.");
                    Parse(source.prestigeRewardMultiplier, "prestigeRewardMultiplier", errors, true);
                }
                else if (source.prestigeRewardResource != null)
                    errors.Add("BalanceMultiplier mode cannot award prestigeRewardResource; select LifetimeCurrency.");
            }
            else if (source.prestigeRewardResource != null)
                errors.Add("prestigeRewardResource requires a prestigeSourceResource.");
            foreach (var x in source.resources ?? Array.Empty<ResourceAsset>())
            {
                if (x == null) continue;
                int before = errors.Count;
                CheckCategory(x.category, x.name + ".category", errors);
                CheckResetPolicy(x.usePrestigePolicy, x.prestigePolicy, x.prestigeTransform, x.name, errors);
                Parse(x.startingAmount, x.name + ".startingAmount", errors,
                    allowNegative: x.negativePolicy != NegativeAmountPolicy.Reject);
                if (x.hasCap)
                {
                    Parse(x.cap, x.name + ".cap", errors);
                    if (errors.Count == before)
                    {
                        var initial = ReadNumber(x.startingAmount);
                        if (x.negativePolicy == NegativeAmountPolicy.ClampToZero) initial = BigDouble.Max(BigDouble.Zero, initial);
                        if (initial > ReadNumber(x.cap)) errors.Add(x.name + ".startingAmount exceeds its cap.");
                    }
                }
                if (!Enum.IsDefined(typeof(NegativeAmountPolicy), x.negativePolicy)) errors.Add(x.name + ".negativePolicy is invalid.");
                if (!Enum.IsDefined(typeof(ResourceNumberFormat), x.numberFormat))
                    errors.Add(x.name + ".numberFormat is invalid.");
                if (x.decimalPlaces < 0 || x.decimalPlaces > 12)
                    errors.Add(x.name + ".decimalPlaces must be in 0..12.");
                if (x.useGrouping && x.numberFormat != ResourceNumberFormat.Fixed && x.numberFormat != ResourceNumberFormat.Compact)
                    errors.Add(x.name + ".useGrouping requires Fixed or Compact format.");
            }
            foreach (var x in source.producers ?? Array.Empty<ProducerAsset>())
            {
                if (x == null) continue;
                CheckCategory(x.category, x.name + ".category", errors);
                CheckResetPolicy(x.usePrestigePolicy, x.prestigePolicy, x.prestigeTransform, x.name, errors);
                CheckAmounts(x.costs, resources, x.name + ".costs", errors);
                CheckAmounts(x.productionPerSecond, resources, x.name + ".productionPerSecond", errors);
                double growth;
                if (!double.TryParse(x.costGrowth, NumberStyles.Float, CultureInfo.InvariantCulture, out growth)
                    || double.IsNaN(growth) || double.IsInfinity(growth) || growth < 1)
                    errors.Add(x.name + ".costGrowth must be a finite number >= 1.");
                CheckConditions(x.prerequisites, resources, producers, upgrades, x.name + ".prerequisites", errors);
                CheckBooleanTree(x.name, x.booleanNodes, x.rootConditionIndex, resources, producers, upgrades, flags, errors);
            }
            foreach (var x in source.upgrades ?? Array.Empty<UpgradeAsset>())
            {
                if (x == null) continue;
                CheckAmounts(x.costs, resources, x.name + ".costs", errors);
                CheckConditions(x.prerequisites, resources, producers, upgrades, x.name + ".prerequisites", errors);
                CheckBooleanTree(x.name, x.booleanNodes, x.rootConditionIndex, resources, producers, upgrades, flags, errors);
                if (x.modifiers == null) { errors.Add(x.name + ".modifiers cannot be null."); continue; }
                for (int i = 0; i < x.modifiers.Length; i++)
                    CheckModifier(x.modifiers[i], resources, producers, x.name + ".modifiers[" + i + "]", errors);
            }
            var dynamicModifierIds = new HashSet<string>(StringComparer.Ordinal);
            var allModifierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var upgrade in source.upgrades ?? Array.Empty<UpgradeAsset>())
                if (upgrade != null && upgrade.modifiers != null)
                    for (int i = 0; i < upgrade.modifiers.Length; i++) allModifierIds.Add(upgrade.id + ":modifier:" + i);
            foreach (var rule in source.rules ?? Array.Empty<RuleAsset>())
                if (rule != null)
                    foreach (var action in rule.actions ?? Array.Empty<RuleActionEntry>())
                        if (action != null && action.kind == RuleActionKind.SetModifier && action.defineModifier)
                        {
                            if (string.IsNullOrWhiteSpace(action.modifierId) || action.modifierId != action.modifierId.Trim())
                                errors.Add(rule.name + ".actions requires a modifier ID without surrounding whitespace.");
                            else if (!allModifierIds.Add(action.modifierId))
                                errors.Add(rule.name + ".actions duplicates modifier ID '" + action.modifierId + "'.");
                            else dynamicModifierIds.Add(action.modifierId);
                        }
            foreach (var x in source.rules ?? Array.Empty<RuleAsset>())
            {
                if (x == null) continue;
                CheckTrigger(x, resources, producers, upgrades, flags, errors);
                if (x.useResourceThreshold)
                {
                    if (x.triggerResource != null) CheckResource(x.triggerResource, resources, x.name + ".triggerResource", errors);
                    Parse(x.threshold, x.name + ".threshold", errors);
                }
                CheckConditions(x.conditions, resources, producers, upgrades, x.name + ".conditions", errors);
                CheckBooleanTree(x.name, x.booleanNodes, x.rootConditionIndex, resources, producers, upgrades, flags, errors);
                bool continuous = x.triggerKind == RuleTriggerKind.Automatic || x.triggerKind == RuleTriggerKind.ResourceThreshold
                    || x.triggerKind == RuleTriggerKind.Time;
                if (float.IsNaN(x.cooldownSeconds) || float.IsInfinity(x.cooldownSeconds) || x.cooldownSeconds < 0
                    || (x.repeat && continuous && x.cooldownSeconds <= 0))
                    errors.Add(x.name + ".cooldownSeconds must be finite, non-negative, and positive for continuously repeating rules.");
                if (x.actions == null) { errors.Add(x.name + ".actions cannot be null."); continue; }
                for (int i = 0; i < x.actions.Length; i++)
                {
                    var action = x.actions[i];
                    string path = x.name + ".actions[" + i + "]";
                    if (action == null) { errors.Add(path + " is missing."); continue; }
                    bool exists = false;
                    if (action.kind == RuleActionKind.SetModifier)
                    {
                        exists = action.modifierId != null && dynamicModifierIds.Contains(action.modifierId);
                        if (!string.IsNullOrEmpty(action.targetId)) errors.Add(path + ".targetId must be blank for SetModifier.");
                        if (action.defineModifier) CheckModifier(action.modifier, resources, producers, path + ".modifier", errors);
                    }
                    if (!string.IsNullOrEmpty(action.targetId))
                    {
                        switch (action.kind)
                        {
                            case RuleActionKind.GrantResource:
                            case RuleActionKind.SpendResource:
                            case RuleActionKind.ScheduleGrantResource:
                                exists = resources.ContainsKey(action.targetId);
                                Parse(action.amount, path + ".amount", errors);
                                if (action.kind == RuleActionKind.ScheduleGrantResource
                                    && (float.IsNaN(action.delaySeconds) || float.IsInfinity(action.delaySeconds) || action.delaySeconds < 0))
                                    errors.Add(path + ".delaySeconds must be finite and non-negative.");
                                break;
                            case RuleActionKind.UnlockProducer: exists = producers.ContainsKey(action.targetId); break;
                            case RuleActionKind.UnlockUpgrade: exists = upgrades.ContainsKey(action.targetId); break;
                            case RuleActionKind.SetFlag: exists = flags.ContainsKey(action.targetId); break;
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

        private static void CheckModifier(ProductionModifier modifier, Dictionary<string, ResourceAsset> resources,
            Dictionary<string, ProducerAsset> producers, string path, List<string> errors)
        {
            if (modifier == null) { errors.Add(path + " is missing."); return; }
            bool exists;
            switch (modifier.scope)
            {
                case AuthoredModifierScope.Producer: exists = modifier.targetId != null && producers.ContainsKey(modifier.targetId); break;
                case AuthoredModifierScope.Resource: exists = modifier.targetId != null && resources.ContainsKey(modifier.targetId); break;
                case AuthoredModifierScope.ProducerCategory:
                    exists = !string.IsNullOrEmpty(modifier.targetId) && producers.Values.Any(x => x.category == modifier.targetId); break;
                case AuthoredModifierScope.ResourceCategory:
                    exists = !string.IsNullOrEmpty(modifier.targetId) && resources.Values.Any(x => x.category == modifier.targetId); break;
                case AuthoredModifierScope.Global: exists = string.IsNullOrEmpty(modifier.targetId); break;
                case AuthoredModifierScope.Click: exists = string.IsNullOrEmpty(modifier.targetId) || resources.ContainsKey(modifier.targetId); break;
                default: exists = false; break;
            }
            if (!exists) errors.Add(path + " has an invalid target kind or ID '" + modifier.targetId + "'.");
            ModifierKind runtimeKind;
            ModifierTarget runtimeTarget;
            if (!TryModifierKind(modifier.kind, out runtimeKind)) errors.Add(path + ".kind is invalid.");
            if (!TryModifierTarget(modifier.scope, out runtimeTarget)) errors.Add(path + ".scope is invalid.");
            Parse(modifier.value, path + ".value", errors,
                allowNegative: modifier.kind == AuthoredModifierKind.Flat || modifier.kind == AuthoredModifierKind.AddPercent);
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

        private static void Parse(string text, string path, List<string> errors, bool positive = false, bool allowNegative = false)
        {
            try
            {
                string pattern = allowNegative ? @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$"
                    : @"^[+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$";
                if (text == null || !Regex.IsMatch(text, pattern))
                    throw new FormatException();
                var value = ReadNumber(text);
                if (!Number.IsFinite(value) || (!allowNegative && value < BigDouble.Zero) || (positive && value <= BigDouble.Zero))
                    throw new FormatException();
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            { errors.Add(path + " must be a finite " + (positive ? "positive " : allowNegative ? "" : "non-negative ")
                + "invariant number (e.g. 1.25e30)."); }
        }

        private static IncrementalFramework.ResourceAmount[] Amounts(ResourceAmount[] values)
        {
            return values.Select(x => new IncrementalFramework.ResourceAmount(x.resource.id, ReadNumber(x.amount))).ToArray();
        }

        private static RequirementDefinition[] Conditions(Requirement[] values)
        {
            return values.Select(x => new RequirementDefinition((IncrementalFramework.RequirementKind)(int)x.kind,
                x.targetId, ReadNumber(x.amount))).ToArray();
        }

        internal static BigDouble ReadNumber(string text) { return BigDouble.Parse(text.Replace('E', 'e')); }

        private static bool TryModifierKind(AuthoredModifierKind kind, out ModifierKind runtime)
        {
            switch (kind)
            {
                case AuthoredModifierKind.Flat: runtime = ModifierKind.Flat; return true;
                case AuthoredModifierKind.AddPercent: runtime = ModifierKind.AddPercent; return true;
                case AuthoredModifierKind.Multiply: runtime = ModifierKind.Multiply; return true;
                case AuthoredModifierKind.Override: runtime = ModifierKind.Override; return true;
                default: runtime = default; return false;
            }
        }

        private static bool TryModifierTarget(AuthoredModifierScope scope, out ModifierTarget runtime)
        {
            switch (scope)
            {
                case AuthoredModifierScope.Producer: runtime = ModifierTarget.ProducerProduction; return true;
                case AuthoredModifierScope.Resource: runtime = ModifierTarget.ResourceProduction; return true;
                case AuthoredModifierScope.Global: runtime = ModifierTarget.GlobalProduction; return true;
                case AuthoredModifierScope.Click: runtime = ModifierTarget.Click; return true;
                case AuthoredModifierScope.ProducerCategory: runtime = ModifierTarget.ProducerCategoryProduction; return true;
                case AuthoredModifierScope.ResourceCategory: runtime = ModifierTarget.ResourceCategoryProduction; return true;
                default: runtime = default; return false;
            }
        }

        private static ModifierDefinition CompileModifier(string upgradeId, int index, ProductionModifier modifier)
        { return CompileModifier(upgradeId + ":modifier:" + index, modifier); }

        private static ModifierDefinition CompileModifier(string id, ProductionModifier modifier)
        {
            ModifierKind kind;
            ModifierTarget target;
            if (!TryModifierKind(modifier.kind, out kind) || !TryModifierTarget(modifier.scope, out target))
                throw new InvalidOperationException("Unsupported authored modifier.");
            return new ModifierDefinition(id, target, modifier.targetId,
                ReadNumber(modifier.value), kind, modifier.priority);
        }

        private static void CheckCategory(string category, string path, List<string> errors)
        {
            if (category != null && category.Length > 0 && (string.IsNullOrWhiteSpace(category) || category != category.Trim()))
                errors.Add(path + " must be empty or a category ID without surrounding whitespace.");
        }

        private static void CheckResetPolicy(bool enabled, ResetPolicy policy, string transform, string path, List<string> errors)
        {
            if (!Enum.IsDefined(typeof(ResetPolicy), policy)) errors.Add(path + ".prestigePolicy is invalid.");
            if (!enabled && policy != ResetPolicy.Reset)
                errors.Add(path + ".prestigePolicy requires usePrestigePolicy.");
            int before = errors.Count;
            Parse(transform, path + ".prestigeTransform", errors);
            if (errors.Count == before && ReadNumber(transform) != BigDouble.One && (!enabled || policy != ResetPolicy.Transform))
                errors.Add(path + ".prestigeTransform requires an enabled Transform policy.");
        }

        private static ConditionDefinition RuleConditions(RuleAsset rule, ResourceAsset defaultResource)
        {
            var requirements = Conditions(rule.conditions).Select(ConditionDefinition.Require).ToList();
            if (rule.useResourceThreshold)
                requirements.Insert(0, ConditionDefinition.Require(new RequirementDefinition(IncrementalFramework.RequirementKind.Resource,
                    (rule.triggerResource != null ? rule.triggerResource : defaultResource).id, ReadNumber(rule.threshold))));
            if (rule.rootConditionIndex >= 0) requirements.Add(CompileBooleanNode(rule.booleanNodes, rule.rootConditionIndex));
            return ConditionDefinition.All(requirements.ToArray());
        }

        private static ConditionDefinition OptionalBooleanTree(ConditionNode[] nodes, int root)
        { return root < 0 ? null : CompileBooleanNode(nodes, root); }

        private static ConditionDefinition CompileBooleanNode(ConditionNode[] nodes, int index)
        {
            var node = nodes[index];
            switch (node.operation)
            {
                case ConditionOperator.Requirement:
                    return ConditionDefinition.Require(Conditions(new[] { node.requirement })[0]);
                case ConditionOperator.Flag: return ConditionDefinition.Flag(node.flagId, node.expected);
                case ConditionOperator.All: return ConditionDefinition.All(node.children.Select(i => CompileBooleanNode(nodes, i)).ToArray());
                case ConditionOperator.Any: return ConditionDefinition.Any(node.children.Select(i => CompileBooleanNode(nodes, i)).ToArray());
                case ConditionOperator.Not: return ConditionDefinition.Not(CompileBooleanNode(nodes, node.children[0]));
                default: throw new ArgumentOutOfRangeException(nameof(node.operation));
            }
        }

        private static void CheckBooleanTree(string owner, ConditionNode[] nodes, int root, Dictionary<string, ResourceAsset> resources,
            Dictionary<string, ProducerAsset> producers, Dictionary<string, UpgradeAsset> upgrades,
            Dictionary<string, FlagAsset> flags, List<string> errors)
        {
            string path = owner + ".booleanNodes";
            if (nodes == null) { errors.Add(path + " cannot be null."); return; }
            if (root == -1 && nodes.Length == 0) return;
            if (nodes.Length > 1024 || root < 0 || root >= nodes.Length)
            { errors.Add(path + " requires a valid rootConditionIndex and at most 1024 nodes."); return; }
            var visited = new HashSet<int>();
            var active = new HashSet<int>();
            Action<int, int> visit = null;
            visit = (index, depth) =>
            {
                string item = path + "[" + index + "]";
                if (index < 0 || index >= nodes.Length) { errors.Add(item + " is out of range."); return; }
                if (depth > 64) { errors.Add(item + " exceeds maximum expression depth 64."); return; }
                if (active.Contains(index)) { errors.Add(item + " creates a condition cycle."); return; }
                if (!visited.Add(index)) { errors.Add(item + " is shared; each tree node must have one parent."); return; }
                var node = nodes[index];
                if (node == null) { errors.Add(item + " is missing."); return; }
                if (node.children == null) { errors.Add(item + ".children cannot be null."); return; }
                if (node.children.Length > 1024) { errors.Add(item + " has too many children."); return; }
                if (node.operation == ConditionOperator.Requirement)
                    CheckConditions(new[] { node.requirement }, resources, producers, upgrades, item, errors);
                else if (node.operation == ConditionOperator.Flag)
                {
                    if (node.flagId == null || !flags.ContainsKey(node.flagId)) errors.Add(item + " references an unknown flag.");
                }
                else if (!Enum.IsDefined(typeof(ConditionOperator), node.operation)) errors.Add(item + " has an invalid operator.");
                bool leaf = node.operation == ConditionOperator.Requirement || node.operation == ConditionOperator.Flag;
                if ((leaf && node.children.Length != 0) || (node.operation == ConditionOperator.Not && node.children.Length != 1)
                    || ((node.operation == ConditionOperator.All || node.operation == ConditionOperator.Any) && node.children.Length == 0))
                    errors.Add(item + " has an invalid child count for " + node.operation + ".");
                active.Add(index);
                foreach (int child in node.children) visit(child, depth + 1);
                active.Remove(index);
            };
            visit(root, 0);
            if (visited.Count != nodes.Length) errors.Add(path + " contains unreachable nodes; remove or connect them.");
        }

        private static void CheckTrigger(RuleAsset rule, Dictionary<string, ResourceAsset> resources,
            Dictionary<string, ProducerAsset> producers, Dictionary<string, UpgradeAsset> upgrades,
            Dictionary<string, FlagAsset> flags, List<string> errors)
        {
            if (!Enum.IsDefined(typeof(RuleTriggerKind), rule.triggerKind)) errors.Add(rule.name + ".triggerKind is invalid.");
            if (rule.filterScheduledEvent && (rule.triggerKind != RuleTriggerKind.ScheduledEvent
                || !Enum.IsDefined(typeof(ScheduledEventTriggerKind), rule.scheduledEventKind)))
                errors.Add(rule.name + ".scheduledEventKind requires a valid ScheduledEvent trigger filter.");
            if (double.IsNaN(rule.triggerTime) || double.IsInfinity(rule.triggerTime) || rule.triggerTime < 0
                || (rule.triggerKind != RuleTriggerKind.Time && rule.triggerTime != 0))
                errors.Add(rule.name + ".triggerTime must be finite, non-negative, and zero for non-Time triggers.");
            if (string.IsNullOrEmpty(rule.triggerTargetId)) return;
            bool exists;
            switch (rule.triggerKind)
            {
                case RuleTriggerKind.ResourceThreshold:
                case RuleTriggerKind.ResourceChanged: exists = resources.ContainsKey(rule.triggerTargetId); break;
                case RuleTriggerKind.ProducerPurchased: exists = producers.ContainsKey(rule.triggerTargetId); break;
                case RuleTriggerKind.UpgradePurchased: exists = upgrades.ContainsKey(rule.triggerTargetId); break;
                case RuleTriggerKind.FlagChanged: exists = flags.ContainsKey(rule.triggerTargetId); break;
                default: exists = false; break;
            }
            if (!exists) errors.Add(rule.name + ".triggerTargetId is unknown or unsupported for this trigger kind.");
        }

        private static RuleAction CompileAction(RuleActionEntry entry)
        {
            switch (entry.kind)
            {
                case RuleActionKind.GrantResource: return new GrantResourceAction(entry.targetId, ReadNumber(entry.amount));
                case RuleActionKind.UnlockProducer: return new UnlockProducerAction(entry.targetId);
                case RuleActionKind.UnlockUpgrade: return new UnlockUpgradeAction(entry.targetId);
                case RuleActionKind.SpendResource: return new SpendResourceAction(entry.targetId, ReadNumber(entry.amount));
                case RuleActionKind.SetFlag: return new SetFlagAction(entry.targetId, entry.flagValue);
                case RuleActionKind.ScheduleGrantResource:
                    return new ScheduleGrantResourceAction(entry.targetId, ReadNumber(entry.amount), entry.delaySeconds, entry.priority);
                case RuleActionKind.SetModifier:
                    return entry.defineModifier ? new SetModifierAction(CompileModifier(entry.modifierId, entry.modifier), entry.enabled)
                        : new SetModifierAction(entry.modifierId, entry.enabled);
                default: throw new ArgumentOutOfRangeException(nameof(entry.kind));
            }
        }
    }
}
