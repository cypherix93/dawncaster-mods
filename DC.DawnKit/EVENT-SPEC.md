# Event Spec v0.1 (DRAFT) — opportunity events (Ink)

Contract + engine design for adding modded **opportunity events** — Ink-scripted
map encounters — via card packs and the C# API. The fourth content family after
cards, weapons/powers, and starting cards (MOD-TOOLKIT M3). Grounded in the
decompiled source; every mechanism below is verified at the cited file/method.
Companion to `CARD-PACK-SPEC.md` (extends the same pack manifest, schemaVersion
bump — §4). Spike verified 2026-07-19: injection path, story-serving gap, action
bridge, ink version pin.

**Scope.** `eventType = opportunity` only. Deliberately out: shrines (rarity
tiers + shrine UI), NPC/merchant events, quests, portals, per-area curated
placement (`Area.areaOpportunities`), new dialogue-action commands, and story
functions. Each is a possible later milestone (§10).

## 1. Ground truth — how the shipped system works

**Two-layer asset.** `AreaEvent : ScriptableObject` holds map-node metadata —
`AreaHandler.EventTypes eventType`, name/description with localization overlay,
sprites, `List<AreaCondition> eventConditions`, `Card aura`, flags
(AreaEvent.cs:5-43). `Dialogue : AreaEvent` adds `TextAsset textFile` (compiled
Ink JSON) + selection filters `CardRariry rarity`, `int minimumLevel`,
`int maxLevel`, `bool unique` (Dialogue.cs:3-16). The pool is
`AssetManager.allEvents` (`List<Dialogue>`, AssetManager.cs:62), loaded with
**world assets** (cleared in `ClearAllCollections`, AssetManager.cs:262; logged
under "WORLD ASSETS LOADED", AssetManager.cs:231). `RefreshCaches()` rebuilds
`eventLookupCache` (keyed by **`Dialogue.name`**, lowercased) only when
`worldAssetsLoaded` (AssetManager.cs:310-320, 1285-1288).

**Selection — the area deck.** When an area deck is built
(`AreaHandler.CreateAreaDeck`), the opportunity slot count is
`Area.opportunities` (+ `extraopportunities` area effects, −1 during invasions;
AreaHandler.cs:355-367). Slots fill in two passes:

1. Area-curated events first: `Area.areaOpportunities`, drawn randomly
   (AreaHandler.cs:520-557).
2. Remaining slots from the **global pool**: `allEvents` filtered to non-shrine
   `eventType`, `areaLevel` within `[minimumLevel, maxLevel]` (`maxLevel == 0`
   = uncapped), not in `PlayerData.doneEvents`, and passing
   `AreaConditionChecker.CheckAreaConditions` (AreaHandler.cs:516-519,
   558-587). **Mod events enter here** — same odds as vanilla generic events.

The deck entry carries **`eventContent = textFile.name`** (AreaHandler.cs:508,
532, 575) — the TextAsset's name, not the Dialogue's. `unique` is copied onto
the entry; picking a unique event adds `eventContent` to the **persistent**
`PlayerData.doneEvents` (AreaHandler.cs:1043-1046).

**Map display.** Opportunity nodes render generically — localized "Opportunity"
label + `defaultOpportunityImage` (EventDisplay.cs:783-789). No per-event art
is needed. (`EventDisplay` does look up `AssetManager.GetEvent(eventContent)`
for shrines/portals — null-tolerant fallbacks at EventDisplay.cs:207-232 — and
that lookup only matches when the Dialogue asset name equals the textFile name;
see the naming rule in §3.)

**Pickup — the story-serving gap (the load-bearing spike finding).** Picking
the node calls `AreaHandler.Dialogue(eventContent)` →
`DialogueManagerINK.StartDialogue(string)` (AreaHandler.cs:1307-1315).
`StartDialogue` **ignores `Dialogue.textFile` entirely** and loads by name:

1. Addressables key
   `"Assets/Localization/LocaleExports/Dialoguefiles_{locale}/{name}.json"`
   (DialogueManagerINK.cs:260-271);
2. on failure, `Resources.Load<TextAsset>("Dialoguefiles/" + name)`
   (DialogueManagerINK.cs:309-313, 323-344);
3. on failure, abort: `SetDialogueRunning(false)` + `CloseDialogue()`
   (DialogueManagerINK.cs:338-343).

A runtime-created TextAsset exists in neither store ⇒ **appending to
`allEvents` alone can never work; a story-serving patch is mandatory** (§6).

**The Ink → game bridge.** The dialogue loop wraps `Ink.Runtime.Story`. Any
story line containing `>>>` (or `>>>>` to auto-continue) is treated as
`;`-separated `command:arg` actions (DialogueManagerINK.cs:1264-1309):
`goto:<knot>` → `story.ChoosePathString`; `STORYFUNCTION:<fn>:<arg>` →
`story.EvaluateFunction` (arg replacement: only `imbueCost`,
DialogueManagerINK.cs:1311-1318); everything else →
`DialogueActionHandler.RunActionCode(string[])` — the **99-command closed
vocabulary** in `../docs/research/reference/dialogue-action-commands.txt`.
Speaker name = first Ink tag on the line, localized via Events table
`{tag}_N` with **fallback to the raw tag** (DialogueManagerINK.cs:584-601) ⇒
inline English speaker tags work with no localization entries. Event *node*
names likewise fall back inline (`GetEventName`, AreaEvent.cs:45-52); note
`GetEventDescription` has **no fallback** (AreaEvent.cs:54-57) — harmless for
opportunities, whose node UI never shows it (EventDisplay.cs:783-789).

**Ink version pin.** Shipped runtime: `Ink.Runtime.Story.inkVersionCurrent =
20`, `inkVersionMinimumCompatible = 18` (ilspycmd over `Ink-Libraries.dll`,
2026-07-19). Shipped stories are inkVersion 20 (sampled from
`resources.assets`: Mimic, Illusionist, Mudfang Confrontation). The runtime
rejects **newer** versions ⇒ packs must ship compiled JSON with
`inkVersion` 18–20: **inklecate v1.0.0 emits 20 (compatible); v1.1.1 emits 21
(rejected)**. Existence proof of the minimal event: the shipped `Mimic` story
is a single line — `>>>>DIRECTCOMBAT:Mimic`.

## 2. Player-facing behavior

- Modded opportunity nodes appear on area maps exactly like vanilla roadside
  events: generic "Opportunity" card back, native frequency (they join the
  same global fill pool — no slot inflation, no odds skew beyond enlarging the
  pool).
- Picking one opens the standard dialogue UI: story text, speaker name from
  Ink tags, choices, and actions (`gold:50`, `addcard:...`, `combat:...`, …)
  firing through the game's own action handler.
- `unique: true` events never re-offer after being picked once (persisted in
  the save's `doneEvents`, same as vanilla uniques).
- `minLevel`/`maxLevel` gate which areas can roll the event.
- Boot report and status row count events like any other content kind; a
  failing event skips only itself.

## 3. Architecture

Mirrors the existing content families: **registration is declarative and
durable; injection happens on lifecycle hooks; one new patch serves stories.**

- **Registry.** New `EventRegistration` (spec + live `Dialogue` instance +
  `TextAsset`) beside cards/talents in `Core/Registry.cs`. Ledger kind
  `"event"` in `RegistrationLedger` — **name-keyed only, no numeric IDs**
  (events have none; no ID-REGISTRY impact). Collision namespace at
  `Register()`: other mods' event names + shipped `Dialogue` asset names +
  shipped dialogue TextAsset names (case-insensitive; §4 validator mirrors
  this offline).
- **The naming rule.** The engine sets `Dialogue.name == TextAsset.name ==
  event name`. Rationale: the area deck keys by `textFile.name`
  (AreaHandler.cs:508) while `eventLookupCache` keys by `Dialogue.name`
  (AssetManager.cs:317) — equality makes every lookup path agree, and makes
  `doneEvents` entries stable and human-readable.
- **Injection = phase 2 (world assets).** Events piggyback on the
  already-patched world hooks (`SetWorldAssetsLoaded` / `LoadWorldAssets`
  postfixes, `Core/InjectionEngine.cs:31-33` + `Core/PatchManager.cs:45-48`):
  prune registrations wiped by `ForceReloadAssets` (membership check against
  `allEvents`, same idiom as cards — InjectionEngine.cs:57-76), construct
  `ScriptableObject.CreateInstance<Dialogue>()` with `HideAndDontSave`, a
  `new TextAsset(json) { name }`, `eventType = opportunity`, filters from the
  spec, `eventConditions = new List<AreaCondition>()` (non-null — the P5
  landmine class), append to `allEvents`, then `AssetManager.RefreshCaches()`
  (rebuilds the event cache in phase 2, AssetManager.cs:1285-1288).
  Per-mod counts join the boot report.
- **Story serving.** Prefix on `DialogueManagerINK.StartDialogue(string)`: if
  the name matches a registered mod event, replicate the vanilla success
  wiring against our stored text and skip the original (details + fail-safe
  gating in §6). Vanilla names pass through untouched. Everything downstream —
  the dialogue loop, `>>>` actions, `EndDialogue`, node consumption — is
  vanilla code operating on a vanilla `Story`.
- **Data flow (pack path).** `pack.json` `events[]` entry → `storyFile`
  (compiled Ink JSON in the pack folder) read at scan time →
  `DawnKit.Events.Build(...)` through the public API — `DawnKit.Packs` stays a
  pure public-API consumer (no Assembly-CSharp reference).
- **C# API.** `DawnKit.Events.Build("Hello Wayfarer").StoryJson(text)` (or
  `.StoryFile(path)`) `.Levels(min, max).Unique().Register()` — validating at
  `Register()`: JSON parses, `inkVersion` ∈ [18, 20], every `>>>` action
  command in the dialogue vocabulary (embedded, like the effect DSL), name
  collisions. Same `RegisterResult` error model as the other builders.

## 4. Data file formats

`pack.json` gains an optional top-level `events` array. Presence requires
`"schemaVersion": 2` — older loaders must refuse the whole pack rather than
silently drop its events (SchemaGate semantics, `DawnKit.Packs/SchemaGate.cs`).

```jsonc
{
  "schemaVersion": 2,
  "pack": "My Pack",
  "events": [
    {
      "name": "Hello Wayfarer",          // event identity: Dialogue.name, TextAsset.name,
                                          //   doneEvents key. Unique vs shipped + all mods.
      "storyFile": "events/HelloWayfarer.ink.json", // compiled Ink JSON, pack-relative
      "minLevel": 0,                      // optional; Dialogue.minimumLevel (default 0)
      "maxLevel": 0,                      // optional; Dialogue.maxLevel (0 = uncapped)
      "unique": false                     // optional; persists into doneEvents when picked
    }
  ]
}
```

Pack folders ship **both** `events/<Name>.ink` (authoring source, committed)
and `events/<Name>.ink.json` (compiled, what the loader reads). Authoring
contract:

- Compile with inklecate **v1.0.0** (emits inkVersion 20; §1 pin). Inky
  releases bundling ink ≥ 1.1 produce inkVersion 21 and will be refused.
- Action lines: `>>>command:arg;command:arg` (`>>>>` to auto-continue), every
  command from `dialogue-action-commands.txt` (binding rule #3 — closed
  vocabulary). `goto:`/`STORYFUNCTION:` are engine-reserved words, validated
  structurally (knot must exist / function arity) rather than against the list.
- Speaker: tag lines with the display name (`Hello there. #A Wayfarer`) —
  renders inline with no localization entries (DialogueManagerINK.cs:584-593).
- End with `-> END` (a story that cannot continue closes the dialogue).

**Validator (gate 1, `validate_pack.py` + `pack.schema.json`).** Schema for
the `events` section; `storyFile` exists; JSON parses; `inkVersion` ∈ [18,20];
`>>>` lines extracted from the compiled JSON's `^`-strings and checked against
the vocabulary with did-you-mean; name collisions vs the extracted shipped
pool **by both Dialogue asset name and TextAsset name** (`tools/out/data/
Dialogue/` has the 148 Dialogue SOs; the extractor grows a
`dialogue-textfile-index` so both namespaces are scanned) and vs sibling
packs. Balance sim (gate 3) does not model events in v0.1 — reward-granting
actions are a sim open question (§11).

## 5. Knobs

- `[Events] Enabled` (bool, default `true`) — master switch; off = no event
  injection, no StartDialogue prefix behavior (vanilla untouched).
- Engine-wide `[Engine] Enabled` / `VerboseLogging` apply as everywhere.
  No other knobs in v0.1: frequency/odds knobs deliberately rejected for MVP
  (they change vanilla balance; §11).

## 6. Patch targets & integration points

| Target (verbatim, verified) | Kind | Why |
|---|---|---|
| `AssetManager.SetWorldAssetsLoaded` / `AssetManager.LoadWorldAssets` | postfix (**already patched** — PatchManager.cs:45-48) | extend the existing handler: inject events + `RefreshCaches()` before the resolver's final pass |
| `DialogueManagerINK.StartDialogue(string)` | prefix (**new**) | serve registered stories; `return false` for mod events, pass-through otherwise |

The prefix replicates, in order, the vanilla wiring for a served story
(StartDialogue preamble + `OnAssetLoaded` success branch,
DialogueManagerINK.cs:260-307): set `dialogueTemp` = name, `HidePortrait(0,0)`,
`SetDialogueRunning(true)`, `story = new Story(json)`, `dialogueName` = name,
`FadeUIIn()`, `EnableVisualDialogueUI()`, `areaUI.SetConversationUI(true)`,
`areaUI.HideAreaUI(0.25f)`, `blocksRaycasts = true`, `ProceedDialogue()`.

Non-patch private members (AccessTools, registered through PatchManager's
member-tracking so each logs "Target found:"/"Target MISSING:" —
PatchManager.cs:126-156): fields `dialogueTemp`, `story`, `dialogueName`,
`areaUI`; methods `HidePortrait`, `SetDialogueRunning`, `FadeUIIn`,
`EnableVisualDialogueUI`, `ProceedDialogue`. **Fail-safe:** if the prefix
target or ANY member is missing, the whole Events integration disables itself
at boot (events are not injected — a node whose story can't be served must
never reach the map; the abort path at DialogueManagerINK.cs:316-321 is not a
player-acceptable fallback).

## 7. Example starting dataset

`examples/ExampleEventPack/` — one data-only pack:

- `events/HelloWayfarer.ink` (+ compiled `.ink.json`, inkVersion 20): two
  choices; one runs `>>>>gold:50` then closes — the smallest story exercising
  text, a choice, an action, and END. (Compiled + string-verified in the
  2026-07-19 spike; `>>>>gold:50` survives compilation as a plain `^`-string.)
- `pack.json` (`schemaVersion: 2`) with the one `events` entry, `minLevel 0`,
  `maxLevel 0`, `unique false` — demonstrates the whole §4 surface.

The C# twin (an `Events.Build` call in `examples/MyFirstMod`) lands with the
API milestone (§10 E1).

## 8. Testing plan (<15 min, shipped example)

Offline: `dmk validate` green on the example pack; deliberate mutations red
(bad command `glod:50` with did-you-mean, inkVersion 21, name collision with
"Mimic"). `python -m pytest tools/tests` green (validator + vocabulary tests).

In-game:
1. Boot with the example pack. Log shows `Target found:` for
   `DialogueManagerINK.StartDialogue` + all members; boot report counts
   1 event, 0 errors.
2. Start a run (any class), enter the first area — verbose log confirms
   "Hello Wayfarer" entered the area-deck pool. Explore until an Opportunity
   node appears; if RNG withholds it, restart the area a couple of times
   (slots beyond the curated list draw from the global pool, §1).
3. Pick it: dialogue opens, speaker reads "A Wayfarer", both choices render.
   "Wave back" → gold +50 visible in the HUD → dialogue closes → node
   consumed. Run continues normally (save/continue round-trips).
4. Edge cases: pick the other choice (no action fires); set `unique: true`,
   pick once, verify it never re-offers in later areas of the same run and
   after continue; remove the pack mid-run, pick nothing — vanilla events
   unaffected; with `[Events] Enabled = false`, boot report shows 0 events and
   the map never offers the mod event.

## 9. Save considerations

- **Persistent residue:** `doneEvents` strings (textFile names) for picked
  unique events — inert after uninstall (filters simply never match;
  AreaHandler.cs:518). Human-readable names, stable across reinstalls (no
  numeric identity anywhere).
- **Mid-run uninstall:** an already-dealt area-deck entry for a missing mod
  event aborts cleanly at pickup (Addressables miss → Resources miss →
  `HandleDialogueFailure` closes the dialogue, DialogueManagerINK.cs:309-343);
  the run continues. Acceptable degradation, documented in the release notes'
  honest list.
- **Game updates:** `ForceReloadAssets` wipes `allEvents`
  (AssetManager.cs:262) → membership-prune + re-inject on the next world-load
  pass, the same durable-registration contract as cards (P3/P21).

## 10. Milestones

- **E1 — the events MVP (this spec):** `Events` registry + builder API +
  world-phase injection + `StartDialogue` prefix; `events` manifest section +
  schemaVersion 2; validator (schema, inkVersion, vocabulary, collisions) +
  extractor's dialogue-textfile index; `ExampleEventPack`; live QA per §8.
  Independently shippable as DawnKit 0.9.0.
- **E2 — authoring conveniences:** `dmk ink` compile step (vendored/fetched
  inklecate v1.0.0 — 24 MB, gitignored under `tools/bin/`), event entries in
  `dmk new` scaffolding, TUTORIAL chapter, C# example twin, optional
  `eventImage`/`extendedView` cosmetics if worth it.
- **E3 — beyond opportunities:** shrines (rarity + shrine display path,
  EventDisplay.cs:205-233), `eventConditions` exposure (quest-flag /
  class-gated events), possibly per-area curation. Each needs its own
  ground-truth pass.

## 11. Open questions

1. **Vocabulary risk tiers.** Some of the 99 action commands mutate meta
   state (`endrun`, quest flags, keystones). Whitelist-all like the effect
   DSL, or lint a "dangerous" tier at validate time?
2. **Sim stance (MOD-TOOLKIT #7 adjacent).** Events grant rewards outside the
   balance sim's model. v0.1 ships unsimmed (like vanilla generic events) —
   should BALANCE-REPORT at least list per-event max gold/cards granted?
3. **Locale interplay.** The prefix serves one inline-English story for every
   locale (vanilla loads per-locale exports). Consistent with the repo's
   English-inline stance — revisit only if pack localization lands.
4. **`STORYFUNCTION` support.** Requires Ink `EXTERNAL`/function definitions
   and the `imbueCost` replacement (DialogueManagerINK.cs:1311-1318). Reserved
   in v0.1 (validator rejects it) until a real use case appears.
5. **inklecate distribution.** Vendor the 24 MB binary in releases, fetch on
   first `dmk ink` run, or document-only? (License is MIT — vendoring is
   clean; size is the argument against.)
