# Corpus stats — the shipped pool, measured

The derived statistics of Dawncaster's shipped content that every budget argument
anchors to. This file is the **single home** for these numbers: packs cite this file,
not each other. (Historically the weapons/powers and starting-card curves were first
derived inside `DC.EmberweaveGrove/DESIGN-NOTES.md` and cited across packs — this file
supersedes those in-pack copies; the pack notes remain as historical records.)

**Provenance.** Everything here is measured from the extracted assets in
`tools/out/data/` (2,525 Cards, 383 Talents, 49 StatusEffects, 170 Battles, 66 tier-0
Talents, 63-card starting corpus) and `tools/out/card-stats.json`. Regenerate after any
game update:

| Numbers | Regenerate with |
|---|---|
| Card pool distributions, damage regression | `python tools/extract_data.py` then `python tools/card_stats.py` |
| Weapon / power / starting-card corpora | `tools/out/data/` scans (Profession + Keystone JSONs, `data-index.json`) |
| Sim envelopes | `python tools/sim/report.py --all` (per-pack BALANCE-REPORT.md) |

Last measured: 2026-07 extraction pass (game version per `tools/out/data-index.json`).

---

## 1. Card pool

- **2,525 cards** total; ~1,243 in the player-legal damage-regression pool
  (`card_stats.py`); 916 player-legal action-routed cards per the sim harness census
  (`tools/sim/README.md`).
- **Cost distribution** (total energy): 15% cost-0, 57% cost-1, 16% cost-2, 11% cost-3,
  2% cost-4. This is a low-cost, high-tempo game — packs must skew the same way.
- **Rarity mix** (player pool): 35% Common / 22% Uncommon / 25% Rare / 19% Legendary.
  Per-10-cards pack guidance: ≈ 3–4 C / 2–3 U / 2–3 R / 1 L.
- **Damage-per-energy regression** (single-statement damage cards, n≈20 of 1,243 —
  pure vanilla is *rare*; shipped cards earn their slot with text, not stats):

  | Total cost | Common | Uncommon | Rare | Legendary |
  |---|---|---|---|---|
  | 0 | ~2–3 dmg | 3 | — | — |
  | 1 | 3–5 (median 4) | 4–5 | ~5 | ~6 |
  | 2 | ~8 | — | 5–8 | — |
  | 3 | up to 12 | — | 8–15 | — |

  Working rule: **~4 damage per energy at Common; each rarity step buys ~+1
  damage-equivalent or one rider.**
- **Complexity:** shipped cards run 1–3 effect codeLines in the overwhelming majority;
  one clean idea, usually one gate.
- **Reward-pull surface** (GAME-MECHANICS Part VII): 3 card rewards per combat
  (`cardRewards`, hard max 6); rarity roll widths scale with luck (Legendary
  `luck×0.1`%, Rare `luck×0.5 (+35 vs boss)`%, Uncommon `luck×2 (+35 vs boss)`%,
  remainder Common); class-color chance 45%; Unique-owned and imbued cards are excluded
  from pools. **Commons dominate what players actually see** — a pack's commons are its
  real interface with the run.
- **Deck-value economy** (`PlayerHandler.DeckValue`): rarity base values Common 5 /
  Uncommon 10 / Rare 25 / Epic 35 / Legendary 50 / Monster 0; `Valuable` ×4.
- Engine ceilings (GAME-MECHANICS Part VII): damage clamp 999,999; status stack cap
  9,999; hand cap 10; energy cap 8; deck cap 999; gold cap 9,999.

## 2. Starting weapons (corpus: 6)

One per class (Knight=Longsword, Rogue=Daggers, Arcanist=Forcewand, Hunter=Hatchet,
Seeker=Knuckles, Warrior=Warmace; Scion none).

- **Statline:** all six are `damage:2`, Common, BasicAttack category, 1 Neutral cost.
  Sole deviation: **Warmace pays 2 Neutral for an ungated status rider.**
- **Identity lives in exactly one rider or condition** — never in a bigger number.
- A weapon's rider repeats **every single turn, all run** — budget it as a permanent
  engine, not a card effect.

## 3. Weapon powers (corpus: 66 tier-0 Talents)

- **Cooldown histogram:** 1×1, 2×20, 3×32, 4×8, 5×2, 6×1, 10×1, 20×1 — **79% at cd
  2–3**.
- **Payoff ladder** (cooldown → what shipped powers pay):
  - **cd 2** — one small/level-scaled next-combat status (Evasion 1; Armor 3×lvl;
    Poison 2×lvl) or minor conversion, often + a passive rider or StartGame talent
    grant.
  - **cd 3** — a start-of-combat card (choice-of-3 or free) OR ~2–3×lvl statuses OR a
    mid heal (10% max HP).
  - **cd 4–5** — permanent deck edits (upgraderandom, addkeywordindeck, destroy) or
    bigger heals.
  - **cd 6+** — dramatic one-offs (reward-pool override 6, Scar removal 10, full
    heal 20).
- **Passive riders do NOT scale with cooldown:** small per-trigger values (heal 1–3,
  1 status stack, 1 resource) or one-time StartGame `addtalent`/`imbue` grants. An
  always-on rider is budgeted as a permanent talent, not as part of the cooldown
  payoff.
- ActivateWeapon fires in **exploration context** (out of combat) — payoffs are
  deck-prep, healing, next-combat statuses, economy. Passive riders carry the
  in-combat identity.

## 4. Starting cards (corpus: 63)

6 Profession defaults (Knight=Bolstered Strike, Rogue=Sneak Attack, Arcanist=Shocking
Grasp, Hunter=Feral Strikes, Seeker=Mindstrike, Warrior=Backswing; Scion none) + 57
distinct `KeystoneType.StartingCard` keystone cards (`tools/out/data/Keystone/*.json`).

- **Cost:** 1 total energy on 51/63 (**81%**); 7× cost-2, 3× cost-3, 2× cost-0. All
  six defaults are exactly 1-cost in class colors.
- **Rarity:** 22 C / 19 U / 18 R / 4 L. Defaults are 5 C + 1 U — Rare is the
  keystone-unlock build-around tier; always-available mod starting cards stay C/U.
- **Type:** Utility 40, Melee 10, Magic 5, Divine 5, Corruption 2, Ranged 1.
  **Category:** Action 56, Enchantment 4, Equipment 3.
- **Complexity:** 1–3 effect codeLines on 60/63 (median 2).
- **Flags:** 62/63 are normal acquirable reward-pool cards (`canBeAcquired: 1`,
  `excludeFromRewards: 0`; sole exception Battleclaw, a starts-in-play equipment).
  Starting cards are NOT reward-excluded — unlike the weapon-card factory default.
- **Shape:** 7/63 literally "Make a Basic Attack" (3 of the 6 defaults) — the corpus
  leans on the weapon; each card is one cheap archetype seed with usually one gate.
- **Deck math:** the pick enters the starting deck **once** (surges + weapon ×6 +
  Block ×2 + starting card ×1 ≈ 13 cards) and is drawn turn 1–2 of nearly every early
  combat. **Reliability, not raw power, is the budget axis.**

## 5. Statuses & battles

- **Status roster is exactly 49 assets** — full table with verified formulas, triggers
  and expiry in GAME-MECHANICS Parts II–III. 23 are `excludeFromRandom` (never rolled
  by `randomstatus`/`randomaffliction`); 4 are `canNotBeIncreased` (also Bane-immune).
- **Battles:** 170 Battle assets; enemy max energy 5, hand cap 5, Deep Wound threshold
  `maxwounds` (default 5), deck of 20 built by cloning `deckList`. Difficulty scaling
  formulas in GAME-MECHANICS Part V. (No design corpus derived yet — do that derivation
  here when monster content onboards, per METHODOLOGY's extension recipe.)

## 6. Sim calibration

- The balance sim (`tools/sim/`) covers **91 fully-simulable shipped cards (9.9%** of
  the 916 action-routed player pool); envelopes are per-rarity P5–P95 power scores in
  each pack's BALANCE-REPORT.md.
- `INSUFFICIENT-SIM` is the *expected* verdict for status/enchantment-heavy designs —
  for those, corpus-anchored budget math (this file) + nearest-neighbor precedent +
  gate-4 in-game QA are the balance instrument, not the sim.
