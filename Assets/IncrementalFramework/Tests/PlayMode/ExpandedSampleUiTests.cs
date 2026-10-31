using System.Collections;
using System.IO;
using System.Linq;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Unity.Expanded;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IdleHands.Tests
{
    public sealed class ExpandedSampleUiTests
    {
        private GameObject root;
        [UnityTearDown] public IEnumerator TearDown()
        { if(root!=null)Object.Destroy(root);yield return null; }
        private ExpandedBootstrap Create(string id)
        {
            root=new GameObject(id);root.SetActive(false);
            var owner=root.AddComponent<ExpandedBootstrap>();owner.SampleId=id;owner.SaveOnLifecycle=false;root.SetActive(true);return owner;
        }
        private static void Click(string caption)
        {
            var button=Object.FindObjectsOfType<Button>().FirstOrDefault(b=>b.GetComponentsInChildren<Text>().Any(t=>t.text.StartsWith(caption)));
            Assert.That(button,Is.Not.Null,"Missing control: "+caption);Assert.That(button.interactable,Is.True,caption);button.onClick.Invoke();
        }
        [UnityTest] public IEnumerator FloodwallReadingChoicesJournalAndSettingsWork()
        {
            var owner=Create("Floodwall");yield return null;yield return null;
            Assert.That(((NarrativeModule)owner.Game.Module).State.Run.Current,Is.EqualTo("appointment"));
            Click("Continue");yield return null;Click("Continue");yield return null;
            Click("Publish the current reading");yield return null;
            var narrative=(NarrativeModule)owner.Game.Module;Assert.That(narrative.State.Run.Current,Is.EqualTo("whistleblower"));
            Assert.That(narrative.State.Run.Variables["trust"].Integer,Is.EqualTo(2));
            Click("JOURNAL");yield return null;
            Assert.That(Object.FindObjectsOfType<InputField>().Length,Is.EqualTo(1));
            var input=Object.FindObjectOfType<InputField>();input.text="gauge";yield return null;
            Assert.That(Object.FindObjectsOfType<Text>().Any(t=>t.text.Contains("The rising gauge")),Is.True);
            Click("SETTINGS");yield return null;Click("Text size:");yield return null;Click("High contrast:");yield return null;
            Click("PLAY");yield return null;
            Assert.That(Object.FindObjectsOfType<Text>().Any(t=>t.text=="The name beneath the number"),Is.True);
            var content=GameObject.Find("Bound content").GetComponent<RectTransform>();
            Assert.That(content.rect.height,Is.GreaterThan(100));
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
            {Directory.CreateDirectory("/tmp/expanded-screens");GuildRpgUiTests.Capture("/tmp/expanded-screens/Floodwall.png");yield return null;}
        }
        [UnityTest] public IEnumerator FoundryControlsBuyMachineAndChangeRecipe()
        {
            var owner=Create("OrbitalFoundry");yield return null;
            while(!owner.Game.AdvanceTo(600)){}
            owner.RefreshView();yield return null;Click("Build drawbench");yield return null;
            var m=(ProductionModule)owner.Game.Module;Assert.That(m.State.Machines.Any(x=>x.Kind=="drawbench"),Is.True);
            Click("Raise allocation priority");yield return null;Assert.That(m.State.Machines.Any(x=>x.Priority==1),Is.True);
        }
        [UnityTest] public IEnumerator RailwayControlsBuildTrackAndConstructAnItinerary()
        {
            var owner=Create("LastlightRailway");yield return null;
            Click("crossing ↔ quarry");yield return null;
            var m=(LogisticsModule)owner.Game.Module;Assert.That(m.State.Tracks,Does.Contain("crossing-quarry"));
            Click("Append stop: valley");yield return null;Click("Append stop: crossing");yield return null;
            Click("food load limit:");yield return null;Click("Apply itinerary");yield return null;
            Assert.That(m.State.Trains[0].Route.Count,Is.EqualTo(2));Assert.That(m.State.Trains[0].Destination,Is.EqualTo("crossing"));
        }
        [UnityTest] public IEnumerator EcologyControlsApplyRestoration()
        {
            var owner=Create("MarshlightReserve");yield return null;Click("restore · free");yield return null;
            Assert.That(((EcologyModule)owner.Game.Module).State.Habitats.Any(h=>h.Water==.55),Is.True);
            var ecology=(EcologyModule)owner.Game.Module;
            foreach(var zone in ecology.Definition.Zones)Assert.That(owner.Game.Execute("restore",zone).Success,Is.True);
            while(!owner.Game.AdvanceTo(240)){}
            foreach(var zone in ecology.Definition.Zones){owner.Game.Execute("introduce",zone,"herons");owner.Game.Execute("introduce",zone,"otters");}
            while(!owner.Game.AdvanceTo(1440)){}
            Assert.That(ecology.IsWon,Is.True);yield return null;yield return null;
            Assert.That(Object.FindObjectsOfType<Text>().Any(t=>t.text.StartsWith("Record field notes")),Is.True,"Timed victory should reveal replay without another input.");
        }
        [UnityTest] public IEnumerator MarketControlsPurchaseCargoAndCreateAnOrder()
        {
            var owner=Create("TideboundExchange");yield return null;Click("Buy 10 grain");yield return null;
            var m=(MarketModule)owner.Game.Module;Assert.That(m.State.Warehouse.Get("grain").ToDouble(),Is.EqualTo(10));
            Click("Buy up to 10 below limit");yield return null;Assert.That(m.State.Orders.Count,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator BellChoicesStartTimedInvestigations()
        {
            var owner=Create("ThirteenthBell");yield return null;Click("Investigate — station");yield return null;
            var m=(TimeLoopModule)owner.Game.Module;Assert.That(m.State.Investigation,Is.EqualTo("station"));
            while(!owner.Game.AdvanceTo(300)){}yield return null;
            Assert.That(m.State.Run.Current,Is.EqualTo("station-result"));
            Click("Continue");yield return null;Assert.That(m.State.Run.Current,Is.EqualTo("town-square"));
        }
    }
}
