using System;
using IncrementalFramework;

namespace IdleHands.UnityIntegration
{
    public sealed class SimulationDriver
    {
        public Simulation Simulation { get; }
        public event Action Changed;

        public SimulationDriver(Simulation simulation)
        { Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation)); }

        public CommandResult Execute(ISimulationCommand command)
        {
            var result = Simulation.Execute(command);
            if (result.Success) Changed?.Invoke();
            return result;
        }

        public AdvanceResult AdvanceTo(double target)
        {
            double before = Simulation.Time;
            var result = Simulation.AdvanceTo(target);
            if (Simulation.Time != before || result.ProcessedEvents != 0) Changed?.Invoke();
            return result;
        }
    }
}
