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
        private readonly long[] rateRebuildCounts;
        private readonly BigDouble[] lifetime;
        private readonly BigDouble[] lifetimeBuffer;
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
        public BigDouble PrestigeCurrencyEarned { get; private set; }
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
            rateRebuildCounts = new long[balances.Length];
            integrationBuffer = new BigDouble[balances.Length];
            lifetime = new BigDouble[balances.Length]; lifetimeBuffer = new BigDouble[balances.Length];
            owned = new long[content.Producers.Count]; enabled = new bool[owned.Length];
            producerUnlocked = new bool[owned.Length]; producerFactors = new BigDouble[owned.Length];
            purchased = new bool[content.Upgrades.Count]; upgradeUnlocked = new bool[purchased.Length];
            ruleFired = new bool[content.Rules.Count]; ruleEligible = new double[ruleFired.Length]; ruleTokens = new EventToken[ruleFired.Length];
            ruleDisabled = new bool[ruleFired.Length];
            ruleArmed = new bool[ruleFired.Length];
            flags = new bool[content.Flags.Count];
            activeModifiers = new bool[content.Modifiers.Count];
            InitializeJournalCapacity();
            for (int i = 0; i < flags.Length; i++) flags[i] = content.Flags[i].Initial;
            var resourceViews = new ResourceView[balances.Length];
            var producerViews = new ProducerView[owned.Length];
            var upgradeViews = new UpgradeView[purchased.Length];
            for (int i = 0; i < balances.Length; i++)
            {
                int index = i; balances[i] = content.Resources[i].Initial; lifetime[i] = BigDouble.Max(BigDouble.Zero, balances[i]);
                resourceViews[i] = new ResourceView(content.Resources[i].Id, content.Resources[i].Cap,
                    () => balances[index], () => EffectiveRate(index), () => lifetime[index]);
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
            journalReady = true;
        }

        public ResourceView GetResource(string id) => Resources[Content.ResourceIndex(id)];
        /// <summary>Diagnostic only: includes initialization/rebuilds and is not persisted.</summary>
        public long RateRebuildCount(string resourceId) => rateRebuildCounts[Content.ResourceIndex(resourceId)];
        public ProducerView GetProducer(string id) => Producers[Content.ProducerIndex(id)];
        public UpgradeView GetUpgrade(string id) => Upgrades[Content.UpgradeIndex(id)];
        public bool HasRuleFired(string id)
        {
            if (id == null || !ruleIndices.TryGetValue(id, out int index)) throw new ArgumentException("Unknown rule id.", nameof(id));
            return ruleFired[index];
        }

        public CommandResult Execute(ISimulationCommand command)
        {
            AssertWriter();
            if (pendingTarget.HasValue) return CommandResult.Fail("Resume the pending advance before executing commands.");
            try { return ExecuteInternal(command); }
            finally { PublishChanges(); }
        }

        private CommandResult ExecuteInternal(ISimulationCommand command)
        {
            var result = ExecuteCommand(command);
            FlushResourceTriggers();
            return result;
        }

        private CommandResult ExecuteCommand(ISimulationCommand command)
        {
            try
            {
                if (command is GrantResourceCommand grant)
                {
                    int index = Content.ResourceIndex(grant.ResourceId);
                    return Grant(index, grant.Amount);
                }
                if (command is ClickCommand click)
                {
                    int index = Content.ResourceIndex(click.ResourceId);
                    if (!Number.IsFinite(click.BaseAmount) || click.BaseAmount < BigDouble.Zero) return CommandResult.Fail("Click amount must be finite and nonnegative.");
                    return Grant(index, clickModifiers[index].Apply(click.BaseAmount));
                }
                if (command is SetFlagCommand flag)
                {
                    int index = Content.FlagIndex(flag.FlagId); Mark(SimulationChangeKind.Flag, index);
                    bool changed = flags[index] != flag.Value; flags[index] = flag.Value;
                    foreach (int rule in Content.RulesForFlag(index)) RefreshRule(rule);
                    if (changed) NotifyTrigger(RuleTriggerKind.FlagChanged, index);
                    return CommandResult.Ok();
                }
                if (command is SetModifierCommand modifier) return SetModifier(Content.ModifierIndex(modifier.ModifierId), modifier.Enabled);
                if (command is TriggerRuleCommand triggerRule) return TriggerManualRule(triggerRule.RuleId);
                if (command is BuyProducerCommand buy) return BuyProducer(Content.ProducerIndex(buy.ProducerId), buy.Count);
                if (command is BuyUpgradeCommand upgrade) return BuyUpgrade(Content.UpgradeIndex(upgrade.UpgradeId));
                if (command is ToggleProducerCommand toggle)
                {
                    int index = Content.ProducerIndex(toggle.ProducerId);
                    if (!producerUnlocked[index]) return CommandResult.Fail("Producer is locked.");
                    if (!ValidProjectedRates(new Dictionary<int, BigDouble>
                        { [index] = Factor(index, owned[index], toggle.Enabled) }))
                        return CommandResult.Fail("Production overflows.");
                    Mark(SimulationChangeKind.Producer, index);
                    enabled[index] = toggle.Enabled; RefreshProducer(index); return CommandResult.Ok();
                }
                if (command is PrestigeCommand) return Prestige();
                if (TryExecuteRegistered(command, out var registered)) return registered;
                return CommandResult.Fail("Unknown command.");
            }
            catch (ArgumentException error) { return CommandResult.Fail(error.Message); }
            catch (ArithmeticException error) { return CommandResult.Fail(error.Message); }
        }

        public IReadOnlyList<ResourceAmount> GetProducerCost(string id, int count = 1)
        {
            AssertWriter();
            int index = Content.ProducerIndex(id);
            if (count < 1 || owned[index] > long.MaxValue - count) throw new ArgumentOutOfRangeException(nameof(count));
            var result = new List<ResourceAmount>();
            BigDouble scale = CostScale(index, count);
            foreach (var cost in Content.ProducerCosts(index))
                result.Add(new ResourceAmount(Content.Resources[cost.ResourceIndex].Id, Number.Multiply(cost.Amount, scale)));
            return result.AsReadOnly();
        }

        public EventToken Schedule(ISimulationCommand command, double time, int priority = 0)
        {
            AssertWriter();
            if (!Finite(time) || time < Time) throw new ArgumentOutOfRangeException(nameof(time));
            SimulationEvent payload;
            if (command is GrantResourceCommand grant)
            {
                int resource = Content.ResourceIndex(grant.ResourceId);
                ValidateGrant(resource, grant.Amount);
                payload = new SimulationEvent(SimulationEventKind.GrantResource, resource, grant.Amount);
            }
            else if (command is ClickCommand click)
            {
                Number.NonNegative(click.BaseAmount, nameof(command));
                payload = new SimulationEvent(SimulationEventKind.Click, Content.ResourceIndex(click.ResourceId), click.BaseAmount);
            }
            else if (command is SetFlagCommand flag)
                payload = new SimulationEvent(SimulationEventKind.SetFlag, Content.FlagIndex(flag.FlagId), enabled: flag.Value);
            else if (command is SetModifierCommand modifier)
                payload = new SimulationEvent(SimulationEventKind.SetModifier, Content.ModifierIndex(modifier.ModifierId), enabled: modifier.Enabled);
            else if (command is TriggerRuleCommand triggerRule)
            {
                if (!ruleIndices.TryGetValue(triggerRule.RuleId ?? "", out int rule)) throw new ArgumentException("Unknown rule id.");
                payload = new SimulationEvent(SimulationEventKind.TriggerRule, rule);
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
        public EventToken Reschedule(EventToken token, double time, int priority = 0)
        {
            AssertWriter();
            if (!Finite(time) || time < Time) throw new ArgumentOutOfRangeException(nameof(time));
            if (!scheduler.TryGet(token, out var entry) || entry.Payload.Kind == SimulationEventKind.Rule)
                throw new ArgumentException("Unknown, stale, or engine-owned event token.", nameof(token));
            return scheduler.Reschedule(token, time, priority, entry.Payload);
        }

        public AdvanceResult AdvanceTo(double target)
        {
            AssertWriter();
            try { return AdvanceCore(target); }
            finally { PublishChanges(); }
        }

        private AdvanceResult AdvanceCore(double target)
        {
            if (!Finite(target) || target < Time || (pendingTarget.HasValue && target < pendingTarget.Value))
                throw new ArgumentOutOfRangeException(nameof(target), "Time must be finite and monotonic, including a pending target.");
            if (!pendingTarget.HasValue)
            {
                PrepareSystems(target);
                systemsPreparedTarget = target;
            }
            pendingTarget = target;
            int processed = 0, work = 0, sameTime = 0;
            double lastEventTime = double.NaN;
            while (true)
            {
                double horizon = systemsPreparedTarget ?? target;
                while (true)
                {
                    if (scheduler.NextStoredTime.HasValue && scheduler.NextStoredTime.Value > horizon) break;
                    work += scheduler.PruneCancelled(EventBudget - work);
                    if (scheduler.HasCancelledHead)
                    {
                        if (scheduler.NextStoredTime.Value > horizon) break;
                        return new AdvanceResult(false, Time, processed, work);
                    }
                    if (!scheduler.TryPeek(out var entry) || entry.Time > horizon) break;
                    if (work == EventBudget) return new AdvanceResult(false, Time, processed, work,
                        sameTime > 1 ? AdvanceDiagnostic.SameTimeCascade : AdvanceDiagnostic.EventBudgetExhausted);
                    Integrate(entry.Time);
                    scheduler.TryDequeue(out entry); processed++; work++;
                    sameTime = entry.Time == lastEventTime ? sameTime + 1 : 1;
                    lastEventTime = entry.Time;
                    Process(entry);
                }
                Integrate(horizon);
                if (scheduler.NextStoredTime.HasValue && scheduler.NextStoredTime.Value <= horizon) continue;
                if (horizon == target) break;
                // Finish the already-prepared interval before extending systems using its final state.
                PrepareSystems(target);
                systemsPreparedTarget = target;
            }
            pendingTarget = null; systemsPreparedTarget = null;
            return new AdvanceResult(true, Time, processed, work);
        }

        public ulong NextRandomUInt64()
        {
            AssertWriter();
            if (injectedRandom != null) return NextInjectedRandom();
            ulong value = randomState;
            value ^= value << 13; value ^= value >> 7; value ^= value << 17;
            randomState = value; return value;
        }

        private CommandResult BuyProducer(int index, int count)
        {
            if (count < 1 || owned[index] > long.MaxValue - count) return CommandResult.Fail("Invalid producer count.");
            if (!producerUnlocked[index]) return CommandResult.Fail("Producer is locked.");
            if (!EvaluateCondition(Content.ProducerPrerequisiteCondition(index), Time)) return CommandResult.Fail("Producer prerequisites are not met.");
            BigDouble scale = CostScale(index, count);
            if (!CanPay(Content.ProducerCosts(index), scale)) return CommandResult.Fail("Insufficient resources or invalid cost.");
            // Validate every cost and all affected rates before mutating any state.
            BigDouble factor = Factor(index, owned[index] + count, enabled[index]);
            if (!ValidProjectedRates(new Dictionary<int, BigDouble> { [index] = factor })) return CommandResult.Fail("Production overflows.");
            Pay(Content.ProducerCosts(index), scale);
            Mark(SimulationChangeKind.Producer, index);
            owned[index] += count; RefreshProducer(index);
            foreach (var cost in Content.ProducerCosts(index)) RefreshRules(cost.ResourceIndex);
            foreach (int rule in Content.RulesForProducer(index)) RefreshRule(rule);
            NotifyTrigger(RuleTriggerKind.ProducerPurchased, index);
            return CommandResult.Ok();
        }

        private BigDouble CostScale(int index, int count)
        {
            double growth = Content.Producers[index].CostGrowth;
            BigDouble start = Number.Pow(new BigDouble(growth), owned[index]);
            if (growth == 1) return new BigDouble(count);
            // Binary composition avoids catastrophic cancellation in (growth^count - 1)/(growth - 1).
            BigDouble sum = BigDouble.Zero, power = BigDouble.One;
            BigDouble blockSum = BigDouble.One, blockPower = new BigDouble(growth);
            int remaining = count;
            while (remaining > 0)
            {
                if ((remaining & 1) != 0)
                {
                    sum = Number.Add(sum, Number.Multiply(power, blockSum));
                    if (remaining > 1) power = Number.Multiply(power, blockPower);
                }
                remaining >>= 1;
                if (remaining > 0) { blockSum = Number.Multiply(blockSum, Number.Add(BigDouble.One, blockPower)); blockPower = Number.Multiply(blockPower, blockPower); }
            }
            return Number.Multiply(start, sum);
        }

        private CommandResult BuyUpgrade(int index)
        {
            if (!upgradeUnlocked[index]) return CommandResult.Fail("Upgrade is locked.");
            if (!EvaluateCondition(Content.UpgradePrerequisiteCondition(index), Time)) return CommandResult.Fail("Upgrade prerequisites are not met.");
            if (purchased[index]) return CommandResult.Fail("Upgrade is already purchased.");
            if (!CanPay(Content.UpgradeCosts(index), BigDouble.One)) return CommandResult.Fail("Insufficient resources.");
            var targets = new SortedSet<int>();
            foreach (int producer in Content.AffectedProducers(index)) targets.Add(producer);
            var projected = new Dictionary<int, BigDouble>();
            var projectedModifiers = new Dictionary<int, StatModifiers[]>();
            foreach (int target in targets) projected.Add(target, Factor(target, owned[target], enabled[target], index));
            foreach (int target in targets) projectedModifiers.Add(target, BuildOutputModifiers(target, index));
            var clickTargets = new SortedSet<int>();
            foreach (var modifier in Content.UpgradeModifiers(index))
            {
                if (modifier.Target == ModifierTarget.Click)
                {
                    if (modifier.TargetIndex >= 0) clickTargets.Add(modifier.TargetIndex);
                    else for (int r = 0; r < balances.Length; r++) clickTargets.Add(r);
                }
            }
            if (!ValidProjectedRates(projected, projectedModifiers)) return CommandResult.Fail("Production overflows.");
            foreach (int resource in clickTargets)
                BuildClickModifiers(resource, index).Apply(BigDouble.One);
            Pay(Content.UpgradeCosts(index), BigDouble.One);
            Mark(SimulationChangeKind.Upgrade, index);
            purchased[index] = true;
            foreach (var pair in projectedModifiers) outputModifiers[pair.Key] = pair.Value;
            foreach (int resource in clickTargets) clickModifiers[resource] = BuildClickModifiers(resource);
            RefreshProducers(targets);
            foreach (var cost in Content.UpgradeCosts(index)) RefreshRules(cost.ResourceIndex);
            foreach (int rule in Content.RulesForUpgrade(index)) RefreshRule(rule);
            NotifyTrigger(RuleTriggerKind.UpgradePurchased, index);
            return CommandResult.Ok();
        }

        private bool CanPay(IReadOnlyList<IndexedAmount> costs, BigDouble scale)
        {
            if (!Number.IsFinite(scale) || scale < BigDouble.Zero) return false;
            foreach (var cost in costs)
            {
                BigDouble value = Number.Multiply(cost.Amount, scale);
                if (!Number.IsFinite(value) || (value > balances[cost.ResourceIndex] &&
                    Content.Resources[cost.ResourceIndex].NegativeAmountPolicy != NegativeAmountPolicy.Allow)) return false;
                NormalizeBalance(cost.ResourceIndex, Number.AddSigned(balances[cost.ResourceIndex], -value));
            }
            return true;
        }

        private void Pay(IReadOnlyList<IndexedAmount> costs, BigDouble scale)
        {
            foreach (var cost in costs)
            {
                int index = cost.ResourceIndex; Mark(SimulationChangeKind.Resource, index);
                BigDouble before = balances[index];
                balances[index] = Number.AddSigned(before, -Number.Multiply(cost.Amount, scale));
                if (before != balances[index]) changedResources.Add(index);
            }
        }

        private CommandResult Grant(int index, BigDouble amount)
        {
            ValidateGrant(index, amount);
            BigDouble value = GrantedBalance(index, balances[index], amount);
            BigDouble earned = EarnedAfter(index, balances[index], value);
            if (!Number.IsFinite(value) || !Number.IsFinite(earned)) return CommandResult.Fail("Grant overflows.");
            Mark(SimulationChangeKind.Resource, index);
            BigDouble before = balances[index];
            balances[index] = value; lifetime[index] = earned; RefreshRules(index);
            if (before != value) changedResources.Add(index);
            return CommandResult.Ok();
        }

        private BigDouble RequirementValue(IndexedRequirement requirement)
        {
            switch (requirement.Kind)
            {
                case RequirementKind.Resource: return balances[requirement.TargetIndex];
                case RequirementKind.LifetimeResource: return lifetime[requirement.TargetIndex];
                case RequirementKind.Producer: return new BigDouble(owned[requirement.TargetIndex]);
                case RequirementKind.Upgrade: return purchased[requirement.TargetIndex] ? BigDouble.One : BigDouble.Zero;
                case RequirementKind.Prestige: return new BigDouble(PrestigeCount);
                default: throw new InvalidOperationException("Unknown requirement.");
            }
        }
        private CommandResult Prestige()
        {
            var definition = Content.Prestige;
            if (definition == null) return CommandResult.Fail("Prestige is not configured.");
            BigDouble reward = BigDouble.Zero;
            BigDouble earnedCurrency = PrestigeCurrencyEarned;
            BigDouble rewardBalance = BigDouble.Zero, rewardLifetime = BigDouble.Zero;
            int rewardIndex = -1;
            BigDouble multiplier;
            if (definition.UsesLifetimeReward)
            {
                reward = GetPrestigeReward();
                if (reward <= BigDouble.Zero) return CommandResult.Fail("No unclaimed lifetime prestige reward.");
                rewardIndex = Content.ResourceIndex(definition.RewardResourceId);
                rewardBalance = GrantedBalance(rewardIndex, balances[rewardIndex], reward);
                if (rewardBalance - balances[rewardIndex] < reward) return CommandResult.Fail("Prestige currency cap prevents the full reward.");
                rewardLifetime = Number.Add(lifetime[rewardIndex], reward);
                earnedCurrency = Number.Add(PrestigeCurrencyEarned, reward);
                multiplier = Number.Add(BigDouble.One, Number.Multiply(earnedCurrency, definition.ProductionBonusPerReward));
            }
            else
            {
                if (balances[Content.ResourceIndex(definition.ResourceId)] < definition.Threshold) return CommandResult.Fail("Prestige threshold not reached.");
                multiplier = Number.Multiply(PermanentMultiplier, definition.MultiplierPerPrestige);
            }
            if (PrestigeCount == long.MaxValue || !Number.IsFinite(multiplier)) return CommandResult.Fail("Prestige overflows.");
            var resetBalances = (BigDouble[])balances.Clone();
            var resetLifetime = (BigDouble[])lifetime.Clone();
            for (int r = 0; r < balances.Length; r++)
            {
                if (r == rewardIndex) { resetBalances[r] = rewardBalance; resetLifetime[r] = rewardLifetime; continue; }
                var resource = Content.Resources[r];
                switch (resource.PrestigePolicy)
                {
                    case ResetPolicy.Reset:
                    case ResetPolicy.Recalculate: resetBalances[r] = resource.Initial; break;
                    case ResetPolicy.Transform: resetBalances[r] = Clamp(r, SignedMultiply(balances[r], resource.PrestigeTransform)); break;
                }
                if (resource.PrestigePolicy == ResetPolicy.Transform && resetBalances[r] > balances[r])
                    resetLifetime[r] = Number.Add(lifetime[r], resetBalances[r] - balances[r]);
            }
            var projected = new Dictionary<int, BigDouble>();
            var projectedModifiers = new Dictionary<int, StatModifiers[]>();
            var resetModifierState = new bool[activeModifiers.Length];
            for (int i = 0; i < owned.Length; i++)
            {
                long count = ResetProducerCount(i, definition);
                bool resetState = ProducerResetPolicy(i, definition) == ResetPolicy.Reset || ProducerResetPolicy(i, definition) == ResetPolicy.Recalculate;
                projected.Add(i, (!enabled[i] && !resetState) || count == 0 ? BigDouble.Zero : new BigDouble(count));
                projectedModifiers[i] = BuildOutputModifiers(i, prestigeReset: definition.ResetUpgrades, modifierState: resetModifierState);
            }
            for (int r = 0; r < balances.Length; r++)
                BuildClickModifiers(r, prestigeReset: definition.ResetUpgrades, modifierState: resetModifierState).Apply(BigDouble.One);
            if (!ValidProjectedRates(projected, projectedModifiers, multiplier)) return CommandResult.Fail("Prestige production overflows.");
            var moduleReset = PrepareModuleReset(out var moduleResetTransaction);
            if (!moduleReset.Success) return moduleReset;
            MarkReset();
            PermanentMultiplier = multiplier; PrestigeCount++;
            PrestigeCurrencyEarned = earnedCurrency;
            for (int r = 0; r < balances.Length; r++)
                if (balances[r] != resetBalances[r]) changedResources.Add(r);
            Array.Copy(resetBalances, balances, balances.Length); Array.Copy(resetLifetime, lifetime, lifetime.Length);
            for (int i = 0; i < owned.Length; i++)
            {
                owned[i] = ResetProducerCount(i, definition);
                var policy = ProducerResetPolicy(i, definition);
                if (policy == ResetPolicy.Reset || policy == ResetPolicy.Recalculate)
                { enabled[i] = true; producerUnlocked[i] = Content.Producers[i].InitiallyUnlocked; }
            }
            if (definition.ResetUpgrades)
                for (int i = 0; i < purchased.Length; i++)
                    if (!Content.Upgrades[i].Permanent)
                    { purchased[i] = false; upgradeUnlocked[i] = Content.Upgrades[i].InitiallyUnlocked; }
            for (int i = 0; i < ruleFired.Length; i++)
                if (Content.Rules[i].ResetOnPrestige) { ruleFired[i] = false; ruleDisabled[i] = false; ruleArmed[i] = false; ruleEligible[i] = Time; }
            for (int i = 0; i < flags.Length; i++) if (Content.Flags[i].ResetOnPrestige) flags[i] = Content.Flags[i].Initial;
            Array.Clear(activeModifiers, 0, activeModifiers.Length);
            moduleResetTransaction.Commit();
            CollectModuleChanges(moduleResetTransaction);
            RebuildAllRates(); RefreshAllRules(); NotifyTrigger(RuleTriggerKind.Prestige); return CommandResult.Ok();
        }

        private BigDouble Factor(int index, long count, bool active, int includeUpgrade = -1)
        {
            return !active || count == 0 ? BigDouble.Zero : new BigDouble(count);
        }

        private bool ValidFactor(int index, BigDouble factor)
        {
            if (!Number.IsFinite(factor)) return false;
            foreach (var output in Content.ProducerOutputs(index))
                if (!Number.IsFinite(Number.Multiply(output.Amount, factor))) return false;
            return true;
        }

        private bool ValidProjectedRates(Dictionary<int, BigDouble> projected, Dictionary<int, StatModifiers[]> projectedModifiers = null, BigDouble? permanent = null)
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
                        if (output.ResourceIndex == resource) total = Number.Add(total, Contribution(producer, resource, output.Amount, factor, projectedModifiers, permanent));
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
                    if (output.ResourceIndex == resource) rate = Number.Add(rate, Contribution(producer, resource, output.Amount, producerFactors[producer]));
            if (!Number.IsFinite(rate)) throw new InvalidOperationException("Production rate overflows.");
            if (rates[resource] != rate) Mark(SimulationChangeKind.Resource, resource);
            rates[resource] = rate;
            if (rateRebuildCounts[resource] < long.MaxValue) rateRebuildCounts[resource]++;
        }
        private void RebuildAllRates()
        {
            RebuildAllModifierCaches();
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
            value = NormalizeBalance(index, value);
            var cap = Content.Resources[index].Cap;
            return cap.HasValue && value > cap.Value ? cap.Value : value;
        }
        private BigDouble GrantedBalance(int index, BigDouble balance, BigDouble amount)
        {
            ValidateGrant(index, amount);
            var cap = Content.Resources[index].Cap;
            if (balance < BigDouble.Zero || amount < BigDouble.Zero) return Clamp(index, Number.AddSigned(balance, amount));
            return cap.HasValue && amount >= Number.AddSigned(cap.Value, -balance) ? cap.Value : Clamp(index, Number.AddSigned(balance, amount));
        }
        private void ValidateGrant(int index, BigDouble amount)
        {
            if (!Number.IsFinite(amount) || (amount < BigDouble.Zero &&
                Content.Resources[index].NegativeAmountPolicy == NegativeAmountPolicy.Reject))
                throw new ArgumentOutOfRangeException(nameof(amount), "Grant violates the resource negative amount policy.");
        }
        private BigDouble NormalizeBalance(int index, BigDouble value)
        {
            if (!Number.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value >= BigDouble.Zero) return value;
            switch (Content.Resources[index].NegativeAmountPolicy)
            {
                case NegativeAmountPolicy.Allow: return value;
                case NegativeAmountPolicy.ClampToZero: return BigDouble.Zero;
                default: throw new ArgumentOutOfRangeException(nameof(value), "Negative resource balance.");
            }
        }
        private BigDouble EarnedAfter(int index, BigDouble before, BigDouble after) =>
            after > before ? Number.Add(lifetime[index], Number.AddSigned(after, -before)) : lifetime[index];
        private static BigDouble SignedMultiply(BigDouble value, BigDouble factor) =>
            value < BigDouble.Zero ? -Number.Multiply(-value, factor) : Number.Multiply(value, factor);
        private void Integrate(double time)
        {
            double elapsed = time - Time;
            if (elapsed < 0) throw new InvalidOperationException("Event is in the past.");
            if (elapsed > 0)
            {
                for (int i = 0; i < balances.Length; i++)
                {
                    BigDouble produced;
                    try { produced = Number.Multiply(rates[i], new BigDouble(elapsed)); }
                    catch (ArithmeticException) when (Content.Resources[i].Cap.HasValue) { produced = Content.Resources[i].Cap.Value; }
                    BigDouble value = GrantedBalance(i, balances[i], produced);
                    BigDouble earned = EarnedAfter(i, balances[i], value);
                    if (!Number.IsFinite(value) || !Number.IsFinite(earned)) throw new InvalidOperationException("Resource integration overflows.");
                    integrationBuffer[i] = value;
                    lifetimeBuffer[i] = earned;
                }
                for (int i = 0; i < balances.Length; i++)
                    if (balances[i] != integrationBuffer[i]) Mark(SimulationChangeKind.Resource, i);
                // Commit the whole integration interval before arming event-driven rules.
                for (int i = 0; i < balances.Length; i++)
                {
                    BigDouble old = balances[i]; balances[i] = integrationBuffer[i]; integrationBuffer[i] = old;
                }
                Array.Copy(lifetimeBuffer, lifetime, lifetime.Length);
            }
            Time = time;
            if (elapsed > 0)
                for (int i = 0; i < balances.Length; i++)
                    if (balances[i] != integrationBuffer[i]) NotifyTrigger(RuleTriggerKind.ResourceChanged, i);
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
            if (hasDueTime && scheduler.TryGet(ruleTokens[index], out var current))
            {
                if (current.Time != due) ruleTokens[index] = scheduler.Reschedule(current.Token, due, 100, current.Payload);
                return;
            }
            scheduler.Cancel(ruleTokens[index]); ruleTokens[index] = default;
            if (hasDueTime) ruleTokens[index] = scheduler.Schedule(due, 100, new SimulationEvent(SimulationEventKind.Rule, index));
        }
        private bool TryRuleTime(int index, out double due)
        {
            due = 0;
            var rule = Content.Rules[index];
            if (ruleDisabled[index] || (ruleFired[index] && !rule.Repeat)) return false;
            if (!ContinuousTrigger(rule.Trigger.Kind) && !ruleArmed[index]) return false;
            return TryConditionTime(index, out due);
        }
        private void Process(ScheduledEvent<SimulationEvent> entry)
        {
            try { ProcessEvent(entry); }
            catch (ArithmeticException error)
            {
                LastEventResult = CommandResult.Fail(error.Message);
                if (entry.Payload.Kind == SimulationEventKind.Rule)
                { ruleFired[entry.Payload.Index] = true; ruleDisabled[entry.Payload.Index] = true; }
            }
        }
        private void ProcessEvent(ScheduledEvent<SimulationEvent> entry)
        {
            var payload = entry.Payload;
            switch (payload.Kind)
            {
                case SimulationEventKind.Rule:
                    ProcessRule(payload.Index); break;
                case SimulationEventKind.GrantResource:
                    LastEventResult = ExecuteInternal(new GrantResourceCommand(Content.Resources[payload.Index].Id, payload.Amount)); break;
                case SimulationEventKind.BuyProducer:
                    LastEventResult = ExecuteInternal(new BuyProducerCommand(Content.Producers[payload.Index].Id, payload.Count)); break;
                case SimulationEventKind.BuyUpgrade:
                    LastEventResult = ExecuteInternal(new BuyUpgradeCommand(Content.Upgrades[payload.Index].Id)); break;
                case SimulationEventKind.ToggleProducer:
                    LastEventResult = ExecuteInternal(new ToggleProducerCommand(Content.Producers[payload.Index].Id, payload.Enabled)); break;
                case SimulationEventKind.Prestige: LastEventResult = ExecuteInternal(new PrestigeCommand()); break;
                case SimulationEventKind.Click:
                    LastEventResult = ExecuteInternal(new ClickCommand(Content.Resources[payload.Index].Id, payload.Amount)); break;
                case SimulationEventKind.SetFlag:
                    LastEventResult = ExecuteInternal(new SetFlagCommand(Content.Flags[payload.Index].Id, payload.Enabled)); break;
                case SimulationEventKind.SetModifier:
                    LastEventResult = ExecuteInternal(new SetModifierCommand(Content.Modifiers[payload.Index].Id, payload.Enabled)); break;
                case SimulationEventKind.TriggerRule:
                    LastEventResult = ExecuteInternal(new TriggerRuleCommand(Content.Rules[payload.Index].Id)); break;
                default: throw new InvalidOperationException("Unknown event kind.");
            }
            if (payload.Kind != SimulationEventKind.Rule)
                NotifyTrigger(RuleTriggerKind.ScheduledEvent, (int)payload.Kind);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double NextTime(double value) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1);
        private void AssertWriter()
        {
            if (notifying) throw new InvalidOperationException("Simulation cannot be mutated during change notifications.");
            if (Thread.CurrentThread.ManagedThreadId != writerThread) throw new InvalidOperationException("Simulation is single-writer and thread-affine.");
        }
    }
}
