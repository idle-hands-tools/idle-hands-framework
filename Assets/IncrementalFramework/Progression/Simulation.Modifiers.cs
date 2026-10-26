using System;
using System.Collections.Generic;
using BreakInfinity;

namespace IncrementalFramework
{
    public sealed partial class Simulation
    {
        private StatModifiers[][] outputModifiers;
        private StatModifiers[] clickModifiers;
        private bool[] activeModifiers;
        public bool IsModifierEnabled(string id) => activeModifiers[Content.ModifierIndex(id)];

        private struct StatModifiers
        {
            public BigDouble Flat, Percent, Product, Override;
            public bool HasOverride;
            public int OverridePriority;
            public string OverrideId;
            public static StatModifiers Empty => new StatModifiers { Product = BigDouble.One };
            public void Add(IndexedModifier modifier)
            {
                switch (modifier.Kind)
                {
                    case ModifierKind.Flat: Flat = Number.AddSigned(Flat, modifier.Value); break;
                    case ModifierKind.Add: Percent = Number.AddSigned(Percent, modifier.Value); break;
                    case ModifierKind.Multiply: Product = Number.Multiply(Product, modifier.Value); break;
                    case ModifierKind.Override:
                        if (!HasOverride || modifier.Priority > OverridePriority ||
                            (modifier.Priority == OverridePriority && string.CompareOrdinal(modifier.Id, OverrideId) > 0))
                        { HasOverride = true; Override = modifier.Value; OverridePriority = modifier.Priority; OverrideId = modifier.Id; }
                        break;
                }
            }
            public BigDouble Apply(BigDouble value)
            {
                if (HasOverride) return Override;
                BigDouble flat = Number.AddSigned(value, Flat), percent = Number.AddSigned(BigDouble.One, Percent);
                return Number.Multiply(Number.Multiply(flat > BigDouble.Zero ? flat : BigDouble.Zero,
                    percent > BigDouble.Zero ? percent : BigDouble.Zero), Product);
            }
        }
        private bool ModifierMatches(IndexedModifier modifier, int producer, int resource)
        {
            switch (modifier.Target)
            {
                case ModifierTarget.GlobalProduction: return true;
                case ModifierTarget.ProducerProduction: return modifier.TargetIndex == producer;
                case ModifierTarget.ResourceProduction: return modifier.TargetIndex == resource;
                case ModifierTarget.ProducerCategoryProduction: return modifier.TargetIndex == Content.ProducerCategoryIndex(producer);
                case ModifierTarget.ResourceCategoryProduction: return modifier.TargetIndex == Content.ResourceCategoryIndex(resource);
                default: return false;
            }
        }
        private StatModifiers[] BuildOutputModifiers(int producer, int includeUpgrade = -1, bool prestigeReset = false, bool[] modifierState = null)
        {
            var outputs = Content.ProducerOutputs(producer);
            var result = new StatModifiers[outputs.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = StatModifiers.Empty;
                foreach (int upgrade in Content.UpgradesForProducer(producer))
                {
                    if ((!purchased[upgrade] && upgrade != includeUpgrade) || (prestigeReset && !Content.Upgrades[upgrade].Permanent)) continue;
                    foreach (var modifier in Content.UpgradeModifiers(upgrade))
                        if (ModifierMatches(modifier, producer, outputs[i].ResourceIndex)) result[i].Add(modifier);
                }
                for (int m = 0; m < activeModifiers.Length; m++)
                    if (!prestigeReset && (modifierState ?? activeModifiers)[m] && ModifierMatches(Content.Modifier(m), producer, outputs[i].ResourceIndex))
                        result[i].Add(Content.Modifier(m));
            }
            return result;
        }
        private StatModifiers BuildClickModifiers(int resource, int includeUpgrade = -1, bool prestigeReset = false, bool[] modifierState = null)
        {
            var result = StatModifiers.Empty;
            foreach (int upgrade in Content.ClickUpgradesForResource(resource))
            {
                if ((!purchased[upgrade] && upgrade != includeUpgrade) || (prestigeReset && !Content.Upgrades[upgrade].Permanent)) continue;
                foreach (var modifier in Content.UpgradeModifiers(upgrade))
                    if (modifier.Target == ModifierTarget.Click && (modifier.TargetIndex < 0 || modifier.TargetIndex == resource)) result.Add(modifier);
            }
            for (int m = 0; m < activeModifiers.Length; m++)
            {
                var modifier = Content.Modifier(m);
                if (!prestigeReset && (modifierState ?? activeModifiers)[m] && modifier.Target == ModifierTarget.Click &&
                    (modifier.TargetIndex < 0 || modifier.TargetIndex == resource)) result.Add(modifier);
            }
            return result;
        }
        private BigDouble Contribution(int producer, int resource, BigDouble amount, BigDouble count,
            Dictionary<int, StatModifiers[]> projected = null, BigDouble? permanent = null)
        {
            if (count == BigDouble.Zero) return BigDouble.Zero;
            var modifiers = projected != null && projected.TryGetValue(producer, out var replacement) ? replacement : outputModifiers[producer];
            var outputs = Content.ProducerOutputs(producer);
            for (int i = 0; i < outputs.Count; i++)
                if (outputs[i].ResourceIndex == resource)
                    return Number.Multiply(modifiers[i].Apply(Number.Multiply(amount, count)), permanent ?? PermanentMultiplier);
            throw new InvalidOperationException("Missing compiled producer output.");
        }
        private void RebuildAllModifierCaches()
        {
            outputModifiers = new StatModifiers[owned.Length][];
            clickModifiers = new StatModifiers[balances.Length];
            for (int p = 0; p < owned.Length; p++) outputModifiers[p] = BuildOutputModifiers(p);
            for (int r = 0; r < balances.Length; r++) clickModifiers[r] = BuildClickModifiers(r);
        }
        private bool PrepareModifierChange(bool[] proposed, out Dictionary<int, StatModifiers[]> outputs,
            out SortedDictionary<int, StatModifiers> clicks, out SortedSet<int> targets)
        {
            targets = new SortedSet<int>(); clicks = new SortedDictionary<int, StatModifiers>();
            for (int m = 0; m < proposed.Length; m++)
            {
                if (proposed[m] == activeModifiers[m]) continue;
                foreach (int producer in Content.ModifierAffectedProducers(m)) targets.Add(producer);
                var modifier = Content.Modifier(m);
                if (modifier.Target == ModifierTarget.Click)
                    for (int r = 0; r < balances.Length; r++)
                        if (modifier.TargetIndex < 0 || modifier.TargetIndex == r) clicks[r] = BuildClickModifiers(r, modifierState: proposed);
            }
            outputs = new Dictionary<int, StatModifiers[]>();
            var counts = new Dictionary<int, BigDouble>();
            foreach (int producer in targets)
            {
                outputs[producer] = BuildOutputModifiers(producer, modifierState: proposed);
                counts[producer] = producerFactors[producer];
            }
            foreach (var click in clicks) click.Value.Apply(BigDouble.One);
            return ValidProjectedRates(counts, outputs);
        }
        private void CommitModifierChange(bool[] proposed, Dictionary<int, StatModifiers[]> outputs,
            SortedDictionary<int, StatModifiers> clicks, SortedSet<int> targets)
        {
            for (int i = 0; i < proposed.Length; i++)
                if (activeModifiers[i] != proposed[i]) Mark(SimulationChangeKind.Modifier, i);
            Array.Copy(proposed, activeModifiers, proposed.Length);
            foreach (var pair in outputs) outputModifiers[pair.Key] = pair.Value;
            foreach (var pair in clicks) clickModifiers[pair.Key] = pair.Value;
            RefreshProducers(targets);
        }
        private CommandResult SetModifier(int index, bool enabled)
        {
            var proposed = (bool[])activeModifiers.Clone(); proposed[index] = enabled;
            if (!PrepareModifierChange(proposed, out var outputs, out var clicks, out var targets)) return CommandResult.Fail("Modifier production overflows.");
            CommitModifierChange(proposed, outputs, clicks, targets); return CommandResult.Ok();
        }
    }
}
