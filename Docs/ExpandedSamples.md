# Expanded sample games

Six additional samples live in `Assets/IncrementalFramework/Samples/Expanded`.
The original cookie, guild RPG, and strategy samples retain their existing scenes,
content, build commands, and save keys.

Use **Idle Hands → Expanded samples → Create missing scenes and story** once if
scenes are missing. Open a scene under `Expanded/Scenes` and press Play.
The generator creates missing assets only and preserves already authored stories.

| Scene | Main decision | Reusable module |
| --- | --- | --- |
| OrbitalFoundry | Allocate shared materials among timed recipes | ProductionModule |
| LastlightRailway | Move station-owned cargo through a train network | LogisticsModule |
| MarshlightReserve | Restore a bounded, coupled food web | EcologyModule |
| ThirteenthBell | Investigate appointments across repeating twelve-minute days | TimeLoopModule |
| TideboundExchange | Trade against supply-sensitive prices and delivery windows | MarketModule |
| Floodwall | Reply to competing accounts and shape an evacuation | NarrativeModule |

These are compact campaigns with completion and replay. The market's last civic
window opens at forty simulated minutes. Other campaign times depend on allocation,
reading speed, and route choices; human playtime has not been benchmarked.

## Playing

**Orbital Foundry:** Mining and smelting begin automatically. Plates build more
machines as well as ship sections. Build a drawbench, electronics bench, and
assembler, then expand smelting and mining to support them. Pause machines once
they have enough output for an objective: excess frames can consume the plates
needed for the hull. Priorities allocate shared inputs; they do not interrupt an
active batch. Hull delivery unlocks cells and fast smelting. Deliver hull, avionics,
and drive to launch. Replays alternate component quantities and add 10% speed.

**Lastlight Railway:** Valley produces food, Crossing and Ridge produce timber,
and Quarry produces tools. Build tracks, append cyclic route stops, configure
per-stop loading limits, and apply the itinerary. Disable unloading at intermediate
stations when cargo should continue farther. An itinerary must include the train's
current or next-arrival station and every adjacent link, including its return leg.
Park after the next arrival to edit a stationary train. Contracts consume inventory
at the destination, never cargo still in transit. Replays change contract ordering
and increase quantities by 20% on alternate campaigns.

**Marshlight Reserve:** Free baseline restoration improves damaged habitats without
spending funds. Establish reeds and voles before introducing herons and otters.
Population deltas explain current trends; recent values show whether interventions
are settling. Keep every species in its displayed health band, average habitat
quality at least 0.55, and water between 0.40 and 0.85 for five minutes. Reintroduce
locally extinct species or relocate an excess population. Field notes survive replay.

**The Thirteenth Bell:** Appointments consume time, and investigation results pause
for reading. Morning meetings overlap, so both cannot be completed in one loop.
Wait for the bell to restart. Facts and discovered half-duration shortcuts survive;
local favors reset. A complete solution requires the original sequence, the bell
chart, and a safe release test. Three endings are available.

**Tidebound Exchange:** Buy and sell buttons preview the total including sequential
price impact and a 3% spread. Choose trade/order quantity and a limit for standing
orders. Orders run at thirty-second market boundaries in creation order. Warehouse
allocations limit holdings per commodity; unallocated goods share remaining space.
Civic delivery windows open during a forty-minute season and unlock additional
markets. Contracts provide funds for later deliveries. Replay changes the demand
seed and retains market access and warehouse upgrades.

**Letters from the Floodwall:** Read correspondence, follow known evidence links,
and send responses. Chapter two has distinct council and neighborhood routes;
chapter three resolves preparations, public trust, and earlier promises into four
endings. The journal searches correspondence, evidence, and known character
profiles. It preserves the actual text shown, including earlier replies. Chapter
checkpoints replay decisions while retaining the ending gallery. A fresh run resets
story variables and checkpoints but keeps discovered endings.

## Narrative authoring

Open the included `Expanded/Content/Floodwall.asset` through **Idle Hands → Narrative
→ Story graph**, or double-click it. **New** creates an independent story.

- Add Passage, Choice, Branch, and Ending nodes with the toolbar. Drag node titles
  to move them, middle/right drag to pan, and use the mouse wheel to zoom.
- Connect an output by clicking its Next, Choice, or Branch button and then
  **Connect here** on the destination. Inspector target pickers also edit or remove
  connections. Ordered branch nodes require a fallback.
- The Node inspector edits speakers, chapters, checkpoints, text, variants,
  responses, eligibility, effects, and journal unlocks. Variants and branch arms
  use the first matching condition.
- The Data inspector owns typed Boolean/integer/set variables, initial values,
  character profiles, journal entries, and the start node. Set members use stable
  IDs. The Text inspector edits keyed strings independently from story structure.
- Conditions offer Boolean equality, integer equality/thresholds, set membership,
  All, Any, and Not. Variable pickers filter by type. Effects set/add values or
  include/remove set members. Optional disclosure keys control player receipts;
  empty disclosure keys keep effects hidden.
- Unavailable responses can be hidden or locked with an explicit reason. Hints
  expose authored known consequences, not hidden variables.
- Use `{variable-id}` in text for a resolved variable value. Use
  `[[evidence-id|readable label]]` for an evidence reference. Known references expose
  journal buttons; undiscovered entries remain inaccessible.
- Copy/paste and Duplicate create fresh node/choice IDs and independent primary
  passage/response text keys. Additional shared text references remain shared.
  Undo/Redo covers data, connections, and graph positions.
- Save/validate reports missing targets/text, duplicate IDs, incompatible variable
  types, unreachable nodes, missing fallbacks, unknown evidence, and automatic
  branch cycles. Click a diagnostic to locate its node. Errors block compilation,
  export, and playtesting.
- Playtest/restart runs a separate headless instance, highlights the current node,
  shows every response's condition and eligibility (including hidden responses),
  supports temporary variable changes, and offers chapter checkpoints. It never
  changes authored defaults or player saves.

Graph positions live on the Unity story asset; compiled story JSON contains only
story definitions. **Export** writes validated JSON. Assign a Story asset to an
`ExpandedBootstrap` to play custom content. The authoring window is an Editor tool
and is excluded from player builds.

## Runtime and persistence contracts

`IncrementalFramework.Features` has no Unity references. `Samples.Expanded` contains
named content only, and `Unity.Expanded` owns the scene shell, UI, and story assets.
The standalone projects mirror those boundaries under `Tools/Features` and
`Tools/Samples/Expanded`.

`FeatureModule<T>` registers an explicit command handler, typed state store, and
versioned snapshot section through `ModuleBuilder`. Each command works on a private
state copy and stages the resulting state through a framework transaction. Rejected
commands do not mutate state. Public state reads are detached copies.

`FeatureGame.AdvanceTo` integrates the framework simulation to each module event
boundary, processes the event transactionally, and stops at its work budget. Keep
and retry the same target when it returns false. Module scheduler payloads and
insertion sequence are persisted through `EventScheduler<FeatureEvent>` rather than
adding sample-specific event kinds to the economy kernel. This initial adapter
hosts one feature module per game; composing multiple feature schedulers in one
runtime is not provided.

Production reserves batch output storage before consuming inputs. Train manifests
own in-transit resources. Ecology reads a shared pre-step state and commits all
changes together. Market demand uses a saved `XorShift64` stream. Narrative
conditions compile to shared typed expressions; transitions apply their effects
inside the enclosing module transaction. Choice/continue commands require the
current view token, so stale or repeated input cannot apply effects twice.

Every new game has a separate `idle-hands.expanded.<id>.v1` save. Native saves use a
temporary file plus backup replacement; WebGL uses PlayerPrefs. Saving occurs every
thirty seconds and at lifecycle boundaries. Snapshots include a feature schema
version and SHA-256 content fingerprint. Changed definitions require an explicit
migration; silently interpreting old saves against changed stories is unsupported.

Offline time is clamped to eight hours and processed with resumable budgets. The
return summary reports actual inventory/population changes. Floodwall advances
only on player input. The time-loop sample stops at investigation results and the
bell boundary; offline absence never chooses responses or begins another loop.

## Verification and exports

Run headless campaign, persistence, and editor tests with Unity EditMode; run actual
dashboard controls with PlayMode. The new tests live in `ExpandedCampaignTests`,
`StoryGraphEditorTests`, and `ExpandedSampleUiTests`. The ordinary standalone
`dotnet test Tools/Tests/Framework.Tests.csproj -c Release` also includes the
headless campaign tests; Editor-only tests are conditionally excluded.

**Idle Hands → Expanded samples → Validate content and saves** checks compiled
content and save round-trips. Export all six using **Idle Hands → Build itch.io →
Expanded samples → All six**, or:

```sh
unity-editor -batchmode -nographics -quit -projectPath "$PWD" \
  -buildTarget WebGL -executeMethod ExpandedSamplesBuild.AllWebGL \
  -logFile /tmp/expanded-webgl.log
```

Individual methods are `FoundryWebGL`, `RailwayWebGL`, `ReserveWebGL`, `BellWebGL`,
`ExchangeWebGL`, and `FloodwallWebGL` on `ExpandedSamplesBuild`.
Outputs are `Builds/<scene-id>/WebGL` and `Builds/<scene-id>-itch.zip`, with
`index.html` at the archive root and responsive 1280×900 presentation. Builds restore
player settings afterward. No build command uploads or publishes a game.

## Verified build

On 2026-10-28, Unity 6000.3.27f1 passed all **319 EditMode** and **30 PlayMode**
tests. This includes earned-resource campaign completion, all four Floodwall
endings, the resolved time-loop ending, resumable save/offline equivalence, actual
dashboard controls, graph duplication/Undo, and isolated editor playtests. The
Floodwall reading interface was rendered and visually inspected. Verification used
Unity's test runners; the standalone .NET SDK was not installed in this environment.

All six final WebGL exports completed successfully. Each ZIP contains seventeen
files (approximately 12.55 MiB compressed); CRC checks, responsive viewport,
per-game title, and loader/framework/data/WebAssembly assets passed validation.
Player build settings were restored. Hosted browser testing remains a publishing
check.

## Guided introductions

All six expanded games include [tutorials](Tutorials.md) with saved progress,
a highlighted opening sequence, and a Skip tutorial action.
