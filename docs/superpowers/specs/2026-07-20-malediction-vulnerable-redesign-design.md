---
date: 2026-07-20
slug: malediction-vulnerable-redesign
supersedes: DC.Cartomancer (foretell/Doom — retired for UX + shipped-overlap reasons)
status: design-approved (blueprint locked; pre-implementation)
---

# Pack redesign spec — DC.Cartomancer → "Malediction" (the Vulnerable hex)

## Why this redesign exists

Gate-4 QA found the Cartomancer basic attack (**Cartomancer's Edge**, `foretell:1`
every swing) is a **user-side no-op that costs 3-4 clicks per turn**: `foretell:1`
reveals exactly one card, so the "reorder" is a modal with no choice, and its only
payoff (arming When-Foretold cards) doesn't justify the per-swing click tax. There is
**no non-modal foretell** — `foretell:N` *is* the reorder UI and the only way to fire
the Reveal trigger — so foretell-on-a-basic-attack is structurally UX-hostile.

Deeper problem surfaced during triage: **Cartomancer was built next to a crowded
niche.** Shipped **Seers Orb** is already a Seeker Foretell+Doom basic weapon; **Torch**
and the Eclipse kit own Foretell; **Focus** over-scales (feeds draw + everything). So
foretell, Doom, and Focus are all unavailable as a distinct spine. User direction:
**full redesign, keep Seeker/Arcanist (INT loadout unchanged), open the fantasy.**

## The chosen lane — grounded gap-analysis

A payoff-reader census over the 2,525-card dump (`tools/out/data/Card/`, counting cards
that scale off a status via the `[[my(status)X]]`/`[[other(status)X]]` token —
`GameTextTranslator.cs:192,2156`) shows which "blue" statuses ship as **appliers with
no payoff layer**:

```
Poison 13 · Bleeding 13 · Burning 11 · Chain 7   <- established archetypes
Frozen 5 · Weakness 1 · Insight 1 · Vulnerable 0  <- underserved / vacant
```

**Vulnerable is the single most uncontested mechanic in the INT space: 0 payoff-readers**,
plus a totally unused engine hook. It avoids Foretell/Doom/Focus and both sibling INT
packs (ClockworkCadence = lightning/Chain/combo; EmberweaveGrove = fire/bury/graveyard).

### Verified mechanics (Phase-0)

- **Vulnerable (the status):** defender takes **+10% × stacks** incoming damage, **capped
  at 10 stacks (+100%)**, **never fades** (GAME-MECHANICS Part II; `CalculateIncomingDamage`;
  `StatusEffect/Vulnerable.json`). Applied via `inflict:N` + `referenceStatus: Vulnerable`
  (the 441-card `inflict*` idiom, Part VI). Read via `[[other(status)Vulnerable]]` or a
  `StatusOnOpponent Vulnerable` condition. **All non-modal.**
- **The `vulnerable:<status>` codeLine hook (the "fester"):** an enchantment carrying a
  `vulnerable` codeLine naming a status **doubles that status's tick damage** (StatusDamage;
  GAME-MECHANICS Part II ~L244) — ships in **zero** cards. Build-around capstone.
  ⚠ **GATE-1 RISK:** confirm `vulnerable` is in the closed *codeLine* vocabulary
  (`docs/research/reference/effect-commands.txt`), not merely a status name. Documented
  fallback below if it isn't.
- Shipped appliers proving the status works: `Hex_of_Vulnerability`, `Sunder_Armor`,
  `Shieldbreaker`, `Sense_Vitals`, `Flammable` (11 appliers total).

## Identity

**Working title: "Malediction" (the Maledictor / curse-caster).** *Provisional — may be
`namestorm`ed; "Malediction / Hexbind / Evil Eye" are candidates.* Arcanist primary /
Seeker secondary, **INT blue — loadout class unchanged**.

**Core loop (all non-modal):** mark the foe with a deepening **Vulnerable** hex → every
card lands harder as the mark grows (+10%/stack, cap +100%) → dedicated payoffs read the
stack count and cash it. The deeper the curse, the worse everything hurts. Second, quieter
pillar: the mark can **fester** — a capstone doubles any affliction ticking on the marked
foe.

**Budget/discipline note (the swinginess risk the user flagged):** Vulnerable is a
*general* incoming-damage amp, so cards that *also* read the stacks compound. At gate 3:
keep our own stack-reader nukes honest (small per-stack coefficients), pace the
application rate (don't hand out cap-fast Vulnerable), and lean on the amp doing the work
rather than stacking amp + big flat burst on one card.

## Loadout (turn-1 story) — sketches, DSL/numbers pinned at draft/gate-3

| Slot | Sketch | Role |
|---|---|---|
| **Weapon** (basic, 1 Neutral, Magic, 2 dmg) | Deal 2 arcane. If the foe is **Vulnerable**, inflict 1 more Vulnerable. | non-modal metronome; **deepens** the mark once seeded, rewards keeping it up |
| **Weapon power** (cd 2) | Start next combat with the foe at **2 Vulnerable**. Passive: a small rider tied to inflicting Vulnerable. | plants the opening mark; shipped `startstatus` opener shape (Baneful/Diviner's) |
| **Starting card** (C, 1 INT) | Deal 2 arcane. Inflict **2 Vulnerable**. | the seed applier — never dead |

**Turn-1 story:** the power pre-marks the foe → the seed card + basic deepen it → your
spells escalate as the hex grows → a reader cashes it.

## Card list (9) — roles + mechanical hooks (names provisional; uniqueness scan at gate-1/2)

| # | Provisional name | R | Cost | Hook | Role |
|---|---|---|---|---|---|
| 1 | Whispered Curse | C | 0 | Inflict 1 Vulnerable + minor value (draw/tempo). | cheap fuel |
| 2 | Weave the Hex | R | 2 INT | Enchantment: whenever you play a **Magic** card, inflict 1 Vulnerable. | the ramp engine — a spell-dense turn snowballs the mark |
| 3 | Reckoning | U | 1 INT | Damage scaling on `[[other(status)Vulnerable]]` (honest coefficient). | stack-reader finisher |
| 4 | Rend the Veil | C/U | 1 INT | Inflict 3 Vulnerable (+ minor). | bigger single-hit applier (Sunder idiom, INT-recolored) |
| 5 | The Withering | R/L | 2 INT | Enchantment, `vulnerable:<status>`: double affliction ticks on the marked foe. | **fester capstone** ⚠ gate-1 vocab check |
| 6 | Warding Gaze | U | 1 INT | Seeker-side: Barrier / tempo scaled by the foe's Vulnerable. | defensive bridge to Seeker |
| 7 | Deepening Dread | U | 1 INT | Inflict Vulnerable; threshold bonus at 5+. | threshold payoff |
| 8 | Sign of Ruin | R | 2 INT | Enchantment: while the foe has 5+ Vulnerable, your Actions deal +2. | payoff plateau (Significator/Stage-Presence shape) |
| 9 | The Hex Comes Due | L | 3 INT | OneUse: big bounded scaling off total Vulnerable, then exile. | capstone — "the mark comes due" |

**Fallback for #5 if `vulnerable` isn't a valid codeLine:** replace with a second
pure-Vulnerable payoff (e.g. a stack-doubling `inflict:[[other(status)Vulnerable]]`
Vulnerable applier — the Strangling-Lashes doubling idiom, verbatim status-safe), keeping
the slot as a curse-deepener with no new engine dependency.

## Collision / differentiation

- Sunder/Shieldbreaker are shipped **Warrior armor-break** — we differentiate by keeping
  this an **INT spell-amp hex** (percentage mark + spell payoffs), not physical armor
  reduction.
- No overlap with the sibling INT packs (lightning/fire) or the retired foretell/Doom
  space.

## Identity/ID bookkeeping (low — reuse in place)

- **Reuse Cartomancer's existing block `700000600-699` in place.** Cards allocate under
  the same IDs (new content, same numbers); no new allocation, no ID-REGISTRY math.
- **Rename is ID-free:** `idBlock` lives in pack.json independent of the name; the
  synthetic set value derives from the *block* (`1000 + 600/100 = 1006`), so the rename
  keeps set **1006**. (Confirmed: these packs use manual sequential blocks, not AutoId;
  AutoId would *move* the block and add churn — reuse-in-place is the low-bookkeeping path.)
- **Pre-release, no save-break.** One-line note in `docs/ID-REGISTRY.md`: block
  `700000600-699` pack renamed Cartomancer → Malediction, repurposed.

## Engine impact: none

100% `pack.json` data-pack work. Every mechanic (`inflict` + `referenceStatus`,
`[[other(status)Vulnerable]]`, enchantment engines) is in the shipped 565-command DSL —
**no C# engine changes**. The sole exception risk is #5's `vulnerable` codeLine (gate-1
vocab check with a documented fallback).

## Implementation loop (METHODOLOGY)

1. **Phase-0 finish:** confirm `vulnerable` codeLine vocabulary; pin Vulnerable
   apply/read idioms against the cited appliers.
2. **Phase-2 corpus scans:** nearest-neighbor sweep for each of the 9 cards + loadout over
   all 2,525 cards + the 78-weapon corpus (name uniqueness, "text-not-stats" difference
   mode, no shipped duplicate).
3. **Rename+reskin:** dir `DC.Cartomancer` → `DC.Malediction`, `pack` field, DESIGN-NOTES,
   art-recipes; keep `idBlock`; rewrite 9 cards + loadout; regenerate art from recipes.
4. **Gates:** `dmk validate` → `dmk artcheck --distinctness` → `dmk sim` (Vulnerable-amp
   cards are simulable; enchantment engines may read INSUFFICIENT-SIM by design) → in-game
   gate-4 QA in the `xdctest0` sandbox with `ModOnlyRewards`.
5. **ID-REGISTRY note + DESIGN-NOTES** (evidence per METHODOLOGY checklist, uncertainty
   ranking with documented fallbacks — esp. #5).

## Open items for the user

- **Final name** (provisional "Malediction") — lock now or `namestorm` before step 3.
- **Fester capstone (#5)** — keep as the `vulnerable:<status>` doubler (pending gate-1),
  or pre-emptively take the pure-Vulnerable fallback and skip the vocab risk.
