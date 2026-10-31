# Prestige, workers, equipment, and generational history

Use stable entity identities and explicit reset ownership. Native economy reset
policies do not automatically reset arbitrary entity collections or feature state.
See [composition foundations](Foundations.md) before implementing these recipes.

## 5. Branching prestige

**Build:** choose between preserving research, retaining workers, or strengthening
future production when restarting a campaign.
**Reuse:** native `ResetPolicy` values, prestige rewards, `DenseStateStore<T>`, and
`IPrestigeResetParticipant`. The native economy has one active prestige definition;
it does not already offer a branch-selection API.

1. Define branch IDs and a saved selection. Display each branch's preserve/reset/
   transform contract before requesting the reset.
2. Keep the native economy's reset policy fixed for a first version. Implement
   branch-dependent transforms for game-owned stores in reset participants.
3. In `Prepare`, validate the selection and stage every custom write through the
   provided `StateTransaction`. Do not commit independently or invoke UI callbacks.
4. Record the chosen permanent reward alongside the reset. Recompute derived bonuses
   from preserved state instead of multiplying the existing bonus again on load.
5. If branches must change native spending or resource/producer preservation rules,
   extend the prestige command's own validation and commit path. Temporarily changing
   live content and then issuing `PrestigeCommand` is not an atomic branch reset.

This participant records a generation in an already registered and snapshotted
integer store. Combine it with your branch-specific participants and native reset.

```csharp
// compile: prestige-participant
using IncrementalFramework.Core;
using IncrementalFramework.Progression;

namespace IdleHands.HowTo
{
    public sealed class RecordGeneration : IPrestigeResetParticipant
    {
        private readonly DenseStateStore<int> generations;
        private readonly int index;
        public ResetPolicy Policy => ResetPolicy.Transform;
        public RecordGeneration(DenseStateStore<int> generations, int index)
        { this.generations = generations; this.index = index; }
        public CommandResult Prepare(SimulationReadModel model, StateTransaction transaction)
        {
            transaction.Set(generations, index, checked(generations[index] + 1));
            return CommandResult.Ok();
        }
    }
}
```

Register the store with `builder.State`, a versioned section for that store with
`builder.Snapshot`, and the participant with `builder.Reset` before creating the
simulation. Typed stores are not automatically included in snapshots. A participant
with `Policy == Preserve` is skipped; use a transforming participant for writes.
The stock feature games' `replay` actions are separate from kernel prestige.

**Persist/reset:** branch choice, permanent entitlement, generation count, and the
custom stores' reset contracts. Stop accepting reset input while timed work is pending.

**Check:** another participant can veto the reset without incrementing the generation;
a repeated load does not add another reward; each branch preserves exactly its
promised data; no old active job retains reservations for resources that were reset.

## 11. Worker specialization and succession

**Build:** workers learn skills, mentor replacements, and eventually retire.
**Reuse:** templates/instances, observable Values, calculated properties, selectors,
modifiers, and saved behaviors. Workers and inheritance rules are game-owned content.

1. Define workers with stable IDs, role, experience, assignment ID, mentor ID, and
   career state. Keep trained base attributes separate from equipped/temporary bonuses.
2. Derive output from role and training using calculated Values or registered
   calculations. Select assigned workers by stable IDs; never use list position as identity.
3. Award experience at an authored work boundary, such as a completed batch. Define
   participation explicitly so an unassigned worker cannot receive duplicate experience.
4. Model mentoring as a timed behavior or saved job with participant IDs. Re-check
   availability at the transfer boundary; removing a mentor must cancel or redirect it.
5. Retire through one transaction: mark retirement, release assignments and modifiers,
   create the successor, and transfer the specified fraction of knowledge. Keep a
   completed succession ID so retries cannot create several replacements.

Keep age in simulated time if speed-ups should accelerate careers. Use wall time
only if that is an explicit separate design, and never read real UTC from a combat
or production formula that must replay deterministically.

**Persist/reset:** base skills, mentorship jobs, assignments, retirement status, and
successor links. Decide whether prestige preserves people, their knowledge, or only
a lineage summary. Rebuild calculated output and subscriptions on restore.

**Check:** independent template instances do not share experience; reassignment
removes old productivity bonuses; a missing mentor does not leave an immortal job;
retirement during offline advancement produces one successor with valid references.

## 12. Equipment with mechanical effects

**Build:** equipment changes targeting, production behavior, or available abilities.
**Reuse:** source-tracked `NumericModifier<T>`, `Effects.Attach/RemoveModifiers`,
shared combat ability definitions, and `IEffectHandler` for specialized operations.

1. Give each item instance a stable ID, slot, owner, immutable revision, and effect
   definition IDs. Validate ownership and slot restrictions in the equipment command.
2. Represent numeric bonuses as modifiers with a source containing owner, slot,
   item instance, and revision. Unequipping removes that source only.
3. Represent behavior changes explicitly: an ability ID, targeting policy, extra
   output rule, or registered custom effect. An attack multiplier alone cannot
   implement cleave, recycling, or a different target selector.
4. Replace old effects and slot references in one transaction. A failed equip must
   keep both the old item and its effects.
5. Reconstruct formulas/handlers on load and restore modifier records. Do not restore
   an already buffed base stat and then apply the same modifier again.

This helper atomically replaces an attack-bonus source. The caller's hero template
must define `WeaponSource` (string) and `Attack` (double). This is an effect helper,
not a full inventory/ownership check. A source identifies an immutable item revision.

```csharp
// compile: equipment
using System;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IdleHands.HowTo
{
    public static class EquipmentExample
    {
        public static bool ReplaceAttackBonus(EntityInstance hero, string source, double amount)
        {
            if (string.IsNullOrEmpty(source) || double.IsNaN(amount) || double.IsInfinity(amount))
                throw new ArgumentException("Invalid equipment effect.");
            var slot = hero.GetValue<string>("WeaponSource");
            string previous = slot.Current;
            if (previous == source) return true;
            var transaction = new EntityTransaction();
            transaction.Set(slot, source);
            new NumericModifier<double>(source + ":attack", source,
                new PropertyKey<double>("Attack"), NumericOperation.Flat, amount)
                .Stage(hero, transaction);
            if (!string.IsNullOrEmpty(previous))
                Effects.RemoveModifiers(previous).Stage(new EvaluationContext(hero), transaction);
            return transaction.Commit();
        }
    }
}
```

For a mechanic such as recycling failed crafts, register a custom effect under a
stable ID. Its handler stages inventory changes through the action's transaction
model. Persist handler IDs/parameters, not delegates. Extend the owning transaction
if the affected inventory belongs to a different state model.

**Persist/reset:** item identity, slot relationships, effect sources, and revision.
Choose whether equipment survives prestige separately from worker training.

**Check:** equipping the same revision twice is idempotent; swapping preserves other
slots' bonuses; invalid arithmetic rolls back the slot change; a save/restore gives
the same attack; a mechanical proc runs once per eligible event, not once per UI refresh.

## 20. A generational world history

**Build:** resets change the same world's institutions and landmarks over time.
**Reuse:** module snapshot sections, reset participants, entity references, and
narrative conditions. A historical ledger and its presentation are extensions.

1. Create separate current-generation and persistent-history stores. Record a compact
   generation entry: generation ID, branch, key discoveries, landmark changes, and
   outcome IDs. Avoid serializing the entire old world into every history entry.
2. During reset preparation, stage the historical entry and the next generation's
   derived starting state together. A reset veto must leave the ledger untouched.
3. Give persistent landmarks stable identities. Rebuild run-local workers and
   buildings, then reconnect them through institution/landmark IDs rather than
   dangling references to deleted characters.
4. Derive new opportunities from preserved facts: a rebuilt bridge unlocks a route,
   a university grants research options, or a prior promise changes a story passage.
5. Bound history growth with summaries or archival pages. If summaries affect gameplay,
   define a deterministic aggregation rule and migrate it explicitly when changed.

**Persist/reset:** the ledger, its next identity, persistent entities, and the
mapping from historical facts to current starting state. Preserve the time bank
through its own timing section rather than copying it into each generation entry.

**Check:** canceled resets create no historical entry; one reset creates one generation;
old references remain inspectable or are explicitly summarized; starting bonuses
rebuild identically on load; many generations do not make every frame scan all history.
