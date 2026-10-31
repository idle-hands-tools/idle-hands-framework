# Event and objective graphs

Use `IncrementalFramework.Progression.Graphs` for milestones, quest chains,
scripted events, and objectives. One definition can have many independent instances,
identified by instance ID, owner entity ID, and run number. A graph has at least one
node. Parallel branches are supported; each run is acyclic and closes when an end
node completes.

`GraphRuntime` lives in the headless Progression assembly. `GraphModule`, its
composition and JSON persistence facade, lives in Persistence under the same
namespace. Neither depends on Unity. `GraphAsset` lives in the Unity adapter.

## Lifecycle

| State | Meaning |
| --- | --- |
| Dormant | Prerequisites or initial activation requirements are unmet |
| Active | Activated, but the trigger requirement is false |
| Ready | Activated and the trigger requirement is true |
| Suspended | A maintained node or its graph has lost activation eligibility |
| Completed | Completion effects committed successfully |
| Skipped | Another end node closed this run first |

Missing requirements mean true. Event nodes default to automatic completion;
objective nodes default to explicit completion. Both can override that policy.
Automatic events with no requirements therefore run as soon as they activate.
An explicit objective with no trigger becomes immediately ready.

Activation defaults to **Latched**: once activated, the activation requirement no
longer gates the node. **Maintained** activation suspends it while the requirement
is false and resumes it when true. Progress remains, active time/counting pauses,
and activation effects do not repeat. Trigger requirements always revalidate, so
Ready can return to Active. Graph-level maintained activation suspends unfinished
nodes as a group.

Node activation, readiness, and completion are separate budgeted transitions. Costs,
rewards, lifecycle markers, and end-of-run closure commit atomically at their own
transition. A failed completion leaves the node uncompleted and exposes `LastError`.
Automatic failures retry after the definition's evaluation interval, or earlier when
an input changes. Explicit failures require another completion request.

Completing an end node also applies graph completion effects and skips unfinished
nodes. Their timers and signal tracking stop. At the same simulation time, native
kernel events run first; graph work is ordered by instance ID and node ID. Two
eligible endings therefore have a deterministic winner.

## Three definitions to adapt

This complete compilation unit illustrates a one-node event, parallel objectives,
and a repeatable graph whose activation must stay satisfied. Each uses an existing
native `gold` resource; the repeatable graph's owner also defines `Enabled` as bool.

```csharp
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression.Graphs;

namespace IdleHands.Examples
{
    public static class GraphExamples
    {
        public static GraphDefinition WealthEvent() => new GraphDefinition(
            "wealth-event", new[]
            {
                new GraphNodeDefinition("celebrate", GraphNodeKind.Event, isEnd: true,
                    trigger: GraphRequirements.Resource("gold", 100),
                    onComplete: new[] { GraphEffect.Native(new GrantResourceAction("gold", 10)) })
            });

        public static GraphDefinition ParallelObjectives() => new GraphDefinition(
            "guild-initiation", new[]
            {
                new GraphNodeDefinition("deliver", trigger: GraphRequirements.Signal("delivery", 3)),
                new GraphNodeDefinition("explore", trigger: GraphRequirements.Signal("exploration")),
                new GraphNodeDefinition("join", GraphNodeKind.Event, isEnd: true,
                    prerequisites: new[] { "deliver", "explore" },
                    prerequisiteMode: GraphPrerequisiteMode.All,
                    onComplete: new[] { GraphEffect.Native(new GrantResourceAction("gold", 25)) })
            });

        public static GraphDefinition RepeatablePatrol() => new GraphDefinition(
            "patrol", new[]
            {
                new GraphNodeDefinition("finish", GraphNodeKind.Event, isEnd: true,
                    trigger: GraphRequirements.All(
                        GraphRequirements.Time(30, GraphTimeScope.ActiveElapsed),
                        GraphRequirements.Signal("patrol-step", 5, GraphProgressScope.Active)),
                    onComplete: new[] { GraphEffect.Native(new GrantResourceAction("gold", 5)) })
            }, activation: GraphRequirements.State("patrol-enabled",
                    Expr.Property(new PropertyKey<bool>("Enabled"))),
            activationMode: GraphActivationMode.Maintained,
            restartCooldown: 60,
            prestigePolicy: ResetPolicy.Reset);
    }
}
```

A node may use `GraphPrerequisiteMode.Any` for alternative paths. A completed end
skips the losing branch. Completion requirements can also reference another node
using `GraphRequirements.NodeCompleted(id)`; these references participate in cycle
and path validation.

## Attach, start, signal, and complete

Register one module containing all your definitions. Create the owner entity before
starting instances. Definitions and templates must be identical when restoring.

```csharp
var graphs = new GraphModule(new[] {
    GraphExamples.WealthEvent(),
    GraphExamples.ParallelObjectives(),
    GraphExamples.RepeatablePatrol()
});
var ownerTemplate = new EntityTemplate("player");
ownerTemplate.Define("Enabled", true);
var builder = new ModuleBuilder();
builder.Resource(new ResourceDefinition("gold"));
builder.Entity(ownerTemplate);
builder.Add(graphs);
var simulation = builder.Create();
ownerTemplate.Instantiate(simulation.Entities, "player:1");

simulation.Execute(new StartGraphCommand("initiation:1", "guild-initiation", "player:1"));
// Drain the SAME target if the work budget is exhausted.
double target = simulation.Time;
while (!simulation.AdvanceTo(target).Completed) { }
simulation.Execute(new PublishGraphSignalCommand("player:1", "delivery", 3));
target = simulation.Time;
while (!simulation.AdvanceTo(target).Completed) { }

var node = graphs.Runtime.View("initiation:1", "deliver");
simulation.Execute(new CompleteGraphNodeCommand("initiation:1", "deliver", node.Run.Current));
```

For a future target, retain the original target in a variable across continuation
calls. Do not substitute the partially reached time. When timing is attached,
drive its complete timeline through `TimingController` instead of advancing the
simulation separately. Existing `SimulationTimeline`, `DeclarativeTimeline`,
`FeatureTimeline`, and `GuildTimeline` share this scheduler. Ready objectives do not
pause time or block offline progress.

Check each command's `CommandResult` and report rejection reasons in the host UI.

Signals are explicit gameplay facts: publish them after a successful delivery,
encounter, or other operation. Each successful publication is a new occurrence;
transport-level duplicate delivery must be handled by the publishing game system.
The command routes to unfinished instances for its owner, not other owners.
Only authored signal keys are retained, and counts must be positive, bounded longs.
If a signal must be atomic with another system's operation, implement a combined
command/transaction rather than issuing two independent commands and assuming
rollback across them.

## Requirements and evaluation

- `All`, `Any`, and `Not` compose requirements.
- `Resource` checks a native balance or lifetime earnings; `Flag` checks a native
  flag. Register flags through `ModuleBuilder.Flag`.
- `State(stableKey, Expression<bool>)` reuses typed expressions and expression
  extensions. Its evaluation context uses the owner as Self, Target, and Source.
- `Signal` chooses counts since run start or only while the relevant node is active.
  Graph-level active-scope requirements use the graph's own active counter.
  Use Run scope for signals that must unlock initial activation; an inactive node
  cannot accumulate active-scoped progress.
- `Time` chooses absolute simulation time, elapsed time since the run started, or
  accumulated active duration. Run time includes periods before graph activation;
  active duration excludes suspension.
- `Custom` resolves an `IGraphRequirementHandler` from `GraphExtensions`. It declares
  property dependencies and an optional next simulation-time evaluation deadline.

Native resource thresholds and time requirements schedule known deadlines directly.
Arbitrary state expressions are evaluated on invalidation and at the definition's
`evaluationInterval` (default one simulated second). Custom handlers with no next
deadline use that same fallback. This is deterministic simulation-time sampling,
not exact root finding for arbitrary formulas. Set an appropriate interval or supply
an exact custom deadline when a short-lived time window matters.

Invalidations currently conservatively wake unfinished instances; declared
custom dependencies are validated, not a promise of a fully indexed graph engine.
Requirement evaluation must be pure. External data must be projected into saved,
observable game state or explicitly invalidate the runtime. Do not read wall time,
mutate game state, or draw randomness from a requirement.

An owner removed from the world suspends its unfinished graph. Adding the same
stable owner identity lets it resume. Automatic restart stops if the owner is
missing. This state can be saved and restored without replaying effects.

## Effects and extensions

`GraphEffect.Native` accepts resource grants/spends, flags, producer/upgrade unlocks,
modifier toggles, and scheduled resource grants. Rules and graphs use the same
kernel action staging/commit path. All costs are validated before grants; rewards
from an operation cannot pay its own otherwise unaffordable costs. Resource caps,
negative-value policies, lifetime earnings, and modifier arithmetic remain enforced.

Register existing modifier definitions with `ModuleBuilder.Modifier`, or provide a
`SetModifierAction` containing a definition: the graph runtime registers that
definition during composition. Conflicting definitions are rejected.

Register an `IGraphEffectHandler` under a stable ID and use `GraphEffect.Custom(id)`
for game-specific consequences. Its `Stage` method receives read-only facts and a
`SimulationTransaction`; stage native actions or registered `DenseStateStore<T>`
writes there. Register and snapshot those custom stores separately. Throw an
argument/arithmetic/invalid-operation exception to reject an effect safely.

Do not issue commands, mutate a live feature-state copy, call an independently
committing `EntityTransaction`, or perform external I/O inside an effect handler.
Those operations cannot be rolled back with this transaction. Stage a durable intent
for later external processing if needed. Arbitrary declarative `EffectDefinition`
instances are not automatically converted into kernel graph transactions.

## Persistence, repetition, and UI

Save through `SnapshotService` normally, or `TimingSnapshotService` when timing is
attached. The graph module contributes a required versioned section containing
instances, run numbers, counters, clocks, lifecycle markers, errors, and pending
work. Restore with fresh module instances before accepting input. Do not start those
instances again after load. Existing saves without graph data require an explicit
migration before adding a graph module to their restore composition.

Graph fingerprints include authored structure, requirements, native effects,
policies, and content version. Increment `contentVersion` when changing the semantics
of a code-authored state expression or custom handler under the same stable ID.
Live delegates and CLR type names are not serialized. A changed graph fingerprint
requires an explicit content migration.

`RestartGraphCommand(instanceId, completedRun)` starts a new run of a completed
instance. A configured strictly positive `restartCooldown` automatically restarts
it when due and also gates manual restart. Old completion commands are rejected by
run number. New runs clear counters and lifecycle markers and retain `PreviousEnding`.
Prestige preserves graphs by default; `ResetPolicy.Reset` starts a fresh run through
the kernel's reset transaction.

`Runtime.Instance` and `Runtime.Instances` return detached snapshots.
`Runtime.View(instanceId, nodeId)` returns observable Values for `Status`, `Run`,
`CanComplete`, `UnmetRequirements`, `LastError`, and `Progress`. Bind these through
`UiBinding<T>.From`. For a compound trigger, `Progress` is the greatest normalized
signal/time leaf progress, a convenience indicator rather than a weighted overall
percentage; authoritative readiness is `CanComplete`.

Views use `Runtime.ViewWorld`, a separate projection entity context suitable for
`Ui.List`. Dispose mounted UI and bind the new runtime's views after loading. UI
formatters must never start, signal, or complete graphs. `Runtime.Changed` also
notifies consumers that need a custom projection.

## Unity asset authoring

Create **Idle Hands → Event and Objective Graph** from the asset menu. Fill in a
stable graph ID/version and at least one node. Mark end nodes explicitly. Node
prerequisite arrays contain node IDs. Use **Validate Graph** from the asset context
menu for structural diagnostics.

Activation/trigger trees contain a flat entry array and root index; `-1` means no
requirement. Composite entries reference child indices; Not has exactly one child.
Entries support native resource/flag checks, signals, time, node completion,
Boolean/integer owner properties, and registered custom requirements.

Objective/event completion defaults follow node kind unless `overrideCompletion`
is enabled. Effects use the existing native action fields or a registered extension
ID. Call `asset.Compile(extensions)` for validation and `asset.Definition()` when
constructing a `GraphModule`. Assets remain immutable content, never runtime state.
Unity authoring does not automatically attach a module or start campaign instances;
the game's bootstrap owns those explicit decisions.

For guided onboarding with temporary UI rules and highlights, see
[tutorials](Tutorials.md).
