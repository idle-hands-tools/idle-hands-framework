# Economies, parallel worlds, ecology, and research

Use [one state authority and complete timeline](Foundations.md) before connecting
systems. Existing module queues and definitions are building blocks; economic
agents, cross-world coordination, environmental coupling, and procedural generation
are extensions described here, not built-in services.

## 4. A changing world economy

**Build:** competitors purchase scarce inputs and respond to excess supply.
**Reuse:** `MarketDefinition`, quotes, stock, price-impact previews, standing-order
semantics, and the market interval. The stock market sample simulates demand, not
independent rival companies.

1. Add rival entities with cash, inventories, strategy IDs, and saved decision
   deadlines. Give every purchase a real counterparty or explicitly modeled market
   source; distinguish monetary transfers from authored money creation.
2. Implement a bounded decision policy: inspect quotes, compute a desired reserve,
   choose one affordable order, and return a command intent. Keep the policy read-only.
3. At each market boundary, freeze the observation inputs if decisions should be
   simultaneous, then commit orders in a documented order such as rival ID and order
   sequence. Otherwise document that later rivals observe earlier trades.
4. Reuse the market's sequential price-impact logic for both previews and fills.
   Revalidate funds, stock, and storage for every fill.
5. Apply demand/regrowth events at authored boundaries. Save sampled future events
   or the exact RNG state and sequence of draws.

A combined market feature state is a practical first extension: it owns all rivals
and the market, so each trade can be one transaction. Do not call separate
spend/grant commands on two simulations and describe the result as atomic.

**Persist/reset:** rival accounts, strategy configuration, outstanding orders,
market history limits, RNG state, and pending decisions. Decide whether rivals reset
with the player's season or continue as persistent institutions.

**Check:** a rival cannot borrow accidentally; failed fills change no account;
identical seeds and inputs replay identically; price previews include large-order
impact at the configured bounds; closed markets leave orders in a defined state.

## 6. Interconnected parallel worlds

**Build:** several economies exchange resources while following one overall clock.
**Reuse:** explicit snapshots, module sections, stable inventory ownership, and
`ITimeline`. There is no supplied multi-world coordinator or cross-simulation transaction.

Start with one combined feature state containing `worldId → worldState`, one
transport ledger, and one queue. This avoids independent clocks and makes cross-world
transactions local. Use a custom composite runtime only when independent
child runtimes are necessary.

For a composite runtime:

1. Create a root simulation for global time and metadata, plus fresh child factories.
   Give every child a stable world ID and save each child's complete snapshot.
2. Maintain a shared next-boundary time and a stable world ordering. Advance children
   toward that boundary with one shared work budget, then commit deliveries and
   advance the root clock only after every child reaches the barrier.
3. If a budget ends after world A reaches the boundary but before B, persist the
   boundary, current child cursor, and each child's time. Resume the remaining child;
   do not rerun A or expose partially synchronized state to gameplay commands.
4. Let the root own in-transit cargo. A shipment must have one owner and a stable
   delivery ID. Use a single authoritative ledger or an explicit idempotent transfer
   protocol; separate child snapshots do not provide a distributed transaction.
5. Expose the root and its complete advancement through your `ITimeline`. Whole-game
   boosts must advance every child, and a child's narrative stop must block/refund
   the combined request rather than letting other worlds silently run ahead.

**Persist/reset:** child snapshots, global time, barrier/cursor, transfer ledger,
acknowledgements if used, and world-level preservation policies. Validate references
and all child schemas before returning a usable restored session.

**Check:** save between child updates and resume without duplicate production;
transfer conservation holds; no root time passes the slowest unfinished child;
forecasting a multiverse does not share a child store with the live session.

## 8. Persistent environmental consequences

**Build:** industrial expansion changes the ecosystem and future productivity.
**Reuse:** the ecological step model, simultaneous pre-step observations, inventory
consumption, and source-tracked modifiers. The current ecology sample is not already
coupled to the factory sample.

1. Add pollution, remaining deposits, habitat condition, and restoration investments
   to a combined game state. Use finite doubles for bounded ecological measures and
   `BigDouble` for economic quantities.
2. Define when pollution occurs: starting a batch, completing one, or operating per
   simulated second. Charge it once at that boundary; do not also charge it in UI updates.
3. Compute each ecological step from a frozen prior state and stage the whole step
   together. Specify consumption limits so several species cannot independently eat
   the same last unit of vegetation.
4. Derive economic penalties from condition. Recalculate from authoritative state
   or replace a stable source modifier; avoid multiplying another permanent penalty
   onto the previous penalty at every step.
5. Add recovery actions and a viable path from damaged states. If deposits are
   exhaustible, define imports, recycling, or alternate production rather than
   relying on a resource source that can disappear permanently.

Gameplay step intervals retain authored per-step effects. Halving
`EcologyDefinition.StepSeconds` changes ecology pacing; it is not automatically a
higher-accuracy integration of the same differential equation. For rate-based
continuous ecology, implement and validate an explicit numerical integration model.

**Persist/reset:** environmental state and remaining deposits. Choose preserved or
transformed land conditions separately from run-local machines and cash.

**Check:** batch pollution is charged once after restore; modifier removal restores
only the intended source; iteration ordering does not double-consume food; a damaged
start remains recoverable using legal actions; long offline runs remain budgeted.

## 10. Procedural research trees

**Build:** each campaign offers different technology dependencies and synergies.
**Reuse:** typed content definitions, indexed unlock requirements, `XorShift64`,
compiled actions/modifiers, content fingerprints, and versioned snapshots. A tree
generator and generated-content reconstruction contract are new code to write.

1. Generate a bounded directed acyclic graph. A simple method assigns depth/layer
   first, then chooses prerequisites only from earlier layers.
2. Use a dedicated seed and stable generated IDs such as `research:campaign:node`.
   Generate costs, effects, and prerequisite lists from whitelisted templates.
3. Validate that every node is reachable, required progression is affordable, and
   early research cannot require a resource produced only by its descendants.
4. Compile the generated graph into explicit actions, upgrades, rules, or a custom
   research module. Define a transaction that spends all research costs and marks
   the node purchased together.
5. Reconstruct identical generated content **before** ordinary snapshot restoration,
   because restoration validates content identity. Store seed plus generator version
   in an outer save manifest, or persist the generated definitions themselves. A
   module section alone is too late to reconstruct the content fingerprint.

Do not assume a seed remains reproducible after changing the generator algorithm.
Keep the old generator version available or write a migration. If research improves
items or production, apply stable-source modifiers and recompute derived values
rather than saving both a modifier and an already multiplied base stat.

**Persist/reset:** generated content identity, purchased nodes, pending projects,
and generator version. Keep permanent discoveries distinct from campaign purchases.

**Check:** generation produces no cycles or unreachable mandatory node; identical
seed/version yields identical IDs and fingerprints; reload does not reroll research;
a failed purchase cannot unlock descendants; replaying old saves uses their generator.
