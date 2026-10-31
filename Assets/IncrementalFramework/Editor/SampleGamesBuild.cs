using IncrementalFramework.Samples.IdleRpg;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.IO;
using System.IO.Compression;
using IncrementalFramework.Unity;
using IncrementalFramework.Games.Rpg;
using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>Separate, reproducible itch.io web exports; never uploads or publishes.</summary>
public static class SampleGamesBuild
{
    private const string Root = "Assets/IncrementalFramework/Samples/";

    [MenuItem("Idle Hands/Build itch.io/Ember Guild (RPG)")]
    public static void RpgWebGL() { Build("IdleRpg", "Ember Guild"); }

    [MenuItem("Idle Hands/Build itch.io/Frontier Council (Strategy)")]
    public static void StrategyWebGL() { Build("IdleStrategy", "Frontier Council"); }

    [MenuItem("Idle Hands/Build itch.io/Both games")]
    public static void AllWebGL() { RpgWebGL(); StrategyWebGL(); }

    [MenuItem("Idle Hands/Validate sample campaigns")]
    public static void ValidateSamples()
    {
        ValidateGuildRpg();
        Validate("IdleStrategy", new[] { "farm", "lumber-camp", "barracks", "market" },
            new[] { "riverlands", "highlands", "citadel" }, "territory", 3);
        UnityEngine.Debug.Log("Guild RPG and strategy campaign validation passed.");
    }

    private static void Validate(string game, string[] producers, string[] encounters, string goal, int expected)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameContentAsset>(Root + game + "/Content/GameContent.asset");
        var compiled = ContentCompiler.Compile(asset);
        var sim = new Simulation(compiled.Content);
        Action<ISimulationCommand> execute = command =>
        {
            var result = sim.Execute(command);
            if (!result.Success) throw new Exception(game + ": " + result.Error);
            sim.AdvanceTo(sim.Time);
        };
        // Play with earned resources, without debug grants. The starter can always be bought.
        execute(new BuyProducerCommand(producers[0]));
        execute(new BuyProducerCommand("lumber-camp"));
        sim.AdvanceTo(sim.Time + 120);
        execute(new BuyProducerCommand("barracks"));
        sim.AdvanceTo(sim.Time + 120);
        execute(new TriggerRuleCommand(encounters[0]));
        execute(new BuyProducerCommand(producers[3]));
        sim.AdvanceTo(sim.Time + 300);
        execute(new TriggerRuleCommand(encounters[1]));
        sim.AdvanceTo(sim.Time + 120);
        sim.AdvanceTo(sim.Time + 600);
        execute(new TriggerRuleCommand(encounters[2]));
        if (sim.GetResource(goal).Balance != expected) throw new Exception(game + ": campaign goal incorrect.");
        execute(new TriggerRuleCommand(encounters[2]));
        if (sim.GetResource(goal).Balance != expected) throw new Exception(game + ": final encounter repeated.");
        foreach (var upgrade in asset.upgrades)
            if (!sim.GetUpgrade(upgrade.id).Purchased) execute(new BuyUpgradeCommand(upgrade.id));
        var snapshot = SnapshotService.Deserialize(SnapshotService.Serialize(SnapshotService.Capture(sim)));
        var restored = SnapshotService.Restore(compiled.Content, snapshot);
        if (restored.GetResource(goal).Balance != sim.GetResource(goal).Balance) throw new Exception(game + ": save lost campaign progress.");
        var resume = SnapshotService.Resume(compiled.Content, snapshot,
            new FixedClock(snapshot.SavedAtUtc.AddHours(24)), compiled.MaximumOfflineSeconds);
        if (!resume.Completed || resume.OfflineSeconds != 28800) throw new Exception(game + ": offline cap incorrect.");
        sim.AdvanceTo(sim.Time + 3000);
        execute(new PrestigeCommand());
        if (sim.GetResource(goal).Balance != 0 || sim.PrestigeCount != 1 || sim.PermanentMultiplier <= 1)
            throw new Exception(game + ": prestige reset incorrect.");
    }

    private static void ValidateGuildRpg()
    {
        var game = new GuildGame(EmberGuild.Create());
        foreach (var cls in new[] { "guardian", "ranger", "mage", "cleric" })
            if (!game.Hire(cls)) throw new Exception(game.LastMessage);
        foreach (var hero in game.State.Heroes)
            if (!game.Assign(hero.Number, 0)) throw new Exception(game.LastMessage);
        if (!game.Dispatch(0, 0, true)) throw new Exception(game.LastMessage);
        while (!game.AdvanceTo(180)) { }
        if (!game.State.BossesDefeated[0] || game.Balance("briar-heart") <= 0)
            throw new Exception("Guild RPG: balanced starter party must defeat Briarwood and earn crafting drops.");
        var json = game.Save(DateTime.UtcNow);
        var restored = GuildGame.Load(EmberGuild.Create(), json, DateTime.UtcNow, out var offline);
        if (restored.State.Heroes.Count != 4 || !restored.State.BossesDefeated[0])
            throw new Exception("Guild RPG: save round-trip lost the party or regional progression.");
        UnityEngine.Debug.Log("Guild RPG: party combat, regional unlock, loot, and save validation passed.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime time) { UtcNow = time; }
    }

    private static void Build(string game, string title)
    {
        ValidateSamples();
        var oldTitle = PlayerSettings.productName;
        var oldWidth = PlayerSettings.defaultWebScreenWidth;
        var oldHeight = PlayerSettings.defaultWebScreenHeight;
        var oldCompression = PlayerSettings.WebGL.compressionFormat;
        var oldBackground = PlayerSettings.runInBackground;
        var oldTemplate = PlayerSettings.WebGL.template;
        var output = "Builds/" + game + "/WebGL";
        try
        {
            PlayerSettings.productName = title;
            PlayerSettings.defaultWebScreenWidth = game == "IdleRpg" ? 1280 : 1000;
            PlayerSettings.defaultWebScreenHeight = game == "IdleRpg" ? 900 : 850;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.template = "APPLICATION:Default";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Root + game + "/" + game + ".unity" },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception(game + " WebGL build failed: " + report.summary.result);
            if (game == "IdleRpg")
            {
                // Fit existing itch.io embeds as well as fullscreen without requiring page-setting changes.
                var indexPath = Path.Combine(output, "index.html");
                const string responsive = "<style>html,body{width:100%;height:100%;overflow:hidden}"
                    + "#unity-container.unity-desktop{position:fixed;inset:0;transform:none;width:100%;height:100%}"
                    + "#unity-canvas{display:block;width:100%!important;height:100%!important}"
                    + "#unity-footer{display:none}</style>";
                File.WriteAllText(indexPath, File.ReadAllText(indexPath).Replace("</head>", responsive + "</head>"));
            }
            // BuildPipeline can retain old files: archive only its known web output directories.
            string archive = "Builds/" + game + "-itch.zip";
            using (var stream = new FileStream(archive, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(output + "/index.html", "index.html");
                foreach (var directory in new[] { "Build", "TemplateData", "StreamingAssets" })
                {
                    var path = Path.Combine(output, directory);
                    if (!Directory.Exists(path)) continue;
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(file, file.Substring(output.Length + 1).Replace('\\', '/'));
                }
            }
            UnityEngine.Debug.Log("itch.io archive ready: " + Path.GetFullPath(archive));
        }
        finally
        {
            PlayerSettings.productName = oldTitle;
            PlayerSettings.defaultWebScreenWidth = oldWidth;
            PlayerSettings.defaultWebScreenHeight = oldHeight;
            PlayerSettings.WebGL.compressionFormat = oldCompression;
            PlayerSettings.runInBackground = oldBackground;
            PlayerSettings.WebGL.template = oldTemplate;
        }
    }
}
