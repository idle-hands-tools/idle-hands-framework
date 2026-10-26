using System;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class RandomSourceTests
    {
        private sealed class TrackedRandom : IRandomSource
        {
            private readonly XorShift64 random = new XorShift64(123);
            public int Calls;
            public string Algorithm => random.Algorithm;
            public ulong State => random.State;
            public ulong NextUInt64() { Calls++; return random.NextUInt64(); }
        }
        [Test]
        public void InjectedRandomIsUsedAndSnapshotContinuesTheSameAlgorithm()
        {
            var source = new TrackedRandom();
            var simulation = new Simulation(new ContentDatabase(new ResourceDefinition[0]), source);
            simulation.NextRandomUInt64();
            Assert.That(source.Calls, Is.EqualTo(1));
            var restored = Simulation.FromState(simulation.Content, simulation.ExportState());
            for (int i = 0; i < 20; i++)
                Assert.That(simulation.NextRandomUInt64(), Is.EqualTo(restored.NextRandomUInt64()));
        }
        [Test]
        public void AdvancingOwnedRandomExternallyIsDetected()
        {
            var source = new TrackedRandom();
            var simulation = new Simulation(new ContentDatabase(new ResourceDefinition[0]), source);
            source.NextUInt64();
            Assert.Throws<InvalidOperationException>(() => simulation.NextRandomUInt64());
            Assert.That(simulation.ExportState().RandomState, Is.EqualTo(123UL));
        }
    }
}
