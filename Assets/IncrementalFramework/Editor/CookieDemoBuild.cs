using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class CookieDemoBuild
{
    public static void WebGL()
    {
        // itch.io can serve these files without custom compression headers.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/IncrementalFramework/Samples/CookieDemo/CookieDemo.unity" },
            locationPathName = "Builds/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Cookie demo WebGL build failed: " + report.summary.result);
    }

    public static void Linux()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/IncrementalFramework/Samples/CookieDemo/CookieDemo.unity" },
            locationPathName = "Builds/Linux/CookieDemo.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Cookie demo build failed: " + report.summary.result);
    }
}
