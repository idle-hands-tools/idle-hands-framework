using IncrementalFramework.Unity;
using IncrementalFramework.Unity.Rpg;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.IO;
using System.Linq;
using IncrementalFramework.Games.Rpg;
using UnityEngine;

namespace IncrementalFramework.Samples.IdleRpg
{
    public sealed class GuildRpgBootstrap : MonoBehaviour, IGuildRpgController
    {
        public GuildGame Game { get; private set; }
        public SampleTutorialSession Tutorial { get; private set; }
        public bool SaveOnLifecycle { get; set; } = true;
        public const string SaveKey = "idle-hands.ember-guild-v3";
        public string SavePath => Path.Combine(Application.persistentDataPath, "ember-guild-v3.json");
        private GuildRpgPresenter presenter;
        private UnityDiagnostics diagnostics;
        private double targetTime;
        private double wallTick;
        private float nextSave;
        private bool paused, savedAtPause;
        private string status = "Hire a Guardian, Ranger, Mage, and Cleric for a balanced first party.";
        public bool CatchingUp => targetTime > Game.Time + 0.1;

        private void Awake()
        {
            Tutorial=new SampleTutorialSession(SaveKey,new[]{
                new SampleTutorialSession.Stage("hire","game-action:hire","Recruit adventurers","Hire a Guardian to begin. A Guardian, Ranger, Mage, and Cleric make a balanced full party."),
                new SampleTutorialSession.Stage("assign","game-action:assign","Form a party","Assign adventurers to the selected party in ROSTER. Complementary classes make expeditions safer."),
                new SampleTutorialSession.Stage("quest","game-action:quest","Start an expedition","Open QUESTS and dispatch a patrol. Watch health and battle reports under PARTIES.")});
            Game = new GuildGame(EmberGuild.Create());
            diagnostics=new UnityDiagnostics(Game.World);
            targetTime = Game.Time;
            presenter = new GuildRpgPresenter(transform, this,Tutorial);
            if (HasSave()) Load();
            presenter.Refresh();
            wallTick = Time.realtimeSinceStartupAsDouble;
            nextSave = Time.unscaledTime + 30;
        }
        private bool HasSave() => Application.platform == RuntimePlatform.WebGLPlayer
            ? PlayerPrefs.HasKey(SaveKey) : File.Exists(SavePath) || File.Exists(SavePath + ".bak");
        private void Update()
        {
            if (paused) return;
            // Monotonic wall time also covers browser-tab throttling; cap a single absence at eight hours.
            double wallNow = Time.realtimeSinceStartupAsDouble;
            double elapsed = Math.Min(GuildDefinition.OfflineCap, Math.Max(0, wallNow - wallTick));
            wallTick = wallNow;
            if (!CatchingUp) targetTime = Math.Max(targetTime, Game.Time) + elapsed;
            Game.AdvanceTo(targetTime, 128);
            PollTutorial();
            if (SaveOnLifecycle && Time.unscaledTime >= nextSave && !CatchingUp)
            { Save(); nextSave = Time.unscaledTime + 30; }
        }
        public void Act(Func<bool> action)
        {
            if (CatchingUp) { status = "Finishing offline quests. Actions will be ready shortly."; return; }
            bool succeeded=action();
            if(succeeded)PollTutorial();
            status = Game.LastMessage;
            presenter.Rebuild();
        }
        private void PollTutorial()=>Tutorial?.Poll(stage=>stage=="hire"?Game.State.Heroes.Count>0:stage=="assign"?Game.State.Heroes.Any(h=>h.Party>=0):stage=="quest"&&Game.State.Parties.Any(p=>p.Active||p.Victories>0));
        public string Status => CatchingUp ? "Simulating offline production and combat: " + Math.Ceiling(targetTime - Game.Time) + "s remaining…" : status;
        public void Save()
        {
            // Keep the pending target's remaining wall duration when saving during catch-up.
            var savedAt = DateTime.UtcNow.AddSeconds(-Math.Max(0, targetTime - Game.Time));
            try
            {
                string json = Game.Save(savedAt);
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                { PlayerPrefs.SetString(SaveKey, json); PlayerPrefs.Save(); }
                else
                {
                    string temp = SavePath + ".tmp";
                    File.WriteAllText(temp, json);
                    if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".bak");
                    else File.Move(temp, SavePath);
                }
                Tutorial.Save();
                status = "Guild saved. Autosave every 30s. This version has a separate save from the previous RPG.";
            }
            catch (Exception error) { status = "Save failed: " + error.Message; }
        }
        public void Load()
        {
            try
            {
                double offline;
                string Read(string path) => Application.platform == RuntimePlatform.WebGLPlayer
                    ? PlayerPrefs.GetString(SaveKey) : File.ReadAllText(path);
                GuildGame loaded;
                try { loaded = GuildGame.Load(EmberGuild.Create(), Read(SavePath), DateTime.UtcNow, out offline); }
                catch (Exception) when (Application.platform != RuntimePlatform.WebGLPlayer && File.Exists(SavePath + ".bak"))
                { loaded = GuildGame.Load(EmberGuild.Create(), Read(SavePath + ".bak"), DateTime.UtcNow, out offline); }
                diagnostics?.Dispose();
                Game = loaded;
                Tutorial.Load();PollTutorial();
                diagnostics=new UnityDiagnostics(Game.World);
                targetTime = Game.Time + offline;
                wallTick = Time.realtimeSinceStartupAsDouble;
                status = "Guild loaded. " + Math.Floor(offline) + "s of offline production and quests (8h maximum).";
                presenter.Rebuild();
            }
            catch (Exception error) { status = "Load failed; current guild kept: " + error.Message; }
        }
        private void OnApplicationPause(bool value)
        {
            if (value)
            {
                savedAtPause = SaveOnLifecycle;
                if (savedAtPause) Save();
                paused = true;
            }
            else if (paused)
            {
                paused = false;
                if (savedAtPause) Load();
                savedAtPause = false;
            }
        }
        private void OnApplicationFocus(bool focused)
        { if (!focused && SaveOnLifecycle) Save(); }
        private void OnDestroy() { presenter?.Dispose();diagnostics?.Dispose();Tutorial?.Dispose(); }
        private void OnApplicationQuit() { if (SaveOnLifecycle) Save(); }
    }
}
