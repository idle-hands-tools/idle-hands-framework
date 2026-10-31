using IncrementalFramework.Samples.IdleRpg;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.Linq;
using System.Collections;
using Object = UnityEngine.Object;
using IncrementalFramework.Unity;
using IncrementalFramework.Games.Rpg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace IdleHands.Tests
{
    public sealed class SampleGamesSmokeTests
    {
        [UnityTest]
        public IEnumerator RpgSceneBootsAndRecruitsARealParty()
        {
            yield return SceneManager.LoadSceneAsync("IdleRpg", LoadSceneMode.Single);
            yield return null;
            var bootstrap = Object.FindObjectOfType<GuildRpgBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            bootstrap.SaveOnLifecycle = false;
            Assert.That(bootstrap.Game, Is.Not.Null);
            Assert.That(Object.FindObjectOfType<Canvas>(), Is.Not.Null);
            Assert.That(bootstrap.Game.Hire("guardian"), Is.True);
            var hero = bootstrap.Game.State.Heroes[bootstrap.Game.State.Heroes.Count - 1];
            Assert.That(bootstrap.Game.Assign(hero.Number, 0), Is.True);
            Assert.That(bootstrap.Game.Dispatch(0, 0, false), Is.True);
            Assert.That(bootstrap.Game.State.Parties[0].Active, Is.True);
            bootstrap.Game.AdvanceTo(bootstrap.Game.Time + 2);
            Assert.That(bootstrap.Game.State.Parties[0].Round, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator StrategySceneBootsAndBuildsFarm() { return Check("IdleStrategy", "farm", "FRONTIER COUNCIL"); }

        [UnityTest]
        public IEnumerator StrategyDashboardPurchasesResearchConquersAndPrestiges()
        {
            yield return SceneManager.LoadSceneAsync("IdleStrategy",LoadSceneMode.Single);yield return null;
            var bootstrap=Object.FindObjectOfType<EconomyBootstrap>();bootstrap.SaveOnLifecycle=false;
            foreach(string resource in new[]{"food","timber","troops"})bootstrap.Execute(new GrantResourceCommand(resource,10000));
            Func<string,Button> find=caption=>Object.FindObjectsOfType<Button>().FirstOrDefault(button=>button.interactable&&button.GetComponentInChildren<Text>().text.Contains(caption));
            foreach(string caption in new[]{"Farm /","Lumber camp /","Barracks /","Crop rotation /","Steel tools /","Officer school /","Riverlands /","River market /","Highlands /","Citadel /"})
            {
                yield return new WaitForSecondsRealtime(.25f);
                var button=find(caption);Assert.That(button,Is.Not.Null,"Expected available strategy action: "+caption);button.onClick.Invoke();yield return null;
            }
            Assert.That(bootstrap.Simulation.GetResource("territory").Balance.ToDouble(),Is.EqualTo(3));
            if(Environment.GetCommandLineArgs().Contains("-guildScreenshot"))
            {yield return null;GuildRpgUiTests.Capture("/tmp/frontier-council-ui.png");}
            long previous=bootstrap.Simulation.PrestigeCount;
            yield return new WaitForSecondsRealtime(.25f);var prestige=find("Prestige [");Assert.That(prestige,Is.Not.Null);prestige.onClick.Invoke();
            Assert.That(bootstrap.Simulation.PrestigeCount,Is.EqualTo(previous+1));Assert.That(bootstrap.Simulation.GetResource("territory").Balance.ToDouble(),Is.Zero);
        }

        private static IEnumerator Check(string scene, string producer, string title)
        {
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            yield return null;
            var bootstrap = Object.FindObjectOfType<EconomyBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            bootstrap.SaveOnLifecycle = false;
            Assert.That(bootstrap.Simulation, Is.Not.Null);
            Assert.That(Object.FindObjectOfType<Canvas>(), Is.Not.Null);
            bool foundTitle = false;
            foreach (var text in Object.FindObjectsOfType<Text>())
                if (text.text.Contains(title)) foundTitle = true;
            Assert.That(foundTitle, Is.True);
            var simulation = bootstrap.Simulation;
            for (int i = 0; i < 100 && simulation.PendingTarget.HasValue; i++) yield return null;
            long before = simulation.GetProducer(producer).Owned;
            foreach (var cost in simulation.GetProducerCost(producer))
                Assert.That(bootstrap.Execute(new GrantResourceCommand(cost.ResourceId, cost.Amount)).Success, Is.True);
            var purchase = bootstrap.Execute(new BuyProducerCommand(producer));
            Assert.That(purchase.Success, Is.True, purchase.Error);
            Assert.That(simulation.GetProducer(producer).Owned, Is.EqualTo(before + 1));
        }
    }
}
