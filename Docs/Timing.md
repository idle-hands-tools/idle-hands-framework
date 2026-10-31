# Offline policies, time banking, and simulation cadence

These APIs are opt-in and engine-independent. Existing Unity bootstraps, sample
settings, legacy save/load calls, and published builds retain their current behavior.
The shared controller lives in `IncrementalFramework.Progression.Timing`; optional
snapshot support lives in `IncrementalFramework.Persistence`.

## Create a controller

```csharp
using IncrementalFramework.Progression.Timing;
using IncrementalFramework.Persistence;

var options = new TimingOptions
{
    OfflineMode = OfflineProgressMode.BankTime,
    MaximumOfflineSeconds = 8 * 60 * 60,
    BankCapacitySeconds = 8 * 60 * 60,
    BankEarningRate = 1,
    TickRateHz = 10,
    WorkBudget = 256,
    BoostMultipliers = new[] { 2d, 4d, 8d }
};
var timing = TimingSnapshotService.Attach(new SimulationTimeline(simulation), options);
```

Use `new TimingController(timeline, options)` if persistence is handled separately.
`Attach` registers the optional `framework.timing` version-1 snapshot section.
The resource is a dedicated time bank, rather than an ordinary spendable production
currency. Its balance uses `BigDouble`; durations and simulation time use finite
`double`. Configuration is copied when the controller is constructed.

The default policy is `AutomaticCatchUp`. Banking earns one second per eligible
offline second by default, holds eight hours, and starts empty. The storage cap
includes reserved but unspent time. Lowering capacity on a later load preserves an
existing balance; new awards wait until there is room. The bank survives prestige.

## Claim offline time

```csharp
OfflineClaimSummary summary = timing.ClaimOffline(savedAtUtc, clock.UtcNow);
// Persist the updated checkpoint before permitting spending.
```

Both timestamps must be UTC. Negative elapsed time grants nothing. Eligible time
is capped per absence; the claim checkpoint advances over the entire absence, so
repeated claims cannot harvest time discarded by the cap.

- Automatic mode queues eligible simulation advancement and grants no bank credit.
- Banking mode grants resource units and queues no new offline simulation work.
- Already-accepted pending work remains owed regardless of the new policy.
- Timelines that disable offline rewards receive neither kind of reward.

The summary reports elapsed time, eligible time, seconds discarded by the absence
cap, queued catch-up, bank credit, and credit discarded by the storage cap. Queued
catch-up is a request, not a claim that all work has already completed. Inspect the
advancement status and actual timeline time for completion or gameplay stops.

Exact-once claims apply to the current restored state and its persisted checkpoint.
Rolling back an older save also rolls back its checkpoint and game state; these
local APIs are not an anti-cheat service. The host identifies actual absences and
must not submit foreground time again as an offline interval.

## Foreground ticks and spending

Call `timing.AdvanceOnline(elapsedForegroundSeconds)` from the host's update loop.
Use monotonic elapsed time. Reset the host's elapsed-time anchor after a resume so
the absence is not also submitted as foreground time.

The controller retains fractions smaller than `1 / TickRateHz`. Missed controller
updates are coalesced into elapsed-time requests; it does not execute an empty loop
for every missed tick. The underlying simulation still stops at every authored
event boundary. Rendering frequency and Unity `Time.timeScale` are independent.

```csharp
var accepted = timing.SetBoostMultiplier(4); // Or 2, 8, or a configured multiplier.
var result = timing.AdvanceOnline(10);
// With enough credit: 40 simulated seconds, costing 30 banked seconds.
timing.StopBoost();

var skipped = timing.RequestSkip(120); // Spend at most 120 affordable seconds.
```

Normal time is processed before bonus time within each accepted update. Boost
credit is reserved when work is accepted and charged only as bonus time actually
advances. Insufficient credit provides affordable acceleration and then returns to
normal speed. Stopping a boost prevents new boosted work; an already accepted
update finishes using its existing reservation.

A skip starts immediately but remains bounded by the work budget. It cannot overlap
pending advancement. An empty bank rejects a skip, while an oversized request
advances only the affordable duration. Gameplay stops release unused reservations
and stop the live boost; narrative choices are never selected automatically.

Advancement returns `ReachedTarget`, `BudgetExhausted`, or `Blocked`:

```csharp
if (result.Status == TimelineAdvanceStatus.BudgetExhausted)
    result = timing.Continue();
```

`Continue` drains owed work without adding wall time. In an active foreground loop,
keep calling `AdvanceOnline` with actual elapsed time: it retains normal foreground
time behind the backlog and does not reserve another boost while older work is
pending. Dispatches, scheduled events, behavior instructions, and combat work are
budgeted. A started segment retains its accepted target until it completes.

Subscribe to `timing.Changed` for an immutable `TimingView`, or read bank balance,
reserved/available balance, multiplier, pending seconds, and pending-work state.
Observer failures are recorded in `LastObserverError`; reentrant mutation is
rejected. `CanAcceptGameplayInput` is false while owed work is pending. A gameplay
stop can be input-ready, so it does not itself disable narrative choices.

The controller and simulation have one logical writer. Do not advance the underlying
game directly after attaching a controller. Ordinary gameplay commands remain
available once pending work has completed. No timer thread or Unity UI is installed.

## Save and resume

Save actual UTC time. Pending targets, ordered normal/boost/skip segments,
reservations, cadence remainder, and the claim checkpoint are explicit state;
never backdate save time to encode unprocessed acceleration.

```csharp
var snapshot = TimingSnapshotService.Capture(timing, clock.UtcNow);
string json = SnapshotService.Serialize(snapshot);

var resumed = SnapshotService.Resume(
    content,
    SnapshotService.Deserialize(json),
    clock,
    options);
var restoredSimulation = resumed.Simulation;
var restoredTiming = resumed.Timing;
```

The timing-aware `Resume` overload restores state, claims the new absence, and
processes one bounded advancement call. Continue through `resumed.Timing` if work
remains. Live boost selection always restores to 1×; previously accepted paid work
and its reservations are retained.

For an existing save without a timing section, pass `adoptLegacy: true` explicitly.
Adoption copies the snapshot, starts the bank at zero, retains native pending work,
and uses the original save UTC timestamp for the first claim. Other module sections,
root schema, and content validation remain strict. Old save/load entry points are
unchanged; opt-in timing saves require a timing-aware loader.

`TimingModule` can also be registered through `ModuleBuilder`. Restore its section
with the other modules, then call `Bind(timeline, options)` after the complete world
exists. Validation is detached; all sections are checked before restoration. The
controller captures/validates detached `TimingState` DTOs for custom persistence.

## Complete-game adapters

Always select the adapter for the complete runtime, rather than wrapping only its
underlying economy:

| Runtime | Adapter |
| --- | --- |
| Economy/kernel | `SimulationTimeline` |
| Declared behaviors and economy | `DeclarativeTimeline` |
| Feature-module game | `FeatureTimeline` in the Features assembly |
| Guild combat and economy | `GuildTimeline` in the RPG assembly |

Fresh feature and guild games can use `TimingSnapshotService.Attach` with their
adapter and retain their existing headless `Save` method. Timing-aware restoration
is available without changing sample bootstraps:

```csharp
var feature = FeatureGame.LoadTimed(module, json, clock.UtcNow, options,
    out var featureTiming, out var featureOffline, adoptLegacy: false);
var guild = GuildGame.LoadTimed(definition, json, clock.UtcNow, options,
    out var guildTiming, out var guildOffline, adoptLegacy: false);
```

For a declarative runtime, register its `DeclarativeModule` in a fresh builder and
pass that builder plus `timelineFactory: sim => new DeclarativeTimeline(module.Runtime)`
to timing-aware `Resume`. This restores behavior continuations alongside timing debt.

Custom `ITimeline` implementations expose their simulation, clock, offline eligibility,
a blocking gate, and budgeted advancement. A `Blocked` result must leave a stable,
input-ready boundary; a budgeted call must retain resumable work at the same target.
Feature modules may optionally implement `ITimelineGate`. The narrative module
blocks timed advancement while reading; the time-loop module blocks at investigation
results, loop boundaries, and endings. Existing raw feature advancement is unchanged.

## Gameplay intervals

Controller cadence does not retune a game's intrinsic mechanics:

| Definition property | Default | Meaning |
| --- | --- | --- |
| `GuildDefinition.CombatRoundSeconds` | 2 | Time between combat rounds |
| `LogisticsDefinition.ProductionTickSeconds` | 30 | Time between station production batches |
| `EcologyDefinition.StepSeconds` | 10 | Time between ecological updates |
| `MarketDefinition.Interval` | 30 | Time between market updates |

Set these finite positive intervals before constructing the runtime. Their values
are captured at construction; existing queued deadlines are not silently retimed.
Effects remain per tick: halving the interval doubles event frequency without
rescaling damage, per-batch production, or ecological rules. Recipe, travel, and
declared behavior durations retain their own authored settings.

Unchanged defaults preserve legacy content identities, including omission of the
new default-valued logistics property from its content fingerprint. Nondefault
feature intervals change content identity and require matching definitions or an
explicit migration. The legacy `GuildDefinition.RoundSeconds` constant remains
available as the default; runtime combat uses the configurable property.

## Verification

Unity 6000.3.27f1 passed all **357 EditMode** and **30 PlayMode** tests on
2026-10-28. Timing coverage includes all four adapters, exact boost/skip accounting,
blocked narrative boundaries, partial and zero-duration work, repeated claims,
legacy adoption, declarative continuations, fractional cadence, configurable
intervals, and prestige preservation. Existing sample dashboards also passed.
The standalone projects include the same sources and tests; this environment's
verification used Unity's test runners.

Fixed cadence also exposed a fractional-time normalization edge case in the
framework's numeric guard. The guard now matches the vendored multiplier's actual
normalization; the pinned BreakInfinity source is unchanged.
