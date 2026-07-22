# Malediction — design notes

**Cluster:** the Vulnerable hex — mark the foe, deepen the mark, cash it. **Classes:**
Arcanist (primary) / Seeker (secondary), INT blue. **ID block:** 700000600–700000699
(cards 700000600–700000608 bottom-up; starting card 700000697, power talent 700000698,
weapon 700000699 top-down). **Synthetic set:** 1006. **Supersedes** the retired
DC.Cartomancer (Foretell/Doom), which overlapped shipped Seeker content (Seers Orb is
already a Foretell+Doom Seeker basic; Torch/the Eclipse kit own Foretell; Focus
over-scales). Full rationale + gap-analysis: `docs/superpowers/specs/2026-07-20-malediction-vulnerable-redesign-design.md`.

## Thesis

The single most **uncontested** INT lane: a payoff-reader census over the 2,525-card
dump found **0 shipped cards read Vulnerable stacks**, versus 11–13 for burn/poison/bleed.
Vulnerable ships as a scattered debuff with no archetype. Malediction is its home: every
card marks or reads the mark, the +10%/stack amp makes the whole deck snowball, and the
mark stays **non-modal** (no reorder UI — the fix for what killed Cartomancer's foretell
basic). Avoids both sibling INT packs (ClockworkCadence = lightning/Chain; EmberweaveGrove
= fire/bury).

## Phase-0 verified mechanics (cite decompile/corpus for every claim)

1. **Vulnerable (the mark):** defender takes **+10% × stacks** incoming damage, **capped
   at 10 stacks (+100%)**, **never fades** (`GAME-MECHANICS.md` Part II L207,
   `CalculateIncomingDamage`; the sim mirrors the cap at `tools/sim/engine.py:305`
   `min(vuln, 10)`). Applied via `inflict:N` + `referenceStatus: "Vulnerable"` — the
   Shieldbreaker / Sunder_Armor / Sense_Vitals idiom (referenceStatus PathID 15608).
2. **Reading the mark is verified-generic, not a gamble.** `[[other(status)Vulnerable]]`
   resolves through `GameTextTranslator.CountStacks` (`GameTextTranslator.cs:2150-2169`):
   it strips the token to the bare name `"vulnerable"` and scans the foe's lasting effects
   by name, returning stacks or `0` — the exact path Doom/Bleeding/Armor use, generic over
   any status. "0 shipped readers" was a design gap, not an engine limit.
3. **The `Vulnerable` codeLine (the fester):** an enchantment whose codeLine is
   `Vulnerable` + a named affliction **doubles that affliction's tick damage**
   (`GAME-MECHANICS.md` Part II ~L244; shipped verbatim in `Flammable.json`). Note it
   doubles a **DoT's** ticks, not the Vulnerable amp — so The Withering is a bridge to DoT
   splashes. (Vocabulary is case-sensitive: the codeLine is `Vulnerable`, capital V.)
4. **Level-scaled debuff opener** `startstatus:[[myLevel]]*2:false` + referenceStatus is
   the Baneful_Weapon / (retired) Diviner's Weapon shape.
5. **Threshold + damage-calc gates:** `AnyValue IsMoreThan [[other(status)Vulnerable]]:N`
   is the Rupture idiom; `CalculateOutgoingDamage adddamage` gated is the
   Rigged_Explosives / Significator idiom.

## Loadout (turn-1 story)

Cursebrand pre-marks the foe (2×level Vulnerable) → Ill Omen / the basic seed and deepen
it → spells escalate as the amp climbs → a reader cashes it.

| Slot | Card | Effect |
|---|---|---|
| Weapon (BasicAttack, Magic, 1 N) | **Baleful Eye** | 2 arcane; if foe Vulnerable, inflict 1 Vulnerable (snowball, only after a seed) |
| Power (cd2) | **Cursebrand** | foe starts next combat at 2×level Vulnerable; passive +1 dmg vs Vulnerable foes |
| Starting (C, 1 INT) | **Ill Omen** | 2 arcane; inflict 2 Vulnerable |

## Card list (9)

3–4 C / 2–3 U / 2–3 R / 1 L guidance → **2 C / 3 U / 3 R / 1 L**. Every `meta.nearestExisting`
leads with a resolvable shipped card; `meta.whyDifferent` names the difference mode.

| # | Card | R | Cost | Role |
|---|---|---|---|---|
| 1 | Whispered Curse | C | 0 | cheap applier + cantrip |
| 2 | Weave the Hex | R | 2 INT | ramp engine (play a Magic card → 1 Vulnerable) |
| 3 | Comeuppance | U | 1 INT | finisher, damage = 2 + foe Vulnerable |
| 4 | Rend the Veil | C | 1 INT | damage-forward chipper |
| 5 | The Withering | R | 2 INT | bridge: double the foe's Poison/Burning/Bleeding ticks |
| 6 | Warding Gaze | U | 1 INT | Barrier = foe Vulnerable (Seeker defense) |
| 7 | Deepening Dread | U | 1 INT | applier + 5+ threshold |
| 8 | Sign of Ruin | R | 2 INT | plateau: while foe 5+ Vulnerable, Actions +2 |
| 9 | The Hex Comes Due | L | 3 INT | OneUse capstone, 2× foe Vulnerable, exile |

## Gate results (2026-07-21)

- **Gate 1 `validate`:** 0 errors (art warnings only until PNGs build).
- **Gate 2 `artcheck --distinctness`:** 0 errors / 0 warnings (curse-themed recipes,
  distinct phashes).
- **Gate 3 `sim`:** all simulable cards **ON-CURVE**; the three Vulnerable appliers land
  P50–P95 (Whispered Curse & Deepening Dread high at P75–P95 — the amp value; a gate-4
  feel watch). Enchantments read INSUFFICIENT-SIM (expected). **Toolchain note:** these
  appliers first tripped `DEGENERATE-RISK` on the scaling-ratio heuristic, which could not
  distinguish a **hard-capped** amp (Vulnerable, +100% ceiling) from an unbounded loop. Fix:
  `dsl.scaling_is_bounded` (`tools/sim/dsl.py`) exempts capped-amp statuses from the scaling
  flag; the independent static loop-sniff still catches genuine loops. 250 pytest pass; the
  other 6 packs' verdicts are unchanged.

## Uncertainty ranking = the gate-4 QA order (each has a documented fallback)

1. 🟡 **`[[other(status)Vulnerable]]` reads across the payoff layer** (Comeuppance,
   Warding Gaze, Deepening Dread threshold, Sign of Ruin, The Hex Comes Due, Baleful Eye's
   gate). Verified-generic in code (item 2 above) — confirm in-game each reads the foe's
   Vulnerable. *Fallback:* none needed if the read works; if any single card misreads,
   swap to a `StatusOnOpponent Vulnerable` condition form.
2. 🟡 **Swinginess of the base×amp double-dip** (Comeuppance, The Hex Comes Due scale off
   Vulnerable *and* are amplified by it) plus Whispered Curse / Deepening Dread sitting
   P75–P95. Bounded by the +100% cap, but verify the ceiling feels fair. *Fallback:* reduce
   the reader coefficients / applier stack counts at gate 3 (levers already isolated).
3. 🟡 **Weave the Hex — `ActionType Is magic` gate on an EndAction enchantment** (novel
   pairing; enum + reader confirmed `ConditionChecker.cs:266-267,697`). *Fallback:* tag the
   pack's Magic cards with a shared cardKeyword, gate via `PlayedCardHasKeyword`
   (Packed-With-Nails-verbatim).
4. 🟡 **Sign of Ruin — foe-status-gated `CalculateOutgoingDamage`** (both halves verbatim,
   pairing novel). *Fallback:* ungated Cruel-Intent form (+1), or scaling
   `adddamage:[[other(status)Vulnerable]]/5`.
5. 🟡 **The Withering — `enchant:other` target direction + DoT dependency.** Flammable
   applies the curse to its holder; we apply to the foe, so verify the foe-applied
   `Vulnerable` codeLine doubles the ticks THEY take. Dead without a splashed DoT
   (user-chosen bridge). *Fallback:* self-contained doubler
   `inflict:[[other(status)Vulnerable]]` Vulnerable (Strangling-Lashes idiom).
6. 🟡 **Cursebrand — `startstatus` + Vulnerable pairing** (command + status shipped,
   pairing new; Diviner's Weapon carried the identical Doom risk). *Fallback:*
   Warding-Weapon `startstatus:[[myMaxHealth]]/5` self-Barrier opener, keep the passive.
