# Ember Guild — guild management idle RPG

Open `IdleRpg.unity` and press Play. This version replaces the original strength-spending RPG with persistent adventurers, party combat, regional expeditions, and a construction/crafting economy.

## First expedition

1. In **Roster**, hire a Guardian (60 gold), Ranger (65), Mage (80), and Cleric (70). The initial guild has 300 gold and a working contract office.
2. Select Dawn company, scroll down to each character, and assign all four. A party can hold four characters, and each character belongs to only one party.
3. In **Quests**, dispatch a Briarwood patrol or hunt the Ancient treant. In **Parties**, watch health, enemy health, combat rounds, and the combat report. Turn on repeat for automatic farming.
4. Boss victories unlock the next region. Defeated enemies grant gold, party experience, and special materials immediately. Bosses drop more materials.
5. Recall a repeating party to change its roster, train, or equip its members. Recall preserves earned loot and requires five seconds of recovery. Defeat stops repeat, preserves characters and earned loot, and requires 20 seconds of recovery.

## Adventurers

| Class | Role | Behavior |
| --- | --- | --- |
| Guardian | Tank | Draws enemy attacks while alive; shield reduces damage after defense. |
| Ranger | Single target | Aimed shots deal 150% attack to one enemy. |
| Rogue | Burst damage | Every third combat round is a triple-damage backstab. |
| Mage | Area damage | Firestorm strikes every living enemy. |
| Cleric | Healer | Heals the most injured living ally, then attacks. |
| Bard | Support | Boosts party attack and healing by 30%; additional Bards do not stack this bonus. |

Characters earn experience from enemy defeats and reach level 30. Gold-funded training adds 12% base stats per rank; each Training Grounds level permits five ranks, up to 20. Training, equipment changes, and party reassignment require the character and affected parties to be home. There is no permanent character death.

## Regional crafting

Every region has a three-wave patrol and a three-wave boss hunt. The boss occupies the final hunt wave. Each defeated minion drops one regional material; the boss drops four. Quest completion grants bonus gold. Quests remain repeatable after their first victory.

| Region | Boss | Material | Crafted equipment |
| --- | --- | --- | --- |
| Briarwood | Ancient treant | Briar hearts | Tier 2: Briarforged |
| Sunken Marsh | Mire hydra | Hydra scales | Tier 3: Hydrascale |
| Frostpeak | Glacier colossus | Frost cores | Tier 4: Frostbound |
| Ember Rift | Ember dragon | Ember shards | Tier 5: Emberforged |

Buy tier 1 Iron weapons, armor, and charms with gold in **Workshop**. Reforging consumes the equipped lower-tier item plus the recipe's gold, ore, timber, and special materials; the new item is immediately equipped. Workshop levels 1–4 unlock tiers 2–5. Weapons add attack and some Cleric healing, armor adds health and defense, and charms add health and stronger Cleric healing. Item effects and exact costs are shown in the dashboard. Defeat the Ember dragon to complete the guild campaign; farming and crafting can continue afterward.

## Guild economy

| Building | Effect |
| --- | --- |
| Contract office | Produces gold for hiring, equipment, training, crafting, and construction. Starts at level 1. |
| Lumberyard | Produces timber for buildings and crafting. |
| Quarry | Produces stone for construction and upgrades. |
| Iron mine | Produces ore for crafting and workshop upgrades. |
| Guild hall | Unlocks up to three independent parties and expands roster capacity to 16. |
| Workshop | Unlocks higher equipment tiers. |
| Training grounds | Raises the paid training cap per adventurer. |

Production buildings can be built/upgraded to level 20, paying gold and raw materials. Each level adds its displayed production rate; prices grow geometrically. Hall, workshop, and training upgrades also consume building materials, creating a loop from raw production to expanded production and new guild capabilities. There is no production upkeep. Gold production means a defeated or empty guild can always recover.

## Saving and offline play

The guild autosaves every 30 seconds, on focus loss, and through **Save guild**. Web builds store the save in browser storage; native/editor builds use `ember-guild-v3.json` with a backup in Unity's persistent data directory. This rework uses a **new entity save slot**: previous published saves are not imported or overwritten.

Reopening or loading applies up to eight hours of offline elapsed time. Raw production and active party battles advance together at combat event boundaries. Repeating quests continue while the party can win; a defeat stops that party. Offline work runs in bounded chunks to keep the dashboard responsive. Party setup and combat state survive saving mid-quest. Combat uses deterministic class abilities rather than random rolls.

## Build, validate, and upload

Use **Idle Hands → Build itch.io → Ember Guild (RPG)** or:

```sh
unity-editor -batchmode -nographics -quit -projectPath "$PWD" \
  -buildTarget WebGL -executeMethod SampleGamesBuild.RpgWebGL \
  -logFile /tmp/guild-rpg-build.log
```

Output: `Builds/IdleRpg-itch.zip`, targeting `thisisnozaku/idle-rpg:html5`.
The preferred RPG viewport is **1280×900**. The export adapts to the existing itch.io iframe size and fullscreen; changing the project's embed dimensions is optional. The strategy game's viewport remains 1000×850.

```sh
Tools/publish-samples.sh rpg          # Preview files only
Tools/publish-samples.sh rpg --push   # Upload the RPG ZIP
```

The engine-independent model and tuning are in `Assets/IncrementalFramework/Games/Rpg/` (`GuildContent.cs`, `GuildGame.cs`, and `GuildState.cs`), with sample declarations in `Samples/IdleRpg/EmberGuild.cs`. Framework simulation owns currency transactions and passive production; the guild model advances combat at two-second boundaries. Presentation is in the sample `GuildRpgBootstrap.cs` and `UnityIntegration/Rpg/GuildRpgPresenter.cs`. The same model sources are linked into `Tools/GuildRpg/IdleHands.GuildRpg.csproj` for headless tests.

`GuildRpgTests` covers affordability, party exclusivity, class roles, regional unlocks, loot, crafting transactions, construction, training caps, defeat/recovery, parallel parties, malformed saves, and exact continuous/offline replay. Its full-campaign test reaches all four regions and crafts Emberforged equipment using earned resources only. PlayMode tests boot the saved scene and exercise hiring, assigning, dispatching, building, and buying equipment through the actual dashboard buttons.

Verification on 2026-10-27 with Unity 6000.3.27f1: 214 EditMode and 16 PlayMode
tests passed (including 12 new guild simulation tests). Roster, construction,
and combat dashboard screenshots were rendered and inspected.
