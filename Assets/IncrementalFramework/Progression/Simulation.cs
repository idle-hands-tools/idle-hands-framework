using System;
using System.Collections.Generic;
using System.Threading;
using BreakInfinity;

namespace IncrementalFramework
{
    /// <summary>The sole writer of runtime state. Commands and advances must run on its creating thread.</summary>
    public sealed partial class Simulation
    {
        private readonly int writerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly EventScheduler<SimulationEvent> scheduler = new EventScheduler<SimulationEvent>();
        private readonly BigDouble[] balances;
        private readonly BigDouble[] rates;
        private readonly BigDouble[] producerFactors;
        private readonly BigDouble[] integrationBuffer;
        private readonly long[] owned;
        private readonly bool[] enabled;
        private readonly bool[] producerUnlocked;
        private readonly bool[] purchased;
        private readonly bool[] upgradeUnlocked;
        private readonly bool[] ruleFired;
        private readonly bool[] ruleDisabled;
        private readonly double[] ruleEligible;
        private readonly EventToken[] ruleTokens;
        private readonly Dictionary<string, int> ruleIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private ulong randomState;
        private double? pendingTarget;
        public ContentDatabase Content { get; }
        public int EventBudget { get; }
        public double Time { get; private set; }
        public double? PendingTarget => pendingTarget;
        public long PrestigeCount { get; private set; }
        public BigDouble PermanentMultiplier { get; private set; } = BigDouble.One;
        public int PendingEventCount => scheduler.Count;
        public CommandResult LastEventResult { get; private set; } = CommandResult.Ok();
        public IReadOnlyList<ResourceView> Resources { get; }
        public IReadOnlyList<ProducerView> Producers { get; }
        public IReadOnlyList<UpgradeView> Upgrades { get; }

        public Simulation(ContentDatabase content, ulong seed = 1, int eventBudget = 10000)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            if (eventBudget < 1) throw new ArgumentOutOfRangeException(nameof(eventBudget));
            EventBudget = eventBudget; randomState = seed == 0 ? 1 : seed;
            balances = new BigDouble[content.Resources.Count]; rates = new BigDouble[balances.Length];
            integrationBuffer = new BigDouble[balances.Length];
            owned = new long[content.Producers.Count]; enabled = new bool[owned.Length];
            producerUnlocked = new bool[owned.Length]; producerFactors = new BigDouble[owned.Length];
            purchased = new bool[content.Upgrades.Count]; upgradeUnlocked = new bool[purchased.Length];
            ruleFired = new bool[content.Rules.Count]; ruleEligible = new double[ruleFired.Length]; ruleTokens = new EventToken[ruleFired.Length];
            ruleDisabled = new bool[ruleFired.Length];
            var resourceViews = new ResourceView[balances.Length];
            var producerViews = new ProducerView[owned.Length];
            var upgradeViews = new UpgradeView[purchased.Length];
            for (int i = 0; i < balances.Length; i++)
            {
                int index = i; balances[i] = content.Resources[i].Initial;
                resourceViews[i] = new ResourceView(content.Resources[i].Id, content.Resources[i].Cap,
                    () => balances[index], () => EffectiveRate(index));
            }
            for (int i = 0; i < owned.Length; i++)
            {
                int index = i; enabled[i] = true; producerUnlocked[i] = content.Producers[i].InitiallyUnlocked;
                producerViews[i] = new ProducerView(content.Producers[i].Id, () => owned[index], () => enabled[index], () => producerUnlocked[index]);
            }
            for (int i = 0; i < purchased.Length; i++)
            {
                int index = i; upgradeUnlocked[i] = content.Upgrades[i].InitiallyUnlocked;
                upgradeViews[i] = new UpgradeView(content.Upgrades[i].Id, () => purchased[index], () => upgradeUnlocked[index]);
            }
            Resources = Array.AsReadOnly(resourceViews); Producers = Array.AsReadOnly(producerViews); Upgrades = Array.AsReadOnly(upgradeViews);
            for (int i = 0; i < ruleFired.Length; i++) ruleIndices.Add(content.Rules[i].Id, i);
            RebuildAllRates();
            RefreshAllRules();
        }

        public ResourceView GetResource(string id) => Resources[Content.ResourceIndex(id)];
        public ProducerView GetProducer(string id) => Producers[Content.ProducerIndex(id)];
        public UpgradeView GetUpgrade(string id) => Upgrades[Content.UpgradeIndex(id)];

        public CommandResult Execute(ISimulationCommand command)
        {
            AssertWriter();
            if (pendingTarget.HasValue) return CommandResult.Fail("Resume the pending advance before executing commands.");
            return ExecuteInternal(command);
        }

        private CommandResult ExecuteInternal(ISimulationCommand command)
        {
            try
            {
                if (command is GrantResourceCommand grant)
                {
                    int index = Content.ResourceIndex(grant.ResourceId);
                    if (!Number.IsFinite(grant.Amount) || grant.Amount < BigDouble.Zero) return CommandResult.Fail("Grant must be finite and nonnegative.");
                    BigDouble value = balances[index] + grant.Amount;
                    if (!Number.IsFinite(value)) return CommandResult.Fail("Grant overflows.");
                    balances[index] = Clamp(index, value); RefreshRules(index); return CommandResult.Ok();
                }
                if (command is BuyProducerCommand buy) return BuyProducer(Content.ProducerIndex(buy.ProducerId), buy.Count);
                if (command is BuyUpgradeCommand upgrade) return BuyUpgrade(Content.UpgradeIndex(upgrade.UpgradeId));
                if (command is ToggleProducerCommand toggle)
                {
                    int index = Content.ProducerIndex(toggle.ProducerId);
                    if (!producerUnlocked[index]) return CommandResult.Fail("Producer is locked.");
                    if (!ValidProjectedRates(new Dictionary<int, BigDouble>
                        { [index] = Factor(index, owned[index], toggle.Enabled) }))
                        return CommandResult.Fail("Production overflows.");
                    enabled[index] = toggle.Enabled; RefreshProducer(index); return CommandResult.Ok();
                }
                if (command is PrestigeCommand) return Prestige();
                if (TryExecuteRegistered(command, out var registered)) return registered;
                return CommandResult.Fail("Unknown command.");
            }
            catch (ArgumentException error) { return CommandResult.Fail(error.Message); }
        }

        public IReadOnlyList<ResourceAmount> GetProducerCost(string id, int count = 1)
        {
            AssertWriter();
            int index = Content.ProducerIndex(id);
            if (count < 1 || owned[index] > long.MaxValue - count) throw new ArgumentOutOfRangeException(nameof(count));
            var result = new List<ResourceAmount>();
            BigDouble scale = CostScale(index, count);
            foreach (var cost in Content.ProducerCosts(index))
                result.Add(new ResourceAmount(Content.Resources[cost.ResourceIndex].Id, cost.Amount * scale));
            return result.AsReadOnly();
        }

        public EventToken Schedule(ISimulationCommand command, double time, int priority = 0)
        {
            AssertWriter();
            if (!Finite(time) || time < Time) throw new ArgumentOutOfRangeException(nameof(time));
            SimulationEvent payload;
            if (command is GrantResourceCommand grant)
            {
                Number.NonNegative(grant.Amount, nameof(command));
                payload = new SimulationEvent(SimulationEventKind.GrantResource, Content.ResourceIndex(grant.ResourceId), grant.Amount);
            }
            else if (command is BuyProducerCommand buy)
            {
                if (buy.Count < 1) throw new ArgumentOutOfRangeException(nameof(command));
                payload = new SimulationEvent(SimulationEventKind.BuyProducer, Content.ProducerIndex(buy.ProducerId), count: buy.Count);
            }
            else if (command is BuyUpgradeCommand upgrade) payload = new SimulationEvent(SimulationEventKind.BuyUpgrade, Content.UpgradeIndex(upgrade.UpgradeId));
            else if (command is ToggleProducerCommand toggle) payload = new SimulationEvent(SimulationEventKind.ToggleProducer, Content.ProducerIndex(toggle.ProducerId), enabled: toggle.Enabled);
            else if (command is PrestigeCommand) payload = new SimulationEvent(SimulationEventKind.Prestige);
            else throw new ArgumentException("Unknown command.");
            return scheduler.Schedule(time, priority, payload);
        }

        public bool Cancel(EventToken token)
        {
            AssertWriter();
            // Rule tokens are engine-owned; callers may cancel only scheduled command tokens.
            if (scheduler.TryGet(token, out var entry) && entry.Payload.Kind == SimulationEventKind.Rule) return false;
            return scheduler.Cancel(token);
        }

        public AdvanceResult AdvanceTo(double target)
        {
            AssertWriter();
            if (!Finite(target) || target < Time || (pendingTarget.HasValue && target < pendingTarget.Value))
                throw new ArgumentOutOfRangeException(nameof(target), "Time must be finite and monotonic, including a pending target.");
            if (!pendingTarget.HasValue) PrepareSystems(target);
            pendingTarget = target;
            int processed = 0;
            while (scheduler.TryPeek(out var entry) && entry.Time <= target)
            {
                if (processed == EventBudget) return new AdvanceResult(false, Time, processed);
                Integrate(entry.Time);
                scheduler.TryDequeue(out entry); processed++;
                Process(entry);
            }
            Integrate(target); pendingTarget = null;
            return new AdvanceResult(true, Time, processed);
        }

        public ulong NextRandomUInt64()
        {
            AssertWriter();
            ulong value = randomState;
            value ^= value << 13; value ^= value >> 7; value ^= value << 17;
            randomState = value; return value;
        }

        private CommandResult BuyProducer(int index, int count)
        {
            if (count < 1 || owned[index] > long.MaxValue - count) return CommandResult.Fail("Invalid producer count.");
            if (!producerUnlocked[index]) return CommandResult.Fail("Producer is locked.");
            BigDouble scale = CostScale(index, count);
            if (!CanPay(Content.ProducerCosts(index), scale)) return CommandResult.Fail("Insufficient resources or invalid cost.");
            // Validate every cost and all affected rates before mutating any state.
            BigDouble factor = Factor(index, owned[index] + count, enabled[index]);
            if (!ValidProjectedRates(new Dictionary<int, BigDouble> { [index] = factor })) return CommandResult.Fail("Production overflows.");
            Pay(Content.ProducerCosts(index), scale);
            owned[index] += count; RefreshProducer(index);
            foreach (var cost in Content.ProducerCosts(index)) RefreshRules(cost.ResourceIndex);
            return CommandResult.Ok();
        }

        private BigDouble CostScale(int index, int count)
        {
            double growth = Content.Producers[index].CostGrowth;
            BigDouble start = BigDouble.Pow(new BigDouble(growth), owned[index]);
            if (growth == 1) return new BigDouble(count);
            // Binary composition avoids catastrophic cancellation in (growth^count - 1)/(growth - 1).
            BigDouble sum = BigDouble.Zero, power = BigDouble.One;
            BigDouble blockSum = BigDouble.One, blockPower = new BigDouble(growth);
            int remaining = count;
            while (remaining > 0)
            {
                if ((remaining & 1) != 0) { sum += power * blockSum; power *= blockPower; }
                remaining >>= 1;
                if (remaining > 0) { blockSum *= BigDouble.One + blockPower; blockPower *= blockPower; }
            }
            return start * sum;
        }

        private CommandResult BuyUpgrade(int index)
        {
            if (!upgradeUnlocked[index]) return CommandResult.Fail("Upgrade is locked.");
            if (purchased[index]) return CommandResult.Fail("Upgrade is already purchased.");
            if (!CanPay(Content.UpgradeCosts(index), BigDouble.One)) return CommandResult.Fail("Insufficient resources.");
            var targets = new SortedSet<int>();
            foreach (var modifier in Content.UpgradeModifiers(index)) targets.Add(modifier.ProducerIndex);
            var projected = new Dictionary<int, BigDouble>();
            foreach (int target in targets) projected.Add(target, Factor(target, owned[target], enabled[target], index));
            if (!ValidProjectedRates(projected)) return CommandResult.Fail("Production overflows.");
            Pay(Content.UpgradeCosts(index), BigDouble.One);
            purchased[index] = true;
            RefreshProducers(targets);
            foreach (var cost in Content.UpgradeCosts(index)) RefreshRules(cost.ResourceIndex);
            return CommandResult.Ok();
        }

        private bool CanPay(IReadOnlyList<IndexedAmount> costs, BigDouble scale)
        {
            if (!Number.IsFinite(scale) || scale < BigDouble.Zero) return false;
            foreach (var cost in costs)
            {
                BigDouble value = cost.Amount * scale;
                if (!Number.IsFinite(value) || value > balances[cost.ResourceIndex]) return false;
            }
            return true;
        }

        private void Pay(IReadOnlyList<IndexedAmount> costs, BigDouble scale)
        {
            foreach (var cost in costs) balances[cost.ResourceIndex] -= cost.Amount * scale;
        }

        private CommandResult Prestige()
        {
            var definition = Content.Prestige;
            if (definition == null) return CommandResult.Fail("Prestige is not configured.");
            if (balances[Content.ResourceIndex(definition.ResourceId)] < definition.Threshold) return CommandResult.Fail("Prestige threshold not reached.");
            BigDouble multiplier = PermanentMultiplier * definition.MultiplierPerPrestige;
            if (PrestigeCount == long.MaxValue || !Number.IsFinite(multiplier)) return CommandResult.Fail("Prestige overflows.");
            var projected = new Dictionary<int, BigDouble>();
            for (int i = 0; i < owned.Length; i++)
            {
                long count = definition.ResetProducers ? 0 : owned[i];
                BigDouble additive = BigDouble.Zero, product = BigDouble.One;
                foreach (int upgrade in Content.UpgradesForProducer(i))
                {
                    if (!purchased[upgrade] || (definition.ResetUpgrades && !Content.Upgrades[upgrade].Permanent)) continue;
                    foreach (var modifier in Content.UpgradeModifiers(upgrade))
                        if (modifier.ProducerIndex == i)
                        {
                            if (modifier.Kind == ModifierKind.Add) additive += modifier.Value;
                            else product *= modifier.Value;
                        }
                }
                projected.Add(i, !enabled[i] || count == 0 ? BigDouble.Zero : new BigDouble(count) * (BigDouble.One + additive) * product * multiplier);
            }
            if (!ValidProjectedRates(projected)) return CommandResult.Fail("Prestige production overflows.");
            PermanentMultiplier = multiplier; PrestigeCount++;
            for (int i = 0; i < balances.Length; i++)
                if (Content.Resources[i].ResetOnPrestige) balances[i] = Content.Resources[i].Initial;
            if (definition.ResetProducers)
                for (int i = 0; i < owned.Length; i++)
                { owned[i] = 0; enabled[i] = true; producerUnlocked[i] = Content.Producers[i].InitiallyUnlocked; }
            if (definition.ResetUpgrades)
                for (int i = 0; i < purchased.Length; i++)
                    if (!Content.Upgrades[i].Permanent)
                    { purchased[i] = false; upgradeUnlocked[i] = Content.Upgrades[i].InitiallyUnlocked; }
            for (int i = 0; i < ruleFired.Length; i++)
                if (Content.Rules[i].ResetOnPrestige) { ruleFired[i] = false; ruleDisabled[i] = false; ruleEligible[i] = Time; }
            RebuildAllRates(); RefreshAllRules(); return CommandResult.Ok();
        }

        private BigDouble Factor(int index, long count, bool active, int includeUpgrade = -1)
        {
            if (!active || count == 0) return BigDouble.Zero;
            BigDouble additive = BigDouble.Zero, multiplier = BigDouble.One;
            foreach (int upgrade in Content.UpgradesForProducer(index))
            {
                if (!purchased[upgrade] && upgrade != includeUpgrade) continue;
                foreach (var modifier in Content.UpgradeModifiers(upgrade))
                {
                    if (modifier.ProducerIndex != index) continue;
                    if (modifier.Kind == ModifierKind.Add) additive += modifier.Value;
                    else multiplier *= modifier.Value;
                }
            }
            return new BigDouble(count) * (BigDouble.One + additive) * multiplier * PermanentMultiplier;
        }

        private bool ValidFactor(int index, BigDouble factor)
        {
            if (!Number.IsFinite(factor)) return false;
            foreach (var output in Content.ProducerOutputs(index))
                if (!Number.IsFinite(output.Amount * factor)) return false;
            return true;
        }

        private bool ValidProjectedRates(Dictionary<int, BigDouble> projected)
        {
            var affected = new HashSet<int>();
            foreach (var pair in projected)
            {
                if (!ValidFactor(pair.Key, pair.Value)) return false;
                foreach (var output in Content.ProducerOutputs(pair.Key)) affected.Add(output.ResourceIndex);
            }
            foreach (int resource in affected)
            {
                BigDouble total = BigDouble.Zero;
                foreach (int producer in Content.ProducersForResource(resource))
                {
                    BigDouble factor = projected.TryGetValue(producer, out var replacement) ? replacement : producerFactors[producer];
                    foreach (var output in Content.ProducerOutputs(producer))
                        if (output.ResourceIndex == resource) total += output.Amount * factor;
                }
                if (!Number.IsFinite(total)) return false;
            }
            return true;
        }

        private void RefreshProducer(int index)
        {
            producerFactors[index] = Factor(index, owned[index], enabled[index]);
            foreach (var output in Content.ProducerOutputs(index))
            { RebuildRate(output.ResourceIndex); RefreshRules(output.ResourceIndex); }
        }

        private void RefreshProducers(IEnumerable<int> indices)
        {
            var resources = new SortedSet<int>();
            foreach (int index in indices)
            {
                producerFactors[index] = Factor(index, owned[index], enabled[index]);
                foreach (var output in Content.ProducerOutputs(index)) resources.Add(output.ResourceIndex);
            }
            foreach (int resource in resources) RebuildRate(resource);
            foreach (int resource in resources) RefreshRules(resource);
        }

        private void RebuildRate(int resource)
        {
            BigDouble rate = BigDouble.Zero;
            foreach (int producer in Content.ProducersForResource(resource))
                foreach (var output in Content.ProducerOutputs(producer))
                    if (output.ResourceIndex == resource) rate += output.Amount * producerFactors[producer];
            if (!Number.IsFinite(rate)) throw new InvalidOperationException("Production rate overflows.");
            rates[resource] = rate;
        }
        private void RebuildAllRates()
        {
            for (int i = 0; i < owned.Length; i++) producerFactors[i] = Factor(i, owned[i], enabled[i]);
            for (int i = 0; i < rates.Length; i++) RebuildRate(i);
        }
        private BigDouble EffectiveRate(int index)
        {
            var cap = Content.Resources[index].Cap;
            return cap.HasValue && balances[index] >= cap.Value ? BigDouble.Zero : rates[index];
        }
        private BigDouble Clamp(int index, BigDouble value)
        {
            var cap = Content.Resources[index].Cap;
            return cap.HasValue && value > cap.Value ? cap.Value : value;
        }
        private void Integrate(double time)
        {
            double elapsed = time - Time;
            if (elapsed < 0) throw new InvalidOperationException("Event is in the past.");
            if (elapsed > 0)
            {
                for (int i = 0; i < balances.Length; i++)
                {
                    BigDouble value = balances[i] + rates[i] * elapsed;
                    value = Clamp(i, value);
                    if (!Number.IsFinite(value)) throw new InvalidOperationException("Resource integration overflows.");
                    integrationBuffer[i] = value;
                }
                Array.Copy(integrationBuffer, balances, balances.Length);
            }
            Time = time;
        }

        private void RefreshRules(int resource)
        { foreach (int index in Content.RulesForResource(resource)) RefreshRule(index); }
        private void RefreshAllRules()
        { for (int i = 0; i < ruleFired.Length; i++) RefreshRule(i); }
        private void RefreshRule(int index)
        {
            bool hasDueTime = TryRuleTime(index, out double due);
            // Preserve the sequence of an already-correct event. Requeuing it would starve
            // later same-time rules when a zero-cooldown repeat changes the shared resource.
            if (hasDueTime && scheduler.TryGet(ruleTokens[index], out var current) && current.Time == due) return;
            scheduler.Cancel(ruleTokens[index]); ruleTokens[index] = default;
            if (hasDueTime) ruleTokens[index] = scheduler.Schedule(due, 100, new SimulationEvent(SimulationEventKind.Rule, index));
        }
        private bool TryRuleTime(int index, out double due)
        {
            due = 0;
            var rule = Content.Rules[index];
            if (ruleDisabled[index] || (ruleFired[index] && !rule.Repeat)) return false;
            int resource = Content.RuleResource(index);
            var cap = Content.Resources[resource].Cap;
            if (cap.HasValue && rule.Threshold > cap.Value) return false;
            due = Math.Max(Time, ruleEligible[index]);
            if (balances[resource] < rule.Threshold)
            {
                if (rates[resource] <= BigDouble.Zero) return false;
                double delta = ((rule.Threshold - balances[resource]) / rates[resource]).ToDouble();
                if (!Finite(delta)) return false;
                double crossing = Time + delta;
                if (crossing <= Time) crossing = NextTime(Time);
                due = Math.Max(due, crossing);
            }
            return Finite(due);
        }
        private void Process(ScheduledEvent<SimulationEvent> entry)
        {
            var payload = entry.Payload;
            switch (payload.Kind)
            {
                case SimulationEventKind.Rule:
                    int index = payload.Index; ruleTokens[index] = default;
                    var rule = Content.Rules[index];
                    if (balances[Content.RuleResource(index)] < rule.Threshold)
                    { RefreshRule(index); return; }
                    var grants = new SortedDictionary<int, BigDouble>();
                    foreach (var action in Content.RuleActions(index))
                        if (action.Kind == RuleActionKind.GrantResource)
                        {
                            int resource = action.TargetIndex;
                            BigDouble value = grants.TryGetValue(resource, out var previous) ? previous : balances[resource];
                            value = Clamp(resource, value + action.Amount);
                            if (!Number.IsFinite(value))
                            {
                                LastEventResult = CommandResult.Fail("Rule resource transaction overflows.");
                                ruleFired[index] = true; ruleDisabled[index] = true;
                                return;
                            }
                            grants[resource] = value;
                        }
                    ruleFired[index] = true;
                    double nextEligible = Time + rule.Cooldown;
                    if (rule.Cooldown > 0 && nextEligible <= Time) nextEligible = NextTime(Time);
                    // No finite instant remains after this cooldown. Persist a disabled rule, not Infinity.
                    if (!Finite(nextEligible)) { ruleDisabled[index] = true; nextEligible = Time; }
                    ruleEligible[index] = nextEligible;
                    foreach (var grant in grants) balances[grant.Key] = grant.Value;
                    foreach (var action in Content.RuleActions(index))
                    {
                        if (action.Kind == RuleActionKind.UnlockProducer) producerUnlocked[action.TargetIndex] = true;
                        else if (action.Kind == RuleActionKind.UnlockUpgrade) upgradeUnlocked[action.TargetIndex] = true;
                    }
                    foreach (var grant in grants) RefreshRules(grant.Key);
                    RefreshRule(index); LastEventResult = CommandResult.Ok(); break;
                case SimulationEventKind.GrantResource:
                    LastEventResult = ExecuteInternal(new GrantResourceCommand(Content.Resources[payload.Index].Id, payload.Amount)); break;
                case SimulationEventKind.BuyProducer:
                    LastEventResult = ExecuteInternal(new BuyProducerCommand(Content.Producers[payload.Index].Id, payload.Count)); break;
                case SimulationEventKind.BuyUpgrade:
                    LastEventResult = ExecuteInternal(new BuyUpgradeCommand(Content.Upgrades[payload.Index].Id)); break;
                case SimulationEventKind.ToggleProducer:
                    LastEventResult = ExecuteInternal(new ToggleProducerCommand(Content.Producers[payload.Index].Id, payload.Enabled)); break;
                case SimulationEventKind.Prestige: LastEventResult = ExecuteInternal(new PrestigeCommand()); break;
                default: throw new InvalidOperationException("Unknown event kind.");
            }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double NextTime(double value) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1);
        private void AssertWriter()
        {
            if (Thread.CurrentThread.ManagedThreadId != writerThread) throw new InvalidOperationException("Simulation is single-writer and thread-affine.");
        }
    }
}
