using System.Collections;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Samples.IdleRpg;
using IncrementalFramework.UI;
using IncrementalFramework.Unity;
using IncrementalFramework.Unity.Expanded;
using IncrementalFramework.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace IdleHands.Tests
{
    public sealed class SampleTutorialUiTests
    {
        private static IEnumerable<UiElement> Elements(UiElement root)=>new[]{root}.Concat(root.Children.SelectMany(Elements));
        [UnityTest]
        public IEnumerator AllSixExpandedGamesShowGuidanceHighlightAndCanSkip()
        {
            foreach(string id in new[]{"OrbitalFoundry","LastlightRailway","MarshlightReserve","ThirteenthBell","TideboundExchange","Floodwall"})
            {
                var root=new GameObject("Tutorial "+id);root.SetActive(false);var game=root.AddComponent<ExpandedBootstrap>();game.SampleId=id;game.SaveOnLifecycle=false;root.SetActive(true);
                yield return null;yield return null;
                Assert.That(game.Tutorial,Is.Not.Null,id);Assert.That(game.Tutorial.Caption,Does.Contain("TUTORIAL"),id);
                var roots=root.GetComponentsInChildren<UiViewLifetime>();var controls=roots.SelectMany(r=>Elements(r.Element)).ToArray();
                Assert.That(controls.Any(c=>c.Highlighted),Is.True,"No highlighted opening action: "+id);
                Assert.That(Object.FindObjectsOfType<UnityEngine.UI.Text>().Any(t=>t.text.Contains("TUTORIAL")),Is.True,id);
                game.Tutorial.Skip();Assert.That(game.Tutorial.Completed,Is.True);Assert.That(controls.Any(c=>c.Highlighted),Is.False,id);
                Object.Destroy(root);yield return null;
            }
        }
        [UnityTest]
        public IEnumerator OriginalThreeScenesHaveSavedTutorialSessions()
        {
            foreach(string name in new[]{"CookieDemo","IdleStrategy","IdleRpg"})
            {
                yield return SceneManager.LoadSceneAsync(name,LoadSceneMode.Single);yield return null;
                SampleTutorialSession tutorial;
                if(name=="IdleRpg"){var game=Object.FindObjectOfType<GuildRpgBootstrap>();game.SaveOnLifecycle=false;tutorial=game.Tutorial;}
                else{var game=Object.FindObjectOfType<EconomyBootstrap>();game.SaveOnLifecycle=false;tutorial=game.Tutorial;}
                Assert.That(tutorial,Is.Not.Null,name);Assert.That(tutorial.Capture(),Does.Contain("framework.graphs"),name);tutorial.Skip();Assert.That(tutorial.Completed,Is.True,name);
            }
        }
    }
}
