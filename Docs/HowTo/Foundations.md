# Compose a game before extending it

Build new behavior in a headless game assembly. Keep content in a sample/content
assembly and Unity input/presentation in an adapter. Link the same sources into a
standalone project and use `noEngineReferences: true` for headless Unity assemblies.
Use stable IDs for templates, instances, commands, recipes, and snapshot sections.

## Choose the composition boundary

| Need | Starting point | State ownership |
| --- | --- | --- |
| Passive economy, purchases, rules | `ModuleBuilder` + `Simulation` | Kernel resources/producers plus explicitly registered module state |
| Timed actions on entities | `DeclarativeModule` | Entity Values and saved behavior continuations |
| A bespoke discrete simulation | `FeatureModule<TState>` + `FeatureGame` | One transactional feature state and its saved event queue |
| Party-based adventure | `GuildGame` and authored `GuildDefinition` | Guild world, characters, parties, and quests |
| Branching story | `NarrativeModule` | Story variables, transcript, journal, and checkpoints |

`ModuleBuilder` composes modules with distinct command types, state IDs, and snapshot
section IDs. Each registered command type has one handler. It is not a dependency
injection container and does not create arbitrary cross-module transactions.

The supplied `ProductionModule`, `LogisticsModule`, `MarketModule`, and
`EcologyModule` are sealed sample mechanics. They all handle `FeatureCommand`;
registering several directly on one builder produces a handler collision. The
current `FeatureGame` advances one feature queue. To combine factory and railway
mechanics, write a combined `FeatureModule<YourWorldState>` owning both inventories
and one queue, or implement distinct-command modules and a complete runtime
coordinator. Reuse definitions and extract narrow calculation services as needed.
The coordinator must own advancement, transfers, and persistence explicitly.

## Keep transactions inside one authority

Use `ICommandHandler<T>` and `SimulationTransaction` for kernel resource costs and
registered `DenseStateStore<T>` writes. Use `ActionDefinition`, `Effects`, and
`EntityTransaction` for entity-backed gameplay. Use `FeatureModule<TState>` when an
entire discrete state can be staged as one feature transaction.

Two separately committed commands are two transactions. Spending in an inventory
and then granting a kernel resource can partially succeed. For an operation that
must change both, move the affected ownership into one transaction model or build
an explicit durable transfer protocol. Validate the full action before committing;
reject insufficient inputs, invalid references, and unavailable capacity together.

Feature `State` getters return detached copies. Mutating such a copy outside a
command does not update the live game. Notifications are for projection and input
feedback; issue later commands from the host rather than mutating simulation state
inside notification callbacks.

## Example: a saved, timed work order

This complete example adds a small workshop with one recipe. A successful command
spends four ore, reserves space for two plates, and schedules completion. It shows
the foundation used by production, contracts, maintenance, and research recipes.
It does not replace the richer supplied production module.

```csharp
// compile: workshop
using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Features;

namespace IdleHands.HowTo
{
    public sealed class WorkshopState : FeatureState
    {
        public Inventory Stock { get; set; } = new Inventory { Capacity = 12 };
        public bool Working { get; set; }
    }

    public sealed class WorkshopModule : FeatureModule<WorkshopState>
    {
        public override string Id => "howto.workshop";
        public WorkshopModule() : base(Initial(), "workshop-content-v1") { }

        private static WorkshopState Initial()
        {
            var state = new WorkshopState();
            state.Stock.Put("ore", 8);
            return state;
        }

        protected override string Act(WorkshopState state, FeatureCommand command, double time)
        {
            if (command.Action != "smelt") return "Unknown workshop action.";
            if (state.Working) return "The workshop is already busy.";
            var input = new Dictionary<string, BigDouble> { ["ore"] = 4 };
            if (!state.Stock.CanTake(input)) return "Requires four ore.";
            if (state.Stock.Used - 4 + state.Stock.Reserved + 2 > state.Stock.Capacity)
                return "No room for the completed batch.";
            state.Stock.Take(input);
            state.Stock.Reserved += 2;
            state.Working = true;
            state.Schedule(time + 5, "batch.complete");
            state.Message = "Smelting.";
            return null;
        }

        protected override void OnEvent(WorkshopState state, FeatureEvent evt, double time)
        {
            if (evt.Kind != "batch.complete" || !state.Working)
                throw new ArgumentException("Invalid workshop completion.");
            state.Stock.Reserved -= 2;
            if (!state.Stock.Put("plate", 2)) throw new InvalidOperationException("Reserved output was lost.");
            state.Working = false;
            state.Message = "Two plates completed.";
        }

        protected override void ValidateState(WorkshopState state)
        {
            base.ValidateState(state);
            if (state.Stock == null) throw new ArgumentException("Missing inventory.");
            state.Stock.Validate();
            int completions = state.Events.Count(e => e.Payload.Kind == "batch.complete");
            if (state.Events.Any(e => e.Payload.Kind != "batch.complete") || completions != (state.Working ? 1 : 0) || state.Stock.Reserved != (state.Working ? 2 : 0))
                throw new ArgumentException("Invalid workshop reservation.");
        }
    }
}
```

Create `new FeatureGame(new WorkshopModule())`, then call `game.Execute("smelt")`.
Advance through `FeatureTimeline` if timing is attached, or `game.AdvanceTo` if it
isn't. The module base registers its handler, state, content fingerprint, and
snapshot section. Restore with a **fresh** `WorkshopModule` and matching content.
For configurable recipes, pass the actual immutable recipe content to the base
constructor instead of a version string alone, so fingerprinting covers it.

**Check:** a repeated smelt while busy spends nothing; a save at time 2 completes
once at time 5; a small work budget produces the same final inventory as a large
one; corrupted reservation data is rejected. Add cancellation only after defining
whether consumed inputs and output reservations are refunded.

## Use one complete clock and one save factory

Attach `TimingController` to the complete runtime: `SimulationTimeline`,
`DeclarativeTimeline`, `FeatureTimeline`, or `GuildTimeline`. A controller around
only a guild's economy would accelerate gold while leaving combat behind.
Honor `HasPendingWork` before accepting gameplay mutations; `Blocked` can mean a
story is waiting for a valid player choice. See [timing](../Timing.md) for accounting.

Write one restore factory for your game. It must register the same templates,
handlers, extensions, systems, and versioned sections with fresh module instances;
restore saved data; rebuild derived bindings and event subscriptions; then bind its
complete timeline. Keep configuration separate from mutable state. Save stable IDs
and explicit DTOs, never delegates or arbitrary CLR type names.

For module-owned queues, persist event payloads, deadlines, ordering/sequence state,
RNG state, and any in-flight work. `Simulation.Schedule` supports the built-in
serializable command kinds, not arbitrary custom command instances. Use declared
behavior continuations or an explicitly snapshotted custom queue for custom events.
Changing a snapshot section requires a version/migration decision; changing feature
content may require a content migration even if the root schema stays the same.

For new typed-state modules, register an `ISnapshotSection` and an
`IPrestigeResetParticipant` when applicable. Entity-backed custom state also needs
an explicit reset policy: the kernel's native resource/producer reset does not
invent a reset for arbitrary entities. The stock feature sample `replay` commands
are separate game actions, not automatic participants in kernel prestige.
