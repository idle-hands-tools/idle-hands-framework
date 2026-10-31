# State-bound UI

`IncrementalFramework.UI` is a headless assembly: declarations, text templates,
observable bindings, and abstract elements that own interaction behavior.
`IncrementalFramework.Unity.UI` supplies the uGUI factory and concrete displays.
Neither shared layer references a genre or a sample game.

## Declare and mount

Create a `UiContext` for an entity world and optional root entity. Expressions use
the current list item as `Self`, or the root outside a list. Custom readers can
access `Root`, `Item`, `World`, and `Clock`. Supply the same extension registry
used by game definitions when expressions reference registered calculations.

```csharp
using IncrementalFramework.Core;
using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;

var context = new UiContext(game.World, game.State, () => game.Time,
    diagnostic: message => UnityEngine.Debug.LogWarning(message),
    actionRejected: reason => ShowStatus(reason));

var gold = game.Economy.GetResource("gold").GetValue<BreakInfinity.BigDouble>("Balance");
var balance = Ui.Text(TextTemplate.Create("Gold: {amount:000.00}",
    TextTemplate.Bind("amount", UiBinding<BreakInfinity.BigDouble>.From(gold))));
balance.Style.FontSize = 20;
balance.Layout.MinHeight = 32;

var hire = Ui.Button("Hire guardian", new UiAction((c, input) =>
    game.Hire("guardian") ? UiActionResult.Success : UiActionResult.Rejected(game.LastMessage)));
hire.CanExecute = UiBinding<bool>.From(c => gold.Current >= 60);
hire.DisabledReason = "Requires 60 gold and an available roster slot.";

var screen = Ui.Column(balance, hire);
screen.Spacing = 12;
MountedUi mounted = UnityUiFactory.Mount(parentTransform, screen, context);
// Use UnityUiView.Stretch(mounted.Root) for a full-parent screen.
// Dispose before replacing the state world, then mount with the new context.
```

The mounting parent should be inside an active Canvas. Unity owns the Canvas,
reference resolution, and EventSystem; the element factory owns the controls.
The existing sample bootstraps set up that scene shell.

A plain string is literal text, including braces. Explicit `TextTemplate.Create`
parses placeholders. Use `{{` and `}}` for braces inside a parsed template.

## Text everywhere

All user-visible text properties use `TextTemplate`: text content, button/toggle
captions, progress/slider captions, tooltips, and disabled reasons. Empty-list
messages are ordinary UI elements, so they support the same templates.

| Placeholder | Value | Output |
| --- | --- | --- |
| `{value:000}` | 7 | `007` |
| `{value:0.00}` | 7 | `7.00` |
| `{value:0.##}` | 7.5 | `7.5` |
| `{value:000.00}` | 7.5 | `007.50` |
| `{value:#,##0.00}` | 1234.5 | `1,234.50` |

Compact notation is available on every text-bearing property:

```csharp
TextTemplate.Create("Gold: {amount:compact}",
    TextTemplate.Bind("amount", UiBinding<double>.Constant(12345)));
// Gold: 12.3k
```

`compact` defaults to `0.#`. Use `compact:0.00` for mandatory trailing zeros,
`compact:0.##` for up to two decimals, or `compact:000.00` for leading zeros.
Suffixes are `k`, `M`, `B`, `T`, `Qa`, `Qi`, `Sx`, `Sp`, `Oc`, and `No`.
Larger magnitudes use engineering notation (for example `12.35e399`). Rounding
promotes the suffix, so `999950` becomes `1M` instead of `1000k`. Negative values
keep their sign; numbers below a thousand use the same decimal pattern without a
suffix. This works with ordinary numeric bindings and BigDouble, preserving the
latter's exponent without narrowing the entire value to double. Compact precision
uses the decimal `0`/`#` patterns above, rather than standard G/E/F specifiers.

Formats are invariant by default. Ordinary `IFormattable` values use their .NET
formats. BigDouble supports the shown decimal patterns and standard G/E/F formats;
fixed formats operate on the mantissa and exponent without narrowing the full
number to double. Fixed output is limited to 4096 integer digits and 99 fractional
digits; use G/E for larger values. Precision remains the precision of BigDouble,
not arbitrary precision. Numeric negative zero is normalized in decimal output.
Invalid syntax, missing names, and duplicate bindings are rejected when declaring
a template. Evaluation errors generate diagnostics and disable interaction.

For presentation that needs custom composition, use
`TextTemplate.Read(c => BuildDescription(c.Item))`. Add a refresh interval only if
it depends on time or unobservable host state.

## Elements and properties

Every declaration exposes `Id`, `Visible`, `Enabled`, `Layout`, `Style`,
`StyleOverrides`, and `Tooltip`. Constants convert to `UiBinding<T>` implicitly.
Use `UiBinding<T>.From(value)`, `.From(expression)`, or `.From(reader)` for dynamic
values. Appearance properties are bindable. Reuse a `UiStyle` and set individual
`StyleOverrides` to avoid modifying that shared style.

`UiLayout` includes Auto/Pixels/Fill width and height, minimum/maximum dimensions,
margin and alignment. Containers own padding, spacing, and child alignment.
Unity's container child alignment determines placement along the cross axis;
Fill dimensions determine flexible growth. Panel and stack children are retained.

| Declaration | Specific properties |
| --- | --- |
| `Ui.Row`, `Ui.Column` | Children, spacing, padding, child alignment |
| `Ui.Panel` | Children, background/border style, padding, spacing, clipping |
| `Ui.Scroll` | Content, direction, scrollbar visibility, sensitivity, initial position |
| `Ui.When` | Condition, content, optional else content |
| `Ui.Text` | Text template; font, size, style, color, alignment, wrapping, overflow |
| `Ui.Progress` | Value, minimum, maximum, caption, caption visibility, fill/track colors, direction |
| `Ui.Button` | Caption, availability, disabled reason, action, content padding |
| `Ui.Toggle` | Button properties plus checked state; action receives Boolean input |
| `Ui.Slider` | Button properties plus value, bounds, step, direction and commit mode; action receives double input |
| `Ui.List` | Items, item template, empty content, layout, selected ID and selection action |

Progress fill clamps to [0,1] and invalid ranges render empty with a diagnostic.
Sliders require finite bounds with maximum greater than minimum and a nonnegative
step. Requests clamp and snap relative to minimum. They default to committing on
release; continuous mode commits changes while dragging. Programmatic display
updates never emit input actions. The game state is authoritative after every
accepted or rejected request.

```csharp
var repeat = Ui.Toggle("Repeat quests",
    UiBinding<bool>.From(c => party.AutoRepeat),
    new UiAction((c, input) => game.SetRepeat(0, (bool)input)
        ? UiActionResult.Success : UiActionResult.Rejected(game.LastMessage)));
```

Use `UiAction.Definition(action)` to dispatch a declarative `ActionDefinition`.
Input is available as its `value` argument. Custom actions can dispatch existing
simulation commands instead. Availability is checked again when invoked; the
action must still enforce game rules transactionally. UI controls do not assign
state properties themselves.

## Lists, notifications, and lifetime

```csharp
var roster = Ui.List(
    UiBinding<System.Collections.Generic.IReadOnlyList<string>>.From(c =>
        c.Root.GetValue<System.Collections.Generic.IReadOnlyList<string>>("HeroIds").Current),
    c => Ui.Text(TextTemplate.Create("{name}: {hp:000.00} HP",
        TextTemplate.Bind("name", UiBinding<string>.From(c.Item.GetValue<string>("Name"))),
        TextTemplate.Bind("hp", UiBinding<double>.From(c.Item.GetValue<double>("Health"))))));
roster.Id = "roster";
roster.EmptyContent = Ui.Text("No adventurers hired.");
```

`Ui.Query(TargetQuery.All().OfKind("hero"))` also supplies list items. Queries
observe additions/removals and Value reads used for filtering or sorting. Lists
retain and reorder rows by stable entity ID, remove missing entities, and scope
row-local element IDs under the list and entity identities. Duplicate items and
duplicate element IDs within a mounted tree produce diagnostics/errors. Initial
static duplicate IDs reject the mount. Selection uses an optional action; selection
state is supplied by the game or a presentation entity. Lists are not virtualized.

Bindings render immediately, track reads, and replace their subscriptions when
conditional reads change. Timed bindings specify seconds, for example
`UiBinding<double>.From(c => c.Clock(), refreshInterval: 0.1)`. The Canvas scheduler
continues checking timed visibility even while an element is hidden. Conditional
panels retain both branches and exclude the hidden branch from layout/input.

Call `mounted.Refresh()` after changes to unobservable host state. Dispose the
mounted view before replacing the game world. Destruction also disposes the tree.
The RPG remounts persistent declarations after load and uses retained roster rows;
the economy presenter projects the simulation journal into a separate observable
presentation entity, retaining its existing formatting and command adapter.

## Alternative displays

Subclass the headless abstract element, not a Unity control. For example, both a
text button and a custom icon button inherit `UiButton`. Renderers implement
`OnMount`, `OnRender`, and `OnDispose`; `OnRender` reads evaluated properties such
as `Text`, `Enabled`, `CanExecute`, and `Tooltip`. Feed input into `Activate()` or
`RequestChange(value)`. The base owns subscriptions, availability rechecking,
action dispatch, rejection reporting, and authoritative refresh.

Implement `IUiElementFactory.Create` to choose displays. For the Unity host, each
display also implements `IUnityUiElement`, exposing its `UnityUiView`; the default
factory demonstrates child mounting and lifecycle. Pass your factory as the final
argument to `UnityUiFactory.Mount`. UI declaration structure is fixed after mount;
use conditional elements and lists for dynamic structure, or remount to replace it.

Headless tests exercise the same operations with multiple fake displays. PlayMode
tests verify actual controls, input feedback prevention, retained rows, hidden
visibility timers, layout, and both sample game flows.

## Tutorials and highlighting

See [tutorials](Tutorials.md) for `IUiPresentationPolicy`, semantic `TargetId` rules,
UI enable/disable overlays, and non-interactive highlight frames. Presentation
overlays preserve authored bindings and normal gameplay eligibility.
