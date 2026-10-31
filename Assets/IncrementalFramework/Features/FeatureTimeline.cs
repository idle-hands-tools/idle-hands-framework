using System;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Timing;

namespace IncrementalFramework.Features
{
    public sealed class FeatureTimeline : ITimeline
    {
        public FeatureGame Game { get; }
        public Simulation Simulation => Game.Simulation;
        public double Time => Game.Time;
        public bool AllowsOfflineRewards => Game.Module.RunsOffline;
        public bool IsBlocked => Game.Module is ITimelineGate gate && gate.IsTimeBlocked;
        public FeatureTimeline(FeatureGame game) { Game=game??throw new ArgumentNullException(nameof(game)); }
        public TimelineAdvanceResult AdvanceTo(double target,int workBudget) => IsBlocked
            ? new TimelineAdvanceResult(Time,TimelineAdvanceStatus.Blocked)
            : Game.AdvanceTimed(target,workBudget);
    }
}
