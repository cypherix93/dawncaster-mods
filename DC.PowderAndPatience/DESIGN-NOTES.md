# PowderAndPatience — design notes

**Cluster:** Fuses — scheduled detonations; the weapon as the clock; timing vs enemy
intents. **ID block:** 700000500–700000599 (cards bottom-up 500–512; loadout top-down:
weapon 599, power talentID 598, starting card 597). **Classes:** Warrior / Hunter.
**Colors:** STR (red) for the ordnance actions, Neutral (grey) for the instruments and
the defensive/trickster pieces, 0-cost grey for the throttle — matches the shipped
cannon kit (Demolition / Toolbelt / Lock and Load = STR 1; bombs themselves = Neutral).

**Source spec:** `docs/design/PACK-LINEUP-PROPOSAL.md` § Pack 2. Every `[VERIFY]` flag
in that spec was resolved in Phase 0 below; deviations are listed at the end with
citations.

---

## Pack thesis

**Mechanical thesis (decompile-verified).**

1. **The engine already ships a complete fuse mechanic: cost-as-countdown.** The seven
   `bomb`-keyword tokens (scripted scan: Bomb, Big_Bomb, Concussive_Bomb, Cryo_Bomb,
   Shrapnel_Bomb, Soulfire_Bomb, Antimage_Bomb) are conjured `OneUse+Persistent` cards
   whose Neutral cost IS the fuse: `StartPhase → reducecost:any:1`,
   `SpendCharge → reducecost:any:1`, `Draw → setcost:N`, and a ready-gate playCondition
   `AnyValue IsLessThan [[costThisCard]]:1` (Bomb.json / Cryo_Bomb.json, decoded against
   the GameTriggers order in decompiled/EventHandler.cs:8–120). A ready bomb is a
   free Action the **player fires by hand** — detonation timing is already a decision.
2. **The Charge system is the tick bus, and it is broadcast to the hand.** Playing any
   card with `charges > 0` from hand auto-Decharges it and fires one `SpendCharge`
   event per charge spent (SpellEffects.Decharge, SpellEffects.cs:12397–12420; play-flow
   hook :615–645), and `SpendCharge` is on the engine's short whitelist of triggers that
   in-hand cards hear (EventHandler.HandTrigger, EventHandler.cs:485 — StartPhase,
   EndTurnPhase, EndAction, ResolveDamage, **SpendCharge**, GainStatusStack,
   InflictStatus, ChangeTide, SummonClone). So **a weapon that carries charges ticks
   every held bomb on every swing with zero new listeners** — shipped bombs print the
   rule themselves ("Speed up the timer when you use a Charge"). Untapped axis: 0 of the
   78 basic-keyword weapons carry charges (scripted scan) — no shipped archetype makes
   the tick *deliberate*.
3. **Planting into the enemy deck is shipped, and planted cards fire on enemy PLAY, not
   on draw.** `addcardtodeck:1:other` + referenceCard is Crossbow/Rapier-verbatim; the
   shipped plants (Snare, Glimmer, Sabotage) are 0-cost PlayAction cards with
   self-targeted payloads (`directdamage:…:self`, `bless` self-applies to the enemy —
   Glimmer's Charmed), played by the enemy AI which plays its hand in order, cost-gated
   (GAME-MECHANICS Part V). On-draw detonation does not exist → the proposal's
   documented fallback applies; the `say:"Surprise."` punchline is a shipped command
   (Hidden_Blade.json `say:Requiescat in pace`; SpellEffects.cs:2383).

**Fictional thesis.** The patient demolitionist: the fight was over before it started —
everyone else just hasn't heard the bang yet.

**Graph-extension proof.**
- **New edge:** basic-attack archetype ↔ bomb pool. Fuse-Cutter makes every swing a
  Charge event; the two archetypes never touch in Part VI's web (bombs live in the
  equipment/charges orbit, weapons in the basic-attack orbit).
- **New enabler for existing payoffs:** the kegs are new `bomb`-keyword issuers feeding
  all 8 shipped bomb readers (scripted scan: Handcannon, Stockpile, Rigged_Explosives,
  Blast_Junkie, Blast_Zone, Tinker, Demolition, Powder_Pockets).
- **New payoff for an existing enabler:** Packed with Nails converts detonations into
  Bleeding — the bombs→bleed edge (vanilla STR attrition) did not exist.

**Faucet-consolidation guard (bomb damage inflation).** The proposal's "only issuer"
claim was corrected in Phase 2: the Rogue/Warrior **cannon kit already issues bombs**
(Restock, Lock and Load, Shrapnel, Set to Blow, Toolbelt, Demolition, Powder Pockets).
This pack is instead the game's first *fuse-timing* archetype and first Warrior/Hunter
loadout access to the pool. Inflation guards, explicit: instruments are OneUse and
hand-capped (cap 10); every keg costs a card + energy to arm and 1–3 turns to mature;
ticks are finite (charges deplete: 3/copy/combat on the weapon and Short Fuse — the
depleted copy is buried, SpellEffects.cs:615–645); detonation is always a manual play;
the only damage multipliers are +1 (power passive) and the shipped payoffs the kegs
merely join. No card in the pack multiplies detonations or replays bombs in a loop.

---

## Slot plan (vs CORPUS-STATS)

| Slot | Card | Cost |
|---|---|---|
| C | Short Fuse | 0 |
| C | Satchel Charge | 1 STR |
| C | Safe Distance | 1 N |
| C | Strike the Match *(starting card, reward-pool)* | 1 STR |
| U | Parting Gift | 1 STR |
| U | Sapper's Patience | 1 STR |
| U | Controlled Demolition | 1 STR |
| R | The Long Con | 1 N |
| R | Packed with Nails | 2 STR |
| R | Chain Reaction | 2 STR |
| L | One Last Job | 2 STR |

4C / 3U / 3R / 1L = 11 player cards (spec ≈3–4/2–3/2–3/1 per 10 ✓). Cost curve
0×1, 1×7, 2×3 (9/64/27%) vs pool 15/57/16/11 — same low-cost skew as VenomousLegacy
(8/58/25/8). Plus 3 reward-excluded tokens (Powder Keg, Blasting Barrel, Ticking Smile)
and the loadout (Fuse-Cutter, Demolitionist Weapon).

**Bridges (quota ≥2):** Packed with Nails (bombs→bleed), Safe Distance (bombs→Armor
decks), Short Fuse / Satchel Charge (Charge-system bridge — 129 acquirable charge
cards, scripted scan, all become fuse accelerants; and our accelerants tick vanilla
bombs). **≥ half reads/feeds vanilla:** Short Fuse, Satchel Charge, Safe Distance,
Controlled Demolition, Sapper's Patience, Packed with Nails, Chain Reaction, One Last
Job all work with the vanilla cannon kit's bombs and/or a vanilla archetype (8/11).

---

## Phase-0 findings (the spec's [VERIFY] flags, resolved)

1. **Fuse plumbing chosen: the shipped cost-as-fuse Bomb pattern**, over the
   alternatives named in the spec:
   - *Enchantment stacks + addstack/removestack*: rejected — stacks don't gate
     playability and would need a bespoke detonation listener; no shipped precedent as
     a countdown-to-fire.
   - *Charges/repairself*: `repairself` operates on LastingEffectContainers (equipment),
     not hand cards (SpellEffects.RepairSelf, :6675) — unusable for bombs; charges ARE
     used, but as the **tick bus** (thesis fact 2), not the counter.
   - *tempvalue counters*: used for The Long Con (count-up), not for bombs — tempValue
     can't gate playability the way cost does.
   - *Doom-style statuses*: detonation would target the status holder, not scale per
     instrument; rejected.
   The Bomb pattern is shipped-verbatim ×7 tokens, gates playability, renders the
   countdown as cost orbs, and already listens to our tick bus. No contest.
2. **"Weapon swing ticks every fuse":** realized as **the weapon is a Charge card**
   (charges 3, auto-Decharge on play-from-hand → SpendCharge broadcast → every held
   bomb's own `reducecost` listener fires). Verified end-to-end at SpellEffects.cs:
   12397–12420 (event per charge, non-Equipment), :615–645 (play-flow + depletion), and
   EventHandler.cs:485 (hand scan). **Limit:** commanded basics (`basicattack:N`) are
   not played from hand → no tick (QA item #1). `reducecost` itself is self-only
   (`ReduceEnergyCost(cc,…)`, SpellEffects.cs:2226) — no command can tick another
   card's cost directly, which is why the tick must ride the SpendCharge event.
3. **Enemy on-draw verdict: unsupported.** Enemy-drawn cards fire nothing on draw;
   planted cards fire **when the enemy plays them** (Snare/Glimmer/Sabotage, all
   PlayAction; enemy plays hand in order, cost-gated, unplayed hand discarded — Part V /
   MonsterHandler). A 0-cost plant therefore fires the turn it's drawn — Ticking Smile
   uses exactly this, with the Snare self-hit idiom + Glimmer self-bless idiom and the
   kept `say:"Surprise."` punchline. This is *better* than the proposal's fallback (no
   dependence on our fuse events at all).
4. **Scrap bridge verdict: dropped.** `scavenge:N` is scrap-pile draw
   (SpellEffects.cs:877) and `addcopytoscrap` copies the *current* card container
   (:1877) — neither can grant Scrap on detonation from an enchantment cleanly. The
   proposal's Demolition Yard was cut (see deviations); the bridge quota is carried by
   bleed/Armor/Charge edges instead.
5. **Closed-vocabulary check:** every command used — `damage`, `directdamage`,
   `reducecost`, `setcost`, `conjure`, `draw`, `bless`, `inflict`, `enchant`,
   `addcardtodeck`, `playkeywordfromhand`, `addtempvalue`, `adddamage`, `decharge`,
   `say`, `addnextcardfree` — is present in `docs/research/reference/`
   effect-commands.txt / talent-commands.txt (scripted membership check via
   `dmk validate`, gate 1 green).

---

## Per-item notes

The full Role / Deck / Budget / Nearest+Difference / DSL evidence lives in each item's
`meta` block in `pack.json` (kept there so the reviewer sees it next to the code lines).
This section carries only what the manifest can't: cross-item reasoning.

- **Powder Keg (T, 700000500)** — fuse-2, 5 damage. Strictly under Cryo_Bomb (fuse-2:
  6 + 6 Frozen) on purpose: ours are mass-issued and clock-accelerated. All four
  effect lines Bomb-verbatim; zero uncertainty.
- **Blasting Barrel (T, 700000501)** — fuse-3, 12, between Soulfire_Bomb (10) and Bomb
  (20). The proposal's "Fuse 3: deal 8" was re-anchored upward after the corpus sweep —
  8 would be strictly worse than Soulfire's shipped line.
- **Ticking Smile (T, 700000502)** — 6 self-damage + self-Stunned + `say:Surprise.`
  on enemy play. Proposal's Dazed swapped for Stunned: Dazed is `canNotBeIncreased`
  and *decrements per card played* (Dazed.json) — on an enemy mid-turn it would
  half-fizzle; Stunned ("cards cannot be played", fades end of turn — Stunned.json) is
  the clean turn-break, precedented player-side by Concussive_Bomb.
- **Short Fuse (C)** — the tick as a card: 2 damage, 3 charges, no other text. The
  charge machinery is the identity; only codeLine is `damage:2`.
- **Satchel Charge (C)** — issuer that is itself a Charge card: the conjured Barrel
  hears the end-of-action SpendCharge (conjure resolves during the action, Decharge at
  EndActionStep) → arrives effectively at fuse 2, everything else ticks 1.
- **Safe Distance (C)** — 4/+2 Armor re-anchored from the proposal's 4/6-if-armed to
  Brace_for_Impact's exact 1-cost shape (4/8 external condition → 4/6 self-owned
  condition). Renamed from "Blast Shielding": shipped card **Blastshield** exists
  (scripted collision sweep) — too close visually.
- **Parting Gift (U)** — 2 damage + plant. The screenshot card.
- **Sapper's Patience (U)** — width + liquidity refund. The draw gate reads
  `[[bombsInHand]]` (proposal said "3+ Fuses armed" — same thing in engine terms).
- **Controlled Demolition (U)** — `playkeywordfromhand:bomb` verified to bypass the
  ready-gate (PlayKeywordFromHand → PlayAction directly, SpellEffects.cs:10036–10048;
  selection UI when holding several — same UX as Handcannon with 2+ bombs).
- **The Long Con (R)** — the proposal's `changethiscard` form-chain (3 extra tokens)
  replaced by the Hidden_Flurry in-hand `tempValue` idiom: counts UP 3/turn held,
  cashes for `[[tempValue+1]]`. Same fiction ("enters as a pathetic jab; lies about
  its worth"), one card instead of four, both halves verbatim-shipped.
- **Packed with Nails (R)** — the bleed bridge; replaced the cut Demolition Yard.
  Detonation-count scaling makes wide kegs vs one Barrel a real portfolio choice.
- **Chain Reaction (R)** — the cascade, via `decharge:1` in the enchantment (rank-1
  uncertainty, fallback written in meta). Bounded: 1 tick per detonation, matured kegs
  still need manual plays, nothing conjures in the loop — the sniffer confirms
  (no DEGENERATE-RISK, gate 3).
- **One Last Job (L)** — mass maturity (4 SpendCharge events in one action), not mass
  damage. The proposal's "detonations are doubled" was cut: `multiplydamage:2` is the
  crit channel (Part VIII — Weakness cancels it, refuses to stack) and no other
  verified doubling channel exists for card damage by keyword.
- **Fuse-Cutter (weapon)** — damage 1 (stat sacrifice per the weapon addendum;
  Blightpin Stiletto precedent) + charges 3. The 78-sweep names Handcannon (Warrior
  keystone — same selection menu, opposite decision: launcher vs metronome), Warmace
  (rider pricing anchor) and Blightpin (sacrifice precedent). Hidden extra cost priced
  in meta: copy depletion (18 swings/combat ceiling).
- **Demolitionist Weapon (power, cd 3)** — moved from the proposal's cd 2 to cd 3
  because "start next combat with a card" is the cd-3 ladder point (CORPUS-STATS §3);
  `addnextcardfree` + referenceCard is Deadly_Weapon's exact cd-3 line. Passive +1 to
  Bombs composes Deadly_Accuracy's t33 `adddamage` (shipped Talent) with
  Rigged_Explosives' `CardContainsKeyword:'bomb'` condition (shipped enchantment).
- **Strike the Match (starting card)** — renamed from the proposal's "Light the Fuse"
  (**name collision**: shipped Light_the_Fuse.json, scripted sweep). The Phase-2 sweep
  also found the corpus's ONE bomb-touching starting card — **Powder_Pockets, a
  Warrior keystone** — which becomes the honest nearestExisting (engine vs seed;
  detailed in meta). Loadout coherence in one sentence: *Strike the Match arms the
  keg, every Fuse-Cutter swing cuts its fuse, and Demolitionist Weapon walks into the
  next fight with one already lit.*

---

## Honest uncertainty ranking (drives gate-4 QA)

1. **Chain Reaction — `decharge:1` in enchantment context.** `decharge` has ZERO
   shipped card users. Verified on paper: enchantment effects run with
   `cc = referenceCard ?? c` (EventHandler.ProcessLastingEffects, EventHandler.cs:
   305–360) = Chain Reaction's own non-Conjured container; Decharge on 0 base charges
   fires exactly one SpendCharge and clamps to 0 (SpellEffects.cs:12397–12420);
   depletion-burial can't trigger outside the play flow. **QA:** play a keg with two
   others held; confirm both tick once and Chain Reaction survives. **Fallback:**
   swap codeLine to `directdamage:2:normal:self` (Caltrops-verbatim), retext as a
   damage echo.
2. **One Last Job — `decharge:3` in play context** (same no-shipped-user caveat; cc
   binding here is the standard played-card container, lower risk). **QA:** hold 3
   bombs incl. a fresh Barrel; confirm all ready after one play. **Fallback:**
   `playkeywordfromhand:bomb` + `damage:8` ("fire one now"), fully verified.
3. **Packed with Nails — EndAction + PlayedCardHasKeyword heard from an enchantment
   container.** Both halves shipped (Hidden_Flurry in hand; Amber_Bomb ships an
   EndAction enchantment line; PlayedCardHasKeyword reads TurnStat.currentAction —
   ConditionChecker.cs:2326). **QA:** detonate a keg, expect 3 Bleeding. **Fallback:**
   Rigged-style `adddamage` rider + move the bleed onto Powder Keg's face.
4. **Demolitionist passive — CardContainsKeyword on a talent's t33 effect** (halves
   shipped separately, combination new). **QA:** compare keg damage with/without the
   power. **Fallback:** +1 folded into keg faces + StartCombat Armor rider.
5. **Ticking Smile — self-Stunned via `bless` from an enemy-played card.** Glimmer
   proves self-bless; Stunned's `canNotBeIncreased` only blocks stacking (we apply 1).
   **QA:** plant it, watch the enemy's turn end early. **Fallback:** drop the Stun,
   raise self-damage to 8 (pure Snare-plus).
6. **Fuse-Cutter — depletion ergonomics.** Mechanics fully verified; the open question
   is *feel* (charge pips on a weapon card; copy burial in marathon fights). **QA:**
   30+ turn fight; confirm the basic rotation degrades gracefully. **Fallback:**
   charges 4.

Fully verified, no flags: Powder Keg, Blasting Barrel, Short Fuse, Satchel Charge,
Safe Distance, Parting Gift, Sapper's Patience, Controlled Demolition, The Long Con,
Strike the Match.

**QA top-3 (gate 4 plays these first):** (1) Chain Reaction cascade tick, (2) One Last
Job mass-tick + Barrel-in-same-turn, (3) Fuse-Cutter swing-tick incl. the negative
case — `basicattack:N` commanded swings must NOT tick.

---

## Validation done (scripted scans, 2026-07)

- **Bomb pool census** (`tools/out/data/Card/`, 2,525 files): 7 `bomb`-cardKeyword
  tokens (all `canBeAcquired:0`, `excludeFromRewards:1`); 8 bomb-reading cards; 5 more
  issuers by referenceCard. No fuse-timing manipulation card exists beyond the implicit
  charge side-effect.
- **Weapon corpus:** 78 basic-keyword cards (CORPUS-STATS §2 denominator); **0/78 carry
  charges** — Fuse-Cutter's combination is unused. Handcannon confirmed a Warrior
  keystone weapon (Keystone scan: type 1, name prefix Warrior).
- **Charge census:** 129 acquirable cards with `charges>0` — the tick-bus bridge
  surface.
- **Starting-card corpus (63):** exactly one bomb-touching card — Powder_Pockets
  (Warrior keystone; conjures Bomb, 3 charges); 10/63 carry charges. Strike the Match's
  nearest and its differentiation are recorded in meta.
- **Name collisions:** all 16 names checked case-insensitive vs 2,525 extracted card
  names + 383 talents + 68 sibling-pack names. Two proposal names failed and were
  renamed: *Light the Fuse* (shipped card) → **Strike the Match**; *Blast Shielding*
  (vs shipped *Blastshield*) → **Safe Distance**.
- **Enum/trigger decodes:** GameTriggers ints decoded from decompiled/EventHandler.cs
  (StartPhase=19, SpendCharge=73, Draw=12, EndAction=44, ActivateWeapon=38,
  CalculateOutgoingDamage=33); ConditionValue/Operator from ConditionChecker.cs
  (AnyValue=25, CardContainsKeyword=58, PlayedCardHasKeyword=97; IsLessThan=0,
  IsMoreThan=1, Is=2). Shipped-card cross-checks: Bomb playCondition `[25|0|
  [[costThisCard]]:1]`, Handcannon `[25|2|[[bombsInHand]]:0]`, Hidden_Flurry
  `[97|2|sinister]`, Rigged_Explosives ench `[58|2|bomb]`.
- **Gates:** `dmk all DC.PowderAndPatience/pack.json` → validate PASS (0 err/0 warn),
  artcheck --distinctness PASS, sim PASS (2 ON-CURVE, 11 INSUFFICIENT-SIM, **0
  DEGENERATE-RISK / 0 OVER**). INSUFFICIENT-SIM is the expected verdict for a
  countdown/enchantment pack (CORPUS-STATS §6); the budget math above + gate-4 QA are
  the balance instrument.

---

## Art direction

**Treatment: powder-room chiaroscuro.** Charcoal blacks, powder-red mid-tones and
brass/fuse-gold highlights (#140d0a / #8a2f1e / #e0a84a family), with a slow-match
orange glow (#ff8c2d) relighting every focal point — the whole pack looks lit by one
burning fuse. Instruments (kegs, satchels) keep their object-centered compositions from
the shipped bomb sprites; the trickster pieces (Ticking Smile, The Long Con) get harder
hue swings and dimmer values so they read as *wrong* objects. A shared 0.28 vignette +
grain finisher unifies the surfaces.

**Sources (all bomb-family or object-matched pool art; none reused by sibling packs;
none equals its card's `meta.nearestExisting`):** Powder Keg ← BOMB; Blasting Barrel ←
BLACKPOWDER-POCKETS; Ticking Smile ← CARD_UNLOCKS_7 (Glimmer, the planted trick);
Short Fuse ← MASKS_OF_MISERY_28; Satchel Charge ← SET_TO_BLOW; Safe Distance ←
BLASTSHIELD_1; Parting Gift ← SNARE; Sapper's Patience ← STARTER-GRENADE; Controlled
Demolition ← LOCK-AND-LOAD; The Long Con ← COIN-FLIP; Packed with Nails ← SHRAPNEL_2;
Chain Reaction ← MASKS_OF_MISERY_32 (Cluster Grenade); One Last Job ←
RIGGED-EXPLOSIVES.

**Same-menu adjacency (ART-MUTATION-SPEC §2):** Fuse-Cutter appears in the Warrior and
Hunter weapon menus (excluded: Warmace, Hatchet, keystone weapons Bolter / Forgehammer /
Hammer / **Handcannon** / Mauler / Reaver / Warfists / Cleavers / Duskblades / Greatbow /
Ritual_Totem / Soul_Kama / Stormaxe / Tomahawk, plus sibling mod Flaying Hook and its
source) — it sources **WOODCARVING_KNIFE** (Carving Knife, a pool card) at 512×873
portrait. Demolitionist Weapon must avoid Warrior/Hunter tier-0 talent art — like
VenomousLegacy, it uses card art only: **TOOLBELT**. Strike the Match sits in the
Warrior/Hunter starting-card menus (excluded sprite set from the keystone scan:
BATTLERAGE, **BLACKPOWDER-POCKETS** (its own nearest — doubly barred), CARD_UNLOCKS_20/
21/27/35, COURT-OF-COMBAT_OLD, DEATH_OF_DIVINITY_0, EARTHEN-SHIELD, FADE-TO-BLACK,
TOTEM-STRIKE, CARDART_2_11, CARDS_METAMORPHOSIS_2_21, plus defaults Backswing/Feral
Strikes and sibling First Blood's DRAW_BLOOD) — it sources **ABILITYART_2_0** (Light
the Fuse, a pool card outside those menus). BLACKPOWDER-POCKETS *is* used for Blasting
Barrel, which appears in no selection menu (tokens are conjured), so the same-menu rule
does not bind it; the ≤2-uses-per-source budget holds (1 use).

Distinctness gate: `validate_art --pack DC.PowderAndPatience --distinctness` → 0
errors (every output distinct from source and siblings; every recipe includes
composition ops — mirror/zoom/rotate — beyond recolor).
