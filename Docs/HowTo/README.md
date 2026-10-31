# Advanced feature implementation guides

These guides turn the twenty advanced-feature ideas into implementation recipes for
Idle Hands. Start with [composition foundations](Foundations.md). Each recipe names
the existing building blocks, the game-owned extension you must write, the order of
operations, save/reset behavior, and checks that establish correctness.

| # | Feature | Guide | Implementation route |
| --- | --- | --- | --- |
| 1 | Programmable automation | [Automation](AutomationAndProduction.md#1-programmable-automation) | Compose declarations; extend with an editable rule compiler |
| 2 | Competing production methods | [Production](AutomationAndProduction.md#2-competing-production-methods) | Configure recipes; extend allocation policy |
| 3 | Supply-network bottlenecks | [Logistics](AutomationAndProduction.md#3-supply-network-bottlenecks) | Extend local inventories and transport events |
| 4 | A changing world economy | [World simulation](WorldSimulation.md#4-a-changing-world-economy) | Extend market events with competing agents |
| 5 | Branching prestige | [Progression](ProgressionAndCharacters.md#5-branching-prestige) | Compose reset policies; add branch selection and module reset participation |
| 6 | Interconnected parallel worlds | [Parallel worlds](WorldSimulation.md#6-interconnected-parallel-worlds) | Build an explicit composite runtime and transfer ledger |
| 7 | Time banking | [Time](TimeAndDiagnostics.md#7-time-banking) | Compose existing timing APIs |
| 8 | Environmental consequences | [Ecology](WorldSimulation.md#8-persistent-environmental-consequences) | Extend ecological state and intervention commands |
| 9 | Faction politics | [Narrative](NarrativeAndExpeditions.md#9-faction-politics) | Compose story variables; extend economic consequences |
| 10 | Procedural research trees | [Research](WorldSimulation.md#10-procedural-research-trees) | Add a deterministic content generator |
| 11 | Worker specialization and succession | [Workers](ProgressionAndCharacters.md#11-worker-specialization-and-succession) | Compose entities, calculations, and timed behaviors |
| 12 | Equipment with mechanical effects | [Equipment](ProgressionAndCharacters.md#12-equipment-with-mechanical-effects) | Compose modifiers/abilities; extend specialized effects |
| 13 | Expedition contingency plans | [Expeditions](NarrativeAndExpeditions.md#13-expeditions-with-contingency-plans) | Extend guild quest commands and round-boundary policies |
| 14 | Evolving contracts and promises | [Contracts](NarrativeAndExpeditions.md#14-evolving-contracts-and-promises) | Extend saved contract state and narrative conditions |
| 15 | Infrastructure failure and resilience | [Resilience](AutomationAndProduction.md#15-infrastructure-failure-and-resilience) | Add failure, maintenance, and recovery events |
| 16 | Discoverable interactions | [Discoveries](NarrativeAndExpeditions.md#16-discoverable-system-interactions) | Compose predicates and persistent discovery flags |
| 17 | Explainable economy dashboards | [Diagnostics](TimeAndDiagnostics.md#17-an-explainable-economy-dashboard) | Build a read-only projection and bind the shared UI |
| 18 | Forecasting and hypothetical planning | [Forecasting](TimeAndDiagnostics.md#18-forecasting-and-hypothetical-planning) | Restore isolated branches with fresh modules |
| 19 | Distinct offline policies | [Offline behavior](TimeAndDiagnostics.md#19-offline-policies-with-distinct-outcomes) | Compose timing policies; add game-owned operation profiles |
| 20 | Generational world history | [History](ProgressionAndCharacters.md#20-a-generational-world-history) | Add a persistent history ledger and reset transforms |

**Compose** means use public APIs that already exist. **Extend** means implement
new behavior in your game's assembly using those APIs. A recipe describing a
planner, generator, coordinator, ledger, or evaluator is specifying code to write;
those are not pre-existing framework services.

The full C# examples are self-contained compilation units; game-specific names and
values are illustrative. Their `// compile: ...` markers identify the examples
checked against the current runtime assemblies. Compilation checks API usage, not
completion of every advanced feature. Follow each recipe's acceptance checks when
implementing it in your game.

Keep [architecture](../EntityFramework.md), [timing](../Timing.md),
[UI bindings](../BoundUI.md), and [sample authoring](../ExpandedSamples.md) nearby.
These guides add documentation and examples; they do not change the shipped games.

## Source starting points

Follow these implementations when adapting a recipe:

- [Module composition and transaction registration](../../Assets/IncrementalFramework/Progression/Composition.cs)
- [Feature state, commands, scheduling, and persistence](../../Assets/IncrementalFramework/Features/FeatureRuntime.cs)
- [Production batches and inventory reservations](../../Assets/IncrementalFramework/Features/Production.cs)
- [Narrative data and validation](../../Assets/IncrementalFramework/Features/NarrativeData.cs)
- [Narrative transitions and choice validation](../../Assets/IncrementalFramework/Features/NarrativeRuntime.cs)
- [Timing configuration](../../Assets/IncrementalFramework/Progression/Timing/TimingOptions.cs)
- [Timing persistence and restore](../../Assets/IncrementalFramework/Persistence/TimingSnapshotService.cs)

For an implemented shared quest/milestone runtime, see
[event and objective graphs](../EventObjectiveGraphs.md).
