# Pack Lineup Proposal — v1 (for review)

Consolidated output of the persona-crew ideation run (2026-07): six lenses (2× game
design, economist, narrative, whimsy, cartomancy) ran a divergent round and an
adversarial round; this document specs only what **survived the fight**. Three new
packs, two refits, one hold.

**Status: PROPOSAL.** Nothing here has been through the METHODOLOGY pipeline yet.
Card texts are design intent, not final DSL — every mechanic marked `[VERIFY]` is a
Phase-0/2 decompile or corpus task before drafting. Numbers are placeholders until
gate 3. On approval, each pack runs the full loop (thesis → corpus scans →
nearest-neighbor sweeps → draft → gates).

---

## Open decisions (need your call)

1. **VenomousLegacy disposition.** Its 12 cards are gate-green and un-dominated (the
   Khanjali problem was the *loadout*, not the card list). Recommendation: keep it as
   a **cards-only attrition pack** (retire Blightpin Stiletto / Festering Weapon /
   Twin Fangs / First Blood from it) and give the Rogue loadout slot to The Final
   Encore. Alternative: retire the pack entirely.
2. **The Sapper's classes.** Crew split three ways; this doc uses the narrative
   argument (**Warrior/Hunter** — sappers are patient tradesmen, and it spreads kits
   across classes). Flip to Hunter/Rogue if you prefer.
3. **Benched with live disagreement** (not specced, revivable): Momentum Mint
   (economist stakes it; whimsy/tarot call it a progress bar), Stolen Hourglass
   (steal-a-turn; cost model contested).

---

# Pack 1 — DC.FinalEncore · "The Final Encore"

**Classes:** Rogue (secondary: Seeker) · **Color:** DEX green · **New pack** — takes
over the Rogue loadout slot.

## Core archetype

**Perform / showmanship tempo.** The act builds every turn (Perform events from the
weapon and cheap tricks), then cashes in one theatrical strike. Escalate → detonate →
start the next act. One visible counter, one payoff — the archetype competes with
Khanjali on burst *tempo*, not stacks, so there is no lane collision.

- **Mechanical thesis (to verify in Phase 0):** the Perform system (`perform`
  command — shipped on Harp/Rattler/Throwing Knife) is near-unissued outside its
  origin kit; `FinishPerformance` is one of only three triggers that scan the
  **graveyard** — a spent card can still take its bow. `[VERIFY: exact Perform
  counter semantics + FinishPerformance firing rules — this is the pack's #1
  decompile task; all Perform numbers below move after it]`
- **Fictional thesis:** a stage magician whose act ends with a body. The mandatory
  every-turn weapon swing is the metronome; the finale is the trick you thought was
  already over.

## Loadout kit

| Slot | Name | Sketch |
|---|---|---|
| Weapon (1 Neutral, Melee) | **Encore Blade** | Deal 2 damage. If this is not the first Action you've played this turn, **Perform 1**. *(gate = CardsPlayedThisTurn > 0, verified condition family)* |
| Weapon power (cd 2) | **Showstopper Weapon** | Start next combat with **2 Perform**. `[VERIFY Perform storable pre-combat; fallback: cd 3 choice-of-3 opener from the pack's 1-cost engines — shipped addnextcard:3 pattern]` |
| Starting card (C, 0-cost) | **Opening Number** | **Perform 1. Draw a card.** *(0-cost cantrip seed; 0-cost common par ≈ 2–3 value)* |

**Turn-1 story:** Opening Number starts the act for free → the basic attack is now
your *second* Action and keeps time → every turn crescendos toward Curtain Call.

## Card list (11)

| # | Card | R | Cost | Sketch | Role |
|---|---|---|---|---|---|
| 1 | Opening Number | C | 0 | Perform 1. Draw 1. | starting card / act fuel |
| 2 | Knife Juggling | C | 1 DEX | Deal 2 damage twice; if you Performed this turn, three times. | multi-hit tempo |
| 3 | Misdirection | C | 1 DEX | Gain 2 Evasion. Perform 1. | defense that keeps the act going |
| 4 | Crowd Work | C | 1 DEX | Deal 3 charm damage. Perform 1. | trickery damage + fuel |
| 5 | Sleight of Hand | U | 1 DEX | Draw 2, discard 1. Perform 1. | filtering, feeds Dead Man's Encore |
| 6 | The Vanishing Act | U | 1 DEX | Gain 2 Evasion, then bury this. **Bury:** Perform 2. | self-burying trick `(Suntree Twig self-Bury precedent)` |
| 7 | Encore | U | 1 DEX | Conjure a copy of the last Action you played this turn. | the echo `(conjurelastplayed / playcopy family)` |
| 8 | Curtain Call | R | 2 DEX | Deal damage equal to twice your Perform. `[VERIFY readout token]` | the cash-out; the kit's payoff |
| 9 | Dead Man's Encore | R | 2 DEX | Enchantment: whenever your card is buried or discarded, Perform 1. | graveyard-as-audience engine |
| 10 | Stage Presence | R | 1 DEX | Enchantment: while you have 5+ Perform, your Actions deal +2. | mid-act payoff plateau |
| 11 | The Final Encore | L | 3 DEX | Deal 3 damage per Perform this combat, then the show ends (exile this). | capstone finale, self-exiling |

**Verification flags:** Perform semantics (#1); Perform-count readout token; whether
Perform persists across turns or resets `[design assumes persists within combat]`.

---

# Pack 2 — DC.PowderAndPatience · "Powder & Patience" (The Sapper)

**Classes:** Warrior/Hunter (open decision #2) · **Color:** STR red / Neutral · **New pack.**

## Core archetype

**Fuses — damage as a scheduled instrument.** Bombs are armed with a face value and a
fuse; **your weapon swing is the clock** (every basic ticks all fuses −1). The
decision is *timing*: steer detonations into the enemy's telegraphed intents,
accelerate with cheap swings, or cash out early at a penalty. The economist's frame:
bombs are zero-coupon bonds with deterministic maturity — and this pack is the
game's **only issuer** (bomb keyword pool exists but no shipped archetype owns it).

- **Mechanical thesis (to verify):** `bomb` cardKeyword + `playkeywordfromhand:bomb`
  shipped (Handcannon); planting cards into the ENEMY deck is shipped tech
  (Crossbow/Rapier `addcardtodeck:1:other`); enemies draw from a real 20-card cloned
  deck (GAME-MECHANICS Part V). `[VERIFY: fuse implementation — armed bombs as
  enchantment stacks ticked by a PlayAction listener vs conjured countdown cards;
  and on-draw detonation for enemy-planted gifts]`
- **Fictional thesis:** the patient demolitionist — the fight was over before it
  started; everyone else just hasn't heard the bang yet.

## Loadout kit

| Slot | Name | Sketch |
|---|---|---|
| Weapon (1 Neutral, Melee) | **Fuse-Cutter** | Deal 1 damage. **Tick every Fuse down 1.** *(stat sacrifice buys the clock — the archetype heartbeat)* |
| Weapon power (cd 2) | **Demolitionist Weapon** | Start next combat with a **Powder Keg** armed (Fuse 2: deal 4). **Passive:** your bombs deal +1. |
| Starting card (C, 1 STR) | **Light the Fuse** | Deal 2 damage. Arm a **Powder Keg** (Fuse 2: deal 4). |

**Turn-1 story:** Light the Fuse plants the first keg → every Fuse-Cutter swing is a
countdown you are steering → the keg matures exactly when you scheduled it.

## Card list (11 + 2 tokens)

| # | Card | R | Cost | Sketch | Role |
|---|---|---|---|---|---|
| T1 | Powder Keg *(token)* | — | — | Armed bomb: Fuse 2, detonate for 4. | the base instrument |
| T2 | Ticking Smile *(token)* | — | — | Planted in the ENEMY deck; when they draw it: 6 damage + 2 Dazed, `say:"Surprise."` | the punchline `[VERIFY on-draw]` |
| 1 | Light the Fuse | C | 1 STR | Deal 2. Arm a Powder Keg. | starting card |
| 2 | Satchel Charge | C | 1 STR | Arm a Big Keg (Fuse 3: deal 8). | the long bond |
| 3 | Short Fuse | C | 0 | Deal 1. Tick all Fuses 1. | free acceleration |
| 4 | Blast Shielding | C | 1 N | Gain 4 Armor; 6 if a bomb is armed. | defense inside the fiction |
| 5 | Parting Gift | U | 1 STR | Plant a **Ticking Smile** in the enemy's deck. | the screenshot card |
| 6 | Chain Reaction | U | 2 STR | Enchantment: when a bomb detonates, tick every other Fuse 1. | cascade engine |
| 7 | Sapper's Patience | U | 1 STR | Arm 2 Powder Kegs. Draw 1 if you have 3+ Fuses armed. | going wide |
| 8 | The Long Con | R | 1 N | Enters as *Pathetic Jab* (deal 1); transforms each turn held; final form: deal 12, Ambush. `(changethiscard shipped)` | the card that lies |
| 9 | Controlled Demolition | R | 2 STR | Detonate all your armed bombs immediately. | early redemption |
| 10 | Demolition Yard | R | 2 STR | Enchantment: your bombs deal +2 and grant 1 Scrap on detonation. | payoff + Scrap economy bridge |
| 11 | One Last Job | L | 2 STR | OneUse: tick all Fuses to 0; detonations are doubled this turn. | capstone |

**Verification flags:** fuse plumbing (#1); enemy on-draw trigger for Ticking Smile
(fallback per whimsy: retime detonation to fuse-ticks on the enemy's turn — the
`say:"Surprise."` punchline survives either way); Scrap grant syntax.

---

# Pack 3 — DC.Cartomancer · "The Cartomancer"

**Classes:** Seeker (secondary: Arcanist) · **Color:** INT blue · **New pack.**

## Core archetype

**Foretell as the payoff engine, not a peek.** Every weapon swing reads the future;
you profit only from futures you *called* — and you may bury the future you refuse.
Reversal (a conjured "answer" arriving in its weaker inverse face), the significator
(a card that empowers the act while it waits), and Doom as fate's countdown give the
kit its texture. Foretell support in the shipped pool is thin `[scripted scan in
Phase 2]` — this pack becomes its home.

- **Mechanical thesis (to verify):** `foretell` + `[[foretell]]` shipped (Torch);
  Doom detonation verified (full-stack arcane burst at EndTurnPhase); Reveal
  trigger exists. `[VERIFY: "was this card Foretold" tracking — may need an
  engine-side marker; reversed/inverse faces via changethiscard]`
- **Fictional thesis:** the one who reads the battle's future — and cheats it. The
  deck is an oracle; every swing turns a card.

## Loadout kit

| Slot | Name | Sketch |
|---|---|---|
| Weapon (1 Neutral, Magic) | **Cartomancer's Edge** | Deal 2 arcane damage. **Foretell 1.** *(every swing turns a card)* |
| Weapon power (cd 2) | **Diviner's Weapon** | Start next combat with **Foretell 2**. **Passive:** cards you play that you Foretold deal +1. `[VERIFY both halves]` |
| Starting card (C, 1 INT) | **The Cut** | Foretell 2, then you may **bury one card you Foretold** — deny the future you saw. |

**Turn-1 story:** The Cut reads the top of the deck and refuses the bad future → the
Edge keeps turning cards → payoffs land on the futures you called.

## Card list (11)

| # | Card | R | Cost | Sketch | Role |
|---|---|---|---|---|---|
| 1 | The Cut | C | 1 INT | Foretell 2; optionally bury one Foretold card. | starting card / fate filter |
| 2 | Turn the Card | C | 0 | Foretell 1. Draw 1 if you've Foretold 3+ this combat. | cheap fuel |
| 3 | Called Shot | C | 1 INT | Deal 5 damage if you Foretold this card; otherwise 2. `[VERIFY foretold-memory; fallback: "if you Foretold this turn"]` | the called future |
| 4 | Omen of Ruin | C | 1 INT | Inflict 2 Doom. Foretell 1. | fate's countdown |
| 5 | Turn of Fate | U | 1 INT | Adapt: played as your first Action = Foretell 2 + draw 1; otherwise deal 4. `(adaptinhand — the one "spread position" card that survived the fight)` | positional meaning, contained |
| 6 | Ask the Bones | U | 1 INT | `say:"Speak."` — conjure your next Foretold card free, **reversed** (its weaker inverse face). `[VERIFY inverse-face tech]` | the oracle answers |
| 7 | Cheat Fate | U | 1 INT | Put a card from your hand on top of your deck, then Foretell 1. | authoring the future `[VERIFY deck-top placement command]` |
| 8 | The Significator | R | 2 INT | Enchantment: while a Foretold card waits on top of your deck, your Actions deal +2. `[VERIFY top-of-deck state read]` | hold-vs-cash tension |
| 9 | Sealed Fate | R | 2 INT | Inflict Doom equal to the cards you've Foretold this combat. | scaling fate payload |
| 10 | Doomwritten | R | 2 INT | Enchantment: when the foe's Doom detonates, they take it again. `[VERIFY Doom-detonation trigger]` | detonation doubler |
| 11 | Inevitable | L | 3 INT | Your Doom no longer decays; Foretell 3. | capstone: fate stops negotiating |

---

# Pack 4 — DC.CrimsonLedger v2 · Blood-Debt refit

**Keep:** the whole gate-green kit (Bloodprice Falchion, Bloodtithe/Soulscribe/
Pawnbroker's, Repossession) and 8 of 11 cards. The refit adds the **debt instrument**
— borrowed power with a due date — which the fight rated the pack's strongest missing
decision (loss aversion made mechanical; the Souls bank becomes collateral).

**Swap out** (weakest three, incl. the pack's own "cut me first" honesty flag):
Paper Cut, Flesh Installment, Usurer's Bargain.

**Swap in:**

| Card | R | Cost | Sketch | Role |
|---|---|---|---|---|
| Collection Notice *(token)* | — | — | Curse shuffled into YOUR deck; when drawn: pay 3 Souls or take 6 damage, then exile it. | the due date `(Umbral Lance curse-injection precedent, self-directed)` |
| Borrowed Strength | C | 0 | Deal 6 damage. Shuffle a **Collection Notice** into your deck. | the loan |
| Soul Loan | U | 1 N | Gain 2 energy this turn. Shuffle a **Collection Notice** into your deck. | tempo on credit |
| Debt Spiral | R | 2 STR+1 Life | Deal 4 damage per Collection Notice you've settled this combat. | payoff for servicing debt `[VERIFY settled-counter]` |

---

# Pack 5 — DC.EmberweaveGrove v2 · Pyre Clock sharpening

**Keep:** kit + card list as gated (all green). Sharpening pass only — make the
reshuffle/fatigue clock the pack's explicit, visible identity:

- **Add:** *Race the Collapse* — R, 1 INT: "Deal damage equal to twice the cards
  remaining in your deck… or twice the cards in your graveyard, whichever is lower."
  `(CardsInDeckRemaining / CardsInGraveyard — both verified conditions; exact form
  to Phase 3)` — the push-your-luck readout the archetype lacked.
- **Cut:** one non-clock card to stay ≤12 (candidate: Ashfeast, the least
  clock-coupled common — final call in the pipeline).
- **Flagged wildcard (QA-gated, optional):** an alternate Emberweave Communion mode
  where deck-death with a full graveyard detonates the graveyard instead of losing —
  "ascension." Ships only if gate-4 proves the termination path.

---

# Pack 6 — DC.ClockworkCadence · hold

Green through gates; "wind the spring" (banked-energy identity) was proposed but
never seconded in the fight. **No changes this wave** — revisit after the three new
packs land.

---

## Class coverage after this wave

| Class | Kits |
|---|---|
| Knight | Ember (kit), CrimsonLedger v2 |
| Rogue | **Final Encore** (new) |
| Arcanist | Ember (kit), Clockwork, Cartomancer splash |
| Hunter | **Powder & Patience** (new, pending decision #2) |
| Seeker | **Cartomancer** (new), Clockwork |
| Warrior | **Powder & Patience**, CrimsonLedger v2 |

## Pipeline plan on approval

Per new pack: Phase-0 decompile of the flagged systems (Perform / fuse plumbing /
Foretell-tracking) → Phase-2 scripted corpus scans (nearest-neighbor over all 2,525
cards + the 78-weapon corpus) → ID allocation (docs/ID-REGISTRY.md) → draft →
`dmk all` gates → sandbox QA (`xdctest0` + `[Sandbox] ModOnlyRewards`). Refits are a
half-loop each. Fan-out: the three new packs are independent and can be built in
parallel.
