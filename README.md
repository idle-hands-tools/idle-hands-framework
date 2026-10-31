# Idle Hands — Unity incremental-game framework

Idle Hands is a headless C# framework for incremental games, with Unity authoring,
reactive UI, and **nine playable samples**. Compose entities, economies, timed
behaviors, event/objective graphs, and tutorials, then extend them through typed
commands, requirements, effects, and persistence modules.

Runtime libraries use **C# 9 / .NET Standard 2.1** and have no Unity engine
references. The Unity project uses **6000.3.27f1**. Standalone tests and benchmarks
use **.NET SDK 8 or newer** and compile the same runtime sources.

## Start here

1. Open this directory with the editor version in
   [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) and let Unity resolve packages.
2. Open [CookieDemo.unity](Assets/IncrementalFramework/Samples/CookieDemo/CookieDemo.unity)
   and press Play, or choose another sample from the table below.
3. Follow the sample's tutorial. Highlighted controls guide the opening actions;
   **Skip tutorial** restores unrestricted tutorial presentation.
4. Read the [architecture and authoring guide](Docs/EntityFramework.md), then the
   [composition foundations](Docs/HowTo/Foundations.md) before combining systems.

Content assets define configuration. Commands/actions own intentional gameplay
changes; presentation observes state. Keep one logical simulation writer, and
resume an unfinished advance toward the **same target** when its work budget expires.

## Framework features

### Entities, definitions, and extension APIs

| Area | Implemented capabilities |
| --- | --- |
| Entities and Values | Reusable templates, independent instances, stable identities, typed property keys, observable scalar/list Values, calculated properties, dependency tracking, and lifecycle notifications |
| Modifiers | Source-tracked numeric and value modifiers; flat, additive-percent, multiply, and override operations; deterministic priority/ID tie-breaking; transactional attachment, replacement, and removal |
| Expressions and targeting | Typed arithmetic/Boolean expressions, property scopes, arguments, entity selectors, relationship queries, filtering, ordering, counting, and aggregation |
| Actions and behaviors | Eligibility checks and staged effects; sequence, conditional, iteration, delay, repeat, and transition behaviors; typed event bindings and saved continuations |
| Custom logic | Registered calculations, target selectors, effect handlers, command handlers, simulation systems, work sources, typed state stores, snapshot sections, and reset participants |
| Composition | Explicit `IGameModule` registration through `ModuleBuilder`, separate assemblies, read models, and fresh module instances for each restored game |
| Diagnostics | Structured framework diagnostics, command rejection reasons, coalesced before/after change notifications, and Unity presentation adapters |

See [entities and authoring](Docs/EntityFramework.md) and
[transaction/composition foundations](Docs/HowTo/Foundations.md).

### Economy, progression, and simulation

| Area | Implemented capabilities |
| --- | --- |
| Large-number economy | BigDouble resources, balances, caps, lifetime earnings, multi-resource costs, and explicit reject/clamp/allow policies for negative grants |
| Passive production | Analytical integration of independent producers, cached rates, selective dependency invalidation, geometric purchase prices, enable/disable controls, and one-time upgrades |
| Production modifiers | Producer/resource/category/global targets; owned-count-scaled base output; flat → summed additive percent → multiplicative → override; prestige scaling applied once |
| Rules and unlocks | Indexed resource, producer, upgrade, flag, and prestige requirements; AND/OR/NOT composition; automatic, manual, time, and gameplay-event triggers; repeat/cooldown policies |
| Transactions | Validate complete costs and preconditions before committing resource changes and registered state writes; native grants/spends, flags, unlocks, modifiers, and scheduled actions share a staging path |
| Prestige | Lifetime-based rewards and permanent production bonuses; reset/preserve/transform/recalculate policies; module participation and whole-reset vetoes |
| Event scheduling | Stable `(time, priority, insertion sequence)` ordering, versioned cancellation/rescheduling, serializable typed payloads, bounded cascades, and resumable work budgets |
| Replay | Injected clocks and seeded RNG with saved state; deterministic execution for the same ordered inputs, content, and tested runtime |

Integration stops at event boundaries before applying effects. An output-rate
change halfway through an advance therefore receives the correct production on
each side of that boundary. Resource caps limit credited accumulation; repeated
advancement to the same time does not award production twice.

### Event and objective graphs

[Graph APIs and examples](Docs/EventObjectiveGraphs.md) support milestones, quests,
scripted events, and objectives:

- One or more nodes, parallel paths, All/Any prerequisites, and explicit end nodes.
- Separate activation and trigger requirements over state, signals, time, flags,
  resource thresholds, and other nodes' completion, plus custom handlers.
- Latched or maintained activation; Dormant, Active, Ready, Suspended, Completed,
  and Skipped node states; automatic or explicit completion.
- Transactional activation/completion effects, revalidated completion commands,
  ending selection, independent owner/instance/run identities, and repeat cooldowns.
- Saved progress, counters, timers, errors, and pending work; prestige policies;
  observable UI projections; typed C# definitions and validated Unity graph assets.

Graph work shares the simulation scheduler and budget. End nodes close their run
and skip unfinished nodes. Required player confirmation does not itself pause the
rest of the simulation.

### Offline progress and time banking

The optional [timing controller](Docs/Timing.md) provides:

- **Automatic catch-up:** advance the same gameplay simulation through an absence.
- **Banked time:** accumulate a dedicated resource and spend it on speed boosts or skips.
- Configurable absence caps, earning rate, bank capacity, allowed multipliers,
  controller tick rate, and per-call work budget.
- Paid-time reservations and charging only for bonus time actually advanced;
  unused reservations are released when gameplay blocks.
- Persisted claims and pending work, explicit legacy-save adoption, and complete
  timeline adapters for economy, declarative, feature, and guild runtimes.
- Configurable combat, logistics, ecology, and market intervals in their definitions.

Timing policies are opt-in. Controller dispatch cadence and authored gameplay
intervals are separate settings; changing per-tick gameplay intervals can change
balance. Local save rollback and device clocks are not authoritative anti-cheat.

### Persistence and recovery

Snapshots preserve schema/content identity, stable IDs, simulation time, UTC save
time, RNG state, balances/lifetime earnings, entities, modifier records, events,
and registered module sections. Declarative continuations, graphs, and optional
timing state use their own persistence contracts.

JSON preserves BigDouble mantissa/exponent values without converting quantities to
`double`. Schema migrations, explicit content checks, temporary-file replacement,
and backup recovery support controlled upgrades and recovery. Offline summaries
report actual credited/net changes, and interrupted catch-up remains resumable.
Use fresh modules when restoring and register sections/reset policies for custom
state. The entity refactor changed the save schema and sample save keys; older
saves require the appropriate explicit migration.

### UI and tutorials

The [state-bound UI library](Docs/BoundUI.md) includes headless declarations and
abstract elements, with a Unity uGUI renderer:

- Text, buttons, toggles, sliders, progress bars, rows/columns, panels, scrolling,
  conditional content, and entity lists with stable retained identities.
- Observable/expression/custom bindings, text templates and numeric formatting,
  layout/style overrides, visibility, enablement, eligibility, tooltips, selection,
  input validation, and subscription disposal.
- Shared behavior across renderers; Unity handles canvases, layout, controls,
  event-system input, and rendering.

[Graph-based tutorials](Docs/Tutorials.md) add owner-scoped instruction panels,
semantic UI targets, temporary enable/disable and show/hide rules, configurable
highlights, parallel-step merging, and Unity tutorial assets. Normal gameplay and
parent eligibility still apply. Completion, suspension, or disposal releases the
presentation overrides. Unity's highlight frames preserve the original border and
do not intercept input. All nine sample games include onboarding, saved tutorial
progress, and a Skip button.

### Gameplay modules and authoring

| Module | Implemented capabilities |
| --- | --- |
| Shared combat | Combatants, living ally/enemy targeting, damage/healing, abilities, priorities, auras, and source-based effects |
| Clicker | Click definitions, calculated click amounts, intentional click actions, and presentation adapters |
| Guild RPG | Persistent classes/adventurers, parties, automatic wave/round combat, quests, regional progression, loot, equipment, crafting, training, and guild production/buildings |
| Strategy | Territory definitions/state, research, declarative conquest, and prestige over the shared economy |
| Batch production | Input-consuming recipes, machine selection and priority, timed batches, output reservations, storage constraints, deliveries, and replay blueprints |
| Logistics | Station inventories, tracks, train manifests, editable itineraries, cargo loading/unloading, timed transport, and delivery contracts |
| Ecology | Habitat quality, water/purity/shelter interventions, coupled species updates, population history, reintroduction/relocation, and stability objectives |
| Markets | Stock-sensitive prices, spread and trade impact, quote previews, travel/cargo, warehouse allocations, standing orders, and timed civic contracts |
| Narrative | Typed Boolean/integer/set variables, requirements/effects, passages, choices, branches, variants, journal/evidence, transcripts, stale-choice tokens, checkpoints, and ending galleries |
| Time loops | Scheduled investigations, appointment windows, repeating days, retained knowledge/shortcuts, and decision/loop-boundary pauses |
| Unity authoring | ScriptableObject content, validation/compilation, event/objective graph assets, tutorial assets, and a visual story graph editor with isolated playtesting |

The optional feature modules are concrete mechanics to adapt. Their sealed sample
implementations share `FeatureCommand` and cannot all be registered directly on
one builder. Combine their state under one authority or implement distinct commands
and a complete coordinator. See the [expanded sample contracts](Docs/ExpandedSamples.md).

## Guides and how-to tutorials

| Guide | What it covers |
| --- | --- |
| [Architecture and authoring](Docs/EntityFramework.md) | Templates, Values, calculations, actions, modifiers, behaviors, extension registration, assembly boundaries, and saves |
| [Composition foundations](Docs/HowTo/Foundations.md) | Choosing a runtime, transactional ownership, a saved timed workshop, and complete restore factories |
| [State-bound UI](Docs/BoundUI.md) | Declaring, binding, mounting, styling, and disposing reusable UI |
| [Event and objective graphs](Docs/EventObjectiveGraphs.md) | Graph lifecycle, requirements, effects, commands, persistence, UI views, and Unity assets |
| [Tutorials and UI control](Docs/Tutorials.md) | Tutorial steps, targets, enable/disable overlays, highlights, authoring, persistence, and sample onboarding |
| [Offline progress and timing](Docs/Timing.md) | Catch-up, time banks, boosts/skips, accounting, cadence, adapters, and migration |
| [Expanded samples and story authoring](Docs/ExpandedSamples.md) | Six campaigns, the narrative editor, module contracts, testing, and export |
| [Original samples and publishing](Assets/IncrementalFramework/Samples/README.md) | RPG/strategy gameplay, scenes, and build/publishing instructions |
| [itch.io projects and upload records](Docs/ItchProjects.md) | Existing project pages, channels, and verified tutorial-build uploads |

The [advanced feature how-to index](Docs/HowTo/README.md) contains twenty recipes.
They distinguish existing composition APIs from game-owned extensions to implement;
the recipes do not imply every proposed advanced mechanic is already shipped.

| # | How-to | # | How-to |
| --- | --- | --- | --- |
| 1 | [Programmable automation](Docs/HowTo/AutomationAndProduction.md#1-programmable-automation) | 2 | [Competing production methods](Docs/HowTo/AutomationAndProduction.md#2-competing-production-methods) |
| 3 | [Supply-network bottlenecks](Docs/HowTo/AutomationAndProduction.md#3-supply-network-bottlenecks) | 4 | [A changing world economy](Docs/HowTo/WorldSimulation.md#4-a-changing-world-economy) |
| 5 | [Branching prestige](Docs/HowTo/ProgressionAndCharacters.md#5-branching-prestige) | 6 | [Interconnected parallel worlds](Docs/HowTo/WorldSimulation.md#6-interconnected-parallel-worlds) |
| 7 | [Time banking](Docs/HowTo/TimeAndDiagnostics.md#7-time-banking) | 8 | [Persistent environmental consequences](Docs/HowTo/WorldSimulation.md#8-persistent-environmental-consequences) |
| 9 | [Faction politics](Docs/HowTo/NarrativeAndExpeditions.md#9-faction-politics) | 10 | [Procedural research trees](Docs/HowTo/WorldSimulation.md#10-procedural-research-trees) |
| 11 | [Worker specialization and succession](Docs/HowTo/ProgressionAndCharacters.md#11-worker-specialization-and-succession) | 12 | [Equipment with mechanical effects](Docs/HowTo/ProgressionAndCharacters.md#12-equipment-with-mechanical-effects) |
| 13 | [Expedition contingency plans](Docs/HowTo/NarrativeAndExpeditions.md#13-expeditions-with-contingency-plans) | 14 | [Evolving contracts and promises](Docs/HowTo/NarrativeAndExpeditions.md#14-evolving-contracts-and-promises) |
| 15 | [Infrastructure resilience](Docs/HowTo/AutomationAndProduction.md#15-infrastructure-failure-and-resilience) | 16 | [Discoverable system interactions](Docs/HowTo/NarrativeAndExpeditions.md#16-discoverable-system-interactions) |
| 17 | [Explainable economy dashboards](Docs/HowTo/TimeAndDiagnostics.md#17-an-explainable-economy-dashboard) | 18 | [Forecasting and hypothetical planning](Docs/HowTo/TimeAndDiagnostics.md#18-forecasting-and-hypothetical-planning) |
| 19 | [Offline policies with distinct outcomes](Docs/HowTo/TimeAndDiagnostics.md#19-offline-policies-with-distinct-outcomes) | 20 | [Generational world history](Docs/HowTo/ProgressionAndCharacters.md#20-a-generational-world-history) |

## Playable samples

| Game | Scene | Demonstrates | itch.io |
| --- | --- | --- | --- |
| Cookie Bakery | `Samples/CookieDemo/CookieDemo.unity` | Clicking, passive production, upgrades, rules, and prestige | [Play](https://thisisnozaku.itch.io/fake-cookie-clicker-demo) |
| Ember Guild | `Samples/IdleRpg/IdleRpg.unity` | Parties, expeditions, equipment, crafting, and guild production | [Play](https://thisisnozaku.itch.io/idle-rpg) |
| Frontier Council | `Samples/IdleStrategy/IdleStrategy.unity` | Production, research, conquest, and prestige | [Play](https://thisisnozaku.itch.io/idle-strategy) |
| Orbital Foundry | `Samples/Expanded/Scenes/OrbitalFoundry.unity` | Batch production and spacecraft assembly | [Play](https://thisisnozaku.itch.io/orbital-foundry) |
| Lastlight Railway | `Samples/Expanded/Scenes/LastlightRailway.unity` | Routes, local inventories, transport, and contracts | [Play](https://thisisnozaku.itch.io/lastlight-railway) |
| Marshlight Reserve | `Samples/Expanded/Scenes/MarshlightReserve.unity` | Habitat restoration and food-web simulation | [Play](https://thisisnozaku.itch.io/marshlight-reserve) |
| The Thirteenth Bell | `Samples/Expanded/Scenes/ThirteenthBell.unity` | Investigations, appointment windows, and time loops | [Play](https://thisisnozaku.itch.io/the-thirteenth-bell) |
| Tidebound Exchange | `Samples/Expanded/Scenes/TideboundExchange.unity` | Price impact, cargo, standing orders, and civic supply | [Play](https://thisisnozaku.itch.io/tidebound-exchange) |
| Letters from the Floodwall | `Samples/Expanded/Scenes/Floodwall.unity` | Branching correspondence, evidence, promises, and endings | [Play](https://thisisnozaku.itch.io/letters-from-the-floodwall) |

Scene paths are relative to `Assets/IncrementalFramework/`. Simulation samples have
capped offline progression; story decisions wait for the player. Tutorial progress
in the samples is a local graph-snapshot sidecar. See the
[tutorial guide](Docs/Tutorials.md#tutorials-in-all-nine-sample-games) for save behavior.
Uploaded page visibility remains controlled by the project's itch.io settings.

## Build, test, and publish

Run Unity EditMode and PlayMode tests through **Window → General → Test Runner**,
or use batch mode:

```sh
unity-editor -batchmode -nographics -projectPath "$PWD" -runTests \
  -testPlatform EditMode -testResults /tmp/idle-hands-editmode.xml \
  -logFile /tmp/idle-hands-editmode.log

unity-editor -batchmode -nographics -projectPath "$PWD" -runTests \
  -testPlatform PlayMode -testResults /tmp/idle-hands-playmode.xml \
  -logFile /tmp/idle-hands-playmode.log
```

The standalone projects mirror runtime assembly boundaries and require no Unity
installation. They exercise shared headless sources; Unity-specific editor and
rendering tests still require Unity:

```sh
dotnet test Tools/Tests/Framework.Tests.csproj -c Release
dotnet run --project Tools/Benchmark/Framework.Benchmark.csproj -c Release
```

Install Unity **Web Build Support**, then export WebGL samples:

```sh
unity-editor -batchmode -nographics -quit -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod CookieDemoBuild.WebGL -logFile /tmp/cookie-webgl.log

unity-editor -batchmode -nographics -quit -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod SampleGamesBuild.AllWebGL -logFile /tmp/rpg-strategy-webgl.log

unity-editor -batchmode -nographics -quit -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod ExpandedSamplesBuild.AllWebGL -logFile /tmp/expanded-webgl.log
```

| Export | Output |
| --- | --- |
| Cookie | `Builds/WebGL`; package its contents with `index.html` at the archive root |
| RPG / strategy | `Builds/IdleRpg-itch.zip`, `Builds/IdleStrategy-itch.zip` |
| Expanded samples | `Builds/<scene-id>-itch.zip` |

Exports use uncompressed runtime files so hosts need no custom compression headers.
Build commands do not upload games. `Tools/publish-samples.sh all --push` uploads
**only RPG and strategy**; its default mode previews. The six expanded targets are
recorded in [itch-projects.json](Tools/itch-projects.json). The cookie game's existing
channel is `thisisnozaku/fake-cookie-clicker-demo:webgl`; the other eight use `html5`.
See [upload records](Docs/ItchProjects.md) for all current pages and build IDs.

Latest verification snapshot (2026-10-30): **401 Unity EditMode tests** and
**33 PlayMode tests** passed. A final focused run also passed all **18 tutorial and
sample-tutorial tests**. Coverage includes graphs, timing, transactional failures,
save/load, UI behavior, and tutorial presence/highlights across all nine samples.
All nine WebGL exports passed archive/runtime/tutorial-metadata checks and their
`tutorials-v1` uploads were confirmed processed and ready by Butler.

The benchmark harness measures 10,000 owned producers across 100 resources over
30 simulated days. A historical reference run (2026-10-28, .NET 8.0.33, four
available Xeon Platinum 8259CL logical processors) measured a **0.584484 ms median**
and **96,544 allocated bytes** per analytical advance, with no scheduled events.
Setup was excluded. Re-run on target hardware and with your game's event workload.

## Assembly map

| Assembly / folder | Responsibility |
| --- | --- |
| `Core` | Entities, observable Values, modifiers, entity/state transactions, clocks, RNG, scheduler, and diagnostics |
| `Definitions` | Typed expressions, selectors, actions, behaviors, validation, and extension registration |
| `Economy` | BigDouble resources, producers, upgrades, rules, costs, and modifier definitions |
| `Progression` | Simulation, commands, native transactions, indexed rules, prestige, work sources, graphs, and timing |
| `Persistence` | JSON snapshots, entity/modifier/behavior records, graph module facade, timing sections, migrations, and offline resume |
| `Combat` | Shared combatants, targeting, abilities, healing/damage, and auras |
| `UI` | Headless declarations, templates, bindings, interaction behavior, presentation policies, and abstract elements |
| `Tutorials` | Owner-scoped tutorial definitions, active-step projections, and temporary UI policy composition |
| `Features` | Optional production, logistics, ecology, market, narrative, and time-loop runtimes |
| `Games/Clicker`, `Games/Rpg`, `Games/Strategy` | Genre-specific gameplay and state |
| `UnityIntegration` | Authoring assets/compiler, uGUI rendering, presentation adapters, bootstraps, and sample tutorial sessions |
| `Samples` | Nine games' content and scenes |
| `Editor` | Story graph editing, authoring validation, and reproducible exports |
| `Tests/EditMode`, `Tests/PlayMode`, `Tools` | Correctness, editor/UI/scene coverage, headless projects, benchmarks, and publishing tools |

Shared runtime libraries do not depend on genres, samples, or Unity presentation.
`GraphModule` lives in Persistence under `IncrementalFramework.Progression.Graphs`;
`GraphRuntime` itself remains independent of JSON and UI. Tutorial presentation
uses the graph runtime and headless UI without introducing a reverse dependency.

## Numeric policy, compatibility, and scope

Currencies, prices, rewards, and production use the MIT-licensed
[Razenpok/BreakInfinity.cs](https://github.com/Razenpok/BreakInfinity.cs), vendored at
commit `ef06986e88f55593622ad38e7f0c11a458df80cd`. Its license is retained. The vendored
source only removes Unity imports/serialization attributes; arithmetic is unchanged.
Its `BigDouble.cs` SHA-256 is
`b7271b69f3e62bca49323ad21e22f0b5a69dead68f8cdc0c365ff47e39ef0a43`.
Guarded economy operations remain separate from the vendored implementation.

BigDouble is floating-point mantissa/exponent arithmetic: sufficiently small changes
to very large balances can disappear at the supported precision. Time uses finite
`double`. JSON uses Newtonsoft.Json (13.0.3 in standalone projects; Unity package
3.2.2), with explicit data types and no arbitrary CLR type-name deserialization.

The economy kernel models independent passive output. Input-consuming batches,
transport, ecology, and markets belong to their optional modules. General continuous
flow solving, autonomous planning, procedural research generation, multi-world
coordination, multiplayer services, cloud saves, mod loading, arbitrary visual
scripting, Jobs/Burst, and DOTS remain extensions or future work. Cross-platform
bitwise floating-point determinism and universal allocation-free execution are
not promised. The how-to recipes explain how to build on these boundaries.
