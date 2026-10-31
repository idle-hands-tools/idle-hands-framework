# Entity, Modifier, and Value framework

The framework supports typed C# declarations for common game behavior, with registered code hooks for specialized logic. Core and genre libraries are independent of Unity. Sample content and Unity presentation are separate assemblies.

For implementation recipes, see the [advanced feature how-to guides](HowTo/README.md),
which distinguish existing composition APIs from game-owned extensions.

Use [event and objective graphs](EventObjectiveGraphs.md) for saved milestones,
parallel objectives, and repeatable event chains.

## Namespace boundaries

| Namespace | Responsibility |
| --- | --- |
| `IncrementalFramework.Core` | Entity templates/instances, observable Values, modifiers, transactions, clocks, RNG, diagnostics |
| `IncrementalFramework.Definitions` | Typed property/event keys, expressions, selectors, actions, behaviors, compilation and extension registration |
| `IncrementalFramework.Economy` | Currency/resource and producer definitions, production modifiers, BigDouble adapters |
| `IncrementalFramework.Progression` | Simulation, commands, indexed rules, composition, shared declarative runtime |
| `IncrementalFramework.Combat` | Combatants, targeting, damage/healing effects, abilities and auras |
| `IncrementalFramework.Persistence` | Economy/entity snapshots, modifier records, delayed behavior continuations |
| `IncrementalFramework.Games.Rpg` | Adventurers, equipment, parties, guilds, regions and wave-based quests |
| `IncrementalFramework.Games.Strategy` | Territory definitions, declarative conquest and observable territory state |
| `IncrementalFramework.Games.Clicker` | Click templates, calculated click amounts and click actions |
| `IncrementalFramework.UI` | Headless UI declarations, shared text templates, bindings and abstract elements |
| `IncrementalFramework.Unity` | Shared authoring, economy UI, reactive bindings and diagnostics |
| `IncrementalFramework.Unity.Rpg`, `.Strategy`, `.Clicker` | Genre presentation adapters |
| `IncrementalFramework.Samples.IdleRpg`, `.IdleStrategy`, `.CookieDemo` | Named game content and scene bootstraps |

Shared assemblies cannot reference genres or samples. Genres cannot reference sibling genres. Shared combat does not depend on RPG classes. Headless `.csproj` projects mirror these assembly boundaries. Unity authoring assets remain ScriptableObjects and compile into the headless definitions.

## Templates and instances

`Entity` is the root of game objects. `EntityTemplate` defines reusable data; `EntityInstance` owns runtime data and a stable identity. Runtime entities also have a `TemplateId`. Existing definition types such as `ResourceDefinition`, `ProducerDefinition`, `UpgradeDefinition`, and `ClassDefinition` descend from `EntityTemplate`.

```csharp
var health = new PropertyKey<double>("Health");
var prototype = new EntityTemplate("adventurer");
prototype.Define(health, 100d);
prototype.DefineList("Tags", new[] { "guild" });

var world = new EntityContext();
var first = prototype.Instantiate(world, "hero:first");
var second = prototype.Instantiate(world, "hero:second");
first.GetValue(health).Current = 50;
// second.Health remains 100; changing prototype defaults affects future instances.
```

Values, owned collections and listeners are independent across instances. Saveable relationships in the games use stable entity IDs. RPG equipment and combat enemies are actual instances; parties store member/enemy IDs. Indexed health accessors are facades over combatant Values, not additional health arrays.

## Ordinary properties and notifications

Typed entities expose normal properties backed by stable `Value<T>` wrappers:

```csharp
hero.Health -= 10;
var value = hero.GetValue(CombatProperties.Health);
value.Changed += change => DrawHealth(change.OldValue, change.NewValue);
```

Assigning a property updates its backing wrapper; it does not replace listeners. Equal scalar assignments do not publish changes. `ValueList<T>` supplies observable add/remove/replace operations and immutable collection snapshots. Wrapper collections should be used instead of mutating raw list/array references.

Direct setters allow immediate typed writes. They do not enforce affordability or game rules. Calculated properties accept direct assignments too, with a `calculated-write` diagnostic. The assigned result remains until an input changes or `Recalculate()` is called. Calculations record dependencies, including property reads on other entities. Dependency cycles are rejected.

Changes propagate to calculated dependents before listeners run. Transactions defer listeners until all writes commit. Observer exceptions become diagnostics and do not undo state. Nested notifications are queued; a cascade exceeding 1,024 deliveries is stopped with a diagnostic. The Unity adapter forwards diagnostics to `Debug.LogWarning`.

## Declarative calculations and actions

Declarations construct inspectable expression/effect objects; ordinary gameplay does not require callbacks.

```csharp
var coins = new PropertyKey<int>("Coins");
var training = new PropertyKey<int>("Training");
var power = new PropertyKey<int>("Power");
var builder = new GameDefinitionBuilder();
builder.Template("adventurer")
    .Value(coins, 100)
    .Value(training, 0)
    .Calculated(power,
        Expr.Add(Expr.Constant(10),
            Expr.Multiply(Expr.Property(training), Expr.Constant(2))));
builder.Action(new ActionDefinition("train", null,
    Effects.Spend(coins, Expr.Constant(20)),
    Effects.Add(training, Expr.Constant(1))));
var definition = builder.Compile();
var actor = definition.Templates["adventurer"].Instantiate(world, "hero");
bool bought = definition.Actions["train"].Execute(
    new EvaluationContext(actor, definition.Extensions), out string error);
```

Spending and granting within one action are staged together. Failure leaves the action's state unchanged. A transaction can also stage entity creation/removal and persistent modifiers. Failed modifier arithmetic rolls back other writes and bindings.

Built-in expressions cover arithmetic, comparisons, Boolean/conditional expressions, integer division/modulo, powers, time, property references, collection counts and sums. Economy supplies BigDouble expressions. Selectors support self/source/owner, identity lookup, related entities, kind/property filters, ordering, and first-N selection. Ties use ordinal instance IDs.

Compilation checks property names/types, required extensions, calculation dependencies and referenced action IDs. The RPG content additionally validates actions against its concrete template schemas.

## Persistent and one-time Modifiers

`Modifier` is the root for changes to entities. `SetValueModifier<T>` provides a one-time assignment; `NumericModifier<T>` applies a persistent numeric effect. Existing economy modifier definitions and rule actions inherit the same foundation.

```csharp
var tx = new EntityTransaction();
new NumericModifier<double>("sword:attack", "item:sword", CombatProperties.Attack,
    NumericOperation.Flat, 9d).Stage(hero, tx);
tx.Commit();
hero.Context.RemoveModifiers("item:sword");
```

Numeric operations support `int`, `long`, `double`, and BigDouble through registered adapters. Their order is flat addition, summed percentage, multiplication, then override. Override priority is highest numeric priority, then highest ordinal modifier ID. Modifiers retain source identities so removing an item or aura does not remove unrelated bonuses.

Explicit commands/actions validate eligibility and costs. Direct setters remain available to tools and custom logic and can intentionally create state that later domain operations reject.

## Behaviors, timing and offline work

`Behavior.Sequence`, `If`, `ForEach`, `Delay`, `Repeat`, and `Transition` define behavior programs. Actions before a delay execute transactionally. Delays suspend execution in a serializable frame containing program position, target IDs, loop state and arguments. Positive repeat intervals and step budgets keep advancement bounded.

```csharp
builder.Behavior(new BehaviorDefinition("practice", Behavior.Repeat(10,
    Behavior.Action("train"))));
```

A standalone `BehaviorRunner` advances declared jobs. `DeclarativeRuntime` couples it to analytical `Simulation` production with a shared clock and work budget. It can advance to behavior boundaries inside a budget-limited, already-prepared production interval. `DeclarativeModule` composes the runtime and its snapshot section through `ModuleBuilder`.

Use fresh module instances when restoring. Event bindings are configuration: register them on each runtime. `EventKey<T>` and `BehaviorRunner.On` bind manual, lifecycle or property-change events to behaviors. Property-change notifications are coalesced at simulation/transaction boundaries; threshold-sensitive resource progression also has the indexed rule scheduler.

## Custom logic

Register code under stable IDs with `ExtensionRegistry`. Available interfaces are `ICalculation<T>` (including Boolean conditions), `ITargetSelector`, and `IEffectHandler`. Custom effects receive an `EffectExecutionContext` and stage writes through its transaction methods. Custom modules may also register commands, systems and snapshot sections through `ModuleBuilder`.

Calculation and selector contexts expose reads rather than mutable entities. Effect contexts provide staged writes and the owning framework RNG. Stateful extensions should keep state in entity Values or explicit snapshot sections. Persist registration IDs and parameters; never serialize delegates or arbitrary CLR type names.

```csharp
builder.Extensions.Register<IEffectHandler>("advanced-effect", new AdvancedEffect());
builder.Action(new ActionDefinition("special", null,
    Effects.Custom("advanced-effect")));
```

Missing required registrations are errors. Delayed arguments use explicit scalar/ID/string-array/BigDouble type tags; complex custom state belongs in Values or registered snapshot sections.

## Persistence and the save-format break

Framework snapshots now include the entity world: copied property data, entity/template IDs, RNG/identity state and persistent modifier applications. Calculated formulas and listeners are configuration and are rebuilt on load. Temporary writes to calculated results are not saved.

The new snapshot schema is version 4. Samples use new save keys/files, so old published saves are not automatically imported. Genuine pre-entity snapshots require explicit migration support; the games do not enable it. Entity reference/schema and modifier validation happens before applying property records. Behavior sections validate registered programs and continuation frames.

The RPG uses this framework snapshot directly rather than embedding a second economy JSON document inside its own save format. It resumes actual party battles, rewards, equipment and guild state with an eight-hour offline cap.

## Sample content and verification

`Samples/IdleRpg/EmberGuild.cs` declares class abilities, role targeting, auras, region enemies, quest waves, prices, recipes, training and construction actions. `Games/Rpg` executes those definitions without class-name switches. Equipment bonuses and training are source-tracked persistent modifiers; damage, healing, hiring and crafting are transactional effects.

Cookie clicking uses `Games.Clicker`; strategy territory presentation uses `Games.Strategy`. Both reuse shared economy templates and declarative rules through Unity authoring adapters.

Run `dotnet test Tools/Tests/Framework.Tests.csproj -c Release`, Unity EditMode/PlayMode tests, and `dotnet run --project Tools/Benchmark/Framework.Benchmark.csproj -c Release`. Build web packages with `SampleGamesBuild.AllWebGL`. The benchmark reports analytical advance latency and allocations separately from setup; reactive Values add observable-state overhead and are not a universal zero-allocation guarantee.

Reusable displays and authoring examples are covered in the [state-bound UI guide](BoundUI.md).
