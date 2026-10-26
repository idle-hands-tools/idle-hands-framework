using System;
using System.Collections.Generic;
using IdleHands.UnityIntegration;
using IncrementalFramework;
using NUnit.Framework;

namespace IdleHands.Tests
{
    public sealed class PresentationAdapterTests
    {
        private sealed class Adapter : IPresentationAdapter
        {
            private readonly int id;
            private readonly List<int> detached;
            public SimulationReadModel State;
            public Func<ISimulationCommand, CommandResult> Dispatch;
            public Adapter(int id, List<int> detached) { this.id = id; this.detached = detached; }
            public void Attach(SimulationReadModel state, Func<ISimulationCommand, CommandResult> dispatch)
            { State = state; Dispatch = dispatch; }
            public void Detach() { detached.Add(id); State = null; Dispatch = null; }
        }
        [Test]
        public void OptionalAdaptersReceiveReadModelsAndCommandDispatchAndDetachInReverseOrder()
        {
            var simulation = new Simulation(new ContentDatabase(new[] { new ResourceDefinition("gold") }));
            var detached = new List<int>();
            var first = new Adapter(1, detached);
            var second = new Adapter(2, detached);
            using (var adapters = new PresentationAdapters(simulation))
            {
                adapters.Register(first); adapters.Register(second);
                Assert.That(first.Dispatch(new GrantResourceCommand("gold", 5)).Success, Is.True);
                Assert.That(first.State.Resource(0).Balance.Mantissa, Is.EqualTo(5));
                Assert.Throws<ArgumentException>(() => adapters.Register(first));
                adapters.Dispose();
                Assert.Throws<ObjectDisposedException>(() => adapters.Register(first));
            }
            Assert.That(detached, Is.EqualTo(new[] { 2, 1 }));
        }
    }
}
