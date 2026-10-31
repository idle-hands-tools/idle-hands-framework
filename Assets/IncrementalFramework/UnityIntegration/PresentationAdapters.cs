using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.Collections.Generic;

namespace IncrementalFramework.Unity
{
    public interface IPresentationAdapter
    {
        void Attach(SimulationReadModel state, Func<ISimulationCommand, CommandResult> dispatch);
        void Detach();
    }

    /// <summary>Optional module presentation composition, separate from headless registration.</summary>
    public sealed class PresentationAdapters : IDisposable
    {
        private readonly Simulation simulation;
        private readonly List<IPresentationAdapter> adapters = new List<IPresentationAdapter>();
        private bool disposed;
        public PresentationAdapters(Simulation simulation)
        { this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation)); }
        public void Register(IPresentationAdapter adapter)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PresentationAdapters));
            if (adapter == null || adapters.Contains(adapter)) throw new ArgumentException("Null or duplicate presentation adapter.");
            adapter.Attach(simulation.GetReadModel(), simulation.Execute);
            adapters.Add(adapter);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = adapters.Count - 1; i >= 0; i--) adapters[i].Detach();
            adapters.Clear();
        }
    }
}
