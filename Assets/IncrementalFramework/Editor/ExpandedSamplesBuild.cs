using System;
using System.IO;
using System.IO.Compression;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using IncrementalFramework.Unity.Expanded;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExpandedSamplesBuild
{
    public const string Root = "Assets/IncrementalFramework/Samples/Expanded/Scenes/";
    public const string StoryPath = "Assets/IncrementalFramework/Samples/Expanded/Content/Floodwall.asset";
    [MenuItem("Idle Hands/Expanded samples/Create missing scenes and story")]
    public static void CreateScenes()
    {
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.GetDirectoryName(StoryPath)); AssetDatabase.Refresh();
        var story = AssetDatabase.LoadAssetAtPath<StoryAsset>(StoryPath);
        if (story == null)
        { story = ScriptableObject.CreateInstance<StoryAsset>(); story.Data = FeatureJson.Write(ExpandedCampaigns.Floodwall()); AssetDatabase.CreateAsset(story,StoryPath); }
        var previous = SceneManager.GetActiveScene();
        bool emptyUntitled=string.IsNullOrEmpty(previous.path) && previous.rootCount==0;
        if(string.IsNullOrEmpty(previous.path) && !emptyUntitled) throw new InvalidOperationException("Save the current untitled scene before generating samples.");
        foreach(var id in ExpandedCampaigns.Ids)
        {
            string path = Root + id + ".unity"; if (File.Exists(path)) continue;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,emptyUntitled?NewSceneMode.Single:NewSceneMode.Additive);
            var root = new GameObject(id); var bootstrap = root.AddComponent<ExpandedBootstrap>(); bootstrap.SampleId = id;
            if (id == "Floodwall") bootstrap.Story = story;
            EditorSceneManager.SaveScene(scene,path); if(!emptyUntitled) EditorSceneManager.CloseScene(scene,true);
        }
        AssetDatabase.SaveAssets();
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        Debug.Log("Six expanded sample scenes and Floodwall story are ready.");
    }
    [MenuItem("Idle Hands/Expanded samples/Validate content and saves")]
    public static void Validate()
    {
        foreach(var id in ExpandedCampaigns.Ids)
        {
            var game = new FeatureGame(ExpandedCampaigns.Create(id)); double target = 120;
            while(!game.AdvanceTo(target,32)) { }
            var json = game.Save(DateTime.UtcNow);
            var restored = FeatureGame.Load(ExpandedCampaigns.Create(id),json,DateTime.UtcNow,out _);
            if(game.Module.Capture()!=restored.Module.Capture()) throw new Exception(id + " save round-trip failed.");
        }
        StoryCompiler.Compile(ExpandedCampaigns.Floodwall()); StoryCompiler.Compile(ExpandedCampaigns.Bell());
        Debug.Log("Expanded sample content and snapshot validation passed.");
    }
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/All six")]
    public static void AllWebGL() { foreach(var id in ExpandedCampaigns.Ids) Build(id); }
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/Orbital Foundry")]
    public static void FoundryWebGL() => Build("OrbitalFoundry");
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/Lastlight Railway")]
    public static void RailwayWebGL() => Build("LastlightRailway");
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/Marshlight Reserve")]
    public static void ReserveWebGL() => Build("MarshlightReserve");
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/The Thirteenth Bell")]
    public static void BellWebGL() => Build("ThirteenthBell");
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/Tidebound Exchange")]
    public static void ExchangeWebGL() => Build("TideboundExchange");
    [MenuItem("Idle Hands/Build itch.io/Expanded samples/Letters from the Floodwall")]
    public static void FloodwallWebGL() => Build("Floodwall");
    private static void Build(string id)
    {
        CreateScenes(); Validate();
        // Validate authored content as well as the default campaign before exporting.
        AssetDatabase.LoadAssetAtPath<StoryAsset>(StoryPath).Compile();
        string oldTitle=PlayerSettings.productName, oldTemplate=PlayerSettings.WebGL.template;
        int oldWidth=PlayerSettings.defaultWebScreenWidth,oldHeight=PlayerSettings.defaultWebScreenHeight;
        bool oldBackground=PlayerSettings.runInBackground;var oldCompression=PlayerSettings.WebGL.compressionFormat;
        try
        {
            PlayerSettings.productName=ExpandedCampaigns.Titles[Array.IndexOf(ExpandedCampaigns.Ids,id)];
            PlayerSettings.defaultWebScreenWidth=1280;PlayerSettings.defaultWebScreenHeight=900;PlayerSettings.runInBackground=true;
            PlayerSettings.WebGL.template="APPLICATION:Default";PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Disabled;
            string output="Builds/"+id+"/WebGL";
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{Root+id+".unity"},locationPathName=output,target=BuildTarget.WebGL });
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception(id+" build failed.");
            string index=output+"/index.html";
            File.WriteAllText(index,File.ReadAllText(index).Replace("</head>","<style>html,body{width:100%;height:100%;overflow:hidden}#unity-container.unity-desktop{position:fixed;inset:0;transform:none;width:100%;height:100%}#unity-canvas{width:100%!important;height:100%!important}#unity-footer{display:none}</style></head>"));
            string archive="Builds/"+id+"-itch.zip";
            using(var stream=new FileStream(archive,FileMode.Create))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(index,"index.html");
                foreach(string directory in new[]{"Build","TemplateData","StreamingAssets"})
                {
                    string path=Path.Combine(output,directory);if(!Directory.Exists(path))continue;
                    foreach(string file in Directory.GetFiles(path,"*",SearchOption.AllDirectories))zip.CreateEntryFromFile(file,file.Substring(output.Length+1).Replace('\\','/'));
                }
            }
            Debug.Log("Expanded sample archive ready: "+archive);
        }
        finally
        {
            PlayerSettings.productName=oldTitle;PlayerSettings.defaultWebScreenWidth=oldWidth;PlayerSettings.defaultWebScreenHeight=oldHeight;
            PlayerSettings.runInBackground=oldBackground;PlayerSettings.WebGL.template=oldTemplate;PlayerSettings.WebGL.compressionFormat=oldCompression;
        }
    }
}
