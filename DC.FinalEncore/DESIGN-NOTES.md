# FinalEncore — design notes

**Cluster:** Perform / showmanship tempo — the Rogue loadout pack (proposal: Pack 1,
`docs/design/PACK-LINEUP-PROPOSAL.md`; takes over the Rogue slot from VenomousLegacy).
**ID block:** 700000400–700000499 (cards bottom-up 700000400–700000408; loadout top-down
weapon 700000499, power 700000498, starting card 700000497 per WEAPON-SPEC §3).
**Colors:** DEX (green) throughout — the shipped Perform space is DEX-dominant (Chorus,
Abracardabra, Flourishing Bow, Daring Dash, Crescendo, Break a Leg all DEX; scripted
scan) and DEX is the Rogue identity per Card.GetColor. The Final Encore (a Song) costs
`{}` → Brown frame, matching all 13 shipped Performance-category cards.

**Pack thesis (mechanical — all three facts decompile-verified in Phase 0).**

1. **Perform is not a counter; it is an act.** `perform:X` upticks the stacks of the
   single active `EffectType.performance` lasting-effect container and fires the
   `Perform` trigger with v=X; with **no** act open it pauses the queue and delves a new
   Performance **from all cards game-wide** carrying the current performance-type
   keyword ("song" default, `fromPlayerCards:false`) — SpellEffects.ExecutePerform
   (SpellEffects.cs:10659-10706) + Delve default branch (SpellEffects.cs:11727-11740).
   Consequence: *every Perform card is also a doorway into the vanilla Song toolkit*,
   and a new Performance-category card extends every bard run in the game.
2. **Completion is a threshold, and the graveyard hears it.** When stacks reach the
   act's `utilityNumber`, its CardEnchantments payload runs, the container is removed,
   and `FinishPerformance` fires (UptickPerformance, SpellEffects.cs:10743-10757).
   `FinishPerformance` is one of exactly three triggers that additionally scan the
   **graveyard** (EventHandler.DiscardTrigger, EventHandler.cs:474-481; scan loop
   434-452) — a spent card can still take its bow. The untapped axis: every shipped
   graveyard user of the trigger does the same thing (`return:this` ×5 — scripted scan).
3. **The act is readable and durable within combat, and only there.**
   `[[performanceStacks]]` reads the active act's stacks, 0 when none
   (GameTextTranslator.cs:432, 1290-1308); `Performing`/`Finale` are ConditionValues
   (ConditionChecker.cs:1302-1310, 1397-1417). The container persists across turns but
   lives in `combatData` and `ExecutePerform` dead-ends outside combat
   (SpellEffects.cs:10662-10678) — which **falsified** the proposal's "start next combat
   with 2 Perform" weapon power (see Showstopper Weapon below).

**Fictional thesis:** a stage magician whose act ends with a body — the mandatory
every-turn swing is the metronome, and the finale is performed by cards you already
spent.

**Graph-extension proof.**
- **New edges:** Perform ↔ bury/Firecast (The Vanishing Act — scripted scan: zero
  shipped cards combine the two systems) and Perform ↔ discard-as-*enabler* (Card
  Trick; the shipped edge only runs discard→Perform via Encore!/Juggling Balls).
- **New payoff for an existing enabler:** The Final Encore joins the game-wide Song
  delve pool, so all 34 shipped `perform:` users gain a new act choice; Curtain Call
  gives act *completion* its first repeatable damage payoff.
- **New enabler for an existing payoff:** Crowd Work / Opening Number / Encore Blade
  are the archetype's first *aggressive* fuel (scan: zero shipped damage-Actions carry
  `perform:`) feeding every shipped Song/finish payoff.

**Slot plan vs CORPUS-STATS.** Reward-facing cards: 4C (Crowd Work, Smoke and Mirrors,
Knife Juggling + starting card Opening Number) / 3U / 2R / 1L-delve-only = 10 cards
(≤12 cap); mix ≈ the 3–4C/2–3U/2–3R/1L per-10 guidance, with the Legendary deliberately
**outside** the reward pool (Songs are delve-only in the shipped game — 13/13 scripted
scan — so the pack's Legendary never dilutes a reward roll at all). Cost curve: 1-cost
×8, 2-cost ×2, plus the costless Song — matches the pool's 57% 1-cost skew. Bridge
quota: ≥2 explicit bridges (Card Trick, The Vanishing Act; The Final Encore and Dead
Man's Encore also read the graveyard) and *every* card feeds or reads the vanilla
Perform/Song system, with Smoke and Mirrors/Knife Juggling additionally touching the
vanilla Evasion and on-hit orbits.

Trigger/condition ints cited below were decoded against `decompiled/EventHandler.cs`
(GameTriggers declaration order) and `decompiled/ConditionChecker.cs` (ConditionValue
order; JSON key `valueToCheck`, operator `conditonOperator` — canonical typo): e.g.
decoded `[14,2,'2']` = `CardsPlayedThisTurn Is '2'` (Vigorating Strike), `[46,2,'']` =
`Finale Is` (Daring Dash), `[26,2,'']` = `Ambush Is` (Daggers).

---

## Phase-0 verification record (the proposal's [VERIFY] flags)

| Proposal assumption | Verdict | Evidence |
|---|---|---|
| `perform` advances a visible counter | **REVISED** — it advances the active *Performance container* (or opens the game-wide Song selection when none is active) | SpellEffects.cs:10659-10706 (ExecutePerform), 10713-10758 (UptickPerformance); Delve default branch 11727-11740 filters `AssetManager.allCards` by the "song" keyword, sorts, shows all |
| `FinishPerformance` fires on completion and scans the graveyard | **CONFIRMED** | UptickPerformance fires it after the payload at stacks ≥ utilityNumber (SpellEffects.cs:10743-10757); EventHandler.DiscardTrigger returns true for exactly EnterStance/AttuneTrigger/FinishPerformance (EventHandler.cs:474-481) and HandleDiscardTriggers iterates `combatData.graveyard` (434-452, player turn only) |
| Perform count readable as a value token | **CONFIRMED** — `[[performanceStacks]]` (0 when no act) | GameTextTranslator.cs:432, 1290-1308; live in Break_a_Leg.json `damage:[[performanceStacks]]*5`, Rattler.json text |
| Perform persists within combat | **CONFIRMED** (lasting-effect container, consumed at completion) | SpellEffects.cs:10662 (P_lastingEffects lookup), 10755 (removal) |
| Perform grantable at combat start / storable pre-combat (weapon power) | **FALSIFIED** — containers live in `combatData`; ExecutePerform requires an enemy with health and dead-ends otherwise | SpellEffects.cs:10662-10678; ActivateWeapon fires in exploration (WEAPON-SPEC §1, PlayerUIHandler.cs:1600-1645) → fallback per the proposal: cd-3 free-opener (`addnextcardfree`, Archivists_Weapon.json template) |
| "Perform this combat" cumulative counter (proposal's Curtain Call / The Final Encore numbers) | **FALSIFIED** — no such counter or token exists; stacks are consumed at completion | ExecutePerform/UptickPerformance are the only writers; GameTextTranslator has only `[[performanceStacks]]` → both cards redesigned (see per-item blocks) |

**Corpus scans (2026-07-20, scripted, over `tools/out/data/`):** the Perform space is
**90 cards** (34 `perform:` users, 13 Performance-category, 12 Hymns, plus readers/
triggers). Key occupancy findings that reshaped the proposal list: Abracardabra +
Inspiring Shanty own "Perform + draw" (killed the proposed 0-cost Opening Number);
Break a Leg owns the stack-readout burst (killed the proposed Curtain Call); Encore! +
Juggling Balls own discard→Perform (killed the proposed Dead Man's Encore enchantment);
Grand Entrance owns first-action-positional Perform (Ambush); Crescendo owns
"finish the act and start the next"; shipped Songs: 5 "song"-keyword (thresholds 2–6,
all R/L, all delve-only), 6 rituals, 3 shredder-pool.

---

## Card-by-card

### 700000400 Crowd Work — C, DEX 1, Melee/Action
- **Role:** aggressive act fuel — the archetype's bread-and-butter gap.
- **Deck:** any vanilla DEX perform/bard shell (Chorus/Abracardabra/Snake Dance) or
  Harp/Rattler/Throwing Knife weapon-swap run; standalone a fair beater whose Perform
  opens the vanilla Song delve.
- **Budget:** 2 dmg + Perform 1 ≈ 4 dmg-equivalent at 1 energy (Rattler prices the
  exchange: 2 dmg == Perform 1). Precedent: Chorus (C, 1 DEX, perform:1 + rider).
- **Nearest:** Chorus.json; Rattler.json. **Difference (mode 3):** scripted scan — zero
  of the 34 shipped `perform:` users are damage Actions; every perform:1 common pays
  utility. First fuel that attacks while the act builds.
- **DSL:** all verified — `perform:1` (Chorus.json verbatim), `damage:2`. No flags.

### 700000401 Smoke and Mirrors — C, DEX 1, Utility/Action
- **Role:** defense that keeps the act running.
- **Deck:** defensive DEX shells (Dash/Lay In Wait orbit); standalone Dash-par.
- **Budget:** shipped 1-DEX Evasion common curve = 1 Evasion + one conditional rider
  (Dash, Come get me!, Lay In Wait, Flourishing Bow — scripted scan). Matched exactly;
  the Perform 1 rider is paid by having an act open.
- **Nearest:** Dash.json; Flourishing_Bow.json. **Difference (mode 2):** the
  `Performing` condition is used by **zero Commons** in the pool (its users are R/L
  build-arounds — Elegance, Serenade, Moondancing, Crescendo, Shredder Axe); shipped
  Evasion commons all gate on position (Ambush) or hand state (Finale). First common
  that pays for keeping an act alive, and its rider advances the state it reads.
- **DSL:** verified — `bless` + referenceStatus Evasion (441-card idiom; Evasion.json),
  Performing condition (ConditionChecker.cs:1302, schema enum member). No flags.

### 700000402 Knife Juggling — C, DEX 1, Melee/Action
- **Role:** multi-hit Performing payoff.
- **Deck:** DEX tempo with on-hit payoffs — Throwing Knife runs close the loop (its
  "Perform every 3 Hits" feeds the act these hits reference); standalone 4 dmg par.
- **Budget:** base 2+2 = 4 (1-energy common baseline); conditional third hit → 6, paid
  by the act setup. Precedent: Cleavers (C, 1 energy, same skeleton, Frenzy gate).
- **Nearest:** Cleavers.json; Shredder_Axe.json (only shipped Performing attack payoff
  — pays crit, not count). **Difference (mode 4):** hit *count* as the Performing
  payoff is unused; chains into the on-hit orbit.
- **DSL:** verified — multi-effect damage (Cleavers/Weeping Blade encoding),
  Performing condition. No flags.

### 700000403 Card Trick — U, DEX 1, Utility/Action — **BRIDGE #1**
- **Role:** card flow + the discard-*enabler* side of the perform↔graveyard edge.
- **Deck:** vanilla discard/Ancestral shells and any perform deck needing flow;
  deliberately sends Dead Man's Encore to the graveyard early.
- **Budget:** anchor Tempered_Soul.json (U: `draw:2` + `discard:1:choose` verbatim
  codeLines + conditional third draw); the Scholar rider is swapped for an
  unconditional Perform 1 — sidegrade, passes power-creep both directions.
- **Nearest:** Tempered_Soul.json; Encore_.json / Juggling_Balls.json (the shipped
  direction: discard→Perform). **Difference (mode 1):** the enabler side of that edge
  has no shipped card — a perform card that *feeds* the discard pile on purpose. The
  decision: which card takes the early bow.
- **DSL:** all three commands verified verbatim (Tempered_Soul.json, Chorus.json). No
  flags.

### 700000404 The Vanishing Act — U, DEX 1, Utility/Action — **BRIDGE #2**
- **Role:** one big act push that removes itself — and its bury event is currency in
  the vanilla burn/Firecast web (GameTriggers.Bury, `[[cardsBuried]]`).
- **Deck:** vanilla bury/Firecast decks (a Bury event with no fire cost) and
  reshuffle-averse perform tempo; standalone 2 Evasion + Perform 2, once per combat.
- **Budget:** above the 1-Evasion common par — the overage (2 Evasion + Perform 2 ≈ 6
  dmg-equivalent) is priced by losing the card for the rest of the combat in a
  reshuffle game (METHODOLOGY §4.1.3 setup-cost rule); Uncommon slot.
- **Nearest:** Grand_Entrance.json (pays Perform 2 for position); Beseech.json /
  Turn_to_Ashes.json (the self-bury mechanism). **Difference (mode 2):** scripted scan
  — zero of the 90 Perform-space cards bury or reference bury, and zero Bury-archetype
  cards Perform. The decision is *when* to spend the trick forever.
- **DSL:** self-bury = `enchant:self` helper whose PostAction runs
  `buryplayed;removeenchant:this` — Turn_to_Ashes.json codeLine verbatim; Beseech.json
  proves the helper catches its own card's PostAction (its text buries Beseech itself).
  Helper enchantment shape (type affliction, combat:false) copies Beseech. Low risk;
  QA confirms the bury lands on itself and not the next card.

### 700000405 Second Act — U, DEX 1, Melee/Action
- **Role:** hand-pacing payoff — the Finale that *refills*.
- **Deck:** any low-cost DEX dump deck (pool is 57% 1-cost); standalone 4 dmg par with
  a dead rider.
- **Budget:** damage:4 base at 1 DEX; Finale riders (Perform 2 + draw 2) paid by
  emptying the hand first (Finale = hand nearly empty, ConditionChecker.cs:1397-1417).
  Anchor: Daring_Dash.json prices a bigger Finale ceiling at Rare; Flourishing Bow's
  Finale: Perform 2 at Common with a richer base — this sits between, at U.
- **Nearest:** Daring_Dash.json; Flourishing_Bow.json. **Difference (mode 3):**
  shipped Finale-perform cards pay act progress or energy; none refills. New decision:
  dump the hand *into* the finale.
- **DSL:** verified — Finale condition decoded from Daring_Dash.json (`[46,2,'']`);
  draw clamped by hand cap 10. No flags.

### 700000406 Curtain Call — R, DEX 2, Utility/Enchantment
- **Role:** act-completion payoff — every act's end is a blast.
- **Deck:** bard shells running cheap-threshold songs + this pack's fuel density; dead
  without perform sources → Rare build-around slot (commons carry the pack,
  METHODOLOGY §4.3.5).
- **Budget:** a committed deck finishes a 2–3-threshold act every ~1.5–2 turns → ~3–4
  dmg/turn sustained at 2 DEX Rare (cf. Scalding, same cost/rarity/trigger surface:
  ~n damage per n-perform act vs my flat 6 — crossover at 6, neither dominates).
- **Nearest:** Scalding.json; Crowd_goes_Wild.json / Angelic_Choir.json (the other
  finish payoffs: items/heals). **Difference (mode 4):** Scalding's damage is built
  per-perform (wants one long act); flat-per-completion inverts the incentive (wants
  many short acts) — making Song *choice by threshold* a real decision for the first
  time. Repeatable immediate damage on FinishPerformance has no shipped user.
- **DSL:** ENCH FinishPerformance trigger verbatim (Scalding.json/Angelic_Choir.json,
  trig 67); enchant:self blessing/combat shape (Salt the Wound / Carnage.json); Unique
  keyword prevents stacking. No flags beyond the trigger's own QA pass.

### 700000407 Dead Man's Encore — R, DEX 2, Melee/Action
- **Role:** the graveyard-as-audience flagship — the dead man strikes on every finale.
- **Deck:** perform decks with discard outlets (Card Trick; vanilla Ancestral shells);
  standalone a clean 5-damage rare.
- **Budget:** Rare 2-cost damage envelope 5–8 (CORPUS-STATS §1): base 5 at the low end;
  the gap pays the graveyard rider (~3 per completion once dead). Not strictly better
  than Break a Leg (U, 2 DEX, 5×stacks burst — bigger ceiling, no engine).
- **Nearest:** Chump_Change.json (graveyard FinishPerformance — like Soulfood and the
  three Dances, all `return:this`); Scalding.json. **Difference (mode 2):** the
  graveyard-scanning trigger has exactly one shipped payload shape (return to hand);
  this is the first card whose bow is a *payload*. New decision: discard it on purpose
  to install the engine.
- **DSL:** FinishPerformance on an Action card firing from the graveyard is
  decompile-verified (EventHandler.cs:434-452; hand/deck scans exclude the trigger —
  HandTrigger list EventHandler.cs:483-490, RunCardsInDeck only for
  StartCombat/NoCombat/EnterCombat, EventHandler.cs:159-163/289-293).
  **UNVERIFIED:** a `damage:` payload from a graveyard container (shipped graveyard
  payloads are only `return:this`; same StartTriggeredEffects path). Fallback: swap
  payload to the Chump Change `return:this` shape. **QA item #1.**

### 700000408 The Final Encore — L, Performance (Song), costless, delve-only
- **Role:** the pack's namesake act — a new Song in the vanilla delve pool whose
  audience is the graveyard.
- **Deck:** chosen at Song selection, not pulled (excludeFromRewards, like 13/13
  shipped Performance cards); spectacular in discard/mill shells. Picking it over Song
  of Sages' draw is the pack's signature decision.
- **Budget:** Songs price by threshold (2–6): threshold 4, payload 2×graveyard
  (typical turn 5–8: 20–40 damage) vs Song of Slaughter (threshold 6, 7×level ≈
  35–56) — lower threshold but investment-gated scaling; crossover, not domination.
- **Nearest:** Song_of_Slaughter.json; Song_of_Strength.json. **Difference (mode 3):**
  extends the entire perform enabler web (ExecutePerform delves game-wide from the
  "song" keyword — SpellEffects.cs:10699); and no shipped Performance reads the
  graveyard (scripted scan) — the damage songs scale with level.
- **DSL:** payload-as-CardEnchantments (type performance, combat, showstacks) with
  trigger None verbatim from Song_of_Slaughter.json; `[[cardsInGraveyard]]` token
  (GAME-MECHANICS Part VI); token*N arithmetic (Break_a_Leg.json). The PlayAction
  `performancetype:song` line is a deliberate no-op filler — shipped Songs carry an
  empty effect list but validate_pack requires ≥1 effect (performancetype verified in
  Ritualist.json/Dance_of_Blight.json; re-asserts the default). **UNVERIFIED:** first
  mod Performance-category card through the loader (schema supports the category and
  enchantment type; runtime untested). **QA item #2** — confirm it appears in the Song
  delve, completes at 4, and its filler line is inert.

---

## Weapons & powers (manifest v1.1)

**Shipped curve (CORPUS-STATS §2–3):** weapons = damage 2, 1 Neutral, Common, exactly
one rider (Warmace: 2 Neutral for an ungated rider); uniqueness denominator = **all 78
basic-keyword weapons**. Powers (66 tier-0 talents): 79% at cd 2–3; cd 3 = a
start-of-combat card (choice-of-3 or free) or 2–3×lvl statuses; passive riders are
small per-trigger values that never scale with cooldown.

### Weapon: Encore Blade — 700000499, Melee, Rogue
- **Role:** the metronome — sequencing-gated Perform in the basic-attack slot.
- **Statline:** *Deal 2 damage. If this is not the first Action you played this turn,
  Perform 1.* Exactly the starting-six shape: damage 2, 1 Neutral, Common, one
  conditional rider.
- **78-weapon sweep (scripted):** 0 of 78 basics gate any rider on play sequencing
  (CardsPlayedThisTurn/AlreadyPlayedThisTurn — zero users among basics). The four
  Perform-adjacent basics gate differently: Harp (Perform 1, no damage, Rebound
  hymns), Rattler (Flanking: Perform *instead of* damage), Throwing Knife (Perform per
  3 hits), Shredder Axe (elite; Performing→crit). Encore Blade keeps both halves only
  when sequenced — it duplicates none of their decisions, and rewards the *opposite*
  sequencing of the Rogue incumbent Daggers (Ambush wants the weapon led; this wants
  it second): a real menu choice.
- **Budget:** Perform 1/turn while sequenced ≈ the Rattler exchange rate (2 dmg),
  gated behind surrendering Ambush-style leads — under Warmace's ungated rider and far
  under Daggers' led ceiling ((2+3)×2 crit).
- **DSL:** `CardsPlayedThisTurn IsMoreThan 0` — off-by-one is shipped-verified: at
  effect time the queue does NOT count the resolving card (Vigorating_Strike.json,
  "third card of the round" = decoded `[14,2,'2']`). **QA flag:** confirm basic-attack
  plays themselves increment the PlayAction queue count for later swings.

### Power: Showstopper Weapon — 700000498, cd 3, Rogue
- **Role:** act ignition (exploration payoff) + completion sustain (passive).
- **Text:** *Start next combat with a free Opening Number. Passive: whenever you
  finish a Performance, restore 2 HEALTH.*
- **Phase-0 note:** the proposal's "start next combat with 2 Perform" is **falsified**
  (thesis fact 3); this is the proposal's own documented fallback — the cd-3
  free-opener ladder mode, seeded with the pack's ignition card so turn 1 still opens
  the show *and* arms the weapon's gate for free.
- **Budget:** Archivists_Weapon.json is the exact template (cd 3: `addnextcardfree:1`
  + one referenceCard + a small heal-per-trigger passive on another trigger). Passive
  ~2–4 HP/combat (1–2 completions) — inside the shipped 1–3-per-trigger rider band.
- **Uniqueness:** 0 of the 66 tier-0 talents touch the Perform system (scripted scan)
  — the archetype has weapons but no weapon power.
- **DSL/flags:** `addnextcardfree` in talent-commands.txt; **UNVERIFIED (two):**
  FinishPerformance as a tier-0 rider trigger has no shipped precedent (legal enum
  member; CheckTalents scans talents on every trigger — EventHandler.cs:150), and
  addnextcardfree referencing a same-pack card assumes cards register before talents.
  **QA item #3.**

---

## Starting card (manifest v1.2)

**Shipped curve (CORPUS-STATS §4):** 63-card corpus, cost 1 on 81%, C/U for mods, 1–3
lines, archetype-seed shape, normal reward-pool flags, reliability over power.

### Starting card: Opening Number — 700000497, Melee, C, 1 DEX, Rogue
- **Loadout coherence (one sentence):** Opening Number leads (1 damage + Perform 1
  starts the act) → Encore Blade swings second with its gate live (2 damage +
  Perform 1) → Showstopper's passive pays out every act the metronome completes.
- **Role:** act ignition + the pack's scaling readout on a reliable 1-cost body.
- **Budget:** turn-1 floor 1 dmg + Perform 1 (~3 dmg-equivalent, under Sneak Attack's
  4 — the archetype commitment price), scaling to 4–6 mid-act; cost 1 (81% mode),
  Common, 2 effect lines.
- **Nearest:** Sneak_Attack.json (Rogue default); Snake_Dance.json (read-then-advance
  ordering, copied verbatim so the damage never waits on the same-card perform — this
  deliberately dodges the delve-pause timing risk). **Difference (mode 3):** scripted
  scan — 0 of the 63 shipped starting cards reference the Perform system at all; this
  is the loadout gap the pack claims, and the deliberately-play-me-first shape is what
  arms the weapon.
- **DSL:** `damage:1+[[performanceStacks]]` — token arithmetic verbatim shape
  (Treaty_of_Joy.json `perform:1+[[banners]]`, Gravedance.json); `[[performanceStacks]]`
  returns 0 with no act (GameTextTranslator.cs:1290-1308) so the floor is exact. No
  flags.

---

## Honest uncertainty ranking (drives gate-4 QA)

1. **Dead Man's Encore** — `damage:3` fired from a graveyard container: trigger + zone
   decompile-verified, but every shipped graveyard payload is `return:this`. QA: put it
   in the discard, finish an act, watch the foe take 3. Fallback: `return:this` payload
   (Chump Change verbatim) — the card keeps its 5-damage body and gains a recursion
   role instead.
2. **The Final Encore** — first mod Performance-category card through the DawnKit
   loader (enchantment type "performance", utilityNumber threshold, Song-delve
   membership, inert PlayAction filler). QA: perform with no act open → it must appear
   in the selection; complete at 4 → payload = 2× discard pile. Fallback: if the loader
   mishandles the category, demote to a normal Rare Action "finale" card reading
   `[[cardsInGraveyard]]` (all-verified tech) and re-gate.
3. **Showstopper Weapon** — FinishPerformance tier-0 rider (no shipped precedent) and
   same-pack referenceCard resolution for addnextcardfree. QA: finish an act with the
   power equipped → +2 HP; activate in exploration → next combat opens with a free
   Opening Number. Fallbacks: rider → StartCombat `bless:1` Evasion (Deadly-Weapon
   StartCombat rider shape); opener → reference a vanilla card (Chorus).
4. **Encore Blade** — CardsPlayedThisTurn count vs basic attacks (does a swing count
   itself for later swings — off-by-one verified for actions, basics assumed same
   path). QA: play two basics in a row — the second must Perform.
5. **The Vanishing Act** — self-catch of the PostAction bury helper (Beseech's wording
   proves self-inclusion; ours must bury *only* itself). QA: play it, then play
   another card — only Vanishing Act may be buried.
6. **Curtain Call / Smoke and Mirrors / Knife Juggling / Card Trick / Second Act /
   Crowd Work / Opening Number** — fully verified constructions (verbatim shipped
   idioms); no flags.

## Validation done offline

- **Phase-0 decompile pass:** ExecutePerform/UptickPerformance/Delve
  (SpellEffects.cs:10631-10842, 11551-11755), DiscardTrigger/HandTrigger/
  RunCardsInDeck zone gating (EventHandler.cs:150-163, 285-293, 434-490),
  PerformanceStacks (GameTextTranslator.cs:1290-1308), Performing/Finale
  (ConditionChecker.cs:1302-1310, 1397-1417), CheckCardsPlayedThisTurn
  (ConditionChecker.cs:1999-2014), AddNextCard command surface (TalentHandler.cs).
- **Perform-space census:** 90 cards touching the system (34 `perform:` users, 13
  Performance-category — all `excludeFromRewards`, 5 "song"-keyword thresholds 2–6);
  scripted scan over `tools/out/data/Card/`.
- **Gap scans backing uniqueness claims (all scripted, 2026-07-20):**
  0/78 basic-keyword weapons sequencing-gated; 0/63 starting cards (57 keystones + 6
  defaults) reference Perform; 0/66 tier-0 talents reference Perform; 0 shipped
  damage-Actions carry `perform:`; 0 cards combine Perform × Bury; graveyard
  FinishPerformance payloads = `return:this` ×5 (Chump Change, Soulfood, three
  Dances); 0 Commons use the Performing condition; 0 Performances read the graveyard.
- **Name collisions:** all 12 names checked case-insensitive against the 2,476
  extracted `m_Name`s + sibling packs — clean. (First drafts "Misdirection" and
  "Sleight of Hand" COLLIDED with shipped cards and were renamed to Smoke and Mirrors
  / Card Trick.) Talent name "Showstopper Weapon" clean vs 383 shipped talents.
- **Command vocabulary:** every codeLine command present in
  `docs/research/reference/effect-commands.txt` (perform, performancetype, damage,
  draw, discard, bless, enchant, buryplayed, removeenchant, heal) /
  `talent-commands.txt` (addnextcardfree) — scripted membership check.
- **IDs:** cards 700000400–700000408 sequential bottom-up; loadout 700000499/498/497
  top-down; all inside the registered block (docs/ID-REGISTRY.md).
- **Gates:** `dmk validate` 0 errors; `dmk artcheck --distinctness` 0 errors;
  `dmk sim` — 9/9 INSUFFICIENT-SIM (the sim subset contains no `perform` command; the
  two low-band scores, Crowd Work and Dead Man's Encore, are the sim seeing only the
  damage half of each card). Per METHODOLOGY §4.1.5, corpus-anchored budget math +
  the QA list above carry the balance burden.

## Art direction

Treatment: **limelight against the dark house** — every card is lit like a stage:
one warm gold spot (the `glow_spot` in #ffd86b/#f2cf6e family), velvet-crimson mids
(#7a2440/#8f2438), and a deep near-black surround (#140d18) so the pack reads as one
theater. The two graveyard pieces (Dead Man's Encore, The Vanishing Act) break toward
ghost-light — cold teal/violet washes — so the "audience of the dead" half of the
fiction is visually distinct from the living stage.

Sources are all Perform-space card arts (path_id-correlated via sprite-index.json),
none previously used by any pack (scripted grep over all `DC.*/art-recipes.json`), and
none equal to its card's `meta.nearestExisting` except Encore Blade (see below):
Crowd Work ← Crowd goes Wild's audience; Smoke and Mirrors ← Elegance, cooled to
mirror-blue; Knife Juggling ← Juggling Balls; Card Trick ← Abracardabra's card fan;
The Vanishing Act ← Grand Entrance mirrored (an entrance played backwards is an exit)
under a violet gradient; Second Act ← Show must go on; Curtain Call ← Crescendo's
finale burst; Dead Man's Encore ← noise-composite of Dance of Spirits + Myth of Gura
(the dead dancing through the story being told); The Final Encore ← Siren's Call
re-lit in stage gold (the legendary deadly song).

Loadout (same-menu adjacency, ART-MUTATION-SPEC §2): Encore Blade sits in the Rogue
weapon menu beside Daggers and Blightpin Stiletto — sourced from **Throwing Knife**
(a mid-run weapon card, in no char-creation menu; it IS in nearestExisting, so the
recipe uses composition-level ops per §2: mirror + radial composite with Serenade)
at 512×873 portrait. Deliberately avoided: CARD_UNLOCKS_44 (Dancing Daggers), which a
scripted check found doubling as a tier-0 talent powerImage. Showstopper Weapon ←
Encore! card art (scripted check: not among the 66 tier-0 powerImage path_ids, so
absent from the power menu). Opening Number sits in the Rogue starting-card menu —
sourced from Herald's trumpet (a pool Action, not a Profession default or StartingCard
keystone).
