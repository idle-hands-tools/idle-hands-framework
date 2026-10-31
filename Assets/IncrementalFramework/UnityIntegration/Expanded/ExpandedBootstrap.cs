using System;
using System.IO;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using UnityEngine;

namespace IncrementalFramework.Unity.Expanded
{
    public sealed class ExpandedBootstrap : MonoBehaviour
    {
        public string SampleId = "OrbitalFoundry";
        public StoryAsset Story;
        public bool SaveOnLifecycle = true;
        public FeatureGame Game { get; private set; }
        public SampleTutorialSession Tutorial { get; private set; }
        public string Status { get; private set; } = "Ready.";
        public bool CatchingUp => target > Game.Time + 0.1;
        private ExpandedPresenter presenter;
        private double target, wall;
        private float nextSave;
        private bool paused;
        private bool needsRebuild;
        private string narrativePosition;
        private bool won;
        private string orderProjection;
        private float nextProjection;
        private System.Collections.Generic.Dictionary<string,BreakInfinity.BigDouble> offlineBefore;
        private double offlineSeconds;
        public string SaveKey => "idle-hands.expanded." + (Story != null ? Story.Compile().Id : SampleId) + ".v1";
        public string SavePath => Path.Combine(Application.persistentDataPath, SaveKey + ".json");
        private IFeatureModule Create() => Story != null ? new NarrativeModule(Story.Compile()) : ExpandedCampaigns.Create(SampleId);
        private void Awake()
        {
            Tutorial=new SampleTutorialSession(SaveKey,SampleTutorialSession.Expanded(Story!=null?"Narrative":SampleId));
            Game = new FeatureGame(Create()); wall = Time.realtimeSinceStartupAsDouble;
            presenter = new ExpandedPresenter(transform, this);
            if (SaveOnLifecycle && HasSave()) Load();
            nextSave = Time.unscaledTime + 30;
            presenter.Rebuild();
        }
        private bool HasSave() => Application.platform == RuntimePlatform.WebGLPlayer ? PlayerPrefs.HasKey(SaveKey) : File.Exists(SavePath) || File.Exists(SavePath + ".bak");
        private void Update()
        {
            if (paused) return;
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = Math.Min(FeatureGame.OfflineCap, Math.Max(0, now - wall)); wall = now;
            if (!CatchingUp) target = Game.Time + elapsed;
            Game.AdvanceTo(Math.Max(Game.Time, target), 128);
            if (Game.Module.AdvanceLimit.HasValue) target = Game.Time;
            if(offlineBefore!=null && !CatchingUp)
            {
                Status="Offline return ("+Math.Floor(offlineSeconds)+"s): "+FeatureSummary.Describe(offlineBefore,Game.Module);
                offlineBefore=null;
            }
            var narrative = Game.Module as NarrativeModule;
            string position = narrative?.Token;
            if (position != narrativePosition) { narrativePosition = position; needsRebuild = true; }
            if(won!=Game.Module.IsWon){won=Game.Module.IsWon;needsRebuild=true;}
            if(Game.Module is MarketModule market && Time.unscaledTime>=nextProjection)
            {
                nextProjection=Time.unscaledTime+.25f;
                string projection=string.Join(";",System.Linq.Enumerable.Select(market.State.Orders,o=>o.Id+":"+o.Remaining));
                if(projection!=orderProjection){orderProjection=projection;needsRebuild=true;}
            }
            if (needsRebuild) { needsRebuild = false; presenter.Rebuild(); }
            presenter.Tick();
            if (SaveOnLifecycle && Time.unscaledTime >= nextSave && !CatchingUp) { Save(); nextSave = Time.unscaledTime + 30; }
        }
        public bool Act(string action, string subject = "", string argument = "", double amount = 0, int quantity = 10)
        {
            if (CatchingUp) { Status = "Finishing offline work. Actions will be available shortly."; return false; }
            var result = Game.Execute(action, subject, argument, amount, quantity);
            Status = result.Success ? Game.Module.Message : result.Error;
            if(result.Success)Tutorial.RecordAction(action);
            needsRebuild = true; return result.Success;
        }
        public void RefreshView() => needsRebuild = true;
        public void Save()
        {
            try
            {
                string json = Game.Save(DateTime.UtcNow.AddSeconds(-Math.Max(0, target - Game.Time)));
                if (Application.platform == RuntimePlatform.WebGLPlayer) { PlayerPrefs.SetString(SaveKey, json); PlayerPrefs.Save(); }
                else
                {
                    File.WriteAllText(SavePath + ".tmp", json);
                    if (File.Exists(SavePath)) File.Replace(SavePath + ".tmp", SavePath, SavePath + ".bak");
                    else File.Move(SavePath + ".tmp", SavePath);
                }
                Tutorial.Save();
                Status = "Saved locally.";
            }
            catch (Exception e) { Status = "Save failed: " + e.Message; }
        }
        public void Load()
        {
            try
            {
                FeatureGame loaded; double offline;
                if (Application.platform == RuntimePlatform.WebGLPlayer) loaded = FeatureGame.Load(Create(), PlayerPrefs.GetString(SaveKey), DateTime.UtcNow, out offline);
                else
                {
                    try { loaded = FeatureGame.Load(Create(), File.ReadAllText(SavePath), DateTime.UtcNow, out offline); }
                    catch (Exception) when (File.Exists(SavePath + ".bak")) { loaded = FeatureGame.Load(Create(), File.ReadAllText(SavePath + ".bak"), DateTime.UtcNow, out offline); }
                }
                Game = loaded; Tutorial.Load(); offlineSeconds=offline;offlineBefore=offline>0?FeatureSummary.Capture(Game.Module):null;target = Game.Time + offline; wall = Time.realtimeSinceStartupAsDouble;
                Status = offline > 0 ? "Loaded. Processing " + Math.Floor(offline) + "s of offline progress (8h maximum)." : "Loaded at the saved passage.";
                needsRebuild = true;
            }
            catch (Exception e) { Status = "Load failed; current game retained: " + e.Message; }
        }
        private void OnApplicationPause(bool value)
        {
            if (value && !paused && SaveOnLifecycle) Save();
            if (!value && paused && SaveOnLifecycle && HasSave()) Load();
            paused = value; wall = Time.realtimeSinceStartupAsDouble;
        }
        private void OnApplicationFocus(bool value) { if (!value && SaveOnLifecycle && Game != null) Save(); }
        private void OnApplicationQuit() { if (SaveOnLifecycle && Game != null) Save(); }
        private void OnDestroy() {presenter?.Dispose();Tutorial?.Dispose();}
    }
}
