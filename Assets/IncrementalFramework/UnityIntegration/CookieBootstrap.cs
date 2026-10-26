using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BreakInfinity;
using IncrementalFramework;
using UnityEngine;

namespace IdleHands.UnityIntegration
{
    public sealed class CookieBootstrap : MonoBehaviour
    {
        [SerializeField] private GameContentAsset content;
        [SerializeField, Min(0.05f)] private float uiRefreshSeconds = 0.1f;
        [SerializeField] private string saveFileName = "cookie-demo.json";
        [SerializeField] private bool autoLoad = true;
        private CompiledGame compiled;
        private CookiePresenter presenter;
        private bool dirty = true;
        private float nextRefresh;
        private bool paused;
        private bool reloadAfterPause;
        private OfflineResumeResult offlineResume;
        public Simulation Simulation { get; private set; }
        public bool SaveOnLifecycle { get; set; } = true;
        public string SavePath => Path.Combine(Application.persistentDataPath, Path.GetFileName(saveFileName));

        private void Awake()
        {
            try
            {
                compiled = ContentCompiler.Compile(content);
                presenter = new CookiePresenter(transform, content, Bake,
                    id => Execute(new BuyProducerCommand(id)),
                    id => Execute(new BuyUpgradeCommand(id)),
                    () => Execute(new PrestigeCommand()), Save, Load, id => Execute(new TriggerRuleCommand(id)));
                SetSimulation(new Simulation(compiled.Content));
                if (autoLoad && (File.Exists(SavePath) || File.Exists(SavePath + ".bak"))) Load();
                Refresh();
            }
            catch (Exception error)
            {
                Debug.LogError("CookieDemo could not start: " + error.Message, this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (Simulation == null || paused) return;
            if (offlineResume != null && !offlineResume.Completed)
            {
                offlineResume.Continue();
                dirty = true;
            }
            else
            {
                var pending = Simulation.PendingTarget;
                var result = Simulation.AdvanceTo(pending ?? (Simulation.Time + Time.unscaledDeltaTime));
                if (result.ProcessedEvents != 0 || pending != Simulation.PendingTarget) dirty = true;
            }
            if (dirty && Time.unscaledTime >= nextRefresh)
            {
                Refresh();
                nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, uiRefreshSeconds);
                dirty = false;
            }
        }

        public CommandResult Execute(ISimulationCommand command)
        {
            var result = Simulation.Execute(command);
            presenter.Status(result.Success ? "Done." : result.Error);
            return result;
        }

        public void Bake()
        {
            Execute(new ClickCommand(compiled.ClickResourceId, compiled.ClickAmount));
        }

        public void Save()
        { TrySave(); }

        private bool TrySave()
        {
            if (Simulation == null) return false;
            try
            {
                SnapshotService.Save(SavePath, Simulation, SystemClock.Instance);
                presenter.Status("Bakery saved.");
                return true;
            }
            catch (Exception error)
            { presenter.Status("Save failed: " + error.Message); return false; }
        }

        public void Load()
        {
            if (compiled == null) return;
            try
            {
                OfflineResumeResult resumed;
                try { resumed = ReadResume(SavePath); }
                catch (Exception) when (File.Exists(SavePath + ".bak"))
                { resumed = ReadResume(SavePath + ".bak"); }
                SetSimulation(resumed.Simulation);
                offlineResume = resumed;
                presenter.Status("Saved bakery loaded.");
                dirty = true;
            }
            catch (Exception error)
            { presenter.Status("Load failed; current bakery was not changed: " + error.Message); }
        }

        private OfflineResumeResult ReadResume(string path)
        {
            var snapshot = SnapshotService.ReadSnapshot(path);
            return SnapshotService.Resume(compiled.Content, snapshot, SystemClock.Instance, compiled.MaximumOfflineSeconds);
        }

        private void SetSimulation(Simulation simulation)
        {
            if (Simulation != null) Simulation.Changed -= OnChanged;
            Simulation = simulation;
            Simulation.Changed += OnChanged;
            dirty = true;
        }

        private void OnChanged(SimulationChangeSet changes) { dirty = true; }

        private void Refresh()
        {
            foreach (var resource in content.resources)
            {
                var view = Simulation.GetResource(resource.id);
                string click = resource.id == compiled.ClickResourceId
                    ? "  |  Click to bake (base +" + compiled.FormatResource(resource.id, compiled.ClickAmount) + ")" : "";
                string cap = view.Cap.HasValue ? " / " + compiled.FormatResource(resource.id, view.Cap.Value) : "";
                presenter.Resource(resource.id, resource.displayName + ": " + compiled.FormatResource(resource.id, view.Balance) + cap
                    + "  (" + compiled.FormatResource(resource.id, view.Rate) + "/sec)" + click);
            }
            foreach (var producer in content.producers)
            {
                var view = Simulation.GetProducer(producer.id);
                var definition = compiled.Content.Producers[compiled.Content.ProducerIndex(producer.id)];
                var cost = Simulation.GetProducerCost(producer.id);
                bool allowed = view.Unlocked && MeetsCondition(definition.PrerequisiteCondition);
                string locked = view.Unlocked ? DescribeCondition(definition.PrerequisiteCondition) : "awaiting unlock rule";
                presenter.Producer(producer.id, producer.displayName + "  [" + view.Owned + "]  |  "
                    + (allowed ? "Buy: " + Costs(cost) : "Locked: " + locked),
                    allowed && Affordable(cost) && !Simulation.PendingTarget.HasValue);
            }
            foreach (var upgrade in content.upgrades)
            {
                var view = Simulation.GetUpgrade(upgrade.id);
                var definition = compiled.Content.Upgrades[compiled.Content.UpgradeIndex(upgrade.id)];
                bool allowed = view.Unlocked && MeetsCondition(definition.PrerequisiteCondition);
                string locked = view.Unlocked ? DescribeCondition(definition.PrerequisiteCondition) : "awaiting unlock rule";
                presenter.Upgrade(upgrade.id, upgrade.displayName + "  |  "
                    + (view.Purchased ? "Purchased" : allowed ? "Buy: " + Costs(definition.Costs)
                    : "Locked: " + locked),
                    !view.Purchased && allowed && Affordable(definition.Costs) && !Simulation.PendingTarget.HasValue);
            }
            var ruleStates = content.rules.Length == 0 ? null
                : Simulation.ExportState().Rules.ToDictionary(x => x.Id, x => x.Fired);
            foreach (var rule in content.rules)
            {
                var definition = compiled.Content.Rules.First(x => x.Id == rule.id);
                presenter.Rule(rule.id, (rule.triggerKind == RuleTriggerKind.Manual ? "TRIGGER / " : "")
                    + (ruleStates[rule.id] ? "UNLOCKED  " : "LOCKED  ") + rule.displayName
                    + "  |  " + DescribeCondition(definition.Condition),
                    !Simulation.PendingTarget.HasValue && (!ruleStates[rule.id] || definition.Repeat)
                    && MeetsCondition(definition.Condition));
            }
            var prestige = compiled.Content.Prestige;
            if (prestige != null && prestige.UsesLifetimeReward)
            {
                var reward = Simulation.GetPrestigeReward();
                presenter.Prestige("Prestige [" + Simulation.PrestigeCount + "]  |  Claim "
                    + compiled.FormatResource(prestige.RewardResourceId, reward) + " " + prestige.RewardResourceId
                    + "; lifetime " + compiled.FormatResource(prestige.ResourceId, Simulation.GetResource(prestige.ResourceId).LifetimeEarned)
                    + " / base " + compiled.FormatResource(prestige.ResourceId, prestige.Threshold)
                    + " " + prestige.ResourceId + ". Apply reset policies; production x" + Format(Simulation.PermanentMultiplier),
                    reward > BigDouble.Zero && !Simulation.PendingTarget.HasValue);
            }
            else presenter.Prestige(prestige == null ? "Prestige disabled" : "Prestige [" + Simulation.PrestigeCount
                    + "]  |  Requires " + compiled.FormatResource(prestige.ResourceId, prestige.Threshold) + " " + prestige.ResourceId
                    + "; apply reset policies, retain x" + Format(prestige.MultiplierPerPrestige) + " production per prestige",
                    prestige != null && Simulation.GetResource(prestige.ResourceId).Balance >= prestige.Threshold
                    && !Simulation.PendingTarget.HasValue);
            if (offlineResume != null)
            {
                if (!offlineResume.Completed) presenter.Offline("Processing saved offline progress: "
                    + offlineResume.RemainingSeconds.ToString("F1") + " seconds remaining; last chunk "
                    + offlineResume.Advance.ProcessedEvents + " events / " + offlineResume.Advance.ProcessedWork + " work units.");
                else
                {
                    presenter.Offline("Welcome back! Simulated " + offlineResume.OfflineSeconds.ToString("F1") + " seconds (cap "
                        + compiled.MaximumOfflineSeconds.ToString("F0") + "s): "
                        + string.Join(", ", offlineResume.Gains.Select(x => x.ResourceId + " "
                            + compiled.FormatResource(x.ResourceId, x.NetGain))));
                    offlineResume = null;
                }
            }
        }

        private bool MeetsConditions(IReadOnlyList<RequirementDefinition> requirements)
        {
            foreach (var requirement in requirements)
            {
                BigDouble value;
                switch (requirement.Kind)
                {
                    case IncrementalFramework.RequirementKind.Resource:
                        value = Simulation.GetResource(requirement.TargetId).Balance; break;
                    case IncrementalFramework.RequirementKind.LifetimeResource:
                        value = Simulation.GetResource(requirement.TargetId).LifetimeEarned; break;
                    case IncrementalFramework.RequirementKind.Producer:
                        value = new BigDouble(Simulation.GetProducer(requirement.TargetId).Owned); break;
                    case IncrementalFramework.RequirementKind.Upgrade:
                        value = Simulation.GetUpgrade(requirement.TargetId).Purchased ? BigDouble.One : BigDouble.Zero; break;
                    case IncrementalFramework.RequirementKind.Prestige:
                        value = new BigDouble(Simulation.PrestigeCount); break;
                    default: return false;
                }
                if (value < requirement.Amount) return false;
            }
            return true;
        }

        private bool MeetsCondition(ConditionDefinition condition)
        {
            switch (condition.Kind)
            {
                case ConditionKind.Requirement: return MeetsConditions(new[] { condition.Requirement });
                case ConditionKind.Flag: return Simulation.GetFlag(condition.FlagId) == condition.Expected;
                case ConditionKind.And: return condition.Children.All(MeetsCondition);
                case ConditionKind.Or: return condition.Children.Any(MeetsCondition);
                case ConditionKind.Not: return !MeetsCondition(condition.Children[0]);
                default: return false;
            }
        }

        private bool Affordable(IReadOnlyList<IncrementalFramework.ResourceAmount> costs)
        {
            return costs.All(x => compiled.Content.Resources[compiled.Content.ResourceIndex(x.ResourceId)].NegativeAmountPolicy
                == NegativeAmountPolicy.Allow || Simulation.GetResource(x.ResourceId).Balance >= x.Amount);
        }

        private string Costs(IReadOnlyList<IncrementalFramework.ResourceAmount> costs)
        { return costs.Count == 0 ? "Free" : string.Join(", ", costs.Select(x => compiled.FormatResource(x.ResourceId, x.Amount) + " " + x.ResourceId)); }

        private static string DescribeCondition(ConditionDefinition condition)
        {
            switch (condition.Kind)
            {
                case ConditionKind.Requirement:
                    return condition.Requirement.Kind + " " + condition.Requirement.TargetId + " >= " + Format(condition.Requirement.Amount);
                case ConditionKind.Flag: return condition.FlagId + " = " + condition.Expected;
                case ConditionKind.Not: return "NOT (" + DescribeCondition(condition.Children[0]) + ")";
                case ConditionKind.And:
                case ConditionKind.Or:
                    var children = condition.Kind == ConditionKind.And
                        ? condition.Children.Where(x => x.Kind != ConditionKind.And || x.Children.Count != 0).ToArray()
                        : condition.Children.ToArray();
                    if (children.Length == 0) return condition.Kind == ConditionKind.And ? "Available" : "Never";
                    return "(" + string.Join(condition.Kind == ConditionKind.And ? " AND " : " OR ", children.Select(DescribeCondition)) + ")";
                default: return "Invalid condition";
            }
        }

        private static string Format(BigDouble number) { return number.ToString("G4"); }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                reloadAfterPause = SaveOnLifecycle && TrySave();
                paused = true;
            }
            else if (paused)
            {
                paused = false;
                if (reloadAfterPause) Load();
                reloadAfterPause = false;
            }
        }

        private void OnApplicationQuit() { if (SaveOnLifecycle) Save(); }
        private void OnDestroy()
        {
            if (Simulation != null) Simulation.Changed -= OnChanged;
        }
    }
}
