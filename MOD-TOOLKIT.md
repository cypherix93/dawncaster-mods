# The Dawncaster Mod Toolkit — direction document

> Living document. This is the "what are we actually building" doc — iterate freely.
> Status: v0.1 draft, 2026-07-18.

## 1. The realization

This repo started as "can we add custom cards?" It is now, de facto, a **general content
toolkit for Dawncaster** — and almost none of it is specific to *our* four packs:

- A modder who has never opened a decompiler can write `pack.json` + art recipes and get
  cards, card sets, weapons, and weapon powers into the live game — **as data, zero C#**.
- The knowledge layer (GAME-MECHANICS, GROUND-TRUTH, the command vocabularies) makes a
  newcomer — human or AI agent — into a competent Dawncaster content designer in one
  sitting. Most mod toolkits document their API; this one documents **the game's design
  language**: budgets, archetypes, timing rules, synergy webs.
- The validation stack (static gate → pool-calibrated balance sim → distinctness gate →
  in-game QA) is a *quality culture*, encoded as tooling. Community packs that pass it
  will feel native.

Our packs (VenomousLegacy, EmberweaveGrove, Clockwork Cadence, CrimsonLedger) are the
**flagship content**: proof the toolkit works, reference implementations, and the house
style guide — but they're product #2. The toolkit is product #1.

## 2. What exists today (the layer map)

| Layer | Components | State |
|---|---|---|
| **Extraction** | `extract_data.py` (full content DB in ~7s), `extract_sprites.py`, `card_stats.py` | ✅ solid, re-runnable after every game patch |
| **Knowledge** | GROUND-TRUTH, GAME-MECHANICS (71KB, claim-cited), `reference/*` vocabularies, asset notes | ✅ the moat — nothing like it exists for this game |
| **Content contracts** | CARD-PACK-SPEC (pack.json), WEAPON-SPEC (v1.1), ART-MUTATION-SPEC | ✅ cards shipped; weapons in flight |
| **Validation & balance** | `validate_pack.py`, `tools/sim/` (envelopes calibrated on the 2,525-card shipped pool), `validate_art.py` | ✅ working; sim covers a DSL subset honestly |
| **Runtime** | BepInEx plugin: PackLoader (two-phase injection, live pack folder), SetScreenPatches (first-class card sets UI, Codex discovery), graceful degradation | ✅ verified live |
| **Art** | Non-AI mutation pipeline: 13-op engine, recipes, distinctness gate, pair contact sheets | ✅ 47/47 built |
| **Methodology** | Spec → parallel design agents w/ uniqueness bars → gates → in-game QA | ✅ practiced, not yet written down as a guide |

## 3. The thesis

**Dawncaster is a data-driven game whose content language is strings and ScriptableObjects;
therefore its mod ecosystem can be data-driven too.** The toolkit's job is to make the
game's own content language *writable* by outsiders, with guardrails that keep community
content native-feeling, balanced-by-default, and legally clean.

Three properties we should never give up:

1. **Content is data.** Packs are JSON + recipes. No community modder compiles anything.
   The single plugin is *our* surface area; everything else is declarative.
2. **The shipped pool is the referee.** Budgets, envelopes, distinctness, idioms — every
   quality gate is *derived from the game's own 2,525 cards*, not from our taste. That's
   what makes "feels native" reproducible.
3. **Legally clean by construction.** No game assets in any repo — decompiles, extracts,
   and mutated art are local artifacts, rebuilt from the modder's own game install.
   Recipes and manifests are the only thing that travels. This isn't a compromise; it's
   the distribution model.

### The AI-native angle (worth saying out loud)

This toolkit was built *by* agent swarms, and it's shaped so agents are first-class
modders: grounding docs are context packs, specs are contracts, validators are feedback
loops, the sim is an oracle. "Design me a frost-themed pack" is already a viable prompt
against this repo. No other game modkit is built this way. Whatever we publish should
keep that property deliberately — the docs ARE the API.

## 4. Gap analysis — what's missing for a community v1.0

### 4.1 The loader as a product
- Split the plugin: `Dawncaster.PackLoader` (stable, versioned, the thing modders depend
  on) vs sandbox/dev extras. Semver + a schema-version handshake (`pack.json` declares
  `"schemaVersion"`; loader refuses newer schemas with a clear message).
- Player-facing surfacing: an in-game "N mod packs loaded / errors" line somewhere
  (main menu corner or the sets screen), not just log files.
- Pack folder convention for non-dev users: `BepInEx/plugins/DawncasterPacks/<Pack>/`
  (already supported) + docs.

### 4.2 Pack identity & metadata
`pack.json` grows a header: `author`, `version`, `description`, `schemaVersion`,
`homepage`. Loader logs and (later) displays these.

### 4.3 ID governance at community scale
Our ID-REGISTRY.md works for one repo, not an ecosystem. Int IDs can't be namespaced, so:
- **Proposal**: deterministic block allocation — `blockStart = 700M + (stableHash(author + "/" + packName) % 900k) * 100`, collision-checked at load (loader refuses packs whose blocks overlap and names the conflict). The central registry remains as an *optional claim ledger* (PR to the toolkit repo) for packs that want guaranteed uniqueness.
- Card/talent *names* are the second collision surface — loader already refuses dupes;
  validator should warn on names too close to shipped ones.

### 4.4 Cross-pack ecosystem behavior
Load order (alphabetical, deterministic), inter-pack references (allowed? v1: no —
packs must be self-contained), duplicate detection, and a documented answer for "two
packs both mutate the same shipped system".

### 4.5 Content-type roadmap (each = spec → validator → loader → flagship examples)
| Type | Difficulty | Notes |
|---|---|---|
| Cards / card sets | ✅ shipped | |
| Weapons + weapon powers | 🔨 in flight | v1.1 |
| **Opportunity events (Ink)** | Medium — **next big one** | Bridge verified (GROUND-TRUTH): compiled Ink JSON + Dialogue SO + `>>>action` lines; needs inklecate in the toolchain + event spec |
| Statuses/keywords | Medium | Needs the localization patch (no inline fallback) — already scoped |
| General talents (tier 1-6) | Easy-ish | Same Talent injection, different selection surfaces |
| Enchantment/conjuration token cards | Easy | Already possible via cards; needs doc + examples |
| Battles/enemies | Harder | Battle SO + AI/intent data; big design surface |
| Areas/quests | Harder | Map graph integration |
| Classes | Hardest | Fixed UI roster; deferred indefinitely |

### 4.6 Modder DX
- **JSON Schema files** (`schemas/pack.schema.json`) → editor autocomplete + red squiggles
  in VS Code for free. Cheap, huge leverage.
- `tools/new_pack.py` scaffolder (folder + manifest skeleton + ID block + recipe stub).
- A **tutorial**: "Your first card in 15 minutes" walking one card from idea → JSON →
  validate → sim → in-game screenshot.
- Docs organization pass: the repo root is accreting specs; a `docs/` structure with a
  reading path (modder path vs contributor path vs designer path).
- Dev loop: pack edits already apply on relaunch; investigate a dev-mode hot reload
  (re-run injection on demand) to cut the loop to seconds.

### 4.7 Ethics & ecosystem hygiene
- **Achievements/leaderboards**: mod content can currently influence achievement runs.
  Decide a stance (config default? community norm? disable leaderboard submission when
  packs active?) — needs a decompile check of what's feasible and an explicit policy doc.
- **Art**: the mutation pipeline's legal-by-construction model must be the *enforced*
  default (tooling already gitignores outputs; docs must explain why, loudly).
- **Wanderlost relationship**: at some maturity point, show them the toolkit. Best case:
  guidance or blessing; worst case: we're already built to comply with a takedown of
  anything asset-derived (recipes make art swappable). Timing is an open question below.
- Game updates: extraction+decompile refresh is ~10 minutes of tooling reruns; document
  the "after a game patch" checklist.

## 5. Proposed milestones

- **M0 — internal (now)**: we are the only users. Finish weapons v1.1; hello-event spike.
- **M1 — "a friend could use this"**: loader split + semver, schema files, scaffolder,
  tutorial, docs reading path, pack metadata header. Success test: one outside person
  ships a 3-card pack without asking us anything.
- **M2 — public beta**: hash-based ID allocation, event support, achievements policy,
  distribution presence (GitHub releases; evaluate Nexus/Thunderstore), announcement in
  the Dawncaster community Discord.
- **M3 — ecosystem**: community pack registry w/ CI validation (GitHub Action running
  validate+sim on PRs), curated "verified balanced" collections, possibly official contact.

## 6. Open questions (iterate here)

1. **Name.** The toolkit deserves its own identity, separate from our packs.
   Early candidates: *Dawnforge* (thematic but collides with in-game Sunforge),
   *Dawnsmith*, *Cardwright*, *Wanderkit*. → run a proper namestorm.
2. **Repo split?** Toolkit (loader+tools+docs) vs our flagship packs — one repo or two?
   Two gets cleaner licensing/versioning; one keeps the flywheel simple. Lean: split at M2.
3. **Achievements stance** — permissive, config-gated, or hard-disabled with packs on?
4. **When to contact Wanderlost** — before public beta (safer, may get blessing) or
   after there's community traction (stronger case, more risk)?
5. **Distribution channel** for the loader plugin (GitHub releases only vs Nexus etc.).
6. **License** for the toolkit code and for the spec/doc layer (MIT for tools? CC-BY for
   docs? packs' own content license guidance for authors?).
7. How much of the **agent methodology** do we publish? (The design-wave playbook could
   be its own doc — "how to run an AI design studio against this toolkit".)

## 7. Non-goals (for now)

- Multiplayer/leaderboard-visible content, cheating tools, achievement farming.
- Redistributing anything derived from game assets.
- Engine-behavior overhaul mods (new mechanics in C#) — the toolkit is a *content*
  toolkit; new DSL commands via Harmony is a possible M3+ extension, not core.
- New classes.
