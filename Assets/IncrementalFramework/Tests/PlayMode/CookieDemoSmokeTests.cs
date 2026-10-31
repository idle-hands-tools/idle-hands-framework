using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System.Collections;
using BreakInfinity;
using IncrementalFramework.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace IdleHands.Tests
{
    public sealed class CookieDemoSmokeTests
    {
        [UnityTest]
        public IEnumerator SavedSceneBootsAndPurchasesAProducerThroughCommands()
        {
            yield return SceneManager.LoadSceneAsync("CookieDemo", LoadSceneMode.Single);
            yield return null;
            var bootstrap = Object.FindObjectOfType<EconomyBootstrap>();
            Assert.That(bootstrap, Is.Not.Null, "Build settings must include the saved CookieDemo scene.");
            bootstrap.SaveOnLifecycle = false;
            Assert.That(bootstrap.Simulation, Is.Not.Null, "The scene must have valid authoring references.");
            Assert.That(Object.FindObjectOfType<Canvas>(), Is.Not.Null);
            for (int frames = 0; frames < 300 && bootstrap.Simulation.PendingTarget.HasValue; frames++)
                yield return null;
            Assert.That(bootstrap.Simulation.PendingTarget.HasValue, Is.False, "Offline catch-up must finish.");
            var simulation = bootstrap.Simulation;
            long before = simulation.GetProducer("cursor").Owned;
            foreach (var cost in simulation.GetProducerCost("cursor"))
                Assert.That(bootstrap.Execute(new GrantResourceCommand(cost.ResourceId, cost.Amount)).Success, Is.True);
            var result = bootstrap.Execute(new BuyProducerCommand("cursor"));
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(simulation.GetProducer("cursor").Owned, Is.EqualTo(before + 1));
            Assert.That(simulation.GetResource("cookies").Rate, Is.GreaterThan(BigDouble.Zero));
            yield return new WaitForSecondsRealtime(0.2f);
            bool hasCursorRow = false;
            foreach (var button in Object.FindObjectsOfType<Button>())
            {
                var label = button.GetComponentInChildren<Text>();
                if (label != null && label.text.Contains("Cursor") && label.text.Contains("[" + (before + 1) + "]"))
                    hasCursorRow = true;
            }
            Assert.That(hasCursorRow, Is.True, "Throttled presenter must reflect the purchased producer.");
        }
    }
}
