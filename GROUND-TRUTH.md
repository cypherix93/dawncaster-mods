# Dawncaster Modding — Ground Truth

Everything in this document is verified against the decompiled game source in `decompiled/`
(decompiled 2026-07-17 from the Steam PC build with ilspycmd 10.1.1). No speculation; file
references point at the decompiled `.cs` files, which mirror runtime types 1:1.

## Environment

| Fact | Value |
|---|---|
| Game install | `E:\Games\Steam\steamapps\common\Dawncaster` (Steam app 3966890) |
| Developer / product | Wanderlost Interactive / Dawncaster (`Dawncaster_Data\app.info`) |
| Engine | Unity **2022.3.62f2**, URP, **Mono scripting backend** (not IL2CPP) |
| Game logic | `Dawncaster_Data\Managed\Assembly-CSharp.dll` (~2.6 MB, decompiles cleanly, 562 files) |
| Key libs shipped | Newtonsoft.Json, Ink runtime (`Ink-Libraries.dll`), SQLite (`Gilzoide.SqliteNet.dll`, used for achievements.db), UniTask, DOTween/LeanTween, TextMeshPro, Unity Localization, Addressables, Steamworks.NET |
| Persistent data | `%USERPROFILE%\AppData\LocalLow\Wanderlost Interactive\Dawncaster\` (saves, `Player.log`, `achievements.db`) |
| Official mod support | **None.** No Steam Workshop/UGC calls in game code, no mod folder scan, no external content loading. |
| Modding viability | **Excellent** — Mono backend means BepInEx 5.x (x64, Mono build) + HarmonyX runtime patching works out of the box, and all game state below is `public static`. |

## Big picture

Dawncaster is almost entirely **data-driven**. All content types are Unity `ScriptableObject`s
baked into `resources.assets` and loaded via `Resources.LoadAll("<Folder>", typeof(T))` into
**public static lists** on a static `AssetManager` class. Card/talent/event *behavior* is not
C# per card — it is a string DSL (`codeLine`) interpreted at runtime by `SpellEffects`.
Narrative events are compiled **Ink** stories with an action-tag bridge into the same DSL world.

Consequence: adding new content = create ScriptableObject instances at runtime (BepInEx plugin),
fill in public fields (including DSL strings), append to `AssetManager` lists, refresh caches.
No asset-bundle surgery required except for art (plain PNG → `Sprite` works).

## Content types and where they load from

`AssetManager` (`decompiled/AssetManager.cs`) loads everything in `LoadPlayerAssets()` /
`LoadWorldAssets()`:

| Resources folder | Type | Static list on `AssetManager` | Lookup |
|---|---|---|---|
| `Cards` | `Card` | `allCards`, `playercards`, `metacards` | `GetCard(string name)`, `GetCard(int id)` — via `cardLookupCache` (name, lowercase) and `cardIdLookupCache` |
| `Talents` | `Talent` | `allTalents` | `GetTalent(int)`, `GetTalent(string)` |
| `Classes` | `Profession` | `allClasses` | `GetClass(string)` |
| `Areas` | `Area` | `allAreas` (filtered by `currentExpansions`) | `GetArea(...)`, `GetAreaByLevel(int)` |
| `Enemies` | `Battle` | `allBattles` | `GetBattle(string)` |
| `Status` | `StatusEffect` | `allStatusEffects` | `GetStatus(string)` |
| `Events` | `Dialogue` | `allEvents` | `GetEvent(string)` |
| `Characters` | `NPC` | `allNPCs` | `GetCharacter(string)` |
| `Invasions` | `Invasion` | `allInvasions` | `GetInvasion(int)` |
| `Affixes` | `Affix` | `allAffixes` | `GetAffix(...)` |
| `Bounties` | `Bounty` | `allBounties` (lazy) | `GetBounty(string)` |
| `Cosmetics` | `Card` (alt art) | replaces cards in-place | `GetCosmeticCard(string)` |
| `Companions` | `Companion` | (loaded on demand) | `GetCompanion(string)` |
| `Dialogue` | `Dialogue` | (loaded on demand) | `GetTargetDialogue(string)` |
| `Products`, `ScoreRanks`, `BattleAnimations` | misc | — | — |

### Injection points (all public — no Harmony needed for the core loop)

- `AssetManager.allCards` / `playercards` / `metacards` / `allTalents` / `allEvents` /
  `allStatusEffects` / ... are **`public static List<T>`** — append directly.
- `AssetManager.RefreshCaches()` — public; rebuilds all name/ID lookup dictionaries.
- `AssetManager.CreateRunLists()` — public; rebuilds `runcards`/`runtalents` (the per-run pools,
  filtered by `PlayerHandler.thePlayerData.excludedsets` and `bannedCards`).
- `AssetManager.AddCardsToCollection(Expansions)` (internal) is the game's own late-add pattern —
  a good template for what a mod loader must do (add to lists + both caches + `CreateRunLists()`).
- Timing: the game loads assets on the loading screen (`[LoadAssets]` phases in Player.log:
  classes → cards → talents → areas → world). A mod should inject **after**
  `CreateCardCollectionsAsync` finishes — easiest via a Harmony postfix on
  `AssetManager.SetPlayerAssetsLoaded()` / `SetAssetsLoaded()`, or by patching
  `BuildCardLookupCaches` (private → Harmony). Version note: `LoadAllAssets()` re-runs and
  clears everything when `Application.version`/`buildGUID` changes (`ForceReloadAssets()` calls
  `ClearAllCollections()`), so re-inject on every load pass, not just once per process.

## Card data model (`decompiled/Card.cs`)

`Card : ScriptableObject`, created in-editor via `[CreateAssetMenu(menuName = "DT Create/Cards")]`.
The card's **display name comes from the ScriptableObject's `name`** (`base.name`), with
localization overlay (see Localization). All fields are public:

- `int cardID` — unique int; caches key on it. Runtime IDs randomly assigned by editor tooling
  (`IDDatabase` picks `Random.Range(0, int.MaxValue)`), so any unused int works for mods.
- `string cardDescription` — inline English text (localization fallback).
- `AssetManager.CardExpansions cardexpansion` — enum: `None, Core, Metaprogress, Metamorphosis,
  Extended, Infinitum, Catalyst, Eclypse, Synthesis`. `None` = excluded from player pool;
  `Metaprogress` = routed to `metacards`.
- `CardType cardType` — `Melee, Magic, Ranged, Utility, Divine, Move, Corruption, Monster`.
- `CardCategory cardCategory` — `Action, Item, Artifact, Conjuration, Enchantment, BasicAttack,
  Summon, Performance, Form, Hymn, Revelation, Affix, Attunement, Equipment, Code, Ingredient,
  Path, Location, Offering, Mantra, Adaptation`.
- `Suffix cardSuffix` — `None, Fire, Frost, Lightning, Phyisical (sic), Ebb, Flow, Infernal,
  Companion, Junk`.
- `CardRariry (sic) cardRarity` — `Common, Uncommon, Rare, Legendary, Monster, Epic, Mythic, Ultra`.
- `Sprite artwork`, `AudioClip audioClip`, `Color frameColorOverwrite`,
  `ColorOverwrite colorCard` (frame color; auto-derived from costs when `None` — see `GetColor()`).
- Costs: `costDEX, costINT, costSTR, costNeutral, costHOLY, costDEXINT, costDEXSTR, costINTSTR,
  costLife` (ints; `GetCostArray()` order: DEX, INT, STR, HOLY, DEXINT, DEXSTR, INTSTR, Neutral, Life).
- `List<CardProperties> keywords` — engine keywords enum (`Chain, Cursed, Conjured, Defiled, Echo,
  Figmented, Firecast, Grounded, Heavy, Lifedrain, Memorized, OneUse, Piercing, Persistent,
  Psionic, Reliable, Recall, Reaping, Rampage, Scrap, Sinister, Unique, Unplayable, Untempered,
  Valuable` — explicit numeric values, non-contiguous).
- `List<string> cardKeywords` — free-form string tags (e.g. `"potion"`) used by pools/filters.
- `string utilityNumber`, `int charges`, `int adaptationSlots`.
- Flags: `uniqueInHand, canBeAcquired, hideConditionGlow, resetTempValues, cullLastWordFromName,
  overwriteUpgradable, cantbeupgraded, requireAllConditions, pauseQueue, excludeFromConjurations,
  excludeFromSunforge, excludeFromRewards, excludeFromCodex`.
- `List<Condition> playConditions` — gate on playing the card.
- `List<CardEffect> CardEffectList` — **the card's behavior** (see Effect DSL).
- `Enchantment CardEnchantments` — enchantment payload (see below).
- `AssetManager.Expansions cosmeticexpansion` — cosmetic/alt-art set gating; `string designedBy`.

At runtime a deck entry is a `CardContainer` (mutable copy: cost array, keywords, charges,
upgrades...) built by `CardHandler.CreateCardContainer(Card)` (`decompiled/CardHandler.cs`).
Mods never construct containers manually for collection content — the game does it.

### Effect DSL — `CardEffect` (`decompiled/CardEffect.cs`)

```csharp
public class CardEffect {            // [Serializable] plain class
    EventHandler.GameTriggers cardTrigger = PlayAction;  // when it fires
    string codeLine;                 // WHAT it does — the DSL
    bool forecast = true;            // show in damage forecast
    Card[] referenceCard;            // cards referenced by ref-type commands
    bool hideReferenceCards;
    StatusEffect referenceStatus;    // status referenced by status commands
    List<Condition> effectConditions;
}
```

- **Syntax**: `codeLine` = statements separated by `;`, each statement = tokens separated by `:`
  → `command:arg:arg...`. Examples from source: `conjure:potion:1:1`, `damage:X`.
  Parsing: `SpellEffects.RunEffect` splits on `;`, `PlayEffect` runs
  `GameTextTranslator.FormatActionLine` (placeholder substitution — e.g. `[[enchantmentStacks]]`,
  card value tokens) then splits on `:` and dispatches on token[0].
- **Interpreter**: `decompiled/SpellEffects.cs` (~460 KB). **565 distinct commands** — full
  vocabulary extracted to `reference/effect-commands.txt`. Includes damage/heal/draw/conjure/
  status/economy/deck-manipulation/etc. Status names (`Poison`, `Burning`, `Armor`, ...) are
  themselves case labels resolved via `AssetManager.GetStatus`.
- **Triggers**: `EventHandler.GameTriggers` (`decompiled/EventHandler.cs`) — ~90 values
  (`PlayAction`, `Draw`, `Kill`, `StartCombat`, `StartPhase`...`InflictStatusDamage`). A card is
  reactive by giving it effects on non-`PlayAction` triggers; `EventHandler.FindTriggeredEvents`
  scans decks/statuses for matching triggers.
- **Public entry point**: `SpellEffects.RunStaticEffect(string codeLine, CardContainer cc = null,
  Card[] referenceCard = null, StatusEffect referenceStatus = null, LastingEffectContainer les =
  null)` — static, public. A mod dev-console can execute arbitrary effect lines with this.

### Conditions (`decompiled/Condition.cs`, `decompiled/ConditionChecker.cs`)

```csharp
public class Condition {  // [Serializable]
    ConditionChecker.ConditionValue valueToCheck;   // WHAT to inspect (~118 values)
    ConditionChecker.ConditionOperator conditonOperator; // IsLessThan, IsMoreThan, Is, IsNot,
                                                         // IsHigherPercent, IsLowerPercent, IsChance
    string targetValue;      // compared against (goes through FormatActionLine, so supports tokens)
    bool ignoreForDisplay;
}
```

`ConditionValue` covers health, cards played by type/category, statuses, deck contents, phase,
enemy identity, quest flags, talent ownership, etc. Evaluation:
`ConditionChecker.CheckAllConditions(...)` — note it is effectively AND across the list
(any failure breaks). `Card.requireAllConditions` toggles related display/evaluation behavior on
the card level.

### Enchantment (`decompiled/Enchantment.cs`)

`Enchantment : LastingEffect` — `enchantmentText`, `combatEnchantment`, `showstacks`, `showIcon`,
`playCardSound`, and its own `List<CardEffect>`. `LastingEffect.EffectType` enum: `blessing,
affliction, other, performance, form, ammo, totem, attunement, stance, equipment, code, affix,
path, location, mantra, hymn`.

## Talents / passives (`decompiled/Talent.cs`)

`Talent : ScriptableObject` (`menuName = "DT Create/Talent"`): `int ID`,
`CardExpansions expansion`, `description`/`flavortext` (inline text + localization overlay),
`Sprite powerImage`, `int tier` (0–6), `List<string> keywords`,
**`List<CardEffect> effectList`** (same DSL as cards), `int cooldown`, flags
(`unique, storyTalent, excludeFromRandom, excludeFromSunforge, excludeFromCodex,
infernalOffering`), prerequisites (`List<Talent> requiredTalents`,
`List<Profession> requiredProfessions`, `rDEX/rINT/rSTR` stat requirements).

So: **new passives = new Talent assets** — identical injection path
(`allTalents` + `BuildTalentLookupCache` via `RefreshCaches()` + `CreateRunLists()` for
`runtalents`). Handling/selection logic lives in `TalentHandler.cs` (70 KB).

## Events / opportunities (Ink)

Two-layer system:

1. **`AreaEvent : ScriptableObject`** (`decompiled/AreaEvent.cs`, `menuName = "DT Create/AreaEvent"`)
   — map-node metadata: `AreaHandler.EventTypes eventType` (`npc, combat, opportunity, quest,
   boss, portal, deadend, campfire, exploration, miniboss, shrine, merchant, none`), name/description
   (+ localization overlay in table `"Events"`), `Sprite eventImage`/`overlayImage`,
   `List<AreaCondition> eventConditions`, `Card aura`, misc flags.
2. **`Dialogue : AreaEvent`** (`decompiled/Dialogue.cs`, `menuName = "DT Create/Dialogue"`) — adds
   **`TextAsset textFile`** = *compiled Ink JSON*, plus selection filters: `CardRariry rarity`,
   `int minimumLevel`, `int maxLevel`, `bool unique`.

Runtime: `DialogueManagerINK` (`decompiled/DialogueManagerINK.cs`) builds `Ink.Runtime.Story`
from the TextAsset. The **Ink → game bridge** (`RunDialogueAction`, line ~1264): any story line
containing `>>>` (or `>>>>` to auto-continue) is stripped and treated as `;`-separated
`command:arg` actions:
- `goto:<knot>` → `story.ChoosePathString`
- `STORYFUNCTION:<fn>:<arg>` → `story.EvaluateFunction`
- everything else → `DialogueActionHandler.RunActionCode(string[])`
  (`decompiled/DialogueActionHandler.cs`, public) — **99 commands**, extracted to
  `reference/dialogue-action-commands.txt` (`addcard`, `combat`, `gold`, `heal`, `merchant`,
  `questflag`, `addtalent`, `endrun`, ...).

**New opportunity events are fully feasible**: write `.ink` → compile with `inklecate` to JSON →
wrap in a `TextAsset` + `ScriptableObject.CreateInstance<Dialogue>()` (set
`eventType = opportunity`, level range, rarity) → append to `AssetManager.allEvents` →
`RefreshCaches()`. Event pickup is by `AreaHandler`/`AreaUI` filtering `allEvents` on
`eventType`, level range, conditions.

## Other content types (verified shapes)

- **`StatusEffect : ScriptableObject`** (`decompiled/StatusEffect.cs`) — wraps a `Status theStatus`
  (see `Status.cs` for its trigger/effect payload); names/descriptions from localization table
  `"Keywords"` keyed by asset name (`<name>_N` / `<name>_D`) — new statuses therefore DO need
  localization entries (or a Harmony patch on the getters) since there's no inline fallback here.
- **`Profession : ScriptableObject`** (classes; `decompiled/Profession.cs`) — `weapons`,
  `talents`, `startingCards` (all direct asset refs), `dexterity/intellect/strength`, visuals.
- **`NPC : ScriptableObject`** — `idname` (lookup key), names, portrait/icon, faction enum.
- **`Battle`** (enemies), **`Area`**, **`Affix`**, **`Bounty`**, **`Invasion`**, **`Keystone`**
  (meta-progress cards/weapons; `AddMetaCards()` pulls from `KeystoneManager`).

## Localization (`decompiled/DawncasterLocalisation/LocalizationHelper.cs`)

Unity Localization string tables (Addressables bundles under `StreamingAssets\aa`). Key pattern:
- Cards: table `"Cards"`, keys `{cardID}_N` (name), `{cardID}_D` (description), `{cardID}_E`
  (enchantment text). Card type words: table `"Cardwords"`.
- Talents: `"Talents"`, `{ID}_N/_D/_F`. Events: `"Events"`, `{assetName}_N/_D`. Battles:
  `"Battles"`. Statuses/keywords: `"Keywords"`, `{assetName}_N/_D`. Classes: `"Professions"`.

`GetLocalValue(table, key, fallback)` **returns the `fallback` argument** when the key is missing
(after trying current locale then English). Card/talent/event getters pass their inline field or
`base.name` as fallback ⇒ **custom cards/talents/events work with inline English text and no
localization table changes**. (StatusEffect getters pass no fallback — see above.)

## Dev/debug surfaces worth reusing

- `CardsToTxtOutput` — dumps card data to text (dev tool left in build).
- `CardTester`, `TalentTester` — dev MonoBehaviours.
- `SpellEffects.RunStaticEffect(...)` — run any DSL line ad hoc.
- Verbose `Debug.Log` everywhere → `Player.log` is an excellent mod-dev feedback loop
  (`[LoadAssets]`, `[SPELLQUEUECHECK]`, cosmetic debug, etc.).
- `AssetManager.SetDebugReport(true)` — extra load reporting.

## Feasibility summary

| Goal | Verdict | Path |
|---|---|---|
| **New cards** | ✅ Easy | `CreateInstance<Card>()`, set fields + `codeLine` effects, PNG→`Sprite` art, append to `allCards`/`playercards`, `RefreshCaches()` + `CreateRunLists()` |
| **New talents/passives** | ✅ Easy | Same via `allTalents`; respects class/stat requirements natively |
| **New opportunity events** | ✅ Moderate | Ink (`inklecate`) → `TextAsset` → `CreateInstance<Dialogue>()` → `allEvents`; `>>>` action lines drive 99 game commands |
| **New statuses/keywords** | ✅ Moderate | `CreateInstance<StatusEffect>()`; needs localization patch for name/desc (no inline fallback) |
| **New enchantments** | ✅ Easy | Serializable payload on cards (`CardEnchantments`) |
| **New classes** | 🟡 Harder | `Profession` asset is easy; UI (class select screens, reward tracks) assumes fixed roster — needs UI patching |
| **New effect commands** (beyond the 565) | 🟡 Harder | Harmony prefix on the `SpellEffects` dispatch to add cases |
| **New expansions as first-class sets** | 🟡 Harder | `CardExpansions`/`Expansions` are enums; reuse an existing member or patch enum handling |
| **Custom card art** | ✅ Easy | `Texture2D.LoadImage(File.ReadAllBytes(png))` → `Sprite.Create` at runtime |

## Watch-outs

- `cardID` collisions: existing IDs occupy random ints; pick from a reserved high range and check
  `AssetManager.GetCard(int)` for collisions at inject time.
- Card name is the SO `name` (lowercased as cache key) — must be unique vs. existing cards.
- Save data references cards by ID/name (`CardContainer.theCardID`; run history) — removing a mod
  mid-run will break that run's deck; content mods should be additive and stable-ID.
- `ForceReloadAssets()` wipes injected content (list `.Clear()`); hook re-injection.
- Achievements/cloud: `Unity.Services.CloudSave` + Steam stats are live; keep modded runs off
  leaderboards where relevant (there's `Leaderboards/` code in the decompile).
- Enum-typed fields (`CardExpansions`, `CardProperties`, ...) limit *new* members without patching;
  string-typed `cardKeywords` + DSL are the flexible extension surface.
- EULA: no official mod policy found in the distributable; runtime-injection (no game-file edits,
  no asset redistribution) is the defensible approach.

## Recommended next spikes

1. **Bootstrap**: install BepInEx 5.4.x (x64 Mono) into the game dir; confirm plugin loads and
   `Player.log`/BepInEx console shows game types accessible.
2. **Hello-card**: Harmony postfix after asset load → inject one card (clone an existing simple
   card's effect line, e.g. a `damage:6` melee action) → verify it appears in Codex / can be
   `addcardtodeck`'d via `SpellEffects.RunStaticEffect`.
3. **JSON card loader**: define a JSON schema mirroring `Card` fields (Newtonsoft is shipped) and
   load a `mods/cards/*.json` folder.
4. **Hello-event**: compile a 5-line Ink story, inject as `opportunity` Dialogue, confirm it
   appears on the map and its `>>>gold:50` action fires.
5. Dump all existing cards via reflection (or reactivate `CardsToTxtOutput`) into
   `reference/cards-dump/` for concrete `codeLine` examples per command.
