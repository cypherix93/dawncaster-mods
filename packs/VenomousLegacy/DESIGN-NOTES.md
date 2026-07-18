# VenomousLegacy — design notes

**Cluster:** Attrition — poison/affliction stacking, bleed/Deep Wound, the Infected angle.
**ID block:** 700000100–700000111 (12 cards). **Colors:** DEX (green) for poison/Infected,
STR (red) for bleed/Deep Wound, DEXSTR (orange) for bridges — matches the shipped pool
(Toxicity/Poison Coating/Aura of Venom = DEX; Cutting Words/Headshot/Duel Mastery = STR;
Lacerate/Festering Wounds/Blood Fever = DEXSTR).

**Pack thesis.** Three timing facts from GAME-MECHANICS Part III are treated as design
material, not trivia:

1. **Poison ticks when the holder plays a card** (Poison.json `effectTrigger: PlayAction`),
   flat 1/tick — so tick *frequency*, not stack size, is the untapped axis.
2. **Bleeding ticks when the holder is dealt damage** (Bleeding.json `DealtDamage`) — so hit
   *count* is a bleed payoff, and overstacked bleed is wasted (−1/tick) until converted.
3. **Infected ticks per card the holder discards and is wiped by ANY health gain**
   (Infected.json `Discard` / `clearAt: GainHealth`) — while **Poison eats incoming healing
   1:1 before it lands** (DoHealing). Poison therefore *shields* Infected from heals. No
   shipped player card touches Infected at all (all six users are Monster-rarity) — that gap
   is this pack's flagship.

Rarity mix 5C/3U/3R/1L ≈ spec's 3–4/2–3/2–3/1 per 10. Cost curve 0×1, 1×7, 2×3, 3×1
(8/58/25/8%) vs pool 15/57/16/11 — same low-cost skew.

Trigger/condition ints cited below were decoded against `decompiled/EventHandler.cs`
(GameTriggers declaration order) and `decompiled/ConditionChecker.cs` (ConditionValue /
ConditionOperator order); e.g. decoded `[251:[[lastDamage]]:0]` = `AnyValue IsMoreThan
"[[lastDamage]]:0"` — the shipped "On Hit" idiom.

---

## Card-by-card

### 700000100 Weeping Blade — C, STR 1, Melee/Action
- **Role:** bleed payoff (hit count).
- **Deck:** STR bleed-tempo; wants any turn-1 bleed source, pairs with Spiteful Ground.
- **Budget:** base 2+2 = 4 dmg = 1-energy common baseline (spec §4). Conditional third hit →
  6 dmg + 3 bleed ticks, paid by the setup card. Precedent: Cleavers (C, 1 energy, 2 dmg +
  Frenzy-gated 2 dmg + 3 Bleeding).
- **Nearest:** `Cleavers.json`. **Difference:** extra attack gated on *foe Bleeding* instead
  of Frenzy — first card whose reward for bleed is more *hits* (= more Bleeding triggers),
  not a stack readout.
- **DSL:** all verified — `damage:X` + condition `StatusOnOpponent Is bleeding`
  (Wildclaw.json / Venomous_Bite.json, decoded `[62:bleeding]`). No uncertainty.

### 700000101 Open Old Wounds — C, 0-cost, Utility/Action
- **Role:** bleed setup + micro-bridge to poison via self-damage (cross-pack hook to
  CrimsonLedger's blood costs — Frenzy counts life payments, Part I).
- **Deck:** any attrition shell; free Bloodlust enabler for Gutter Toxin.
- **Budget:** 0-cost common ~2–3 dmg-equivalent: 2 Bleeding + conditional 2 Poison.
  Precedent: Withering_Touch.json (0-cost C, 3 Poison + conditional 3).
- **Nearest:** `Withering_Touch.json`. **Difference:** the conditional rider crosses
  archetypes (bleed base, poison kicker) and keys off Frenzy — no 0-cost affliction card
  reads a self-damage condition.
- **DSL:** verified — `inflict` + referenceStatus, `Frenzy Is` (Cleavers.json `[422:]`).

### 700000102 Gutter Toxin — C, DEXSTR 1, Utility/Action — **BRIDGE #1**
- **Role:** poison setup that rewards bleed-first sequencing.
- **Deck:** dual-attrition; Potency amplifies BOTH DoTs (Potency adds to every
  affliction-type tick — SpellEffects.StatusDamage).
- **Budget:** vs Toxicity (DEX 1 C, 5 Poison + 1 Potency): −1 Poison for a conditional
  +1 net Potency. Not strictly better/worse — passes the power-creep gate.
- **Nearest:** `Toxicity.json`. **Difference:** first poison card gated on a *bleed*
  condition (Bloodlust). Festering Wounds bridges the other direction (bleed-inflict →
  Poison); the play-order decision is new.
- **DSL:** verified — `bless` + Potency (Contaminate.json), `Bloodlust Is`
  (Vampirism.json / Grievous_Injury.json `[432:]`).

### 700000103 Septic Bloom — C, DEX 1, Utility/Action
- **Role:** Infected setup; the pack's timing-synergy poster child.
- **Deck:** DEX affliction stacking; core common of the Infected package.
- **Budget:** vs Toxicity: swaps 1 Poison + Potency for 1 Infected (~1–3 dmg/turn until
  wiped — enemies discard unplayed hand every turn, MonsterHandler loop).
- **Nearest:** `Toxicity.json` (shape); `Buzzbite.json` (only Infected idiom, Monster).
  **Difference:** first player-pool Infected inflicter, and the Poison half mechanically
  guards the Infected half (poison eats heals before they become "health gained", so small
  heals no longer wipe Infected). A synergy that exists only in the verified timing rules.
- **DSL:** verified — `inflict` + referenceStatus Infected (Buzzbite.json). Note: the
  poison-shields-Infected interaction itself is an inference from decompile-verified rules
  (DoHealing / clearAt GainHealth); worth one sim case to confirm eaten heals don't fire
  GainHealth.

### 700000104 Rupture — C, STR 1, Melee/Action
- **Role:** bleed→Deep Wound payoff.
- **Deck:** STR big-bleed; converts overstacked (otherwise decaying) Bleeding into
  execute progress (5 Deep Wounds slay — SpellEffects.DeepWound).
- **Budget:** 3 dmg (under 4 baseline) + conditional Deep Wound. Anchor: Cutting_Words
  (STR 1 C, 1 dmg + Flanking-gated DW, can't be upgraded).
- **Nearest:** `Cutting_Words.json`. **Difference:** every shipped DW setup keys off attack
  context (Flanking/crit/even HP/card position/Flow/basic count); none keys off the bleed
  stack the archetype already builds. New payoff for an existing setup.
- **DSL:** verified — `deepwound` under dual conditions (Cutting_Words.json
  `[251:[[lastDamage]]:0 ; 372:]`); `[[other(status)Bleeding]]` token (Vampirism.json,
  Heartache.json). No uncertainty.

### 700000105 Quarantine — U, DEX 1, Utility/Action
- **Role:** Infected engine + heal-hate hand disruption.
- **Deck:** DEX Infected tempo vs sustain enemies.
- **Budget:** 2 Infected + situational mass discard at 1 energy U.
- **Nearest:** `Silence.json` (forcediscard idiom; not player-pool — expansion None);
  `Buzzbite.json`. **Difference:** one line does three jobs no shipped card combines:
  forced discards *immediately tick* Infected (per-discard trigger), discarding their Heal
  cards removes Infected's only escape hatch, and it's the archetype's first hand attack.
- **DSL:** `forcediscard:heal` verified verbatim in Silence.json (discards enemy Heal-hint
  cards). **UNVERIFIED:** whether forcediscard'ed cards fire the `Discard` trigger and thus
  tick Infected — assumed yes (it is a discard); needs sim/in-game check. If not, the card
  still functions as Infected + heal-strip.

### 700000106 Ague Fever — U, DEX 1, Utility/Action
- **Role:** Infected enabler via energy denial.
- **Deck:** DEX Infected control vs high-energy elites.
- **Budget:** enemy max energy is 5 (MonsterHandler.GetMaxEnergy); 2 Sickened ≈ −1 enemy
  card for 2 turns + 1 Infected ≈ 1-cost U control (anchor: Sleeping_Poison.json, DEX 1 U).
- **Nearest:** `Pestilent_Authority.json` (Monster: Infected+Poison+Slowed spray).
  **Difference:** zero cards in the pool inflict Sickened (grep-confirmed; status asset +
  RunStatusEffect case exist). And the pairing is causal: fewer enemy energy → fewer plays
  → more unplayed cards discarded at their end of turn → more Infected ticks. Energy denial
  that feeds the DoT. (Deliberate tension: it *starves* Symbiotic Carapace's poison-tick
  income — a real deck-building choice inside the pack.)
- **DSL:** `inflict` + referenceStatus Sickened. **UNVERIFIED:** Sickened has never shipped
  on a card; its per-PlayPhase energy strip vs enemies needs an in-game check.

### 700000107 Salt the Wound — U, DEXSTR 1, Utility/Enchantment — **BRIDGE #2**
- **Role:** Deep Wound → affliction converter (pulls the STR wound deck toward DEX poison).
- **Deck:** DEXSTR wound-affliction hybrid (Cutting Words / Rupture / Follow Through as DW
  sources; Gutter Toxin's Potency amplifies the output).
- **Budget:** Festering_Wounds (U, DEXSTR 1) pays 2 Poison on a much more frequent trigger
  (every bleed-inflict); DW lands ~1–2/turn in a dedicated deck, so 3 Poison + 2 Bleeding
  per proc is proportional.
- **Nearest:** `Cruelty.json` (draws on the Deepwound trigger). **Difference:** the
  Deepwound→afflictions payoff has no shipped user; Cruelty pays cards, this pays damage
  and crosses archetypes.
- **DSL:** verified — ENCH `Deepwound` trigger (Cruelty.json trig 66), combat self-enchant
  shape (Carnage.json / Festering_Wounds.json: theType 0, combatEnchantment 1). Assumes the
  Deepwound trigger fires for wounds *you* inflict (true for Cruelty's wording). Low risk.

### 700000108 Symbiotic Carapace — R, DEX 2, Utility/Enchantment
- **Role:** poison tick-frequency payoff (defense).
- **Deck:** defensive DEX poison — the enemy's own card plays (which tick their Poison)
  bank Armor mid-enemy-turn, exactly when it protects; Armor fading at StartPhase
  (Armor.json) self-balances the income.
- **Budget:** ~3–5 Armor/enemy turn while poisoned ≈ sustained defensive rare at 2 energy
  (anchor: Spectral_Guardian.json, DEXSTR 2 R).
- **Nearest:** `Bolgar_Blightbane.json` (Monster-only: heal 3 per enemy poison tick).
  **Difference:** the entire player pool pays off poison stacks or inflict-events, never
  ticks; this is the tick payoff, player-side, with Armor instead of healing.
- **DSL:** ENCH `InflictStatusDamage` + `DamageType Is poison` verified verbatim in
  Bolgar_Blightbane.json (trig 78, `[702:poison]`). **Deliberate simplification /
  UNVERIFIED:** Bolgar adds a side-gating condition (decoded `[342:]` EnemyTurn) whose
  relative-vs-absolute semantics I could not verify; I dropped it and wrote the text
  symmetrically ("whenever Poison damage is dealt"), so behavior matches text under either
  reading (your own poison ticks would also feed it). Sim check requested.

### 700000109 Pyrrhic Remedy — R, DEXSTR 2, Utility/Action
- **Role:** Infected capstone / anti-heal trap.
- **Deck:** DEXSTR attrition vs sustain and healing bosses; keeps the foe choosing between
  rotting (Infected) and drinking poison (heal → 3 Poison → poison eats the next heal →
  reapplied Infected sticks).
- **Budget:** 3 Infected + combat-long heal punishment at 2 energy R. Heals are occasional,
  so the enchantment is a trap, not an engine — priced like Dressed_to_Kill (R, DEX 2,
  conditional engine).
- **Nearest:** `Bone_Pin.json` (enchant:other + GainHealth heal-hate). **Difference:**
  Bone Pin *locks* healing for one turn; this *converts* healing into afflictions for the
  combat — a punish instead of a lock, and the first card that makes Infected's escape
  hatch itself toxic.
- **DSL:** verified — `enchant:other` persistent on foe (Bone_Pin.json, Trail_of_Blood.json),
  ENCH `GainHealth` trigger (Bone_Pin.json trig 7), bless-afflicts-holder pattern
  (Blood_Levy.json), theType "other" mirrors Bone Pin's foe-enchant (theType 2) with
  combatEnchantment 1 (Carnage.json). **Assumption flagged:** poison-eaten heals don't fire
  GainHealth (DoHealing replaces healing with stack removal) — no loop expected; sim check.

### 700000110 Spiteful Ground — R, STR 2, Utility/Enchantment
- **Role:** bleed tick engine (reactive).
- **Deck:** STR bleed-stack decks — every enemy card play takes 1 piercing AND triggers
  their full Bleeding tick; also advances Weeping Blade's condition. Poison decks get the
  same event for free (their poison already ticks on enemy plays).
- **Budget:** ~3–5 piercing/turn passive at 2 energy R + tick value only in bleed decks
  (anchor: Caltrops, U, 1 Neutral, one-round burst version).
- **Nearest:** `Caltrops.json`. **Difference:** Caltrops is a one-round burst that removes
  itself; this is a combat-long frequency engine whose real payload is Bleeding triggers,
  not the ping. New mode (permanent reactive tick source), not new numbers on Caltrops.
- **DSL:** codeLine shape copied from Caltrops.json (ENCH trig 39 `EnemyPlaysAction`,
  `directdamage:...:normal:self`) including its odd `:self` token, which Caltrops
  demonstrably uses to hit the foe from a self-enchant during the enemy turn.
  **UNVERIFIED (two):** (a) that `directdamage` fires `DealtDamage` and therefore triggers
  Bleeding — Caltrops pairing pierce + Bleeding per action suggests yes; (b) the exact
  `:self` target semantics outside Caltrops's context. Both need sim; if (a) fails the card
  keeps its ping role but loses the tick-engine identity.

### 700000111 Metastasis — L, DEXSTR 3, Utility/Action — **BRIDGE capstone**
- **Role:** go-wide finisher across poison, bleed, Infected (and any splashed Decay/Bane).
- **Deck:** the dual-attrition pile: 3–4 affliction types out, then double ~25–35 stacks
  and redraw 3–4 cards ([[otherAfflictions]] counts DISTINCT afflictions — Drain_Soul.json
  usage — vs otherTotalAfflictions for stacks).
- **Budget:** L 3-cost anchors: Avatar_of_Blight (DEX 3 L), Toxic_Bloom (DEXSTR 3 L).
  Value scales with setup per spec §4's conditional rule; playCondition (foe must have an
  affliction) prevents dead draws; draw clamped by hand cap 10.
- **Nearest:** `Profanity.json` (+1/+3 all foe afflictions); `Aura_of_Venom.json` (doubles
  Poison only, every turn, C). **Difference:** rewards *breadth* — mono-poison prefers Aura
  of Venom (repeatable), flat pumps prefer Profanity; only the multi-affliction deck this
  pack builds wants Metastasis. Dodges strictly-better on both neighbors.
- **DSL:** `doubleafflictions` is in effect-commands.txt; `draw:[[X]]` token-draw verified
  (Feast_on_Misery.json `draw:[[otherAfflictions]]+[[hexes]]`); playCondition idiom
  verified (Cling_to_Sanity.json). **UNVERIFIED:** the `:other` target argument — shipped
  usage is only `doubleafflictions:self` (Falling_Apart.json). Fallbacks if sim rejects it:
  bare `doubleafflictions` (default-other like the inflict family) or Profanity-style
  `increaseafflictions:[[...]]`. Must be sim-checked before ship.

---

## Honest uncertainty ranking (for the sim-harness pass)

1. **Metastasis** — `doubleafflictions:other` target arg unverified (only `:self` shipped).
2. **Spiteful Ground** — does `directdamage` trigger Bleeding (DealtDamage)? And `:self`
   target semantics on EnemyPlaysAction (copied verbatim from Caltrops).
3. **Symbiotic Carapace** — dropped Bolgar's `[342:]` side-gate; symmetric behavior assumed
   and reflected in the text.
4. **Quarantine** — forcediscard'ed cards assumed to fire the Discard trigger (Infected
   ticks).
5. **Ague Fever** — Sickened has zero shipped card users; enemy-side behavior unchecked.
6. **Pyrrhic Remedy / Septic Bloom** — assumption that poison-eaten heals do not count as
   "gaining health" (protects Infected, avoids GainHealth loops); inference from
   decompile-verified DoHealing behavior.

Fully verified, no flags: Weeping Blade, Open Old Wounds, Gutter Toxin, Rupture,
Salt the Wound.

## Validation done offline

- All codeLine commands present in `reference/effect-commands.txt` (scripted check).
- All 12 names collision-free vs the 2,525 extracted `m_Name`s, case-insensitive
  (scripted check).
- cardIDs sequential 700000100–700000111 inside the allocated block.
- Trigger names (`PlayAction`, `Deepwound`, `InflictStatusDamage`, `GainHealth`,
  `EnemyPlaysAction`) and condition names (`AnyValue`, `StatusOnOpponent`, `Frenzy`,
  `Bloodlust`, `DamageType`) are exact enum member spellings from decompiled
  EventHandler.cs / ConditionChecker.cs.
- Art files not yet produced (ART-PIPELINE step); paths reserved as `art/<Name>.png`.

## Art direction

Treatment: sickly and wet. Toxin green stays the dominant light source (most sources are
already green-lit poison art), but every card gets bruise-purple shadow washes and bone
highlights (#4a2b52 / #d8d2b4 family) so the bleed cards and poison cards share one
infected-flesh palette; the bleed pieces keep their red but dirtied toward wine. Blades for
the bleed suite (Toxicity's weeping dagger, Plague Strike), disease-cloud and skull pieces
for the Infected angle (Cloudkill, Toxic Bloom, Withering Touch). Composites mark the body-
horror payoffs: Rupture (Lacerate gashes noise-blended into Grievous Injury's bite) and
Symbiotic Carapace (Bone Ritual's ribcage inside Aura of Venom's aura).
