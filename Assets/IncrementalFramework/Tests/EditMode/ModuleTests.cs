using BreakInfinity;
using IncrementalFramework;
using NUnit.Framework;

namespace IncrementalFrameworkTests
{
    public sealed class ModuleTests
    {
        private sealed class Craft : ISimulationCommand { }
        private sealed class CraftHandler : ICommandHandler<Craft>
        {
            public readonly DenseStateStore<int> Counters = new DenseStateStore<int>();
            private readonly int crafted;
            public CraftHandler() { crafted = Counters.Register("crafted", 0); }
            public int Crafted => Counters[crafted];
            public CommandResult Build(Craft command, SimulationReadModel state, SimulationTransaction transaction)
            {
                transaction.Spend("gold", 6);
                transaction.Grant("gem", 1);
                transaction.Set(Counters, crafted, Crafted + 1);
                return CommandResult.Ok();
            }
        }
        private sealed class CraftModule : IGameModule
        {
            public readonly CraftHandler Handler = new CraftHandler();
            public void Register(ModuleBuilder builder)
            {
                builder.Resource(new ResourceDefinition("gold", 10));
                builder.Resource(new ResourceDefinition("gem"));
                builder.Command(Handler);
            }
        }
        private sealed class ScheduledSystem : ISimulationSystem
        {
            public int Calls;
            public void BeforeAdvance(SimulationReadModel state, double target, SimulationSchedule schedule)
            {
                Calls++;
                if (state.Time < 100 && target >= 100)
                {
                    schedule.Schedule(new GrantResourceCommand("gold", 1), 100);
                    schedule.Schedule(new GrantResourceCommand("gold", 2), 100);
                }
            }
        }

        [Test]
        public void ModuleRegistersDefinitionsAndAtomicResourceAndTypedStateCommands()
        {
            var module = new CraftModule();
            var simulation = new ModuleBuilder().Add(module).Create();
            Assert.That(simulation.Execute(new Craft()).Success, Is.True);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(new BigDouble(4)));
            Assert.That(simulation.GetResource("gem").Balance, Is.EqualTo(BigDouble.One));
            Assert.That(module.Handler.Crafted, Is.EqualTo(1));
            Assert.That(simulation.Execute(new Craft()).Success, Is.False);
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(new BigDouble(4)));
            Assert.That(simulation.GetResource("gem").Balance, Is.EqualTo(BigDouble.One));
            Assert.That(module.Handler.Crafted, Is.EqualTo(1));
        }

        [Test]
        public void ModuleSystemsScheduleCommandsAndAreNotRepeatedOnBudgetResume()
        {
            var builder = new ModuleBuilder();
            builder.Resource(new ResourceDefinition("gold"));
            var system = new ScheduledSystem();
            builder.System(system);
            var simulation = builder.Create(eventBudget: 1);
            Assert.That(simulation.AdvanceTo(200).Completed, Is.False);
            Assert.That(simulation.AdvanceTo(200).Completed, Is.True);
            Assert.That(system.Calls, Is.EqualTo(1));
            Assert.That(simulation.GetResource("gold").Balance, Is.EqualTo(new BigDouble(3)));
        }
    }
}
