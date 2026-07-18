# DawnKit public API reference (engine 0.7.0)

Terse reference for the C# surface of `DawnKit.dll`. Tutorials live in the
examples ([examples/MyFirstMod](examples/MyFirstMod/README.md) — C#,
[examples/ExamplePack](examples/ExamplePack/README.md) — data-only); the data
contracts in [CARD-PACK-SPEC.md](CARD-PACK-SPEC.md) / [WEAPON-SPEC.md](WEAPON-SPEC.md);
the engine design contract in [SPEC.md](SPEC.md); build/install/config walkthrough
in [src/README.md](src/README.md). Everything below is implemented in
`src/DawnKit/` — the code is the source of truth.

## 1. Assemblies

| Assembly | Plugin GUID | You reference it when |
|---|---|---|
| `DawnKit.dll` | `dcmods.dawnkit` | Writing a C# content mod. The only DLL you reference (`Private=false`); owns all Harmony patches. |
| `DawnKit.Packs.dll` | `dcmods.dawnkit.packs` | Never — it's the bundled data client that loads `pack.json` folders through this same API. |

Client mod skeleton (see MyFirstMod for the complete compiling project):

```csharp
[BepInPlugin("com.example.mymod", "My Mod", "1.0.0")]
[BepInDependency(DawnKit.DawnKitPlugin.Guid)]   // "dcmods.dawnkit"
public sealed class MyMod : BaseUnityPlugin
{
    private void Awake() { /* register everything here */ }
}
```

Client mods reference no `Assembly-CSharp` and no Harmony. Csproj references:
`DawnKit.dll` (HintPath), `BepInEx.dll`, `UnityEngine.dll` +
`UnityEngine.CoreModule.dll` — all `Private=false`.

## 2. Lifecycle contract

**Register in `Awake()`.** Registration is declarative and durable — the
engine records it and:

1. constructs + injects at **phase 1** (player assets loaded), then refreshes
   caches and run lists (`AssetManager.RefreshCaches`, `CreateRunLists`);
2. resolves name-declared references (`EffectSpec.ReferenceStatus` /
   `ReferenceCards`) at **phase 2** (world assets loaded — statuses don't
   exist earlier);
3. **re-applies automatically** after the game's version-change asset wipe
   (`ForceReloadAssets`) — pruned by list membership, rebuilt idempotently by
   ID/name;
4. re-attaches weapons/powers to freshly reloaded `Profession` assets (keyed
   by ID/name, never object identity).

Clients never observe phases, hooks, or reloads, and never call
`AssetManager.GetStatus`/`GetCard` themselves. Registering after boot is
applied on the next injection pass (next asset reload) — supported, not
recommended.

## 3. Registries

| Registry | Builds | `.All` returns |
|---|---|---|
| `DawnKit.Sets` | `Register(...)` → `SetHandle` | `IReadOnlyList<SetHandle>` (registration order) |
| `DawnKit.Cards` | `Build(name)` → `CardBuilder` | `IReadOnlyList<RegistrationInfo>` of kind `"card"` |
| `DawnKit.Weapons` | `Build(name)` → `WeaponBuilder` | kind `"weapon"` |
| `DawnKit.WeaponPowers` | `Build(name)` → `WeaponPowerBuilder` | kind `"weaponPower"` |

`RegistrationInfo` (one row per Register() **attempt**, including failed
ones): `Owner`, `Kind` (`"card"`/`"weapon"`/`"weaponPower"`/`"set"`), `Id`,
`Name`, `Ok`, `Error`.

ID/name spaces: cards and weapons share the cardID + card-name space; weapon
powers (talents) have their own talentID + talent-name space; sets have their
own value space. **Names are identity** and compared case-insensitively
everywhere.

### 3.1 `Sets`

```csharp
SetHandle Sets.Register(string displayName, string author)          // AutoId block (§7)
SetHandle Sets.Register(string displayName, long idBlockStart,
                        string author = null)                       // explicit block
string    Sets.FindDisplayName(int setValue)                        // null if unknown
bool      Sets.TryParseNativeExpansion(string raw, out string canonicalName)
```

A registered set surfaces as its own named, colored, toggleable row (with
eye-icon preview) in the run-settings and Sunforge set screens, like official
sets; toggling it never affects run bonuses and Sunforge reroll math stays
correct. **Returns `null` on refusal** (empty name/author; explicit block
outside 700,000,000–799,999,999 or not aligned to 100; block or set value
already owned by a different mod — error logged with both owners named).
Passing a null `SetHandle` to `.InSet(...)` is harmless (treated as no set),
so the fail-safe pattern is: fall back to a native `.Expansion(...)` and ship
without a set row. Re-registering the same displayName on the same block is
idempotent (returns the existing handle).

`SetHandle`: `DisplayName`, `Author`, `SetValue` (synthetic
`CardExpansions` int: `1000 + (idBlockStart − 700,000,000)/100`),
`IdBlockStart`.

### 3.2 Card / weapon builders

`Cards.Build(name)` → `CardBuilder`; `Weapons.Build(name)` → `WeaponBuilder`
(same surface plus `.ForClasses`). All setters return the builder. String
overloads take the **game's exact enum spellings** (data-path parity); typed
overloads take the clean-spelled mirror enums (§6). Everything validates at
`Register()` (§5).

| Method | Semantics |
|---|---|
| `.Owner(string)` | Ownership string for logs/ledger/conflicts. Default: resolved from the call stack — the calling plugin's BepInEx GUID when its chainloader entry is complete, else the plugin's assembly name (the usual result for Awake-time registration). |
| `.Id(int)` | Explicit cardID from your registered block ([docs/ID-REGISTRY.md](../docs/ID-REGISTRY.md)). Mutually exclusive with `.AutoId()`. |
| `.AutoId()` | Next ID from the mod's deterministic block (§7): the set's block when combined with `.InSet`, else the block hashed from the owner string (requires `.Owner("author/ModName")` in that case). Cards bottom-up, weapons top-down. |
| `.InSet(SetHandle)` | Membership in a registered mod set — the card carries its synthetic expansion. Wins over `.Expansion(...)`. |
| `.Expansion(string \| Expansion)` | Native `AssetManager.CardExpansions` member. A card with neither `.InSet` nor `.Expansion` fails ("missing expansion"). `None`/`Metaprogress` never reach the player pool (game-side `ProcessCard` filter). |
| `.Type(string \| CardType)` | Required. `Card.CardType` member. |
| `.Category(string \| CardCategory)` | Required. `Card.CardCategory` member. Weapons: must be `BasicAttack` (enforced). |
| `.Rarity(string \| Rarity)` | Required. Game type `Card.CardRariry`. |
| `.Suffix(string \| Suffix)` | Optional; unset = `None`. Explicit null/empty string fails (manifest parity). `Suffix.Physical` maps to the game's `Phyisical`. |
| `.Cost(key, value)` / `.Costs(dict)` | Additive. Keys (whitelist): `DEX INT STR HOLY Neutral DEXINT DEXSTR INTSTR Life`; unknown key fails the item. |
| `.Description(string)` | Card text. |
| `.UtilityNumber(string)` / `.Charges(int)` | As in the manifest. |
| `.Keyword(string)` / `.Keywords(seq)` | `Card.CardProperties` member names (validated enums). |
| `.CardKeywords(seq)` | Free-text keyword strings (`Card.cardKeywords`). |
| `.Flag(string)` / `.Flags(seq)` | Boolean card flags, whitelisted: `uniqueInHand canBeAcquired hideConditionGlow resetTempValues cullLastWordFromName overwriteUpgradable cantbeupgraded requireAllConditions pauseQueue excludeFromConjurations excludeFromSunforge excludeFromRewards excludeFromCodex`. Unknown flag fails the item. |
| `.PlayCondition(s)` / `.Effect(s)` / `.Enchantment(...)` | Spec objects (§4), or the `.Effect(Trigger, codeLine, configure?)` shorthand. |
| `.Art(absolutePngPath)` | 512×512 PNG → Sprite (100 PPU, Clamp). Missing/unloadable file falls back to the generated cost-colored placeholder — never fails the item. |
| `.CodexDiscovery(bool)` | Mark discovered in the Codex (in-memory only; default true). |
| `.Register()` | Runs all validation; returns `RegisterResult` (§5). |

`WeaponBuilder` extras: `.ForClasses(params string[] | IEnumerable<string>)` —
exact `Profession` asset names; `"all"` = every class. Unknown class name
skips **that attachment only** at injection, never the weapon. Weapon
structural conventions enforced by the engine: category `BasicAttack`,
`excludeFromRewards` forced true, registered in `allCards` only (offered at
character creation, never in reward pools).

### 3.3 Weapon power builder

`WeaponPowers.Build(name)` → `WeaponPowerBuilder`. A weapon power is a tier-0
`Talent` with an `ActivateWeapon`-triggered payload; the engine forces the
structural conventions (tier 0, `unique`, empty
`requiredTalents`/`requiredProfessions`).

| Method | Semantics |
|---|---|
| `.Owner` / `.Id(talentID)` / `.AutoId()` / `.InSet(set)` | As above, in the **talent ID space** (talent IDs reuse the block's numbers, allocated top-down from the block end). |
| `.Expansion(string \| Expansion)` | Optional; default `Core` (or the set's expansion via `.InSet`). |
| `.Description` / `.Flavortext` / `.Cooldown(turns)` / `.Keywords(seq)` | Talent display/behavior fields. Runtime cooldown changes are floor-clamped to 1 by the engine. |
| `.Requirements(rDEX, rINT, rSTR)` | Attribute display gating at character creation only. |
| `.Effect(...)` / `.Effects(...)` | Same spec objects; codeLines validate against the **talent union vocabulary** (effect ∪ talent commands, 660 total). |
| `.ForClasses(...)` | As for weapons. |
| `.Art(absolutePngPath)` | 512×512 `powerImage` PNG; white two-band placeholder fallback. |
| `.Register()` | → `RegisterResult`. |

## 4. Spec types

```csharp
new EffectSpec(Trigger.PlayAction, "damage:6")   // typed ctor, or object-init:
new EffectSpec {
    Trigger = "PlayAction",       // EventHandler.GameTriggers member name (game spelling)
    CodeLine = "damage:6",        // effect DSL; every command validated
    Forecast = true,              // default true
    ReferenceStatus = "Burning",  // StatusEffect asset NAME — engine resolves phase 2
    ReferenceCards = new List<string> { ... },  // card names, index-aligned ref slots
    HideReferenceCards = false,
    Conditions = new List<ConditionSpec> { ... },
}
new ConditionSpec { Value = "StatusOnOpponent", Op = "Is", Target = "bleeding",
                    IgnoreForDisplay = false }   // ConditionChecker enum member names
new EnchantmentSpec { Type = "...",              // LastingEffect.EffectType member name
                      Text = "...", Combat = false, ShowStacks = true,
                      Effects = ... }
```

References are declared by asset **name** and resolved by the engine at the
correct load phase — an unresolved name is logged (and listed in the
diagnostics dump), never a crash.

## 5. Validation at `Register()` — the error model

`Register()` runs every check now and returns:

```csharp
public sealed class RegisterResult {
    bool Ok; string Error;               // Error null when Ok
    string Kind; string Owner; string Name;
}
```

**Always check it** — the canonical idiom:

```csharp
var r = Cards.Build("Glacial Lance")...Register();
if (!r.Ok) Logger.LogError($"[MyMod] Glacial Lance refused: {r.Error}");
```

Rules:

- **Per-item isolation**: one failed item is skipped with one named error
  (first failure wins); your other items, other mods, and the game load on.
  Nothing ever throws out of `Register()`.
- Every attempt (ok or failed) is retained in the ownership ledger — failures
  are named again in the boot report, the in-game status row, and the
  `DiagnosticsDump` file, so an unchecked result still isn't silent.
- Checks, in order: name + ID present → AutoId allocation (block claim /
  exhaustion) → enum membership → codeLine vocabulary (per `;`-statement,
  command = text before the first `:`; 565 effect commands for cards, 660
  talent-union for powers) → cost-key whitelist → flag whitelist → weapon
  `BasicAttack` requirement → cross-mod ID/name conflict vs every other
  registered mod → shipped-pool ID/name conflict (once pools are loaded;
  re-checked at injection either way).
- **Enum parsing** (string setters): exact match first, then
  case-insensitive with a logged warning ("only matched … case-insensitively
  — fix the manifest spelling"). No match → error with **did-you-mean**
  suggestions (case fix > prefix > Levenshtein ≤ 2, top 3 — same rule as
  `tools/validate_pack.py`). Typed enum overloads always produce exact game
  spellings, so they never warn.
- Conflict errors name **both claimants**, e.g.
  `MyMod/Glacial Lance: cardID 700000100 already owned by VenomousLegacy (card "Weeping Blade")`.

## 6. Enums — clean-spelled mirrors

The C# API is clean-spelled **only**: `CardType`, `CardCategory`, `Suffix`,
`Rarity`, `Expansion`, `Trigger` in namespace `DawnKit` mirror the game enums
1:1 in member order, with corrected spellings mapped internally to the game's
canonical typos:

| DawnKit | Game |
|---|---|
| `Rarity` (type name) | `Card.CardRariry` |
| `Suffix.Physical` | `Card.Suffix.Phyisical` |

Every other member name is identical. String setters and data packs use the
game's exact spellings (1:1 traceability rule) — the engine warn-and-accepts
case slips there, never in the typed path.

## 7. AutoId — deterministic ID blocks

```
block = 700,000,000 + (FNV1a32(utf8(lower(author + "/" + modName))) % 1,000,000) × 100
```

FNV-1a 32-bit, offset 2166136261, prime 16777619. 1,000,000 blocks × 100 IDs
inside the verified-free mod range 700,000,000–799,999,999. The synthetic set
value follows: `1000 + (block − 700M)/100`.

- **Determinism is a guarantee**: same owner string → same block on every
  machine, forever. Set values and save data depend on it — never rename your
  author/mod pair after shipping.
- Allocation within a block, in registration order: cards bottom-up from the
  block start; weapons top-down from the block end; talents top-down in their
  own ID space. Exhaustion (cards meet weapons, or 100 talents) fails the item
  with a named error.
- **Hard refusal, no probing**: a block already claimed by a different owner
  string (another AutoId mod, or an explicit `Sets.Register(name,
  idBlockStart)` claim) refuses the registration with both owners named. The
  engine never drifts to an adjacent block — that would silently break set
  values and saves. Every refusal carries the remedy: register an explicit
  free block.
- The Python twin (`tools/gamedata.py: autoid_block()`) computes the same
  blocks offline for data packs; both implementations pin the same 5
  reference vectors (`tools/tests/test_autoid.py`; boot logs
  `AutoId self-check: 5/5 reference vectors OK.` — an error here means
  determinism is broken, don't trust auto blocks that session).

**Explicit blocks instead of AutoId when**: your mod needs more than 100 IDs,
you hit a (astronomically rare) hash collision, or the block is already
allocated in [docs/ID-REGISTRY.md](../docs/ID-REGISTRY.md) (this repo's packs).
Then: `Sets.Register(name, idBlockStart: 7xx_xxx_x00, author: ...)` +
`.Id(...)` per item (or `.InSet(set).AutoId()` — AutoId allocates from an
explicit set's block, too).

## 8. Ownership, conflicts, status

- Every `Register()` records `(owner, kind, id, name, ok, error)` in the
  ledger. Owner = explicit `.Owner(...)`, else resolved from the calling
  plugin (GUID or assembly name — see §3.2).
- After each injection pass the engine logs one consolidated, deduplicated
  **boot report** block: `N mods · M items registered, K applied, F failed ·
  C conflicts`, per-mod lines, and every conflict with both claimants.
- **Status row** (player-facing): with any mod content registered, the
  run-settings set screen appends one non-interactive row —
  `DawnKit: 4 mods, 63 items loaded`, plus `— K errors, see
  BepInEx/LogOutput.log` when anything failed.
- **`[Engine] DiagnosticsDump = true`** writes
  `BepInEx/DawnKit-diagnostics.txt` at boot completion (overwritten each
  boot): per-mod items with IDs/names/sets/classes and applied/FAILED status,
  the conflict list, unresolved references. The bug-report channel — attach it
  to issue reports.
- **`Mods.ReportFailedMod(owner, error)`** (M2): the seam for failures that
  happen before any item can be registered — e.g. `DawnKit.Packs` refusing a
  whole pack over a newer-than-supported manifest `schemaVersion` (a pack.json
  declaring `schemaVersion` above the loader's supported value — currently 1;
  absent means 1 — is refused entirely, with the remedy "update DawnKit.Packs"
  in the log error). The reported mod appears in the boot report as
  `<owner>: 1 registered, 0 applied, 1 failed`, counts into the status row's
  error count, and its reason shows in the DiagnosticsDump.

## 9. Knobs

`BepInEx\config\dcmods.dawnkit.cfg` (engine):

| Key | Default | Meaning |
|---|---|---|
| `Engine.Enabled` | `true` | Master switch (fail-safe rule). `false` = no patches, no injection — completely vanilla. |
| `Engine.VerboseLogging` | `false` | Decision-level Debug logs (per-item routing, ref-resolution detail, class counts). |
| `Engine.DiagnosticsDump` | `false` | Write `BepInEx\DawnKit-diagnostics.txt` at boot completion (§8). |

`BepInEx\config\dcmods.dawnkit.packs.cfg` (data client):

| Key | Default | Meaning |
|---|---|---|
| `Packs.PacksPath` | `<plugin dir>\DawncasterPacks` | Directory whose subdirs are scanned for `pack.json`. |
| `Packs.ExpansionOverride` | *(empty)* | Emergency: force every pack card's expansion to a native `CardExpansions` member; disables synthetic sets (no set rows). |
| `Packs.AutoDiscoverModCards` | `true` | Mark loaded mod cards discovered in the Codex (in-memory; never writes `Codex.dtt`). |

`BepInEx\config\com.dawncastermods.sandbox.cfg` (dev sandbox, never shipped):
`Sandbox.InjectSandboxCard` (default `false`) — the SandboxStrike test card.

## 10. Log vocabulary — debugging, especially after game updates

All engine lines are prefixed `[DawnKit]` in `BepInEx\LogOutput.log`. The
boot sequence a healthy install prints:

| Line | Meaning |
|---|---|
| `Command vocabulary: 565 effect / 660 talent-union.` | Embedded DSL vocabularies loaded. |
| `AutoId self-check: 5/5 reference vectors OK.` | AutoId determinism verified (§7). |
| `Target found: <Class.Method>` ×16 + `Target found: <member> (member)` ×4 | Every Harmony patch target / private member resolved. **After a game update, count these first**: a missing one logs `Target MISSING: <label> — that integration is disabled.` and only that integration is off (skip-don't-crash) — the log names exactly what broke. |
| `DawnKit 0.7.0 loaded — 16/16 patch targets applied.` | Boot summary. |
| `<owner>: N cards injected, M skipped (hook: <hook>)` | Phase-1 injection per mod. |
| `<owner>: N weapons, M weapon powers injected (classes: …)` | Weapon/power injection + class attachment. |
| `Class weapon/talent counts: Arcanist=3w/6t, …` | Post-attachment per-class totals. |
| `Synthetic card sets: <Name>=(CardExpansions)<value> [N cards], …` | Set table with values and counts. |
| `Reference resolution: N resolved, M unresolved (hook: …)` | Phase-1/phase-2 ref passes; unresolved names are listed. |
| `── Boot report ──` … `── end boot report ──` | Consolidated per-mod counts + conflicts (§8). |
| `Run-settings set row added: … / Sunforge set row added: … / Status row added: …` | Set-screen integration firing when those screens open. |
| `Set toggle: <Name> ((CardExpansions)<value>) excluded=<bool>` | Player toggled a mod set. |
| `CreateRunLists: runcards=N [per-set counts] excludedsets=[…]` | Run-pool rebuild probe — proves cards enter/leave the pool on toggle. |
| `Codex: marked N mod cards as discovered … / N mod expansions added to shownExpansions filter.` | Codex integration. |
| `Diagnostics dump written: <path>` | `DiagnosticsDump` output location. |

## 11. Porting from raw Harmony

If your content mod patches the game directly today, the port is deletion:
remove your patches on the asset-load path (`SetPlayerAssetsLoaded` /
`LoadPlayerAssets` hooks, cache refreshes, reload re-injection, set-screen or
Codex UI patches) and your ScriptableObject construction code, then declare
the same content through §3 builders in `Awake()`. The engine already handles
the parts that break: the sync-vs-async boot paths, the phase-2 status
timing, re-injection after `ForceReloadAssets`, non-null list/`HideAndDontSave`
SO hygiene, run-pool routing, set-screen bonus/reroll math, and Codex
filters. Keep Harmony only for actual **behavior** changes (new mechanics,
rebalancing) — that's out of DawnKit's scope by design; content injection
through the API and behavior patches through your own Harmony instance
coexist fine (use your own `Harmony` ID, don't touch the engine's 16
targets).
