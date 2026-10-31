using System;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Games.Rpg;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Timing;
using IncrementalFramework.Samples.Expanded;
using IncrementalFramework.Samples.IdleRpg;
using NUnit.Framework;

namespace IdleHands.Tests
{
    public sealed class TimingTests
    {
        private static readonly DateTime Utc = new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc);
        private static TimingOptions BankOptions() => new TimingOptions { OfflineMode=OfflineProgressMode.BankTime };
        private static Simulation Sim(int budget=10000)
        {
            var builder=new ModuleBuilder();builder.Resource(new ResourceDefinition("gold",0));
            builder.Producer(new ProducerDefinition("worker",null,new[]{new ResourceAmount("gold",1)}));
            var sim=builder.Create(eventBudget:budget);Assert.That(sim.Execute(new BuyProducerCommand("worker")).Success,Is.True);return sim;
        }
        private static void Drain(TimingController timing,int budget=256)
        {int calls=0;while(timing.HasPendingWork){timing.Continue(budget);Assert.That(++calls,Is.LessThan(10000));}}
        private sealed class TestTimeline : ITimeline
        {
            public Simulation Simulation{get;}
            public double Time=>Simulation.Time;
            public bool AllowsOfflineRewards{get;set;}=true;
            public double Chunk{get;set;}=double.PositiveInfinity;
            public double BlockAt{get;set;}=double.PositiveInfinity;
            public bool IsBlocked=>Time>=BlockAt;
            public int Calls{get;private set;}
            public TestTimeline(Simulation simulation){Simulation=simulation;}
            public TimelineAdvanceResult AdvanceTo(double target,int workBudget)
            {
                Calls++;Simulation.AdvanceTo(Math.Min(target,Math.Min(Time+Chunk,BlockAt)));
                return new TimelineAdvanceResult(Time,IsBlocked?TimelineAdvanceStatus.Blocked:Time==target?TimelineAdvanceStatus.ReachedTarget:TimelineAdvanceStatus.BudgetExhausted,1);
            }
        }
        [Test] public void AutomaticCatchUpHonorsCapAndDoesNotBankTheSameAbsence()
        {
            var sim=Sim();var timing=new TimingController(new SimulationTimeline(sim));
            var summary=timing.ClaimOffline(Utc,Utc.AddHours(9));
            Assert.That(summary.EligibleSeconds,Is.EqualTo(28800));Assert.That(summary.DiscardedOfflineSeconds,Is.EqualTo(3600));
            Assert.That(sim.Time,Is.Zero);Assert.That(timing.BankSeconds,Is.EqualTo(BigDouble.Zero));
            Drain(timing);Assert.That(sim.Time,Is.EqualTo(28800));Assert.That(sim.GetResource("gold").Balance.ToDouble(),Is.EqualTo(28800).Within(1e-7));
            Assert.That(timing.ClaimOffline(Utc,Utc.AddHours(9)).EligibleSeconds,Is.Zero);
        }
        [Test] public void BankingClampsEachAbsenceAndTheStorageBalance()
        {
            var options=BankOptions();options.BankCapacitySeconds=10;options.BankEarningRate=2;options.MaximumOfflineSeconds=20;
            var timing=new TimingController(new SimulationTimeline(Sim()),options);
            var summary=timing.ClaimOffline(Utc,Utc.AddSeconds(30));
            Assert.That(summary.EligibleSeconds,Is.EqualTo(20));Assert.That(summary.DiscardedOfflineSeconds,Is.EqualTo(10));
            Assert.That(summary.BankedSeconds.ToDouble(),Is.EqualTo(10));Assert.That(summary.DiscardedBankSeconds.ToDouble(),Is.EqualTo(30));
            Assert.That(timing.Timeline.Time,Is.Zero);Assert.That(timing.HasPendingWork,Is.False);
            Assert.That(timing.ClaimOffline(Utc,Utc.AddSeconds(30)).BankedSeconds,Is.EqualTo(BigDouble.Zero));
            Assert.That(timing.ClaimOffline(Utc,Utc.AddSeconds(31)).DiscardedBankSeconds.ToDouble(),Is.EqualTo(2));
        }
        [Test] public void BackwardWallClockNeverGrantsOrReopensAnOldClaim()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());
            Assert.That(timing.ClaimOffline(Utc,Utc.AddSeconds(-10)).BankedSeconds,Is.EqualTo(BigDouble.Zero));
            timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            Assert.That(timing.ClaimOffline(Utc,Utc).BankedSeconds,Is.EqualTo(BigDouble.Zero));
            Assert.That(timing.ClaimOffline(Utc,Utc.AddSeconds(10)).BankedSeconds,Is.EqualTo(BigDouble.Zero));
            Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(10));
        }
        [Test] public void FourTimesForTenSecondsCostsThirtyBankedSeconds()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(100));
            Assert.That(timing.SetBoostMultiplier(4).Success,Is.True);timing.AdvanceOnline(10);
            Assert.That(timing.Timeline.Time,Is.EqualTo(40).Within(1e-9));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(70).Within(1e-9));
            Assert.That(timing.ReservedBankSeconds,Is.EqualTo(BigDouble.Zero));
        }
        [Test] public void LimitedBankGrantsOnlyAffordableBoostAndReturnsToNormalSpeed()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(2));
            timing.SetBoostMultiplier(4);timing.AdvanceOnline(10);
            Assert.That(timing.Timeline.Time,Is.EqualTo(12));Assert.That(timing.BankSeconds,Is.EqualTo(BigDouble.Zero));Assert.That(timing.BoostMultiplier,Is.EqualTo(1));
            timing.AdvanceOnline(1);Assert.That(timing.Timeline.Time,Is.EqualTo(13));
        }
        [Test] public void ChangingAndStoppingBoostAffectsOnlyNewWork()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(100));
            timing.SetBoostMultiplier(2);timing.AdvanceOnline(1);timing.SetBoostMultiplier(8);timing.AdvanceOnline(1);timing.StopBoost();timing.AdvanceOnline(1);
            Assert.That(timing.Timeline.Time,Is.EqualTo(11));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(92).Within(1e-9));
            Assert.That(timing.SetBoostMultiplier(3).Success,Is.False);
        }
        [Test] public void PendingBoostKeepsItsReservationAndNormalTimeIsProcessedFirst()
        {
            var timeline=new TestTimeline(Sim()){Chunk=.5};var timing=new TimingController(timeline,BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            Assert.That(timing.SetBoostMultiplier(4).Success,Is.True);
            Assert.That(timing.AdvanceOnline(1,1).Status,Is.EqualTo(TimelineAdvanceStatus.BudgetExhausted));
            Assert.That(timing.Timeline.Time,Is.EqualTo(.5));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(10));Assert.That(timing.ReservedBankSeconds.ToDouble(),Is.EqualTo(3));
            timing.StopBoost();Drain(timing,1);
            Assert.That(timing.Timeline.Time,Is.EqualTo(4));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(7).Within(1e-9));
            timing.AdvanceOnline(1,1);Drain(timing,1);Assert.That(timing.Timeline.Time,Is.EqualTo(5));
        }
        [Test] public void ForegroundTimeDuringBacklogIsRetainedWithoutReservingAnotherBoost()
        {
            var timeline=new TestTimeline(Sim()){Chunk=.5};var timing=new TimingController(timeline,BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(100));
            timing.SetBoostMultiplier(4);timing.AdvanceOnline(1,1);timing.AdvanceOnline(1,1);Drain(timing,1);
            Assert.That(timeline.Time,Is.EqualTo(5));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(97).Within(1e-8));
        }
        [Test] public void SkipReservesOnlyAffordableTimeAndRejectsOverlap()
        {
            var timeline=new TestTimeline(Sim()){Chunk=1};var timing=new TimingController(timeline,BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(5));
            timing.RequestSkip(20,1);Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(4));Assert.That(timing.ReservedBankSeconds.ToDouble(),Is.EqualTo(4));
            Assert.That(timing.HasPendingSkip,Is.True);Assert.Throws<InvalidOperationException>(()=>timing.RequestSkip(1));
            Drain(timing,1);Assert.That(timeline.Time,Is.EqualTo(5));Assert.That(timing.BankSeconds,Is.EqualTo(BigDouble.Zero));
            Assert.Throws<InvalidOperationException>(()=>timing.RequestSkip(1));
        }
        [Test] public void BlockedSkipRefundsUnprocessedReservation()
        {
            var timeline=new TestTimeline(Sim()){BlockAt=2};var timing=new TimingController(timeline,BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            var result=timing.RequestSkip(8);
            Assert.That(result.Status,Is.EqualTo(TimelineAdvanceStatus.Blocked));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(8));
            Assert.That(timing.ReservedBankSeconds,Is.EqualTo(BigDouble.Zero));Assert.That(timing.HasPendingWork,Is.False);Assert.That(timing.CanAcceptGameplayInput,Is.True);
        }
        [Test] public void PartiallyBlockedBoostChargesOnlyExtraTimeActuallyAdvanced()
        {
            var timeline=new TestTimeline(Sim()){BlockAt=2};var timing=new TimingController(timeline,BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            timing.SetBoostMultiplier(4);var result=timing.AdvanceOnline(1);
            Assert.That(result.Status,Is.EqualTo(TimelineAdvanceStatus.Blocked));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(9));
            Assert.That(timing.BoostMultiplier,Is.EqualTo(1));timing.AdvanceOnline(10);Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(9));
        }
        [Test] public void CadenceIsIndependentOfFrameRateAndRetainsFractionalSeconds()
        {
            foreach(double hz in new[]{2d,10d,60d})
            {
                var a=new TestTimeline(Sim());var options=new TimingOptions{TickRateHz=hz};var timing=new TimingController(a,options);
                for(int i=0;i<300;i++)timing.AdvanceOnline(1d/30);
                Assert.That(a.Time,Is.EqualTo(10).Within(1e-8));
            }
            var slow=new TimingController(new SimulationTimeline(Sim()),new TimingOptions{TickRateHz=2});
            slow.AdvanceOnline(.2);Assert.That(slow.Timeline.Time,Is.Zero);slow.AdvanceOnline(.2);Assert.That(slow.Timeline.Time,Is.Zero);
            slow.AdvanceOnline(.2);Assert.That(slow.Timeline.Time,Is.EqualTo(.5));Assert.That(slow.CaptureState().CadenceRemainderSeconds,Is.EqualTo(.1).Within(1e-9));
        }
        [Test] public void LongUpdateCoalescesEmptyTicksRatherThanScanningThem()
        {
            var timeline=new TestTimeline(Sim());var timing=new TimingController(timeline,new TimingOptions{TickRateHz=1000});timing.AdvanceOnline(28800);
            Assert.That(timeline.Time,Is.EqualTo(28800));Assert.That(timeline.Calls,Is.EqualTo(1));
        }
        [Test] public void OptionsAreCapturedAndInvalidConfigurationIsRejected()
        {
            var options=BankOptions();var timing=new TimingController(new SimulationTimeline(Sim()),options);options.TickRateHz=0;options.BoostMultipliers[0]=99;
            Assert.That(timing.Options.TickRateHz,Is.EqualTo(10));Assert.That(timing.SetBoostMultiplier(2).Success,Is.True);
            foreach(double invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity})
                Assert.Throws<ArgumentOutOfRangeException>(()=>new TimingController(new SimulationTimeline(Sim()),new TimingOptions{TickRateHz=invalid}));
            Assert.Throws<ArgumentOutOfRangeException>(()=>timing.AdvanceOnline(double.NaN));Assert.Throws<ArgumentOutOfRangeException>(()=>timing.RequestSkip(-1));
            Assert.Throws<ArgumentException>(()=>timing.ClaimOffline(DateTime.SpecifyKind(Utc,DateTimeKind.Unspecified),Utc));
        }
        [Test] public void NotificationsAreObservableAndCannotReenterMutations()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());int changes=0;
            timing.Changed+=view=>{changes++;timing.StopBoost();};
            timing.ClaimOffline(Utc,Utc.AddSeconds(5));Assert.That(changes,Is.EqualTo(1));Assert.That(timing.LastObserverError,Does.Contain("notifications"));
            Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(5));
        }
        [Test] public void OfflineDisabledTimelineGetsNeitherKindOfReward()
        {
            foreach(var mode in new[]{OfflineProgressMode.BankTime,OfflineProgressMode.AutomaticCatchUp})
            {
                var timing=new TimingController(new SimulationTimeline(Sim(),false),new TimingOptions{OfflineMode=mode});
                var summary=timing.ClaimOffline(Utc,Utc.AddHours(1));Assert.That(summary.RewardsEnabled,Is.False);Assert.That(summary.EligibleSeconds,Is.Zero);
                Assert.That(timing.BankSeconds,Is.EqualTo(BigDouble.Zero));Assert.That(timing.HasPendingWork,Is.False);
            }
        }
        [Test] public void BudgetedBoostRoundTripRetainsExactDebtAndCharges()
        {
            var sim=Sim();var timing=TimingSnapshotService.Attach(new TestTimeline(sim){Chunk=.5},BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(100));
            timing.SetBoostMultiplier(4);timing.AdvanceOnline(1,1);timing.Continue(1);timing.Continue(1);
            DateTime savedAt=Utc.AddSeconds(101);var snapshot=TimingSnapshotService.Capture(timing,savedAt);
            Assert.That(snapshot.SavedAtUtc,Is.EqualTo(savedAt));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(99.5));
            var restored=TimingSnapshotService.Resume(sim.InitialContent,snapshot,new TimingUtcClock(savedAt),BankOptions(),
                timelineFactory:s=>new TestTimeline(s){Chunk=.5});
            Assert.That(restored.Timing.BoostMultiplier,Is.EqualTo(1));Drain(timing,1);Drain(restored.Timing,1);
            Assert.That(restored.Simulation.Time,Is.EqualTo(sim.Time));Assert.That(restored.Timing.BankSeconds.ToDouble(),Is.EqualTo(timing.BankSeconds.ToDouble()).Within(1e-9));
            Assert.That(restored.Timing.BankSeconds.ToDouble(),Is.EqualTo(97).Within(1e-9));
        }
        [Test] public void SkipsSurviveSaveAtAZeroDurationEventBoundary()
        {
            var sim=Sim();sim.Schedule(new GrantResourceCommand("gold",1),5);sim.Schedule(new GrantResourceCommand("gold",2),5);
            var timing=TimingSnapshotService.Attach(new SimulationTimeline(sim),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(5));
            timing.RequestSkip(5,1);Assert.That(timing.HasPendingWork,Is.True);Assert.That(sim.Time,Is.EqualTo(5));
            var snapshot=TimingSnapshotService.Capture(timing,Utc.AddSeconds(5));
            var resumed=TimingSnapshotService.Resume(sim.InitialContent,snapshot,new TimingUtcClock(Utc.AddSeconds(5)),BankOptions());
            Drain(timing);Drain(resumed.Timing);Assert.That(resumed.Simulation.GetResource("gold").Balance,Is.EqualTo(sim.GetResource("gold").Balance));
            Assert.That(resumed.Timing.BankSeconds,Is.EqualTo(BigDouble.Zero));
        }
        [Test] public void LegacyAdoptionPreservesOwedWorkAndDoesNotBankItAgain()
        {
            var sim=Sim(1);sim.Schedule(new GrantResourceCommand("gold",1),1);sim.Schedule(new GrantResourceCommand("gold",1),2);Assert.That(sim.AdvanceTo(10).Completed,Is.False);
            var old=SnapshotService.Capture(sim,Utc);
            Assert.Throws<InvalidOperationException>(()=>TimingSnapshotService.Resume(sim.InitialContent,old,new TimingUtcClock(Utc),BankOptions()));
            var resumed=TimingSnapshotService.Resume(sim.InitialContent,old,new TimingUtcClock(Utc.AddSeconds(20)),BankOptions(),adoptLegacy:true);
            Drain(resumed.Timing);Assert.That(resumed.Simulation.Time,Is.EqualTo(10));Assert.That(resumed.Timing.BankSeconds.ToDouble(),Is.EqualTo(20));
            Assert.That(old.Sections,Is.Empty);Assert.That(old.SavedAtUtc,Is.EqualTo(Utc));
        }
        [Test] public void CadenceRemainderSurvivesSaveAndChangedLoadCadence()
        {
            var sim=Sim();var timing=TimingSnapshotService.Attach(new SimulationTimeline(sim));timing.AdvanceOnline(.09);
            var snapshot=TimingSnapshotService.Capture(timing,Utc);
            var resumed=TimingSnapshotService.Resume(sim.InitialContent,snapshot,new TimingUtcClock(Utc),new TimingOptions{TickRateHz=40});
            Drain(resumed.Timing);Assert.That(resumed.Simulation.Time,Is.EqualTo(.075).Within(1e-9));
            resumed.Timing.AdvanceOnline(.01);Assert.That(resumed.Simulation.Time,Is.EqualTo(.1).Within(1e-9));
        }
        [Test] public void PolicyChangesPreserveOldWorkAndClaimsOnlyNewAbsence()
        {
            var sim=Sim();var timing=TimingSnapshotService.Attach(new TestTimeline(sim){Chunk=1});timing.ClaimOffline(Utc,Utc.AddSeconds(10));timing.Continue(1);
            var saved=TimingSnapshotService.Capture(timing,Utc.AddSeconds(10));
            var resumed=TimingSnapshotService.Resume(sim.InitialContent,saved,new TimingUtcClock(Utc.AddSeconds(15)),BankOptions());Drain(resumed.Timing);
            Assert.That(resumed.Simulation.Time,Is.EqualTo(10));Assert.That(resumed.Timing.BankSeconds.ToDouble(),Is.EqualTo(5));
        }
        [Test] public void InvalidTimingSnapshotsCannotChangeAnExistingController()
        {
            var timing=new TimingController(new SimulationTimeline(Sim()),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(5));
            var corrupt=timing.CaptureState();corrupt.BankSeconds=-1;
            Assert.Throws<ArgumentException>(()=>timing.RestoreState(corrupt));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(5));
            corrupt=timing.CaptureState();corrupt.Work.Add(new TimingWork{TargetTime=20,Kind=TimingWorkKind.Skip});
            Assert.Throws<ArgumentException>(()=>timing.RestoreState(corrupt));Assert.That(timing.HasPendingWork,Is.False);
        }
        [Test] public void BankSurvivesPrestige()
        {
            var b=new ModuleBuilder();b.Resource(new ResourceDefinition("gold",100));b.Prestige(new PrestigeDefinition("gold",10,2));var sim=b.Create();
            var timing=TimingSnapshotService.Attach(new SimulationTimeline(sim),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            Assert.That(sim.Execute(new PrestigeCommand()).Success,Is.True);Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(10));
        }
        [Test] public void DeclarativeTimelineBoostAdvancesBehaviorAndEconomyTogether()
        {
            var b=new GameDefinitionBuilder();var count=new PropertyKey<int>("count");b.Template("actor").Value(count,0);
            b.Action(new ActionDefinition("count",null,Effects.Add(count,Expr.Constant(1))));
            b.Behavior(new BehaviorDefinition("repeat",Behavior.Repeat(1,Behavior.Action("count"))));
            var definition=b.Compile();var sim=Sim();var runtime=new DeclarativeRuntime(sim,definition);var actor=definition.Templates["actor"].Instantiate(sim.Entities,"actor");runtime.Behaviors.Start("repeat",actor.Id);
            var timing=new TimingController(new DeclarativeTimeline(runtime),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));timing.SetBoostMultiplier(4);timing.AdvanceOnline(1);Drain(timing);
            Assert.That(sim.Time,Is.EqualTo(4));Assert.That(actor.GetValue(count).Current,Is.GreaterThanOrEqualTo(4));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(7));
        }
        [Test] public void FeatureTimelineBoostAndRestoreIncludesActualMachineCompletions()
        {
            var game=new FeatureGame(ExpandedCampaigns.Create("OrbitalFoundry"));var timing=TimingSnapshotService.Attach(new FeatureTimeline(game),BankOptions());
            timing.ClaimOffline(Utc,Utc.AddSeconds(100));timing.SetBoostMultiplier(4);timing.AdvanceOnline(10);Drain(timing);
            Assert.That(game.Time,Is.EqualTo(40));Assert.That(((ProductionModule)game.Module).State.Depot.Get("ore"),Is.GreaterThan(BigDouble.Zero));
            string json=SnapshotService.Serialize(TimingSnapshotService.Capture(timing,Utc.AddSeconds(110)));
            var loaded=FeatureGame.LoadTimed(ExpandedCampaigns.Create("OrbitalFoundry"),json,Utc.AddSeconds(120),BankOptions(),out var clock,out var offline);
            Assert.That(clock.BankSeconds.ToDouble(),Is.EqualTo(80));Assert.That(offline.BankedSeconds.ToDouble(),Is.EqualTo(10));Assert.That(loaded.Time,Is.EqualTo(40));
        }
        [Test] public void NarrativeBoundariesRefundSkipsAndNeverChooseForThePlayer()
        {
            var game=new FeatureGame(ExpandedCampaigns.Create("ThirteenthBell"));var timing=TimingSnapshotService.Attach(new FeatureTimeline(game),BankOptions());
            timing.ClaimOffline(Utc,Utc.AddSeconds(1000));Assert.That(game.Execute("investigate","station").Success,Is.True);
            Assert.That(timing.RequestSkip(500).Status,Is.EqualTo(TimelineAdvanceStatus.Blocked));
            Assert.That(game.Time,Is.EqualTo(150));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(850));
            Assert.That(((TimeLoopModule)game.Module).State.Run.Current,Is.EqualTo("station-result"));
            var story=new FeatureGame(ExpandedCampaigns.Create("Floodwall"));var storyTiming=new TimingController(new FeatureTimeline(story),BankOptions());
            Assert.That(storyTiming.AdvanceOnline(10).Status,Is.EqualTo(TimelineAdvanceStatus.Blocked));Assert.That(story.Time,Is.Zero);
            Assert.That(storyTiming.ClaimOffline(Utc,Utc.AddHours(1)).EligibleSeconds,Is.Zero);
        }
        [Test] public void GuildTimelineAndConfigurableCombatKeepRoundEffectsIntact()
        {
            GuildGame Start(double interval)
            {
                var definition=EmberGuild.Create();definition.CombatRoundSeconds=interval;var game=new GuildGame(definition);
                Assert.That(game.Hire("guardian"),Is.True);Assert.That(game.Assign(game.State.Heroes[0].Number,0),Is.True);Assert.That(game.Dispatch(0,0,false),Is.True);return game;
            }
            var normal=Start(2);var fast=Start(.5);normal.AdvanceTo(2);fast.AdvanceTo(.5);
            Assert.That(fast.State.Parties[0].Round,Is.EqualTo(normal.State.Parties[0].Round));
            Assert.That(fast.State.Parties[0].Enemies,Is.EqualTo(normal.State.Parties[0].Enemies));
            var timing=TimingSnapshotService.Attach(new GuildTimeline(fast),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));timing.SetBoostMultiplier(4);timing.AdvanceOnline(.5);Drain(timing);
            Assert.That(fast.Time,Is.EqualTo(2.5).Within(1e-9));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(8.5).Within(1e-9));
            string json=SnapshotService.Serialize(TimingSnapshotService.Capture(timing,Utc.AddSeconds(11)));
            var d=EmberGuild.Create();d.CombatRoundSeconds=.5;var loaded=GuildGame.LoadTimed(d,json,Utc.AddSeconds(11),BankOptions(),out var clock,out _);
            Assert.That(loaded.Time,Is.EqualTo(fast.Time));Assert.That(clock.BankSeconds,Is.EqualTo(timing.BankSeconds));
        }
        [Test] public void GameplayIntervalsAreIndependentOfControllerCadence()
        {
            var railway=ExpandedCampaigns.Railway();Assert.That(FeatureJson.Write(railway),Does.Not.Contain("ProductionTickSeconds"));railway.ProductionTickSeconds=5;
            var module=new LogisticsModule(railway);var game=new FeatureGame(module);var timing=new TimingController(new FeatureTimeline(game),new TimingOptions{TickRateHz=2});
            timing.AdvanceOnline(10);Drain(timing);Assert.That(module.State.Stations["valley"].Get("food").ToDouble(),Is.EqualTo(16));
            var reserve=ExpandedCampaigns.Reserve();reserve.StepSeconds=2;var ecology=new EcologyModule(reserve);var ecologyGame=new FeatureGame(ecology);
            var ecologyTiming=new TimingController(new FeatureTimeline(ecologyGame),new TimingOptions{TickRateHz=1});ecologyTiming.AdvanceOnline(4);Drain(ecologyTiming);
            Assert.That(ecology.State.Habitats[0].History.Count,Is.EqualTo(2));
            var exchange=ExpandedCampaigns.Exchange();exchange.Interval=2;var market=new MarketModule(exchange);var marketGame=new FeatureGame(market);
            var marketTiming=new TimingController(new FeatureTimeline(marketGame),new TimingOptions{TickRateHz=1});marketTiming.AdvanceOnline(4);Drain(marketTiming);
            Assert.That(market.State.Quotes[0].History.Count,Is.EqualTo(2));
        }
        [Test] public void RetryingBudgetedWorkKeepsItsAcceptedTarget()
        {
            var timeline=new TestTimeline(Sim()){Chunk=.1};var timing=new TimingController(timeline);
            timing.AdvanceOnline(1,1);double accepted=timing.CaptureState().Work[0].TargetTime;
            timing.AdvanceOnline(1,1);Assert.That(timing.CaptureState().Work[0].TargetTime,Is.EqualTo(accepted));
            Drain(timing,1);Assert.That(timeline.Time,Is.EqualTo(2).Within(1e-9));
        }
        [Test] public void DeclarativeContinuationsAndBoostDebtRoundTripTogether()
        {
            var b=new GameDefinitionBuilder();var count=new PropertyKey<int>("count");b.Template("actor").Value(count,0);
            b.Action(new ActionDefinition("count",null,Effects.Add(count,Expr.Constant(1))));
            b.Behavior(new BehaviorDefinition("repeat",Behavior.Repeat(1,Behavior.Action("count"))));var definition=b.Compile();
            var sim=Sim();var module=new DeclarativeModule(definition);new ModuleBuilder().Add(module).Attach(sim);
            definition.Templates["actor"].Instantiate(sim.Entities,"actor");module.Runtime.Behaviors.Start("repeat","actor");
            var timing=TimingSnapshotService.Attach(new DeclarativeTimeline(module.Runtime),BankOptions());
            timing.ClaimOffline(Utc,Utc.AddSeconds(100));timing.SetBoostMultiplier(4);timing.AdvanceOnline(1,1);
            var saved=TimingSnapshotService.Capture(timing,Utc.AddSeconds(101));var loadedModule=new DeclarativeModule(definition);
            var resumed=TimingSnapshotService.Resume(sim.InitialContent,saved,new TimingUtcClock(Utc.AddSeconds(101)),BankOptions(),
                new ModuleBuilder().Add(loadedModule),s=>new DeclarativeTimeline(loadedModule.Runtime));
            Drain(timing,1);Drain(resumed.Timing,1);
            Assert.That(resumed.Simulation.Time,Is.EqualTo(sim.Time));Assert.That(resumed.Timing.BankSeconds,Is.EqualTo(timing.BankSeconds));
            Assert.That(resumed.Simulation.Entities.Find("actor").GetValue(count).Current,Is.EqualTo(sim.Entities.Find("actor").GetValue(count).Current));
        }
        [Test] public void OfflineClaimsCannotUseReservedCreditAsFreeStorageSpace()
        {
            var options=BankOptions();options.BankCapacitySeconds=10;
            var timing=new TimingController(new TestTimeline(Sim()){Chunk=1},options);timing.ClaimOffline(Utc,Utc.AddSeconds(10));timing.RequestSkip(5,1);
            var claim=timing.ClaimOffline(Utc,Utc.AddSeconds(20));
            Assert.That(claim.BankedSeconds.ToDouble(),Is.EqualTo(1));Assert.That(timing.BankSeconds.ToDouble(),Is.EqualTo(10));
            Assert.That(timing.ReservedBankSeconds.ToDouble(),Is.EqualTo(4));
        }
        [Test] public void LargeConfiguredBoostIsBoundedByAffordableCreditBeforeOverflow()
        {
            var options=BankOptions();options.BoostMultipliers=new[]{1e308};
            var timing=new TimingController(new SimulationTimeline(Sim()),options);timing.ClaimOffline(Utc,Utc.AddSeconds(1));
            Assert.That(timing.SetBoostMultiplier(1e308).Success,Is.True);timing.AdvanceOnline(10);
            Assert.That(timing.Timeline.Time,Is.EqualTo(11));Assert.That(timing.BankSeconds,Is.EqualTo(BigDouble.Zero));
        }
        [Test] public void AllGameplayIntervalsRejectInvalidValuesAndCaptureConstructionSettings()
        {
            var rail=ExpandedCampaigns.Railway();rail.ProductionTickSeconds=0;Assert.Throws<ArgumentException>(()=>new LogisticsModule(rail));
            var eco=ExpandedCampaigns.Reserve();eco.StepSeconds=double.NaN;Assert.Throws<ArgumentException>(()=>new EcologyModule(eco));
            var market=ExpandedCampaigns.Exchange();market.Interval=double.PositiveInfinity;Assert.Throws<ArgumentException>(()=>new MarketModule(market));
            var guild=EmberGuild.Create();guild.CombatRoundSeconds=-1;Assert.Throws<ArgumentOutOfRangeException>(()=>new GuildGame(guild));
            rail=ExpandedCampaigns.Railway();rail.ProductionTickSeconds=5;var module=new LogisticsModule(rail);var game=new FeatureGame(module);
            module.Definition.ProductionTickSeconds=100;while(!game.AdvanceTo(10)){}
            Assert.That(module.State.Stations["valley"].Get("food").ToDouble(),Is.EqualTo(16));
        }
        [Test] public void SavingAndRestoringTheSameClaimDoesNotAccumulateItAgain()
        {
            var sim=Sim();var timing=TimingSnapshotService.Attach(new SimulationTimeline(sim),BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(10));
            var saved=TimingSnapshotService.Capture(timing,Utc.AddSeconds(10));
            var a=TimingSnapshotService.Resume(sim.InitialContent,saved,new TimingUtcClock(Utc.AddSeconds(20)),BankOptions());
            var b=TimingSnapshotService.Resume(sim.InitialContent,saved,new TimingUtcClock(Utc.AddSeconds(20)),BankOptions());
            Assert.That(a.Timing.BankSeconds.ToDouble(),Is.EqualTo(20));Assert.That(b.Timing.BankSeconds,Is.EqualTo(a.Timing.BankSeconds));
            var latest=TimingSnapshotService.Capture(a.Timing,Utc.AddSeconds(20));
            var again=TimingSnapshotService.Resume(sim.InitialContent,latest,new TimingUtcClock(Utc.AddSeconds(20)),BankOptions());
            Assert.That(again.Timing.BankSeconds.ToDouble(),Is.EqualTo(20));
        }
        [Test] public void ADetachedTimingStateCannotMutateItsController()
        {
            var timing=new TimingController(new TestTimeline(Sim()){Chunk=.1},BankOptions());timing.ClaimOffline(Utc,Utc.AddSeconds(5));timing.RequestSkip(1,1);
            var copy=timing.CaptureState();copy.BankSeconds=900;copy.Work[0].TargetTime=900;
            Assert.That(timing.BankSeconds.ToDouble(),Is.LessThan(5));Assert.That(timing.CaptureState().Work[0].TargetTime,Is.EqualTo(1));
        }
    }
}
