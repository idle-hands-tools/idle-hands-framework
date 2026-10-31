# Sample games

Open this project with the Unity version in `ProjectSettings/ProjectVersion.txt`
and install its Web Build Support module. Open either scene and press Play:

| Game | Scene | Loop |
| --- | --- | --- |
| Ember Guild | `IdleRpg/IdleRpg.unity` | Hire class-based adventurers, form parties, quest through four regions, craft equipment, and expand a producing guild. |
| Frontier Council | `IdleStrategy/IdleStrategy.unity` | Invest in food and timber, build barracks, claim Riverlands, Highlands, and the Citadel. |

**Ember Guild** now has a dedicated [guild RPG guide](IdleRpg/README.md), with six
classes, individual characters, combat roles, three unlockable parties, four
regions, enemy crafting drops, five equipment tiers, and production/construction
buildings. Its new entity save is separate from the original RPG. Tuning is in the
engine-independent `Samples/IdleRpg/EmberGuild.cs`; presentation has its own dashboard.

**Frontier Council** remains a UI-driven strategy sample using authored
ScriptableObjects in `IdleStrategy/Content`. Multi-resource construction and
one-time conquest spend troops and supplies. Research doubles named producer
output. Prestige resets the campaign and normal economy while legacy survives
and adds 20% production each. The strategy and cookie demos retain separate saves.

Every 30 seconds, on focus loss, and through the Save button, games save locally.
Web builds use PlayerPrefs (browser IndexedDB); native/editor builds use JSON files
in Unity's persistent data directory. Offline production is capped at eight hours.
Browser storage must be allowed; clearing site data removes saves. Background tab
scheduling varies by browser. Close/reopen or use Load to apply offline catch-up.

## Build and package

Use **Idle Hands → Build itch.io → Both games**, or run from the repository root:

```sh
unity-editor -batchmode -nographics -quit -projectPath "$PWD" \
  -buildTarget WebGL -executeMethod SampleGamesBuild.AllWebGL \
  -logFile /tmp/sample-games-build.log
```

Individual methods are `SampleGamesBuild.RpgWebGL` and
`SampleGamesBuild.StrategyWebGL`. Each selects only its own scene and sets the
product title, per-game viewport (RPG 1280×900; strategy 1000×850), default web template, background execution, and
uncompressed web assets. Editor player settings are restored afterward.

Outputs:

- `Builds/IdleRpg/WebGL/` and `Builds/IdleRpg-itch.zip`
- `Builds/IdleStrategy/WebGL/` and `Builds/IdleStrategy-itch.zip`

The ZIPs contain `index.html` at the root and the web runtime assets. They omit
Unity debug and build report directories. Generated builds are ignored by git.

## Upload to itch.io

The configured projects are [Ember Guild](https://thisisnozaku.itch.io/idle-rpg)
and [Frontier Council](https://thisisnozaku.itch.io/idle-strategy).
For each existing project, choose **HTML Game**, upload the matching
ZIP, and enable **This file will be played in the browser**. Use an embedded size
of **1280×900 for RPG** or **1000×850 for strategy** and allow fullscreen, or select fullscreen launch. Keep the page
in draft while testing purchase buttons, scrolling, campaign progression,
save/reload, and offline return in the hosted iframe. Then publish the page.
These steps follow [itch.io's HTML5 upload guide](https://itch.io/docs/creators/html5).
No itch.io account credentials are required for building; these scripts do not upload.

### Butler upload targets

From the repository root:

```sh
# Preview the packaged files without uploading:
Tools/publish-samples.sh all

# Upload both builds after authenticating with `butler login`:
Tools/publish-samples.sh all --push
```

Select `rpg` or `strategy` instead of `all` to upload only one game. Targets are
`thisisnozaku/idle-rpg:html5` and `thisisnozaku/idle-strategy:html5`.
The script uses the packaged ZIPs, checks all selected archives exist first, and
lets Butler upload differences against the previous build. Building remains separate from uploading.

After the first Butler upload, enable **This file will be played in the browser**
for the `html5` file on each project's Edit page, and save the page. Butler does
not set this flag or change the page's visibility. See the
[Butler HTML upload instructions](https://itch.io/docs/butler/pushing.html#html--playable-in-browser-games).

## Verification

Every export first runs `SampleGamesBuild.ValidateSamples`. Run it from
**Idle Hands → Validate sample campaigns** or in batch mode. The RPG check hires a
balanced party, defeats Briarwood, checks material drops and a region unlock, and
round-trips the guild save. The strategy check covers campaign completion,
research, one-time conquest, save round-trips, offline caps, and prestige.

Run Unity's EditMode and PlayMode suites for the full contracts. The new RPG tests
include a complete earned-resource campaign, atomic crafting, class abilities,
parallel parties, active quest saves, bounded eight-hour offline catch-up, and
buttons in the actual dashboard. See the [RPG guide](IdleRpg/README.md).

## Six expanded samples

Additional factory, railway, ecology, time-loop, market, and narrative campaigns
are available under `Expanded/Scenes`. See the [expanded samples and story editor
guide](../../../Docs/ExpandedSamples.md) for controls, authoring, tests, and exports.
