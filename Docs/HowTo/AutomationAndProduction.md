# Automation, production, logistics, and resilience

Read [the transaction and composition foundations](Foundations.md) first. The
extensions named below are game-owned code to implement; the supplied sample
modules provide concrete algorithms and state models to adapt.

## 1. Programmable automation

**Build:** editable policies such as “smelt when ore exceeds a protected reserve.”
**Reuse:** typed expressions, `ActionDefinition`, `Behavior.Repeat`, and
`DeclarativeModule`. Start with an authored policy before building a rule editor.

1. Put the policy's inputs on entity Values: inventory, reserve, enable flag, and
   priority. Keep policy definitions separate from their last execution result.
2. Declare the intentional action with atomic costs/effects and its own requirement.
   Checking eligibility in the policy does not replace checking it in the action.
3. Poll at an authored positive interval, or subscribe to relevant indexed changes.
   For several eligible policies, sort by explicit priority then stable rule ID.
4. Revalidate after every successful action because earlier actions may consume the
   same inputs. Limit actions per decision boundary to prevent automation cascades.
5. For player-editable rules, add a small compiler accepting whitelisted conditions
   and actions. Resolve IDs and validate property types; never load executable C#.

This complete definition demonstrates the existing declarative route. It converts
four ore to two plates instantly when its five-second automation check runs; use a
scheduled batch, as in [the workshop](Foundations.md#example-a-saved-timed-work-order),
when crafting itself must take time.

```csharp
// compile: automation
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IdleHands.HowTo
{
    public static class AutomationExample
    {
        public static CompiledGameDefinition Build()
        {
            var ore = new PropertyKey<BigDouble>("Ore");
            var plates = new PropertyKey<BigDouble>("Plates");
            var reserve = new PropertyKey<BigDouble>("Reserve");
            var enabled = new PropertyKey<bool>("AutomationEnabled");
            var builder = new GameDefinitionBuilder();
            builder.Template("workshop")
                .Value(ore, new BigDouble(12)).Value(plates, BigDouble.Zero)
                .Value(reserve, new BigDouble(4)).Value(enabled, true);
            var eligible = Expr.And(Expr.Property(enabled),
                Expr.AtLeast(Expr.Property(ore), Expr.Add(Expr.Property(reserve), Expr.Constant(new BigDouble(4)))));
            builder.Action(new ActionDefinition("smelt", eligible,
                Effects.Spend(ore, Expr.Constant(new BigDouble(4))),
                Effects.Add(plates, Expr.Constant(new BigDouble(2)))));
            builder.Behavior(new BehaviorDefinition("auto-smelt",
                Behavior.Repeat(5, Behavior.If(eligible, Behavior.Action("smelt")))));
            return builder.Compile();
        }
    }
}
```

Create a `Simulation` (which registers BigDouble numeric operations) before
building and using this definition. Register it through `DeclarativeModule`, instantiate its workshop
template, and start `auto-smelt` on that actor. Starting a repeating behavior can run
its first body immediately; put an initial `Delay` before it if the first check
should wait. Drive the complete `DeclarativeTimeline` and save its behavior section.

**Persist/reset:** policy IDs and settings, any cooldowns, and behavior continuations.
Re-register subscriptions after restore. Decide explicitly whether prestige keeps
policies while clearing their run-local execution history.

**Check:** two policies cannot double-spend ore; disabling a policy prevents future
actions; offline execution matches bounded online advancement; editing a policy
cannot revive an already consumed continuation or create a zero-delay loop.

## 2. Competing production methods

**Build:** fast, wasteful smelting versus slower, efficient smelting.
**Reuse:** `RecipeDefinition` input/output dictionaries and `Seconds`, machine recipe
selection, output reservations, and `ProductionModule` as the sample implementation.

1. Author two stable recipe IDs for the same machine kind. For example, four ore
   becomes two plates in fifteen seconds, while eight ore becomes five in ten.
2. Define unlock conditions and expose both input efficiency and output per second.
   Compare net output under the actual constrained input supply, not just recipe speed.
3. Retain the active recipe until its batch completes. A selection change affects
   the next batch; it does not rewrite already consumed inputs.
4. For power, workers, or waste, add those accounts to a combined production state.
   At start, validate every input and reserve output/waste capacity together.
5. Extend allocation with an explicit policy for shared power or labor. Discrete
   batches are supported; a general continuous flow solver is a separate extension.

Treat power as either a per-batch cost or reserved concurrent capacity, deliberately.
If it is capacity, release it on completion/cancellation rather than permanently
spending it. State whether partial cancellation refunds ingredients.

**Persist/reset:** selected and active recipes, reservations, completion deadlines,
and allocation priority. Reset live jobs and their reservations together; preserve
unlocked recipe knowledge separately when desired.

**Check:** a recipe switch cannot change an active batch's yield; input starvation
leaves a machine waiting; output caps account for reservations from every machine;
a faster recipe can still lose to an efficient recipe under ore scarcity.

## 3. Supply-network bottlenecks

**Build:** a factory can have enough total timber but still wait for a delivery.
**Reuse:** local `Inventory`, `RouteStop`, `TrainState`, track durations, and logistics
arrival/departure events. The railway sample has finite cargo capacity but unlimited
track traffic capacity; congestion is new behavior to implement.

1. Give each store a stable owner ID. Every item belongs to exactly one station,
   vehicle manifest, or explicitly represented shipment.
2. Combine logistics and production under one authority if delivered materials feed
   machines. Do not add the sealed sample modules directly to one builder.
3. Add per-link capacity and a saved waiting queue. Departure atomically acquires a
   link slot and moves cargo into the vehicle manifest; arrival releases the slot.
4. Start waiting departures in a deterministic order. Re-check destination capacity
   at arrival and retain undeliverable cargo aboard, or reserve destination storage
   at departure if that is your chosen contract.
5. Handle route edits at arrival. Removing a link must reject while occupied, wait
   for clearance, or apply an explicitly authored reroute policy.

For a first implementation, use nonnegative edge travel times and fixed itineraries.
Add shortest-path rerouting later with stable tie-breaking; persist the chosen
route so loading a save does not choose a different journey halfway through it.

**Persist/reset:** manifests, occupied slots, waiting order, routes, arrival times,
and destination reservations. A reset must account for cargo aboard vehicles.

**Check:** inventory totals are conserved across transfer and save/load; a capacity-one
link cannot launch two trains simultaneously; full stations do not delete cargo;
budget exhaustion at arrival cannot release a slot or grant cargo twice.

## 15. Infrastructure failure and resilience

**Build:** breakdowns make maintenance and redundant capacity valuable.
**Reuse:** observable entity state, source-tracked modifiers, timed events, inventory
transactions, and seeded RNG. No built-in reliability or insurance module exists.

1. Add condition, operating state, maintenance deadline, and an event generation to
   each facility. Define which states permit starting or finishing a batch.
2. For deterministic wear, schedule a failure threshold. For stochastic failures,
   sample the next deadline once when entering an operating state and persist it;
   never redraw a failure every rendered frame.
3. On failure, atomically mark the facility unavailable and resolve its active job
   according to your contract: suspend with remaining duration, refund, or lose an
   explicitly displayed amount. Reuse modifiers only for numeric effects, not as a
   substitute for tracking job ownership.
4. Repair commands spend parts and schedule recovery. Use versioned event tokens
   or a saved generation check so superseded maintenance/failure events are harmless.
5. Add redundancy as alternate reachable capacity. Model insurance as a separate
   contract keyed by incident ID, with eligibility and one-time payout state.

If sampling inside a copy-on-command feature transaction, use RNG state stored in
that copied state and commit its new state with the outcome. Drawing from a live
external RNG before a transaction fails would advance randomness despite rollback.

**Persist/reset:** condition, next sampled failure, interrupted jobs, generation
numbers, and incident/payout IDs. Preserve insurance only if the reset contract says so.

**Check:** failed repair spends nothing; superseded failures do not disable a repaired
facility; each incident pays once; online/offline execution consumes the same RNG
sequence; removing a debuff does not accidentally remove unrelated equipment bonuses.
