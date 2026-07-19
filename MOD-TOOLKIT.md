# The Dawncaster Mod Toolkit — direction document

> Living document, iterate freely. v0.2 — reframed 2026-07-18: **engine first, for human
> modders**. (v0.1's "docs are the API / AI-native" framing is retired; the agent-friendly
> tooling remains a nice property, not the product.)

## 1. What we're building

A modding toolkit for **human modders** — explicitly including not-so-technical ones.
The measure of success is that creating content follows an easily human-usable pattern:
describe the card, don't program the injection. Two stages:

1. **The engine (now)** — a clean library that sits on top of the game's own code and
   handles content injection *properly*: lifecycle, registration, collision safety,
   UI integration, degradation. Modders install it (BepInEx plugin/library), then add
   content either from C# against a typed API, or purely as data (pack folders). All the
   hard-won engine knowledge in this repo — load phases, two-phase reference resolution,
   re-injection after asset wipes, set-screen integration, Codex discovery — gets encoded
   **once, in the engine**, instead of living in one monolithic plugin.

2. **The designer toolkit (later)** — a UI where someone designs a card without touching
   JSON: pick abilities from a palette (the 565 effect commands surfaced as human-readable
   building blocks), plug in values, pick/combine/mutate art visually, export a pack.
   Everything we built for pipeline automation (vocabularies, validators, balance sim,
   art mutation ops) becomes the *backend* of that UI. Out of scope until the engine is
   solid — but every engine decision should keep this door open.

Our four packs stay the flagship content: they prove the engine, and they'll be the
sample library the designer UI ships with.

## 2. The engine — design

Working name: **DawnKit** (placeholder — naming is open question #1).

### 2.0 The pain ledger — the value proposition, concretely

Every row below is a pain **we personally hit** building the four packs. The engine's
entire reason to exist is that no modder ever hits them again. Each row is therefore an
M1 requirement with the acceptance test: *"a modder can ship content without knowing
this exists."*

| # | Pain we hit | What it cost us | Engine answer |
|---|---|---|---|
| 1 | Finding injection points at all | Full decompile + tracing the AssetManager lifecycle | Lifecycle owned by the engine; clients declare content, never hook |
| 2 | Sync vs async boot paths diverge (`SetPlayerAssetsLoaded` not called on the sync path) | A live-debug session + dual hooks | Engine hooks both; invisible |
| 3 | `ForceReloadAssets` silently wipes injected content | Re-injection logic + the object-identity-vs-ID keying gotcha | Registrations are durable; engine re-applies automatically |
| 4 | References resolve in a later load phase (statuses after cards; early `GetStatus` risks re-entrant loading) | Two-phase resolver design | Refs declared by name; engine resolves at the right phase |
| 5 | ScriptableObject landmines: null lists NRE the Codex, missing `HideAndDontSave` lets Unity tear down cards on scene change, `CardEnchantments` must be non-null | Crash-hunting + factory conventions | Engine factories construct everything safely |
| 6 | cardID space is a minefield (shipped IDs scattered to 41M; our first-choice 900k range had 235 collisions) | A collision-analysis pass to find free space | Hash-based auto-ID allocation + boot-time collision refusal with a named conflict |
| 7 | Card names are identity (lowercase-keyed caches, display name == SO name) | Collision checks hand-rolled in three places | One ownership registry validates names/IDs across ALL mods |
| 8 | Card sets are a C# enum — you can't just "add a set" | The whole synthetic-CardExpansions design (stable derived values, persistence analysis) | `Sets.Register("Name")` — one line |
| 9 | The set-selection UI doesn't know your set exists | Prefab row cloning, invoking a private `ToggleSet`, patching FOUR raw-int render points, eye-icon preview wiring | Engine owns all set-screen integration; registered sets just appear, named, with previews |
| 10 | Excluding mod sets farms run bonuses (bonus transmutes/rerolls key off `excludedsets.Count`) | Found only by reading reward code | Engine counts only native exclusions |
| 11 | Sunforge reroll math assumes the enum's size | Same research pass | Engine adjusts baseline per registered set |
| 12 | New cards render as undiscovered silhouettes in the Codex; a second hidden filter drops synthetic-set cards from the list entirely | Codex.dtt reverse-engineering | Auto-discovery + filter integration, config-gated |
| 13 | Localization: inline text works for cards/talents/events but silently NOT for statuses; key patterns are undocumented (`{cardID}_N/_D/_E`) | Decompile verification per content type | Inline text always works; engine handles the loc plumbing per type |
| 14 | The effect DSL is entirely undocumented (565 commands, `;`/`:` syntax, `[[tokens]]`), with idiom traps — `inflict`+referenceStatus is used by 441 shipped cards, the lookalike status-name-command form by exactly 1 | Extraction + statistical idiom analysis | Vocabulary embedded in the engine; validated at `Register()` with did-you-mean errors; documented ability reference |
| 15 | The game's enums contain typos that are API surface (`CardRariry`, `Phyisical`) | Spelling discipline everywhere | The API exposes **correctly-spelled** enums and maps to the game's internally — humans never type the typo |
| 16 | Art specs are tribal knowledge (512×512 full-bleed, frame drawn by UI, PPU, HideFlags), and sprite names don't match card names (PPtr correlation required) | Sprite extraction + correlation tooling | `.Art("file.png")` just works; placeholder fallback built in |
| 17 | Player-pool membership has hidden rules (`ProcessCard`: expansion None/Metaprogress, Monster rarity, Companion suffix all silently exclude) | The Core-camouflage detour before real sets | Sensible defaults; membership explicit in the builder |
| 18 | Weapons/powers have structural conventions (weapon = BasicAttack card + `excludeFromRewards`; power = tier-0 Talent; live Profession list attachment) | A dedicated research pass + spec | `Weapons.Build()` / `WeaponPowers.Build()` encode the conventions |
| 19 | The only feedback channel is log-file spelunking (BepInEx LogOutput + Player.log) | Every verification round-trip | Player-facing in-game status ("3 mods, 51 cards, 1 error — click for details") + per-mod diagnostics dump |
| 20 | Save-safety on uninstall was unknowable without research (stale enum ints, Codex residue, in-run decks) | A dedicated analysis pass | Degradation behaviors are engineered + documented guarantees |
| 21 | Game updates invalidate everything quietly (version change triggers asset wipe/reload) | Discovered in AssetManager code | Engine survives reloads by design; toolchain ships a patch-day checklist |

That table is the elevator pitch: **we already paid the tuition; the library is the
refund for everyone else.** When we evaluate any M1 API proposal, the question is "which
rows does this erase, and does it add any new ones?"

### 2.1 Shape

Two assemblies, one repo:

- **`DawnKit.dll`** — the engine library. A BepInEx plugin other mods declare a
  `BepInDependency` on. Exposes the public API below. Owns ALL Harmony patches — client
  mods never patch the game themselves for content injection.
- **`DawnKit.Packs.dll`** — the bundled data-driven client: scans pack folders
  (`BepInEx/plugins/DawncasterPacks/*/pack.json`), builds content through the same public
  API. This is our current PackLoader, repositioned as *a consumer* of the engine.

Our current `Dawncaster.Sandbox` monolith gets split into exactly those two, plus a thin
dev-sandbox we keep for experiments.

### 2.2 Public API sketch (C# consumption mode)

Everything below already works inside today's plugin — this is a refactor into a stable
surface, not new invention:

```csharp
[BepInPlugin("com.example.mymod", "My Mod", "1.0.0")]
[BepInDependency(DawnKit.GUID)]
public class MyMod : BaseUnityPlugin
{
    void Awake()
    {
        // Registration is declarative and lifecycle-safe: DawnKit applies it at the
        // right load phase, re-applies after ForceReloadAssets, resolves references
        // in phase 2, and refreshes caches/run-lists. Client code runs zero patches.
        var set = DawnKit.Sets.Register("My Frost Set", author: "example");

        DawnKit.Cards.Build("Glacial Lance")
            .Id(712_345_600)                       // or .AutoId() => hash-allocated block
            .InSet(set)
            .Type(CardType.Magic).Category(CardCategory.Action).Rarity(Rarity.Common)
            .Cost(int_: 1)
            .Description("Deal 4 frost damage. Frozen foes take double.")
            .Effect(Trigger.PlayAction, "damage:4:frost", e => e.RefStatus("Frozen"))
            .Art("art/GlacialLance.png")           // PNG -> Sprite; placeholder fallback
            .Register();

        DawnKit.Weapons.Build("Icebrand")...       // BasicAttack card + class attachment
        DawnKit.WeaponPowers.Build("Flash Freeze") // tier-0 Talent
            .Cooldown(3)
            .Effect(Trigger.ActivateWeapon, "startstatus:...")
            .ForClasses("Arcanist", "Seeker")
            .Register();
    }
}
```

API principles:
- **Builders validate at Register()** — enum spellings, command vocabulary, ID/name
  collisions (against the shipped pool AND all other registered mods), returning rich
  errors. Fail one item, never the load.
- **Lifecycle is the engine's problem.** Clients never know about
  `SetPlayerAssetsLoaded` vs `LoadPlayerAssets`, phase-2 status resolution, or cache
  refresh. (This knowledge is exactly what's encoded in today's PackLoader — it moves
  into the engine core.)
- **Everything registerable is inspectable**: `DawnKit.Cards.All`, ownership metadata
  (which mod registered what) — the foundation for conflict reporting and later UI.
- **Semver + capability handshake**: packs/mods declare the schema/API version they
  target; engine refuses newer-than-supported with a clear player-visible message.

### 2.3 Engine internals (what moves where)

| Today (Dawncaster.Sandbox) | Engine home |
|---|---|
| Injection hooks + idempotency + re-injection | `DawnKit.Core.Lifecycle` |
| Two-phase referenceStatus/referenceCards resolution | `DawnKit.Core.RefResolver` |
| Card/Talent SO factories (non-null lists, HideAndDontSave...) | `DawnKit.Content.Factories` |
| Synthetic CardExpansions allocation + set rows + name patches | `DawnKit.Integration.Sets` |
| Codex auto-discovery | `DawnKit.Integration.Codex` |
| Profession weapon/talent attachment | `DawnKit.Integration.Classes` |
| PNG→Sprite + placeholder art | `DawnKit.Content.Art` |
| pack.json parsing | `DawnKit.Packs` (client, not engine) |
| Command vocabulary / enum tables | `DawnKit.Content.Vocabulary` (embedded resources) |

New engine responsibilities (not yet built anywhere):
- **Mod manifest & ownership registry** (which mod owns which IDs/names; conflict report
  at boot, player-readable).
- **Hash-based auto-ID allocation** (`stableHash(author/modName)` → block) with explicit
  override, so casual modders never think about IDs.
- **Player-facing status**: "X mods, Y cards loaded, Z errors" surfaced in-game (main
  menu), errors named per mod — log-file spelunking must not be required to know
  something failed.
- **Diagnostics mode**: per-mod dump of registered content for bug reports.

### 2.4 What the engine is NOT
- Not a game-behavior framework: no rebalancing hooks, no new-DSL-command extension
  (possible later, separate opt-in module — it changes the compatibility story).
- Not an asset distributor: art travels as recipes or original files only, never
  extracted/derived game assets.

## 3. The offline tooling, repositioned

The Python stack (extract → validate → sim → artmutate → contact sheets) stays, as the
**authoring/CI toolchain** — it's what the future designer UI wraps:

- Near-term human-accessibility wins (cheap, before any UI):
  - `schemas/pack.schema.json` → autocomplete + validation in any editor.
  - `tools/new_pack.py` scaffolder.
  - A single entry CLI (`python tools/dmk.py validate|sim|art|sheet`) so modders learn
    one command.
  - The tutorial: "Your first card in 15 minutes."
- The knowledge docs (GAME-MECHANICS etc.) become **modder documentation** — the
  reference manual for what abilities exist and how the game actually behaves.

## 4. Milestones (revised)

- **M0 (now)**: finish weapons v1.1 in the current monolith. Keep proving content types.
- **M1 — the engine extraction**: split Sandbox → `DawnKit` + `DawnKit.Packs`; public
  API v0; ownership registry + boot conflict report; player-facing load status; semver.
  Success test: our own four packs load through the public API with zero private hooks,
  and a 20-line example C# mod (in `examples/`) registers one card.
- **M2 — modder release**: JSON schema files, scaffolder, unified CLI, tutorial, API
  docs, GitHub release of the two DLLs. Success test: one outside human ships a small
  pack (data-only) and one ships a C# mod, without asking us anything.
- **M3 — more content types on the engine**: opportunity events (Ink), statuses,
  general talents — each as engine API + pack schema + example.
- **M4 — the designer UI**: visual card designer backed by the vocabularies, validator,
  sim, and art-mutation ops; exports packs. (Design doc when we get here — web app vs
  desktop, etc.)

## 5. Standing policies (carry over from v0.1, unchanged)

- **Content is data** for the no-code path; C# path exists but never *requires* game
  patching by clients.
- **The shipped pool is the referee**: budgets/envelopes/distinctness derived from the
  game's own content — it's what makes community content feel native.
- **Legally clean by construction**: decompiles, extracts, and mutated art are local
  rebuildable artifacts; only recipes/manifests/original art travel.
- Achievements/leaderboard stance: policy decision still open (question #3).

## 6. The monorepo (pattern adopted from `ftk2-mods`)

This repo becomes a **multi-mod monorepo** in the `D:\src\mods\ftk2-mods` style: shared
docs + shared toolchain at the root, one directory per shippable package, each with its
own SPEC/notes. The toolkit is just **one package among several**; our packs are sibling
packages that consume it — the engine's first and permanent dogfood.

### 6.1 Target layout

```
dawncaster-mods/
  README.md                     # package table (what/status/priority), like ftk2's
  MOD-TOOLKIT.md                # this direction doc
  docs/
    CONVENTIONS.md              # per-package rules + SPEC template (adapted from ftk2)
    design/                     # content-design methodology (binding) + corpus stats
      METHODOLOGY.md
      CORPUS-STATS.md
    research/                   # decompile-verified knowledge, shared by all packages
      GROUND-TRUTH.md
      GAME-MECHANICS.md
      asset-extraction-notes.md
      reference/                # effect/dialogue/talent command vocabularies
  tools/                        # shared Python toolchain (extract/validate/sim/art)
    out/, bin/                  # machine artifacts — gitignored, rebuildable
  decompiled/                   # gitignored, local-only (unchanged policy)

  DC.DawnKit/                   # THE ENGINE PACKAGE
    SPEC.md                     # engine spec (grows out of §2 here)
    CARD-PACK-SPEC.md           # the data contracts ARE its extensibility surface,
    WEAPON-SPEC.md              #   so they live with the engine package
    ART-MUTATION-SPEC.md
    src/                        # DawnKit + DawnKit.Packs (+ dev sandbox) — M1 split
    examples/                   # the 20-line example C# mod, template data pack

  DC.VenomousLegacy/            # CONTENT PACKAGES (one per pack)
    pack.json                   #   consume DawnKit.Packs; no code
    DESIGN-NOTES.md
    BALANCE-REPORT.md
    art-recipes.json
    art/                        # gitignored (derivative outputs, built locally)
  DC.EmberweaveGrove/
  DC.ClockworkCadence/          # dir renamed — package dirs use no spaces;
  DC.CrimsonLedger/             #   in-game display name stays "Clockwork Cadence"
                                #   (comes from pack.json, not the folder)
```

Conventions adopted from ftk2 (see its `docs/CONVENTIONS.md`), adapted:

- **Package = shippable unit.** Any top-level `DC.*/` dir with a `pack.json` is a content
  package (tools discover by that convention); packages with `src/` ship DLLs.
- **The engine hardcodes no content and no tuning numbers** — ftk2's non-negotiable rule,
  already our thesis. DawnKit ships zero cards; even its example content lives in
  `examples/`.
- **Per-package SPEC.md** follows a shared template (in CONVENTIONS.md): purpose,
  player-facing behavior, architecture, data formats, knobs, patch targets, example
  dataset, testing plan, save considerations, milestones, open questions. (ftk2's
  multiplayer section is dropped — Dawncaster is single-player.)
- **"Target found: X" logging** for every Harmony patch target at boot (ftk2/EOR
  practice) — makes game-update breakage diagnosable from logs alone. Adopt in DawnKit.
- **Fail-safe rule**: every engine feature has a master `Enabled` knob; parse failures
  log loudly and leave vanilla behavior untouched.
- **Naming**: plugin GUIDs `dcmods.<package>` (e.g. `dcmods.dawnkit`); package dirs
  `DC.<PascalCase>`; content IDs keep the block policy (per-pack numeric blocks).

### 6.2 Migration plan (do it as pure moves, before the M1 extraction)

Two separate commits, in order, so history stays reviewable:

1. **Restructure commit — `git mv` only, zero content edits**: create `docs/research/`,
   move GROUND-TRUTH/GAME-MECHANICS/asset notes/reference there; move the three data
   specs into `DC.DawnKit/`; `packs/<Name>` → `DC.<Name>` (Clockwork Cadence loses the
   space); `src/` → `DC.DawnKit/src/`. History preserved via rename detection.
2. **Path-fixup commit**: tools' pack discovery (`packs/*` glob → `DC.*/pack.json`
   convention), validator/sim/artmutate path constants, doc cross-links, `.gitignore`
   paths, README rewrite as the package table — plus the game-side
   `Packs.PacksPath` config repoint (and `DawnKit.Packs` default becomes the
   `DC.*`-aware scan).

Then the M1 engine extraction happens *inside* the already-clean layout. Future mods
(a Dawncaster equivalent of WarBrain, event packs, etc.) land as new `DC.*` packages —
and yes, they build on DawnKit, because that's the point.

## 7. Open questions

1. **Name** for the engine (DawnKit placeholder; run a namestorm — must not collide with
   in-game terms like Sunforge).
2. ~~Repo split timing~~ — **resolved by §6**: monorepo, ftk2-style; the engine is one
   package (`DC.DawnKit`), content packs are sibling packages. No split.
3. **Achievements stance** — permissive / config-gated / disabled-with-mods.
4. **When to contact Wanderlost** (lean: before M2's public release).
5. **License**: engine MIT? docs CC-BY? guidance for pack authors' own content?
6. **C# API ergonomics**: builders (sketched above) vs attribute-driven declarations vs
   both — decide during M1 with a real example mod as the test.
7. How opinionated should `DawnKit.Packs` be about balance — should the loader *warn*
   in-game when a pack never went through the sim gate?
