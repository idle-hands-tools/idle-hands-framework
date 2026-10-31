# Tutorials and temporary UI control

Tutorials reuse [event/objective graph](EventObjectiveGraphs.md) nodes for saved
progress. The headless `IncrementalFramework.Tutorials` assembly projects active
steps into UI instructions and temporary presentation rules. Unity renders a
non-interactive highlight frame; other renderers can use the same highlight state.

## Author a tutorial

Create a graph normally, then map its node IDs to tutorial presentation. This
example starts by guiding a resource action and then a producer purchase:

```csharp
using IncrementalFramework.Core;
using IncrementalFramework.Progression;
using IncrementalFramework.Progression.Graphs;
using IncrementalFramework.Tutorials;
using IncrementalFramework.UI;

var graph = new GraphDefinition("intro", new[] {
    new GraphNodeDefinition("earn", GraphNodeKind.Event,
        trigger: GraphRequirements.Signal("earned")),
    new GraphNodeDefinition("buy", GraphNodeKind.Event, isEnd: true,
        prerequisites: new[] { "earn" },
        trigger: GraphRequirements.Signal("producer-bought"))
});
var tutorial = new TutorialDefinition("intro", new[] {
    new TutorialStep("earn", new[] {
        new TutorialUiRule("earn-button", enabled: true, highlighted: true)
    }, instructions: "Earn enough gold to purchase a producer."),
    new TutorialStep("buy", new[] {
        new TutorialUiRule("buy-button", enabled: true, highlighted: true,
            highlightColor: new UiColor(1, .78f, .16f), highlightWidth: 4)
    }, instructions: "Buy the highlighted producer.")
}, baseline: new[] {
    new TutorialUiRule("buy-button", enabled: false)
});
```

Register `graph` through `GraphModule` in your game's composition. Create the owner
entity, then issue `StartGraphCommand(instanceId, "intro", ownerId)` and drain work
at the current target. Construct a controller and pass it to UI contexts:

```csharp
using var guide = new TutorialController(graphModule.Runtime, ownerId,
    new[] { tutorial });
var context = new UiContext(simulation.Entities, presentation: guide);
var earn = Ui.Button("Earn", earnAction);
earn.TargetId = "earn-button";
var buy = Ui.Button("Buy", purchaseAction);
buy.TargetId = "buy-button";
```

`TargetId` is a stable semantic UI identifier. It defaults to the declaration's
`Id` when omitted. Several controls can share a semantic target, such as every
"Assign to party" button. Give them distinct declaration IDs within their identity
scope. For one particular list row, include its stable entity ID in `TargetId` and
the rule's target. Item contexts inherit the presentation policy.

Publish a graph signal only after the corresponding gameplay operation succeeds:
`PublishGraphSignalCommand(ownerId, "earned")` or `"producer-bought"`. The tutorial
layer never generates gameplay input or claims objectives merely because a view
was displayed. For an operation that must be atomic with progress, stage both in
one authoritative command rather than assuming two commands form one transaction.

## Presentation behavior

- Baseline rules apply while an instance of the tutorial graph is Active. Active
  and Ready mapped nodes contribute their step rules; Dormant, Suspended,
  Completed, and Skipped nodes do not.
- Step settings override baseline settings for that tutorial instance. Unspecified
  settings leave the baseline or authored UI setting alone.
- Parallel steps and independent tutorial instances merge conservatively:
  disable/hide wins over enable/show; any requested highlight wins over an explicit
  no-highlight. Highlight color/width use the highest step priority, then stable
  instance ID and node ID. Baseline appearance is the fallback.
- `enabled: true` permits normal interaction; it never bypasses a declaration's
  `Enabled`, an ancestor's disabled state, or `CanExecute`/gameplay validation.
  Likewise, tutorial visibility cannot reveal something authored as hidden.
- Suspending a graph, completing an ending, or disposing a controller releases its
  overrides. Original styles and bindings remain intact.
- Missing targets are harmless. A target mounted later immediately receives the
  current rule, which supports tabs, scene changes, and retained lists.

`TutorialController.ActiveSteps` exposes instance/run/node identity, title,
instructions, and priority for your instruction panel. Titles/descriptions default
to the corresponding graph node's metadata. Use `Changed` to refresh an instruction
projection; observer failures are available through `LastObserverException`.

The generic UI contract is `IUiPresentationPolicy`. `UiDefinition` also supports
ordinary `Highlighted`, `HighlightColor`, and `HighlightWidth` bindings independently
of tutorials. Mounted `UiElement` instances expose the resolved values to renderers.
Unity draws four non-raycast frame edges around the target, keeping the existing
border and layout. Highlights do not obstruct buttons or become new focus targets.

## Save, load, and ownership

Persist the graph through its normal snapshot section. Do not serialize a
`TutorialController`, mounted UI, or highlight GameObjects. After restoration,
construct a new controller over the restored runtime and remount UI with its
presentation context. The current step and presentation are derived from saved
node status, including explicit objectives waiting for confirmation.

Controllers are scoped to an owner entity ID, so another player's or faction's
graph does not control this UI. Dispose mounted UI when replacing it, and dispose
the controller when leaving its session. UI disposal removes policy subscriptions;
controller disposal restores still-mounted controls before detaching observers.

## Unity authoring

Create **Idle Hands → Tutorial** and assign a `GraphAsset`. Add steps referencing
that asset's node IDs, optional instruction text, and target rules. Each enabled,
visible, and highlighted setting supports **Unchanged**, **On**, and **Off**.
Color and width have explicit override toggles.

Use **Validate Tutorial** from the asset context menu. It checks the graph, step
references, duplicate targets, and appearance values. Build the gameplay module from
`asset.graph.Definition()` and presentation from `asset.Definition()`. A tutorial
asset does not automatically start a campaign graph.

## Tutorials in all nine sample games

Every playable sample now shows an introductory instruction panel and highlights
relevant existing controls. **Skip tutorial** releases restrictions immediately.
Save/load retains tutorial progress. The samples use an independent graph snapshot
in a PlayerPrefs sidecar keyed by the existing game's save identity plus
`.tutorial.v1`; their gameplay JSON formats and offline simulation remain compatible.
Tutorial graphs do not advance during absence. Existing gameplay saves with no
sidecar begin onboarding, and invalid tutorial sidecars reset onboarding without
altering the gameplay save.

| Sample | Guided opening |
| --- | --- |
| Cookie Bakery | Bake → purchase a producer → purchase an upgrade |
| Frontier Council | Survey → start production → buy research/development |
| Ember Guild | Hire adventurers → assign a party → dispatch a quest |
| Orbital Foundry | Set allocation priority → choose a recipe → deliver a section |
| Lastlight Railway | Open track → apply an itinerary → fulfill a delivery contract |
| Marshlight Reserve | Restore habitat → manage water → improve purity |
| Tidebound Exchange | Buy cargo → travel → sell at the destination |
| Thirteenth Bell | Read/respond → follow the next passage and consequences |
| Floodwall | Read/respond → follow the next passage and consequences |

Samples leave normal navigation and save/load available. Prestige and campaign
replay are disabled during onboarding and become available when it completes or
is skipped. Affordability and other gameplay restrictions continue to apply.
The RPG also highlights QUESTS navigation when a dispatch is the next step.
Existing producer/upgrade/party facts can satisfy applicable onboarding steps in
loaded or already advanced games. Only successful actions advance action-driven
steps; rejected purchases, routes, or choices do not count.
