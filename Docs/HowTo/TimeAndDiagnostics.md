# Time banking, diagnostics, forecasts, and offline behavior

Use [the timing reference](../Timing.md) for the accounting contract and restore
APIs. This guide shows how to turn those APIs into game features. An operation
profile, explanation graph, or scenario comparison is game-owned behavior to build.

## 7. Time banking

**Build:** absence earns a bank of seconds that players spend on accelerated play.
**Reuse:** `TimingOptions`, `TimingController`, timeline adapters, and
`TimingSnapshotService`. This feature already has a framework implementation.

1. Wrap the complete runtime in its timeline adapter. Use `SimulationTimeline` for
   a native economy, `DeclarativeTimeline` for declared behaviors, `FeatureTimeline`
   for a feature game, or `GuildTimeline` for a guild game.
2. Attach timing persistence once. Configure `BankTime`, earning rate, absence cap,
   bank capacity, allowed multipliers, tick frequency, and work budget.
3. Claim an absence from the saved UTC timestamp to the current UTC timestamp.
   Save the updated claim before exposing bank spending. Do not also run the legacy
   offline reward path, or claim intervals already supplied as foreground time.
4. Drive foreground elapsed time through `AdvanceOnline`. Continue pending work
   with `Continue`; display the pending state and gate gameplay with
   `CanAcceptGameplayInput`. A blocked story requires its valid choice before time
   can resume. Work-budget exhaustion requires another continuation, not a new target.
5. Let players select an allowed `SetBoostMultiplier`, call `StopBoost`, or request
   an explicit `RequestSkip`. Display available and reserved seconds separately.

This complete helper accepts the appropriate adapter instead of choosing one that
might leave part of a game behind:

```csharp
// compile: time-bank
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression.Timing;

namespace IdleHands.HowTo
{
    public static class TimeBankExample
    {
        public static TimingController Attach(ITimeline timeline)
        {
            return TimingSnapshotService.Attach(timeline, new TimingOptions
            {
                OfflineMode = OfflineProgressMode.BankTime,
                MaximumOfflineSeconds = 8 * 60 * 60,
                BankCapacitySeconds = 12 * 60 * 60,
                BankEarningRate = 1,
                TickRateHz = 10,
                WorkBudget = 256,
                BoostMultipliers = new[] { 2d, 4d, 8d }
            });
        }
    }
}
```

At 4× speed, one foreground second supplies one normal second and requests three
paid bonus seconds. The bank pays only for bonus time actually simulated. Reserved
seconds prevent overlapping requests from spending the same entitlement; blocking
refunds unused reservations. Tick frequency controls timing dispatch, whereas
`CombatRoundSeconds`, `ProductionTickSeconds`, ecological `StepSeconds`, and market
`Interval` control authored gameplay pacing. Changing those intervals can change
balance. They are not interchangeable settings.

**Persist/reset:** capture with `TimingSnapshotService.Capture(timing, utcNow)` and
serialize with `SnapshotService.Serialize`. Use timing-aware restore APIs; a new
controller attached after an ordinary load would lose the saved bank and claims.
Timing is opt-in: existing Unity bootstraps do not automatically expose these controls.
Treat mode changes as a deliberate configuration/migration decision, not as a second
claim on the same absence. Repeated claims on the current state are protected;
rollback to an older local save is not a server-authoritative anti-cheat system.

**Check:** compare 1×, 4×, and explicit skips over the same simulated horizon;
partially exhausted banks pay only the available bonus; pauses spend no unadvanced
time; a save/load during pending work keeps the reservation and target; a repeated
claim adds nothing; changing render frame rate does not change final results.

## 17. An explainable economy dashboard

**Build:** show why a machine is idle, where resources go, and which upgrade changes
net output rather than presenting only an unexplained total.
**Reuse:** observable Values, modifier sources, read models, `Ui` declarations, and
`TextTemplate`. See [state-bound UI](../BoundUI.md) for rendering adapters.

1. Create a read-only projection with stable row IDs. Include gross output, actual
   consumption, net flow, inventories, reservations, constraints, and observation time.
   Distinguish an instantaneous rate from a measured average over a stated interval.
2. Record explanation data where calculations happen: authored expression inputs,
   modifier source IDs, active recipe, and rejected-action reason. A cached producer
   rate alone cannot explain an arbitrary combined-world calculation.
3. Reconcile totals with the actual accounting path. Apply shared bonuses once,
   report caps and starvation, and distinguish promised output from completed output.
   The kernel does not supply a universal explanation graph; implement that projection
   alongside your custom calculations.
4. Bind observable values directly. For retained lists, `Ui.List` consumes entity
   identity lists; expose custom projection rows as entities with stable IDs rather
   than passing arbitrary DTO lists to that API.
5. Rebuild subscriptions on restore and dispose the old mounted view. Refresh
   non-observable projections at a bounded cadence, not by running gameplay commands
   from text formatting or binding callbacks.

This complete example binds an existing native gold resource without polling:

```csharp
// compile: dashboard
using BreakInfinity;
using IncrementalFramework.Progression;
using IncrementalFramework.UI;

namespace IdleHands.HowTo
{
    public static class DashboardExample
    {
        public static TextDefinition Gold(Simulation simulation)
        {
            var amount = simulation.GetResource("gold").GetValue<BigDouble>("Balance");
            return Ui.Text(new TextTemplate("Gold: {amount:compact}",
                TextTemplate.Bind("amount", UiBinding<BigDouble>.From(amount))));
        }
    }
}
```

**Persist/reset:** save authoritative simulation data, not rendered strings or a
second mutable economy. Recompute explanation rows after restore and reset.

**Check:** opening the dashboard changes no gameplay state or RNG; displayed totals
match the ledger; removing an item removes its explanation; rows retain identity
across sort changes; repeated mounting/loading does not duplicate subscriptions.

## 18. Forecasting and hypothetical planning

**Build:** compare “buy this producer” against “save for research” in an isolated
future, with assumptions and a bounded computation budget.
**Reuse:** snapshots, deterministic RNG, fresh restore factories, and the same
advancement engine that handles real play.

1. Capture a settled decision boundary. Copy content/configuration and restore a
   fresh world per scenario; never temporarily alter and undo the live game.
2. Apply the hypothetical command through its real validation path. A rejected
   purchase is a rejected scenario, not permission to grant its effects directly.
3. Advance each scenario toward the same horizon, continuing the same target when
   its work budget expires. Publish a result only when that horizon is reached, or
   explicitly label a bounded partial result with its reached time.
4. Compare a baseline from the same snapshot and saved RNG state. Display assumed
   policies, horizon, constraints, and any unresolved decision that blocks a forecast.
5. Invalidate results when relevant inputs change. Cache by snapshot/content identity,
   hypothetical choices, and horizon, rather than by a displayed balance alone.

The following complete helper deliberately supports only a plain native economy
without module snapshot sections. It proves the isolation pattern; it is not a
restore factory for a guild, story, declared behavior, or time-bank game.

```csharp
// compile: forecast
using System;
using BreakInfinity;
using IncrementalFramework.Persistence;
using IncrementalFramework.Progression;

namespace IdleHands.HowTo
{
    public static class ForecastExample
    {
        public static BigDouble Purchase(Simulation live, string producerId,
            string resourceId, double horizonSeconds, DateTime snapshotUtc)
        {
            if (double.IsNaN(horizonSeconds) || double.IsInfinity(horizonSeconds) || horizonSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(horizonSeconds));
            if (live.PendingTarget.HasValue)
                throw new InvalidOperationException("Finish pending work before forecasting.");
            var snapshot = SnapshotService.Capture(live, snapshotUtc);
            if (snapshot.Sections.Count != 0)
                throw new InvalidOperationException("Use a complete game restore factory for modules.");
            var clone = SnapshotService.Restore(live.InitialContent, snapshot);
            var purchase = clone.Execute(new BuyProducerCommand(producerId));
            if (!purchase.Success) throw new InvalidOperationException(purchase.Error);
            double target = clone.Time + horizonSeconds;
            if (double.IsInfinity(target)) throw new ArgumentOutOfRangeException(nameof(horizonSeconds));
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                if (clone.AdvanceTo(target).Completed)
                    return clone.GetResource(resourceId).Balance;
            }
            throw new InvalidOperationException("Forecast exceeded its continuation budget.");
        }
    }
}
```

For custom games, supply a factory that reconstructs their complete runtime with
fresh modules and all required snapshot sections. Restore directly rather than
using an offline `Resume` method: a hypothetical branch has not earned a new absence.
For timing-enabled games, register a fresh `TimingModule`, restore, then bind it to
the complete timeline; do not issue another offline claim. Use its controller if
modeling boosts or already earned pending timing work. Do not share live mutable
module instances, entity contexts, or RNG instances between scenarios.

**Persist/reset:** forecasts are disposable derived results. A stored plan may keep
scenario inputs and their version, but it must be recomputed after a reset or load.

**Check:** the live world's balances, time, queued work, and RNG remain unchanged;
the same plan produces the same result; a real execution matches the forecast until
an external choice diverges; section-bearing saves are rejected by the small helper;
a blocked narrative reports the decision instead of inventing an answer.

## 19. Offline policies with distinct outcomes

**Build:** players choose between normal catch-up and time banking; an expedition
may additionally have a saved conservative or aggressive operating profile.
**Reuse:** `AutomaticCatchUp`, `BankTime`, complete timeline adapters, and saved
feature/genre state. Operating profiles are an extension, not a third built-in
`OfflineProgressMode` or a multiplier applied to the wallet during load.

1. Use automatic catch-up when normal gameplay should run during absence. Use bank
   mode when absence should award spendable seconds instead. Configure maximum
   absence, earning rate, and bank capacity independently.
2. If adding risk profiles, define exact gameplay effects: encounter selection,
   retreat threshold, target preference, or permitted resource consumption. Save
   the selected profile and version before departure.
3. Execute those rules through the same gameplay transitions during foreground and
   catch-up advancement. A different “offline reward calculator” will diverge at
   starvation, death, caps, and decision gates.
4. Freeze or record profile changes at explicit boundaries. A player cannot inspect
   simulated outcomes and then retroactively select the policy that earned them.
   If intentionally using different offline rules, save that explicit operation mode
   and test its boundaries rather than hiding it in the load callback.
5. Stop at meaningful required choices. Honor timeline blocking and the runtime's
   `AllowsOfflineRewards` policy; do not award both banked and simulated seconds for
   a single interval. Continue budget-exhausted work without claiming absence again.

For a feature extension, put the profile in your combined state and read it in the
existing encounter/job event handler. For guild combat, a new strategy interpreter
belongs at the defined round/quest boundary; see
[expedition contingencies](NarrativeAndExpeditions.md#13-expeditions-with-contingency-plans).
Keep profile selection separate from UI rendering and from elapsed-time entitlement.

**Persist/reset:** selected/captured profile, its content version, active operation
mode, deterministic RNG state, pending jobs, and timing claims. Resolve old profile
versions through a content migration or reject them with a useful error.

**Check:** fixed-profile online and automatic catch-up agree over equal simulated
time; a return-time profile change affects only future decisions; repeated resumes
do not duplicate claims; bank mode does not also finish all offline expeditions;
blocking, caps, and exhausted work budgets behave consistently in both modes.
