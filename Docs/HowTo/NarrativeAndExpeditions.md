# Politics, expeditions, contracts, and discovery

Start with [shared ownership](Foundations.md) and the
[narrative editor workflow](../ExpandedSamples.md#narrative-authoring). Bounded story
variables are suitable for trust, facts, and promises; a large-value economy still
needs `BigDouble` accounts in an appropriate transactional owner.

## 9. Faction politics

**Build:** favors and alliances unlock actions with conflicting obligations.
**Reuse:** story integer/Boolean/set variables, conditional choices, variants,
branches, journal unlocks, and disclosed/hidden effects.

1. Define stable faction IDs and bounded relationship variables. Model membership,
   permits, and witnessed agreements as sets of stable IDs.
2. Author choices whose requirements read those values and whose effects change
   them. Decide which locked choices should explain their requirements and which
   must stay hidden to avoid spoilers.
3. Use ordered branches or conditional text for reactions to earlier actions.
   Every automatic branch requires a fallback; validate reachability and endings.
4. For economic consequences, implement a combined transaction owner or a durable
   consequence ledger with idempotent application. A story transition followed by a
   separate economy command is not automatically atomic.
5. Expose the current petition, known obligations, and disclosed consequences through
   the narrative view. Keep secret values out of player-facing reason strings.

This complete story grants a permit in exchange for faction favor. It uses only
existing narrative data and compiles with `StoryCompiler`.

```csharp
// compile: faction-story
using IncrementalFramework.Features.Narrative;

namespace IdleHands.HowTo
{
    public static class FactionStoryExample
    {
        public static StoryDefinition Build()
        {
            var story = new StoryDefinition { Id = "permit-v1", Title = "The Guild Petition", Start = "petition" };
            story.Variables.Add(new StoryVariable { Id = "guild.favor", Kind = StoryVariableKind.Integer,
                Initial = new StoryValue { Integer = 2 } });
            story.Variables.Add(new StoryVariable { Id = "permits", Kind = StoryVariableKind.Set });
            story.Text["petition.subject"] = "A trading permit";
            story.Text["petition.body"] = "The guild will recognize a favor in exchange for access.";
            story.Text["request"] = "Spend two favor to request a permit.";
            story.Text["reason"] = "Requires two guild favor.";
            story.Text["decline"] = "Keep your favor for another request.";
            story.Text["granted.subject"] = "Access granted";
            story.Text["granted.body"] = "Your permit is recorded.";
            story.Text["declined.subject"] = "Another day";
            story.Text["declined.body"] = "No agreement was made.";
            var petition = new StoryNode { Id = "petition", Kind = StoryNodeKind.Choice,
                SubjectKey = "petition.subject", TextKey = "petition.body", Checkpoint = true };
            var request = new StoryChoice { Id = "request-permit", TextKey = "request", Target = "granted",
                Unavailable = UnavailableChoice.Locked, DisabledReasonKey = "reason",
                Requirement = new StoryCondition { Kind = ConditionKind.AtLeast, Variable = "guild.favor", Integer = 2 } };
            request.Effects.Add(new StoryEffect { Kind = StoryEffectKind.AddInteger, Variable = "guild.favor", Integer = -2 });
            request.Effects.Add(new StoryEffect { Kind = StoryEffectKind.Include, Variable = "permits", Member = "trade-pass" });
            petition.Choices.Add(request);
            petition.Choices.Add(new StoryChoice { Id = "decline-permit", TextKey = "decline", Target = "declined" });
            story.Nodes.Add(petition);
            story.Nodes.Add(new StoryNode { Id = "granted", Kind = StoryNodeKind.Ending,
                SubjectKey = "granted.subject", TextKey = "granted.body" });
            story.Nodes.Add(new StoryNode { Id = "declined", Kind = StoryNodeKind.Ending,
                SubjectKey = "declined.subject", TextKey = "declined.body" });
            return StoryCompiler.Compile(story);
        }
    }
}
```

Issue a choice through `FeatureGame.Execute("choose", choiceId, narrative.View.Token)`.
The token prevents stale inputs from applying effects twice. Core story effects
support typed variable operations, not arbitrary economic callbacks. To add a new
kind of story effect, extend compilation, validation, execution, persistence, and
editor controls together, or write a combined game module around extracted story
transition logic. Do not assume subclassing adds new fields to `NarrativeState` saves.

**Persist/reset:** relationship values, permits, visited nodes, transcript, and any
consequence ledger. Explicitly choose which facts survive a loop or prestige.

**Check:** insufficient favor leaves both values unchanged; repeated tokens are
rejected; secret relationships do not leak through locked reasons; checkpoint replay
restores its earlier political state while retaining the intended ending gallery.

## 13. Expeditions with contingency plans

**Build:** parties retreat, consume supplies, or change tactics automatically.
**Reuse:** guild party/quest state, shared combat targeting and abilities, explicit
commands, and round-boundary scheduling. A general contingency-plan interpreter and
retreat command are new code; `GuildGame` is sealed.

1. Add a bounded plan definition with ordered conditions and allowed actions. Example:
   retreat below a health ratio; otherwise heal if supplies remain; otherwise attack.
2. Choose one decision boundary, such as before each combat round. Read a snapshot
   of current health, supplies, and quest state; do not evaluate from render callbacks.
3. Extend the guild's command/runtime path or implement a new expedition module.
   Reuse combat definitions while keeping plan decisions independent of class names.
4. Stage supply spending and its effect together. A retreat must mark the expedition
   inactive, cancel/supersede future work, resolve cargo/loot, and set recovery time.
5. Bound the number of decisions per round and use stable tie-breaking. A changed
   plan takes effect at the next safe boundary, not halfway through an ability action.

**Persist/reset:** plan ID/version, party assignment, supplies, current tactic, next
round, and retreat/recovery state. Drive the complete expedition timeline so speed-ups
and offline time advance combat and economy together.

**Check:** a failed heal does not spend a potion; retreat cannot award completion loot;
restoring during recovery cannot restart an old round; the same inputs/seed produce
the same action order; budget splitting between parties does not change decisions.

## 14. Evolving contracts and promises

**Build:** customers remember late deliveries, poor quality, and previous commitments.
**Reuse:** market/logistics contract definitions, inventory transactions, stable IDs,
and story promise sets. Add contract-instance history; a completed-ID list alone is
not enough for reputation, deadlines, and negotiation.

1. Give each accepted contract an instance ID, customer, requested goods/quality,
   deadline, reward terms, and explicit status. Separate acceptance from fulfillment.
2. Reserve capacity or resources at acceptance only if the contract promises such a
   reservation. A promise to supply goods is not itself a transfer of those goods.
3. Fulfill with one transaction that consumes eligible goods, grants the reward,
   marks completion, and records the relationship effect or a durable consequence.
4. Schedule deadline evaluation at a defined priority relative to same-time deliveries.
   Persist a generation or event token so renegotiation invalidates the old deadline.
5. Feed aggregate customer history into future offers and narrative variants. Keep
   immutable fulfilled terms so later balance changes do not rewrite past promises.

**Persist/reset:** accepted terms, deadlines, status, completion IDs, and customer
history. Apply reset policy separately to open obligations and remembered reputation.

**Check:** delivery exactly at the deadline follows the documented order; completion
cannot pay twice; a renegotiated deadline does not also fail the old contract; a
failed multi-item delivery spends nothing; offline expiration does not choose a
story response on the player's behalf.

## 16. Discoverable system interactions

**Build:** unusual combinations reveal recipes, lore, or permanent synergies.
**Reuse:** compiled conditions, indexed unlock rules, story fact sets, entity
selectors, and persistent source modifiers. A discovery evaluator over a bespoke
combined state may be needed when predicates cross module boundaries.

1. Define discoveries with stable IDs, eligibility predicates, reveal text, and
   one-time rewards. Separate “discovered” from “currently active.”
2. Subscribe to the relevant inputs or evaluate at a known boundary. For example,
   check after a completed batch and an ecological step, not on every visible UI row.
3. Record first discovery and grant its reward in the same transaction. Evaluate
   ongoing synergy effects from current conditions using a stable modifier source.
4. Remove only the active synergy when conditions stop holding; keep the player's
   discovery record. Rebuild active effects from authoritative state after load.
5. Add authored hints based on partial knowledge. Editor/debug views can expose the
   full predicate; player views should reveal only what the game intends.

For very large discovery catalogs, build dependency indexes for the properties or
resource IDs that can affect each predicate. The kernel's indexed resource rules
can cover native economy conditions, but they do not automatically index arbitrary
feature-state JSON or every relationship in a combined world.

**Persist/reset:** discovery IDs and reward-claimed IDs; recompute temporary active
synergies. Preserve knowledge through prestige only when explicitly configured.

**Check:** repeated eligibility never repeats the reward; removing a synergy leaves
unrelated modifiers intact; hidden discoveries stay out of the player journal until
revealed; switching UI tabs cannot trigger a discovery or mutate its reward state.
