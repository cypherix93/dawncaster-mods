# The Dawncaster Mod Toolkit — direction document

> Living document, iterate freely. v0.2 — reframed 2026-07-18: **engine first, for human
> modders**. (v0.1's "docs are the API / AI-native" framing is retired; the agent-friendly
> tooling remains a nice property, not the product.)

## 1. What we're building

A modding toolkit for **human modders**, in two stages:

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

## 6. Open questions

1. **Name** for the engine (DawnKit placeholder; run a namestorm — must not collide with
   in-game terms like Sunforge).
2. **Repo split timing** — engine repo vs content repo (lean: at M2, engine graduates).
3. **Achievements stance** — permissive / config-gated / disabled-with-mods.
4. **When to contact Wanderlost** (lean: before M2's public release).
5. **License**: engine MIT? docs CC-BY? guidance for pack authors' own content?
6. **C# API ergonomics**: builders (sketched above) vs attribute-driven declarations vs
   both — decide during M1 with a real example mod as the test.
7. How opinionated should `DawnKit.Packs` be about balance — should the loader *warn*
   in-game when a pack never went through the sim gate?
