using IncrementalFramework.Samples.IdleRpg;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using IncrementalFramework.Games.Rpg;
using IncrementalFramework.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IdleHands.Tests
{
    public sealed class GuildRpgUiTests
    {
        private static Button FindButton(string startsWith)
        {
            var button = Object.FindObjectsOfType<Button>().FirstOrDefault(candidate => candidate.interactable &&
                candidate.GetComponentInChildren<Text>().text.StartsWith(startsWith, StringComparison.Ordinal));
            Assert.That(button, Is.Not.Null, "Expected enabled button: " + startsWith);
            return button;
        }

        [UnityTest]
        public IEnumerator HireAssignDispatchAndInspectGuildThroughTheDashboard()
        {
            yield return SceneManager.LoadSceneAsync("IdleRpg", LoadSceneMode.Single);
            yield return null;
            var bootstrap = Object.FindObjectOfType<GuildRpgBootstrap>();
            bootstrap.SaveOnLifecycle = false;
            // Fresh state is intentional: this test must not depend on a developer's local save.
            if (bootstrap.Game.State.Heroes.Count != 0) Assert.Ignore("Fresh dashboard test requires no existing v2 guild save.");
            foreach (string cls in new[] { "Guardian", "Ranger", "Mage", "Cleric" }) FindButton("Hire " + cls + " /").onClick.Invoke();
            for (int i = 0; i < 4; i++) FindButton("Assign to Dawn company").onClick.Invoke();
            Assert.That(bootstrap.Game.Members(0).Count, Is.EqualTo(4));
            FindButton("QUESTS").onClick.Invoke();
            FindButton("Hunt Ancient treant").onClick.Invoke();
            Assert.That(bootstrap.Game.State.Parties[0].Active, Is.True);
            Assert.That(bootstrap.Game.AdvanceTo(bootstrap.Game.Time + 180), Is.True);
            Assert.That(bootstrap.Game.State.BossesDefeated[0], Is.True);
            FindButton("GUILD").onClick.Invoke();
            FindButton("Build / upgrade:").onClick.Invoke();
            FindButton("WORKSHOP").onClick.Invoke();
            FindButton("Buy and equip Iron Weapon").onClick.Invoke();
            Assert.That(bootstrap.Game.State.Heroes[0].Weapon, Is.EqualTo(1));
            FindButton("PARTIES").onClick.Invoke();
            yield return null;
            Assert.That(Object.FindObjectsOfType<Text>().Any(text => text.text.Contains("Dawn company", StringComparison.OrdinalIgnoreCase)), Is.True);

            if (Environment.GetCommandLineArgs().Contains("-guildScreenshot"))
            {
                FindButton("ROSTER").onClick.Invoke();
                yield return null;
                Capture("/tmp/ember-guild-roster.png");
                FindButton("GUILD").onClick.Invoke();
                yield return null;
                Capture("/tmp/ember-guild-buildings.png");
                FindButton("QUESTS").onClick.Invoke();
                FindButton("Dispatch patrol").onClick.Invoke();
                bootstrap.Game.AdvanceTo(bootstrap.Game.Time + 2);
                yield return null;
                Capture("/tmp/ember-guild-combat.png");
            }
        }

        internal static void Capture(string path)
        {
            var canvas = Object.FindObjectOfType<Canvas>();
            var cameraObject = new GameObject("Review camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.075f, 0.10f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 100;
            var render = new RenderTexture(1280, 900, 24);
            camera.targetTexture = render;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = render;
            var texture = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            camera.targetTexture = null; render.Release();
            Object.Destroy(texture); Object.Destroy(render); Object.Destroy(cameraObject);
        }
    }
}
