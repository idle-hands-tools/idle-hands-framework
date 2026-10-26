# Idle Hands — Unity incremental framework

A headless C# incremental-game kernel and a small playable cookie game. Independent
passive production is implemented; this is not an input-consuming production-chain
or general-purpose scripting engine.

## Open the demo

1. Open this directory as a project in **Unity 2022.3 LTS**.
2. Let Unity resolve `Packages/manifest.json` and import the assets.
3. Open `Assets/IncrementalFramework/Samples/CookieDemo/CookieDemo.unity` and press
   Play. Click for cookies, buy a producer, purchase an upgrade, unlock additional
   content, and prestige. The interface also exposes save/load and offline gains.
4. Run the EditMode and PlayMode suites in **Window → General → Test Runner**.

The scene uses ScriptableObject content assets, not ScriptableObjects as mutable
game state. Unity owns input and presentation; commands own intentional changes.
Save files are local, human-readable JSON. Delete the local save to start fresh.

## Headless verification

The .NET projects link the **same source** as Unity, in separate projects matching
the runtime assembly boundaries. Install .NET SDK 8 or newer:

```sh
dotnet test Tools/Tests/Framework.Tests.csproj -c Release
dotnet run --project Tools/Benchmark/Framework.Benchmark.csproj -c Release
```

No Unity installation is needed for these commands. They do not replace an Editor
import, scene serialization validation, or PlayMode execution. Build outputs,
package caches, and test results are ignored.

## Dependency and numeric policy

`Assets/IncrementalFramework/ThirdParty/BreakInfinity` vendors the MIT-licensed
[Razenpok/BreakInfinity.cs](https://github.com/Razenpok/BreakInfinity.cs) at commit
**`ef06986e88f55593622ad38e7f0c11a458df80cd`**. Its license is retained.
The only downstream source change removes the conditional `UnityEngine` import
and Unity serialization attributes; arithmetic is unchanged. The resulting
`BigDouble.cs` SHA-256 is
`b7271b69f3e62bca49323ad21e22f0b5a69dead68f8cdc0c365ff47e39ef0a43`.
There is no runtime download or custom big-number replacement.

Currencies, costs, production, modifiers, and prestige quantities use
`BreakInfinity.BigDouble`. This is normalized floating-point mantissa/exponent
arithmetic, **not arbitrary precision**: sufficiently small additions to large
balances disappear at the port's supported precision. Simulation time and elapsed
seconds use finite `double`. Saves preserve the mantissa and integer exponent,
never a `BigDouble → double` conversion. JSON uses the pinned Newtonsoft.Json
package (13.0.3 for the standalone runner; Unity package 3.2.1).

## Architecture guide

| Assembly / folder | Responsibility |
| --- | --- |
| `Core` | Clocks, seeded RNG, command/module contracts, stable scheduler, change journal |
| `Economy` | Immutable resource/producer definitions, BigDouble costs, modifiers |
| `Progression` | Simulation orchestration, transactions, upgrades, unlock rules, prestige |
| `Persistence` | Versioned snapshot DTOs, JSON, safe file storage, offline catch-up |
| `UnityIntegration` | ScriptableObject authoring/compiler, bootstrap, UI |
| `Tests/EditMode`, `Tests/PlayMode` | Headless correctness and scene smoke tests |

Dependencies flow from Persistence through Progression, Economy, and Core.
The headless assemblies and BreakInfinity have `noEngineReferences: true`.
UnityIntegration references the headless assemblies, never the reverse. Standalone
projects deliberately compile these as separate assemblies to catch accidental
boundary violations.

### Time, ordering, and transactions

Use one logical writer. Commands and event processing are synchronous; do not
call the simulation concurrently or mutate it from notification callbacks.
Stable content IDs are compiled into dense integer indexes. Reads of runtime
resource state are indexed, and no producer MonoBehaviour computes economy.
UTC resume time comes from `IClock`. RNG can be injected through `IRandomSource`;
this save schema accepts the documented `xorshift64-13-7-17` algorithm, whose
64-bit state is persisted. Give each simulation its own source and do not advance
that source externally. The seed constructor uses the same replay algorithm.

`AdvanceTo(targetSimulationTime)` rejects backwards time and integrates constant
rates analytically. The binary min-heap orders events by
**(time, priority, insertionSequence)**. Integration stops at each pending event
timestamp before applying effects: an event at 100 during advancement from 0 to
200 receives 100 seconds of old production and 100 of new production. Caps clamp
accumulation, not lifetime production beyond the cap. Advancing twice to the same
time never grants production twice.

Cancellation uses version tokens; canceled work is lazily discarded. Scheduled
payloads are typed serializable data, not delegates. A bounded event budget can
leave advancement incomplete: retain the target and resume instead of discarding
events or jumping past them. Rules also have a cascade limit so malformed
self-triggering content cannot lock the runtime.

Commands validate costs and preconditions before committing all effects. Costs
sharing a resource are aggregated, preventing a multi-resource purchase from
partially spending funds. Coalesced notifications are published after commit;
presentation must issue another command rather than writing state.

### Production and progression

Producer prices are `baseCost × growthFactor^ownedCount`, calculated in BigDouble.
Independent enabled producers contribute cached rates. Resource and category
modifier dependency indexes dirty only affected producer rates. Modifier order is
**flat → summed additive percent → multiplicative → override**; additive
percent values are fractions (`0.5` means +50%).
The pipeline evaluates each producer/output's owned-count-scaled base rate.
Override selection uses highest priority, then ordinal stable modifier ID.
The permanent prestige multiplier is applied once after this pipeline.
Flat and additive-percent debuffs may be negative; effective passive output is
clamped nonnegative. Negative-rate consumption/decay processes are not implemented.
One-time upgrades have fixed authored multi-resource prices, not upgrade levels.

Rules subscribe to indexed dependencies, not a global per-frame scan. Conditions
include resource/owned-count/upgrade/flag predicates and AND/OR/NOT; effects are
explicit grants/spends, flags, unlocks, modifiers, and scheduled typed events.
There is intentionally no arbitrary asset-supplied C# execution.

Prestige is an atomic reset with configurable reset/preserve/transform/recalculate
policies. Reward derives from lifetime earnings; the permanent multiplier is
recomputed rather than repeatedly stacking the same bonus after loading.

### Persistence and offline progress

Snapshots include schema/content identity, simulation and UTC wall time, RNG,
stable-ID state, lifetime earnings, flags, modifiers, and queued events. Unsupported
critical versions and mismatched content fail explicitly; migration contracts
provide an intentional upgrade path. Never enable Newtonsoft `TypeNameHandling`
on untrusted save data.

File saves write a sibling temporary file before replacing the destination and
retaining a backup where supported. Backups are recovery data, not cloud storage.
On resume, elapsed injected UTC time is clamped to zero and to a configured maximum,
then passed through the same analytical event-boundary advancement. Offline
summaries report actual net gains (after caps/effects); budget exhaustion remains
resumable. Device time is **not authoritative** for competitive games or anti-cheat.

### Extension points and limits

Composition is explicit, with module, command-handler, simulation-system, typed
state, and snapshot-section contracts. Future modules can register data and
behavior without a global mutable singleton. Resource-input processes and flow
solvers are extension seams only: coupled production is not implemented.

`IGameModule.Register(ModuleBuilder)` registers definitions, commands, systems,
typed stores, and snapshot sections. `ModuleBuilder.Create()` compiles definitions
and attaches registrations in order. A builder belongs to **one** simulation;
restore with fresh module instances, not stores shared with another running game.
Custom command handlers receive a read model and a `SimulationTransaction`;
stage resource costs/grants and `DenseStateStore<T>` writes there. The simulation
validates the entire transaction before committing. Register a versioned
`ISnapshotSection` for module-owned persistent values. Systems schedule the
built-in serializable command payloads before advancement; they are not
per-producer Unity updates. Custom immediate handlers are registered explicitly;
arbitrary command objects and delegates are never serialized.
`IPrestigeResetParticipant` stages module reset/transform/recalculated values
before prestige commits; Preserve participants are left untouched. Validation
can veto the entire reset. Optional Unity adapters compose through
`PresentationAdapters`, keeping presentation dependencies out of headless modules.
For native balances/counts, Recalculate reinitializes from compiled content and
rebuilds derived rates. Modules with their own derived state implement its formula
in their reset participant; no arbitrary formulas or asset-supplied C# are loaded.

Replay determinism means the same ordered inputs, seed, content, and tested
runtime. Cross-platform bitwise floating-point determinism is not promised.
Hot-path arrays, cached rates, indexed dependencies, and heap scheduling avoid
per-second offline work. Snapshot/command boundaries may allocate; this is not a
claim of universal zero-allocation execution.

Explicitly deferred: combat, loot/equipment, territories/population/logistics,
diplomacy, narrative branching UI, autonomous planning, multiplayer/network
services, mod loaders, visual scripting/graph editors, arbitrary cycle solvers,
batch-buy optimization, Jobs/Burst, and DOTS.

## Implementation report

Implemented: analytical/event-boundary production, validated purchases and
multi-resource transactions, modifiers, indexed unlocks/rules, prestige, exact
versioned saves, capped/resumable offline catch-up, module composition, authoring
assets, a saved demo scene, and automated test/benchmark projects.

The standalone suite verifies large values, exact save round-trips, atomic failure,
geometric prices, modifiers/invalidation, stable ordering/cancellation, caps,
the explicit t=100 rate-change case, cascades, prestige/reset participation,
seeded replay, migrations/backups, and analytical 24-hour catch-up.
Runtime assemblies have built independently with C# 9/.NET Standard 2.1.
The completed headless contract suite passed **200 tests** with no failures.
**Unity Editor import and PlayMode tests have not been run:** no Editor is
installed in the implementation sandbox. Use the Unity Test Runner before shipping.

Measured reference run (2026-10-26): Ubuntu 24.04.5, .NET **8.0.31**, Intel Xeon
Platinum 8573C, four available logical processors. For **10,000 owned producers,
100 resources, 30 days**, three warmups and seven measured repetitions:
advance min/median/mean/max **0.065376 / 0.088335 / 0.089017 / 0.126184 ms**,
**64,320 allocated bytes** during advancement and **0 scheduled events**.
All final balances and owned counts were checked. Compilation, fixture creation,
and purchases are excluded from advancement timing and reported separately by the
harness. These are measured cached-independent-production results on a virtual
runner, not invented desktop numbers or a prediction for event-heavy games.
The current immutable change journal allocates per changed advancement interval;
the benchmark includes that cost. It is not a zero-allocation runtime.
The initial under-100-ms catch-up goal is a profiling target, not a cross-machine
guarantee. Re-run the harness on your target hardware and Unity runtime.