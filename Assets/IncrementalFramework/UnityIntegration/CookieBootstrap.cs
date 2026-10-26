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
        private Dictionary<string, BigDouble> offlineBefore;
        private double offlineSeconds;
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
                    () => Execute(new PrestigeCommand()), Save, Load);
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
            Simulation.AdvanceTo(Simulation.PendingTarget ?? (Simulation.Time + Time.unscaledDeltaTime));
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
            Execute(new GrantResourceCommand(compiled.ClickResourceId, compiled.ClickAmount));
        }

        public void Save()
        {
            if (Simulation == null) return;
            try
            {
                SnapshotService.Save(SavePath, Simulation, DateTime.UtcNow);
                presenter.Status("Bakery saved.");
            }
            catch (Exception error)
            { presenter.Status("Save failed: " + error.Message); }
        }

        public void Load()
        {
            if (compiled == null) return;
            try
            {
                SimulationSnapshot snapshot;
                try { snapshot = SnapshotService.Deserialize(File.ReadAllText(SavePath)); }
                catch (Exception) when (File.Exists(SavePath + ".bak"))
                { snapshot = SnapshotService.Deserialize(File.ReadAllText(SavePath + ".bak")); }
                var restored = SnapshotService.Restore(compiled.Content, snapshot);
                var before = restored.Resources.ToDictionary(x => x.Id, x => x.Balance);
                double seconds = Math.Min(compiled.MaximumOfflineSeconds,
                    Math.Max(0, (DateTime.UtcNow - snapshot.SavedAtUtc).TotalSeconds));
                double target = (restored.PendingTarget ?? restored.Time) + seconds;
                restored.AdvanceTo(target);
                SetSimulation(restored);
                offlineBefore = before;
                offlineSeconds = seconds;
                presenter.Status("Saved bakery loaded.");
                dirty = true;
            }
            catch (Exception error)
            { presenter.Status("Load failed; current bakery was not changed: " + error.Message); }
        }

        private void SetSimulation(Simulation simulation)
        {
            if (Simulation != null) Simulation.Changed -= OnChanged;
            Simulation = simulation;
            Simulation.Changed += OnChanged;
            dirty = true;
        }

        private void OnChanged() { dirty = true; }

        private void Refresh()
        {
            foreach (var resource in content.resources)
            {
                var view = Simulation.GetResource(resource.id);
                string click = resource.id == compiled.ClickResourceId ? "  |  Click to bake +" + Format(compiled.ClickAmount) : "";
                presenter.Resource(resource.id, resource.displayName + ": " + Format(view.Balance)
                    + "  (" + Format(view.Rate) + "/sec)" + click);
            }
            foreach (var producer in content.producers)
            {
                var view = Simulation.GetProducer(producer.id);
                var cost = Simulation.GetProducerCost(producer.id);
                bool allowed = view.Unlocked && Meets(producer.prerequisites);
                presenter.Producer(producer.id, producer.displayName + "  [" + view.Owned + "]  |  "
                    + (allowed ? "Buy: " + Costs(cost) : "Locked: " + Requirements(producer.prerequisites)),
                    allowed && Affordable(cost) && !Simulation.PendingTarget.HasValue);
            }
            foreach (var upgrade in content.upgrades)
            {
                var view = Simulation.GetUpgrade(upgrade.id);
                var definition = compiled.Content.Upgrades[compiled.Content.UpgradeIndex(upgrade.id)];
                bool allowed = view.Unlocked && Meets(upgrade.prerequisites);
                presenter.Upgrade(upgrade.id, upgrade.displayName + "  |  "
                    + (view.Purchased ? "Purchased" : allowed ? "Buy: " + Costs(definition.Costs)
                    : "Locked: " + Requirements(upgrade.prerequisites)),
                    !view.Purchased && allowed && Affordable(definition.Costs) && !Simulation.PendingTarget.HasValue);
            }
            foreach (var rule in content.rules)
                presenter.Rule(rule.id, (Simulation.HasRuleFired(rule.id) ? "UNLOCKED  " : "LOCKED  ")
                    + rule.displayName + "  |  "
                    + (rule.triggerResource == null ? compiled.ClickResourceId : rule.triggerResource.id)
                    + " >= " + rule.threshold + " + " + Requirements(rule.conditions));
            var prestige = compiled.Content.Prestige;
            presenter.Prestige(prestige == null ? "Prestige disabled" : "Prestige [" + Simulation.PrestigeCount
                + "]  |  Requires " + Format(prestige.Threshold) + " " + prestige.ResourceId
                + "; reset bakery, retain x" + Format(prestige.MultiplierPerPrestige) + " production per prestige",
                prestige != null && Simulation.GetResource(prestige.ResourceId).Balance >= prestige.Threshold
                && !Simulation.PendingTarget.HasValue);
            if (offlineBefore != null)
            {
                if (Simulation.PendingTarget.HasValue) presenter.Offline("Processing saved offline progress…");
                else
                {
                    presenter.Offline("Welcome back! Simulated " + offlineSeconds.ToString("F1") + " seconds (cap "
                        + compiled.MaximumOfflineSeconds.ToString("F0") + "s): "
                        + string.Join(", ", Simulation.Resources.Select(x => x.Id + " "
                            + Format(x.Balance - offlineBefore[x.Id]))));
                    offlineBefore = null;
                }
            }
        }

        private bool Meets(Requirement[] requirements)
        {
            return Simulation.MeetsConditions(requirements.Select(x => new ConditionDefinition(
                (ConditionKind)(int)x.kind, x.targetId, ContentCompiler.ReadNumber(x.amount))).ToArray());
        }

        private bool Affordable(IReadOnlyList<IncrementalFramework.ResourceAmount> costs)
        { return costs.All(x => Simulation.GetResource(x.ResourceId).Balance >= x.Amount); }

        private static string Costs(IReadOnlyList<IncrementalFramework.ResourceAmount> costs)
        { return costs.Count == 0 ? "Free" : string.Join(", ", costs.Select(x => Format(x.Amount) + " " + x.ResourceId)); }

        private static string Requirements(Requirement[] requirements)
        {
            return requirements.Length == 0 ? "Available" : string.Join(" + ", requirements.Select(
                x => x.kind + " " + x.targetId + " >= " + x.amount));
        }

        private static string Format(BigDouble number) { return number.ToString("G4"); }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused) { if (SaveOnLifecycle) Save(); paused = true; }
            else if (paused) { paused = false; if (SaveOnLifecycle) Load(); }
        }

        private void OnApplicationQuit() { if (SaveOnLifecycle) Save(); }
        private void OnDestroy()
        {
            if (Simulation != null) Simulation.Changed -= OnChanged;
        }
    }
}
