using System;
using System.Collections.Generic;
using IncrementalFramework.Core;

namespace IncrementalFramework.Progression
{
    /// <summary>A saved module queue competing with native events for the simulation's work budget.</summary>
    public interface ISimulationWorkSource
    {
        string Id { get; }
        double? NextTime { get; }
        void Invalidate(double time);
        void Process(SimulationWorkContext context);
    }

    public sealed class SimulationWorkContext
    {
        private readonly Func<ISimulationCommand, CommandResult> execute;
        private bool open = true;
        public double Time { get; }
        internal SimulationWorkContext(double time, Func<ISimulationCommand, CommandResult> execute)
        { Time = time; this.execute = execute; }
        public CommandResult Execute(ISimulationCommand command)
        { if (!open) throw new InvalidOperationException("Work context has expired."); return execute(command); }
        internal void Close() => open = false;
    }

    public sealed partial class Simulation
    {
        private readonly List<ISimulationWorkSource> workSources = new List<ISimulationWorkSource>();
        private ISimulationWorkSource activeWorkSource;
        public void RegisterWorkSource(ISimulationWorkSource source)
        {
            AssertWriter();
            if (source == null || string.IsNullOrWhiteSpace(source.Id) || workSources.Exists(s => s.Id == source.Id))
                throw new ArgumentException("Invalid or duplicate work source.");
            workSources.Add(source);
            workSources.Sort((a,b) => StringComparer.Ordinal.Compare(a.Id,b.Id));
        }
        private void InvalidateWorkSources()
        { foreach (var source in workSources) if (source != activeWorkSource) source.Invalidate(Time); }
        private ISimulationWorkSource NextWorkSource(double horizon)
        {
            ISimulationWorkSource selected = null; double next = double.PositiveInfinity;
            foreach (var source in workSources)
            {
                double? due = source.NextTime;
                if (!due.HasValue) continue;
                if (!Finite(due.Value) || due.Value < Time) throw new InvalidOperationException("Invalid module work deadline.");
                if (due.Value <= horizon && due.Value < next) { selected = source; next = due.Value; }
            }
            return selected;
        }
        private void ProcessWorkSource(ISimulationWorkSource source)
        {
            activeWorkSource = source;
            var context = new SimulationWorkContext(Time, ExecuteInternal);
            try { source.Process(context); }
            finally { context.Close(); activeWorkSource = null; }
        }
    }
}
