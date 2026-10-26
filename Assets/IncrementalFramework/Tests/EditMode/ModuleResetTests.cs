using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class ModuleResetTests
    {
        private sealed class Participant : IPrestigeResetParticipant
        {
            private readonly DenseStateStore<int> store;
            private readonly int index;
            private readonly int result;
            private readonly bool fail;
            public ResetPolicy Policy { get; }
            public int Calls { get; private set; }
            public Participant(DenseStateStore<int> store, int index, ResetPolicy policy, int result, bool fail = false)
            { this.store = store; this.index = index; Policy = policy; this.result = result; this.fail = fail; }
            public CommandResult Prepare(SimulationReadModel state, StateTransaction transaction)
            {
                Calls++;
                transaction.Set(store, index, Policy == ResetPolicy.Transform ? store[index] * result : result);
                return fail ? CommandResult.Fail("Reset vetoed.") : CommandResult.Ok();
            }
        }
        private static ModuleBuilder Builder()
        {
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold", 10));
            builder.Producer(new ProducerDefinition("worker", null, new[] { new ResourceAmount("gold", 1) }));
            builder.Prestige(new PrestigeDefinition("gold", 10, 2));
            return builder;
        }

        [Test]
        public void PrestigeAppliesModuleResetTransformRecalculateAndPreserveTogether()
        {
            var store = new DenseStateStore<int>();
            int reset = store.Register("reset", 5), preserve = store.Register("preserve", 5);
            int transform = store.Register("transform", 5), recalculate = store.Register("recalculate", 5);
            var preserved = new Participant(store, preserve, ResetPolicy.Preserve, 99);
            var builder = Builder();
            builder.State("module", store);
            builder.Reset(new Participant(store, reset, ResetPolicy.Reset, 2));
            builder.Reset(preserved);
            builder.Reset(new Participant(store, transform, ResetPolicy.Transform, 2));
            builder.Reset(new Participant(store, recalculate, ResetPolicy.Recalculate, 42));
            var simulation = builder.Create();
            var result = simulation.Execute(new PrestigeCommand());
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(store[reset], Is.EqualTo(2));
            Assert.That(store[preserve], Is.EqualTo(5));
            Assert.That(store[transform], Is.EqualTo(10));
            Assert.That(store[recalculate], Is.EqualTo(42));
            Assert.That(preserved.Calls, Is.Zero);
        }

        [Test]
        public void ModuleResetVetoDoesNotPartiallyResetNativeOrTypedState()
        {
            var store = new DenseStateStore<int>();
            int index = store.Register("counter", 5);
            var builder = Builder();
            builder.Reset(new Participant(store, index, ResetPolicy.Reset, 0, fail: true));
            var simulation = builder.Create();
            Assert.That(simulation.Execute(new BuyProducerCommand("worker")).Success, Is.True);
            Assert.That(simulation.Execute(new PrestigeCommand()).Success, Is.False);
            Assert.That(simulation.GetProducer("worker").Owned, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(new BigDouble(10)));
            Assert.That(simulation.PrestigeCount, Is.Zero);
            Assert.That(store[index], Is.EqualTo(5));
        }
    }
}
