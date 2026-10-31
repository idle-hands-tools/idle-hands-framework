using System;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Timing;

namespace IncrementalFramework.Games.Rpg
{
    public sealed class GuildTimeline : ITimeline
    {
        public GuildGame Game { get; }
        public Simulation Simulation => Game.Economy;
        public double Time => Game.Time;
        public bool AllowsOfflineRewards { get; }
        public bool IsBlocked => false;
        public GuildTimeline(GuildGame game,bool allowsOfflineRewards=true)
        {Game=game??throw new ArgumentNullException(nameof(game));AllowsOfflineRewards=allowsOfflineRewards;}
        public TimelineAdvanceResult AdvanceTo(double target,int workBudget)=>Game.AdvanceTimed(target,workBudget);
    }
}
