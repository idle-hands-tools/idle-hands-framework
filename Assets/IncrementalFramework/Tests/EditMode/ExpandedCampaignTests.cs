using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using NUnit.Framework;

namespace IdleHands.Tests
{
    public sealed class ExpandedCampaignTests
    {
        private static readonly DateTime Utc = new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc);
        private static void Act(FeatureGame game, string action, string target = "", string arg = "", double amount = 0)
        { var result = game.Execute(action,target,arg,amount); Assert.That(result.Success, Is.True, action + " " + target + ": " + result.Error); }
        private static void Advance(FeatureGame game, double seconds, int budget = 256)
        { double target = game.Time + seconds; int calls = 0; while (!game.AdvanceTo(target,budget)) Assert.That(++calls, Is.LessThan(100000)); }
        [TestCase("OrbitalFoundry")][TestCase("LastlightRailway")][TestCase("MarshlightReserve")]
        [TestCase("ThirteenthBell")][TestCase("TideboundExchange")][TestCase("Floodwall")]
        public void EverySampleRoundTripsAndUsesItsOwnContentIdentity(string id)
        {
            var game = new FeatureGame(ExpandedCampaigns.Create(id)); Advance(game,45);
            string json = game.Save(Utc);
            var loaded = FeatureGame.Load(ExpandedCampaigns.Create(id),json,Utc.AddHours(24),out double offline);
            Assert.That(loaded.Module.Capture(), Is.EqualTo(game.Module.Capture()));
            Assert.That(offline, Is.EqualTo(game.Module.RunsOffline ? 28800 : 0));
            string other = id == "OrbitalFoundry" ? "LastlightRailway" : "OrbitalFoundry";
            Assert.Throws<InvalidOperationException>(() => FeatureGame.Load(ExpandedCampaigns.Create(other),json,Utc,out _));
        }
        [TestCase("OrbitalFoundry")][TestCase("LastlightRailway")][TestCase("MarshlightReserve")][TestCase("TideboundExchange")]
        public void BudgetedOfflineAdvanceMatchesContinuousAndSaveResume(string id)
        {
            var one = new FeatureGame(ExpandedCampaigns.Create(id)); var two = new FeatureGame(ExpandedCampaigns.Create(id));
            Advance(one,720,10000); Advance(two,365,1);
            var restored = FeatureGame.Load(ExpandedCampaigns.Create(id),two.Save(Utc),Utc,out _); Advance(restored,355,3);
            Assert.That(restored.Module.Capture(), Is.EqualTo(one.Module.Capture()));
        }
        [TestCase("OrbitalFoundry")][TestCase("LastlightRailway")][TestCase("MarshlightReserve")][TestCase("TideboundExchange")][TestCase("Floodwall")]
        public void RejectedCommandAndNonfiniteInputLeaveStateUnchanged(string id)
        {
            var game = new FeatureGame(ExpandedCampaigns.Create(id)); var before = game.Module.Capture();
            Assert.That(game.Execute("missing").Success, Is.False);
            Assert.That(game.Execute("buy",amount: double.NaN).Success, Is.False);
            Assert.That(game.Module.Capture(), Is.EqualTo(before));
        }
        [Test]
        public void FoundryCompletesWithEarnedComponentsAndReservationsSurviveLoad()
        {
            var m = new ProductionModule(ExpandedCampaigns.Foundry()); var g = new FeatureGame(m);
            Advance(g,600); Act(g,"buy","drawbench"); Act(g,"buy","electronics"); Act(g,"buy","assembler"); Act(g,"buy","smelter");
            Advance(g,300);
            int waits=0;while(m.State.Depot.Get("frame")<6){Advance(g,10);Assert.That(++waits,Is.LessThan(300));}
            Act(g,"toggle",m.State.Machines.Single(x=>x.Kind=="assembler").Id);
            Act(g,"toggle",m.State.Machines.Single(x=>x.Kind=="electronics").Id);
            Advance(g,300);
            while(m.State.Depot.Get("plate")<60){Advance(g,10);Assert.That(++waits,Is.LessThan(300));}
            Act(g,"deliver","hull");
            Act(g,"toggle",m.State.Machines.Single(x=>x.Kind=="assembler").Id);
            Act(g,"toggle",m.State.Machines.Single(x=>x.Kind=="electronics").Id);
            Act(g,"recipe",m.State.Machines.First(x=>x.Kind=="smelter").Id,"fast-smelt");
            Advance(g,180); Act(g,"buy","extractor");
            Advance(g,1020); Act(g,"deliver","avionics");
            Act(g,"recipe",m.State.Machines.Single(x=>x.Kind=="electronics").Id,"cell");
            Advance(g,600); Act(g,"recipe",m.State.Machines.Single(x=>x.Kind=="electronics").Id,"circuit");
            Advance(g,600);
            Assert.That(m.State.Depot.Get("cell").ToDouble(),Is.GreaterThanOrEqualTo(40),"Cells");
            Assert.That(m.State.Depot.Get("circuit").ToDouble(),Is.GreaterThanOrEqualTo(30),"Circuits");
            Assert.That(m.State.Depot.Get("frame").ToDouble(),Is.GreaterThanOrEqualTo(12),"Frames");
            Act(g,"deliver","drive"); Assert.That(m.IsWon,Is.True);
            Assert.That(m.State.Depot.Reserved, Is.GreaterThanOrEqualTo(BigDouble.Zero));
            var before = m.Capture(); Assert.That(g.Execute("deliver","drive").Success, Is.False); Assert.That(m.Capture(), Is.EqualTo(before));
            var saved = FeatureGame.Load(new ProductionModule(ExpandedCampaigns.Foundry()),g.Save(Utc),Utc,out _);
            Advance(g,100); Advance(saved,100); Assert.That(saved.Module.Capture(),Is.EqualTo(m.Capture()));
            Act(g,"replay"); Assert.That(m.State.Launches,Is.EqualTo(1)); Assert.That(m.State.Delivered,Is.Empty); Assert.That(m.Costs("hull",m.State.Launches)["plate"].ToDouble(),Is.EqualTo(72).Within(1e-9));
        }
        [Test]
        public void SharedInputsCannotBeDoubleSpentAndOutputSpaceIsReserved()
        {
            var d = ExpandedCampaigns.Foundry(); d.Capacity = 8;
            var m = new ProductionModule(d); var g = new FeatureGame(m); Advance(g,30);
            Assert.That(m.State.Depot.Used + m.State.Depot.Reserved,Is.LessThanOrEqualTo(m.State.Depot.Capacity));
            Assert.That(m.State.Machines.Any(x=>x.Blocked=="Storage full"),Is.True);
        }
        private static RouteStop Stop(string station,string product=null,int quantity=40) => new RouteStop
        { Station=station, Load=product==null ? new Dictionary<string,BigDouble>() : new Dictionary<string,BigDouble>{{product,quantity}} };
        private static RouteStop Pass(string station) => new RouteStop { Station=station, Unload=false };
        private static void Route(FeatureGame game,params RouteStop[] stops) {
            var m=(LogisticsModule)game.Module;int wait=0;
            while (!stops.Any(x=>x.Station==(m.State.Trains[0].Destination??m.State.Trains[0].Station))) { Advance(game,1); Assert.That(++wait,Is.LessThan(200)); }
            Act(game,"route","train:1",FeatureJson.Write(stops));
        }
        [Test]
        public void RailwayCompletesWithTransportedCargo()
        {
            var m = new LogisticsModule(ExpandedCampaigns.Railway()); var g = new FeatureGame(m);
            Advance(g,180); Route(g,Stop("valley","food"),Stop("crossing","timber")); Advance(g,180);
            Act(g,"contract","crossing-food"); Act(g,"contract","valley-repairs");
            Act(g,"track","crossing-quarry");
            Route(g,Stop("valley","food"),Pass("crossing"),Stop("quarry","tools"),Pass("crossing")); Advance(g,720);
            Act(g,"contract","quarry-rations"); Act(g,"track","crossing-ridge"); Act(g,"track","quarry-ridge");
            Route(g,Stop("quarry","tools"),Stop("ridge"),Pass("crossing"),Stop("quarry","tools"),Pass("crossing")); Advance(g,720);
            Act(g,"contract","ridge-repairs");
            Route(g,Stop("valley","food"),Pass("crossing"),Stop("ridge"),Pass("crossing")); Advance(g,720);
            Act(g,"contract","ridge-supplies"); Act(g,"track","ridge-observatory");
            Route(g,Stop("valley","food"),Pass("crossing"),Pass("ridge"),Stop("observatory"),Pass("ridge"),Pass("crossing")); Advance(g,1800);
            Route(g,Stop("crossing","timber"),Pass("ridge"),Stop("observatory"),Pass("ridge")); Advance(g,1800);
            Route(g,Stop("quarry","tools"),Pass("ridge"),Stop("observatory"),Pass("ridge")); Advance(g,1800);
            Act(g,"contract","observatory"); Assert.That(m.IsWon,Is.True);
        }
        [Test]
        public void InvalidRouteDoesNotLoseCargoAndInTransitEditWaitsForArrival()
        {
            var m = new LogisticsModule(ExpandedCampaigns.Railway()); var g = new FeatureGame(m); Advance(g,90);
            Route(g,Stop("valley","food"),Stop("crossing")); var before = m.Capture();
            Assert.That(g.Execute("route","train:1",FeatureJson.Write(new[]{Stop("valley"),Stop("observatory")})).Success,Is.False); Assert.That(m.Capture(),Is.EqualTo(before));
            Route(g,Stop("crossing","timber"),Stop("valley")); Assert.That(m.State.Trains[0].PendingRoute,Is.Not.Null);
            var loaded = FeatureGame.Load(new LogisticsModule(ExpandedCampaigns.Railway()),g.Save(Utc),Utc,out _); Advance(g,40); Advance(loaded,40);
            Assert.That(loaded.Module.Capture(),Is.EqualTo(m.Capture()));
        }
        [Test]
        public void WetlandCanRecoverAndRemainHealthy()
        {
            var m = new EcologyModule(ExpandedCampaigns.Reserve()); var g = new FeatureGame(m);
            foreach(var zone in m.Definition.Zones) Act(g,"restore",zone);
            Advance(g,240);
            foreach(var zone in m.Definition.Zones) { Act(g,"introduce",zone,"herons"); Act(g,"introduce",zone,"otters"); }
            Advance(g,1200); Assert.That(m.IsWon,Is.True,m.Capture());
            Assert.That(m.State.Habitats.All(h=>h.History.Count<=60),Is.True);
            Act(g,"replay"); Assert.That(m.State.FieldNotes,Is.EqualTo(1)); Assert.That(m.IsWon,Is.False);
        }
        [Test]
        public void EcologyDoesNotDependOnSpeciesIterationOrder()
        {
            var d = ExpandedCampaigns.Reserve(); var reversed = FeatureJson.Copy(d);
            Array.Reverse(reversed.Species); Array.Reverse(reversed.InitialPopulations);
            var a=new EcologyModule(d);var b=new EcologyModule(reversed);var ga=new FeatureGame(a);var gb=new FeatureGame(b);
            Advance(ga,120);Advance(gb,120);
            foreach(var habitat in a.State.Habitats) foreach(var p in habitat.Populations)
                Assert.That(b.State.Habitats.Single(h=>h.Id==habitat.Id).Populations[p.Key],Is.EqualTo(p.Value).Within(1e-10));
        }
        [Test]
        public void ExchangeCompletesUsingContractEarningsAndNeverBorrows()
        {
            var m = new MarketModule(ExpandedCampaigns.Exchange()); var g = new FeatureGame(m);
            foreach(var contract in m.Definition.Contracts)
            {
                Advance(g,Math.Max(0,contract.AvailableAfterSeconds-g.Time));
                foreach(var item in contract.Goods) Act(g,"buy","harbor",item.Key,item.Value.ToDouble());
                Act(g,"contract",contract.Id);
            }
            Assert.That(m.IsWon,Is.True); Assert.That(m.State.Cash,Is.GreaterThan(BigDouble.Zero));
            Assert.That(m.State.Access.Count,Is.EqualTo(3));
            var before=m.Capture();Assert.That(g.Execute("buy","capital","oil",1000).Success,Is.False);Assert.That(m.Capture(),Is.EqualTo(before));
        }
        [Test]
        public void StandingOrdersRespectLimitsAndAffordabilityAcrossSaveResume()
        {
            var m=new MarketModule(ExpandedCampaigns.Exchange());var g=new FeatureGame(m);
            var q=m.State.Quotes.First();double initial=m.Price(q);
            Act(g,"order-buy","harbor","grain",initial*2);Advance(g,30);
            Assert.That(m.State.Warehouse.Get("grain"),Is.EqualTo(new BigDouble(10)));Assert.That(m.State.Cash,Is.GreaterThanOrEqualTo(BigDouble.Zero));
            Assert.That(m.State.Orders,Is.Empty);
            Act(g,"order-sell","harbor","grain",.01);
            var loaded=FeatureGame.Load(new MarketModule(ExpandedCampaigns.Exchange()),g.Save(Utc),Utc,out _);
            Advance(g,30);Advance(loaded,30);Assert.That(m.State.Warehouse.Get("grain"),Is.EqualTo(BigDouble.Zero));
            Assert.That(loaded.Module.Capture(),Is.EqualTo(m.Capture()));
        }
        [Test]
        public void FloodwallHasFourEarnedReachableEndingsAndBothChapterTwoRoutes()
        {
            var definition=ExpandedCampaigns.Floodwall(); Assert.That(StoryCompiler.Validate(definition),Is.Empty);
            var pending=new Queue<string>(); var first=new NarrativeModule(definition);pending.Enqueue(first.Capture());
            var endings=new HashSet<string>();var visited=new HashSet<string>();int work=0;
            while(pending.Count>0)
            {
                Assert.That(++work,Is.LessThan(10000));var module=new NarrativeModule(definition);module.Restore(1,pending.Dequeue());var game=new FeatureGame(module);
                foreach(var id in module.State.Run.Visited)visited.Add(id);
                var view=module.View;
                if(view.Kind==StoryNodeKind.Ending){endings.Add(module.State.Run.Current);continue;}
                if(view.Kind==StoryNodeKind.Passage){Act(game,"continue",arg:view.Token);pending.Enqueue(module.Capture());continue;}
                foreach(var choice in view.Choices.Where(c=>c.Enabled))
                {var copy=new NarrativeModule(definition);copy.Restore(1,module.Capture());var branch=new FeatureGame(copy);Act(branch,"choose",choice.Id,view.Token);pending.Enqueue(copy.Capture());}
            }
            Assert.That(endings.Count,Is.EqualTo(4),string.Join(",",endings));
            Assert.That(visited,Does.Contain("landing-briefing"));Assert.That(visited,Does.Contain("council-briefing"));
        }
        [Test]
        public void NarrativeIgnoresStaleInputAndRestoresCheckpointsWithoutReapplyingEffects()
        {
            var m=new NarrativeModule(ExpandedCampaigns.Floodwall());var g=new FeatureGame(m);string token=m.Token;
            Act(g,"continue",arg:token);Assert.That(g.Execute("continue",argument:token).Success,Is.False);
            string before=m.Capture();var restored=FeatureGame.Load(new NarrativeModule(ExpandedCampaigns.Floodwall()),g.Save(Utc),Utc.AddDays(3),out double offline);
            Assert.That(offline,Is.Zero);Assert.That(restored.Module.Capture(),Is.EqualTo(before));
            Act(g,"continue",arg:m.Token);Act(g,"choose","publish",m.Token);
            Assert.That(m.State.Run.Variables["trust"].Integer,Is.EqualTo(2));
            Act(g,"checkpoint","appointment");Assert.That(m.State.Run.Variables["trust"].Integer,Is.EqualTo(1));
            Assert.That(m.State.Run.Transcript.Count,Is.EqualTo(1));
        }
        [Test]
        public void NarrativeCompilerRejectsMissingTargetsTypesAndAutomaticCycles()
        {
            var d=ExpandedCampaigns.Floodwall();d.Nodes[0].Next="missing";Assert.That(StoryCompiler.Validate(d).Any(e=>e.Message.Contains("target")),Is.True);
            d=ExpandedCampaigns.Floodwall();d.Nodes[0].Effects.Add(new StoryEffect{Variable="trust",Kind=StoryEffectKind.SetBoolean});Assert.That(StoryCompiler.Validate(d).Any(e=>e.Message.Contains("type")),Is.True);
            d=ExpandedCampaigns.Floodwall();var branch=d.Nodes.Single(n=>n.Id=="route");branch.Next=branch.Id;Assert.That(StoryCompiler.Validate(d).Any(e=>e.Message.Contains("cycle")),Is.True);
        }
        [Test]
        public void BellInvestigationsPauseAtDecisionsAndKnowledgeSurvivesReset()
        {
            var m=new TimeLoopModule(ExpandedCampaigns.Bell(),ExpandedCampaigns.Investigations());var g=new FeatureGame(m);
            Act(g,"investigate","station");Advance(g,28800);
            Assert.That(g.Time,Is.EqualTo(150));Assert.That(m.State.AwaitingDecision,Is.True);Assert.That(m.State.Run.Variables["facts"].Members,Does.Contain("ticket"));
            Act(g,"continue",arg:m.Token);Act(g,"wait-bell");Advance(g,28800);
            Assert.That(g.Time,Is.EqualTo(720));Assert.That(m.State.AtLoopBoundary,Is.True);
            Act(g,"reset-loop");Assert.That(m.State.Run.Variables["facts"].Members,Does.Contain("ticket"));Assert.That(m.State.Loop,Is.EqualTo(1));
            Act(g,"investigate","station");Advance(g,1000);Assert.That(g.Time,Is.EqualTo(795));
        }
        [Test]
        public void BellCampaignCanReleaseTheTownWithEarnedKnowledgeAcrossLoops()
        {
            var m=new TimeLoopModule(ExpandedCampaigns.Bell(),ExpandedCampaigns.Investigations());var g=new FeatureGame(m);
            void Investigate(string id) { Act(g,"investigate",id);Advance(g,1000);Act(g,"continue",arg:m.Token); }
            Investigate("station");
            Assert.That(g.Execute("investigate","market").Success,Is.False,"Morning appointments must be mutually exclusive.");
            Investigate("archive");Investigate("workshop");Investigate("courier");
            Act(g,"wait-bell");Advance(g,1000);Act(g,"reset-loop");
            Investigate("market");Investigate("vault");Investigate("bell");
            Act(g,"choose","solve",m.Token);Assert.That(m.State.Run.Current,Is.EqualTo("ending-release"));Assert.That(m.IsWon,Is.True);
            Assert.That(m.State.Endings,Does.Contain("ending-release"));
        }
        [Test]
        public void TranscriptRetainsResolvedTextAndInlineReferencesAreValidated()
        {
            var d=ExpandedCampaigns.Floodwall();var m=new NarrativeModule(d);var g=new FeatureGame(m);
            Act(g,"continue",arg:m.Token);Assert.That(m.View.EvidenceIds,Does.Contain("gauge"));
            Assert.That(m.View.Body,Does.Not.Contain("[["));
            var envelope=FeatureJson.Read<FeatureEnvelope<NarrativeState>>(m.Capture());
            envelope.State.Run.Variables["trust"].Integer=99;string old=m.State.Run.Transcript[0].Body;
            m.Restore(1,FeatureJson.Write(envelope));Assert.That(m.State.Run.Transcript[0].Body,Is.EqualTo(old));
            d.Text["ledger.body"]+=" [[missing|evidence]]";Assert.That(StoryCompiler.Validate(d).Any(e=>e.Message.Contains("inline evidence")),Is.True);
        }
        [Test]
        public void ResponseHistoryRecordsTextShownBeforeChoiceEffects()
        {
            var d=ExpandedCampaigns.Floodwall();d.Text["publish.text"]="Publish with trust {trust}";
            var module=new NarrativeModule(d);var game=new FeatureGame(module);
            Act(game,"continue",arg:module.Token);Act(game,"continue",arg:module.Token);
            string shown=module.View.Choices.Single(c=>c.Id=="publish").Text;
            Act(game,"choose","publish",module.Token);
            Assert.That(module.State.Run.Variables["trust"].Integer,Is.EqualTo(2));
            Assert.That(module.State.Run.Transcript.Single(t=>t.Node=="first-warning").Response,Is.EqualTo(shown));
            Assert.That(shown,Is.EqualTo("Publish with trust 1"));
        }
        [Test]
        public void BulkTradeQuoteMatchesSequentialUnitsAtPriceImpactBounds()
        {
            var m=new MarketModule(ExpandedCampaigns.Exchange());
            foreach(bool buy in new[]{true,false})
            {
                var quote=new MarketQuote{Commodity="grain",Market="harbor",Demand=buy?.45:2,Impact=buy?2.9:-.7};
                double bulk=m.Quote(quote,100,buy).ToDouble(),sequential=0;
                for(int count=0;count<100;count++)
                {sequential+=m.Quote(quote,1,buy).ToDouble();quote.Impact=Math.Max(-.75,Math.Min(3,quote.Impact+(buy?.015:-.015)));}
                Assert.That(bulk,Is.EqualTo(sequential).Within(1e-8));
            }
        }
    }
}
