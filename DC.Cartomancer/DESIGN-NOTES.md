# Cartomancer — design notes

**Cluster:** Foretell-and-fate — reading the future for profit, authoring it, and Doom as
the countdown. **ID block:** 700000600–700000699 (cards 700000600–700000608 bottom-up;
loadout top-down: weapon 700000699, power talentID 700000698, starting card 700000697).
**Colors:** mono-INT blue (Seeker, secondary Arcanist) + 1 Neutral weapon — cost profile
IS frame color (GetColor rules); every energy-costed item in the pack is INT.
**Origin:** PACK-LINEUP-PROPOSAL § Pack 3, adapted where Phase 0/2 falsified it (see
"Deviations from the proposal" — the proposal's central `[VERIFY]` flags mostly resolved
*against* its assumptions, and the pack was rebuilt on what the engine actually does).

## Pack thesis (v1 — post-verification)

**Mechanical thesis** (all decompile-verified):

1. **Foretell has a complete, shipped payoff plumbing that mods can key into.**
   `foretell:N` reveals the top N(+`floor(Focus/3)`) cards of your deck, fires the
   `Reveal` trigger (enum 72) **per revealed card** — including that card's own
   Reveal-trigger effects, run while it is still in the deck — then the player puts one
   card on top and the rest go to the **bottom**; after the selection, the global
   `Foretell` trigger (enum 96) fires once (SpellEffects.cs:1898-1903, 4464-4524;
   DeckViewer.cs:1489-1497, 1025-1035). "When Foretold" per-card memory is the shipped
   `Reveal + addtempvalue` idiom (Seers_Orb.json, Prophetic_Bolt.json, Starmap.json) —
   there is **no** engine-side "was this card Foretold" flag (DeckViewer.Foretell only
   reorders lists), which resolves the proposal's biggest open question: the documented
   fallback IS the shipped mechanism.
2. **Focus is the foretell-size dial.** `[[foretell]]` = `GetForetell()` =
   `floor(Focus/3)` extra revealed cards — it is *not* a foretold-count readout
   (GameTextTranslator.cs:312, 723-737; Focus.json adds +1 draw per 6 stacks at round
   start). Focus↔Foretell is a native feedback loop the pack can pump from both sides.
3. **Doom is a timing mini-game.** Doom detonates its **full stacks** as arcane status
   damage at EndTurnPhase and clears (`ClearOnTrigger: 1`), decaying 1 per Action played
   until then (Doom.json: effectTrigger 24, clearAt 1, lowerInsteadOfClear 1;
   SpellEffects.Doom:13382-13399 → `StatusDamage(stacks, "arcane")`). Doom is
   `theType 1` = affliction, so Potency boosts its ticks (SpellEffects.cs:13794-13824).
4. **`foretellenemy` is fully implemented and has 0 users in 2,525 cards** (scripted
   codeLine scan). It shows the foe's top N and moves the pick to the top of the real
   20-card cloned enemy deck (SpellEffects.cs:1901-1903, 4507-4523, 4526-4543;
   GAME-MECHANICS Part V) — an entire unused decision axis: intent control.

**The honest gap statement (Phase 2 correction).** The proposal assumed "Foretell
support in the shipped pool is thin." **False**: the scripted scan found **34 shipped
cards** referencing foretell (list in Validation), a full DEXINT "Eclipse" archetype with
enablers (Torch, Battle Sense, Observe, Recon, Augury…), per-card-Foretold counters
(Desert Rush, Sifting Time, Moonwell, Aries' Reflections, Call of the Void), When-Foretold
payoffs (Prophetic Bolt, Starmap, Seeker's Wisdom, Meteor Storm, Seers Orb) and
conjure-the-revealed engines (Clear/Arcane Visions, Passing of Time). What is *actually*
missing — verified gap by gap, and this pack fills exactly these:

- **0 mono-INT foretell casters** (every shipped `foretell:` caster costs DEXINT, DEX,
  INTSTR, Life or 0/Neutral — scan) → the pack is the archetype's mono-blue home.
- **0 `foretellenemy` users** → Stack the Deck (flagship new axis).
- **0 cards that trigger or read Doom for damage** (readers convert to Darkness/healing/
  counters only — scan) → Doomwritten, Sealed Fate.
- **0 player-pool reward cards with a board-affecting Reveal effect** (only the
  reward-excluded Seers Orb) → Omen of Ruin.
- **0 damage-keeping foretell basics** in the 78-weapon corpus (Torch and Seers Orb both
  sacrifice ALL damage) → Cartomancer's Edge.
- **0 of 63 starting cards reference Foretell** (scan) → The Cut.
- **0 of 66 tier-0 talents touch Doom** (scan) → Diviner's Weapon.

**Fictional thesis:** the one who reads the battle's future — and cheats it. The deck is
an oracle; every swing turns a card; the futures you refuse go to the bottom of the deck.

**Graph-extension proof:**
- **New edge:** foretell kit ↔ enemy-intent control (`foretellenemy` — this edge has no
  shipped instances at all; Stack the Deck creates it).
- **New payoff for an existing enabler:** Doomwritten gives the shipped Doom-ramp
  enablers (Impending Doom, Inevitable End, Quicksand, Wicker Crux) an on-demand
  detonation; The Significator gives the shipped foretell-count setup a damage payoff.
- **New setup for an existing payoff:** Cheat Fate and Cartomancer's Edge arm the shipped
  When-Foretold pool (Prophetic Bolt, Meteor Storm, Starmap, Seeker's Wisdom) on demand.

## Slot plan (vs CORPUS-STATS)

10 card-type items (9 pool + 1 starting card) + weapon + power = 12 total, under the
12-card cap.

- **Rarity:** 4C (Turn the Card, Omen of Ruin, Stack the Deck, The Cut) / 3U (Turn of
  Fate, Cheat Fate, Ask the Bones) / 2R (Doomwritten, The Significator) / 1L (Sealed
  Fate) — vs guidance ≈3–4C/2–3U/2–3R/1L per 10.
- **Cost curve:** 0×1, 1×7, 2×1, 3×1 = 10/70/10/10% vs pool 15/57/16/11% — same
  low-cost skew, slightly 1-heavy (the archetype's fuel is cheap by design; VL shipped
  8/58/25/8 under the same rule).
- **Bridges:** ≥2 explicit — Turn the Card + Ask the Bones (Focus), Omen of Ruin +
  Doomwritten + Sealed Fate (vanilla Doom archetype), Cheat Fate (vanilla Eclipse
  foretell kit). 8 of 10 cards read or feed a vanilla archetype (all but Stack the Deck
  — universal control — and The Significator, which counts vanilla foretells too).
- **Complexity:** every card 1–3 effect lines (median 2).

## Phase-0 verification record (the proposal's [VERIFY] flags, resolved)

| Proposal flag | Verdict | Evidence |
|---|---|---|
| "was this card Foretold" tracking | **No engine marker exists.** Per-card memory = `Reveal`(72) + `addtempvalue`, per-count memory = enchantment `Reveal addstack:1` | DeckViewer.cs:1489-1497 (pure reorder); Seers_Orb/Prophetic_Bolt/Starmap (tempValue); Desert_Rush/Moonwell/Aries (addstack) |
| `foretell` semantics | reveal top N + Focus bonus → Reveal per card → pick top, rest to bottom → global Foretell trigger | SpellEffects.cs:4464-4524; DeckViewer.cs:1025-1035, 1489-1497; keyword text GameTextTranslator.cs:2321 |
| `Foretell` GameTrigger | fires once per completed own-deck foretell selection (no card arg) | DeckViewer.PostSelectionTriggers:1025-1030; shipped user Exploration.json (T96) |
| `Reveal` trigger | fired per revealed card during own-deck foretell, card context; card's own Reveal effects run from the deck | SpellEffects.cs:4490-4503 |
| Doom detonation / decay | full stacks as arcane StatusDamage at EndTurnPhase, then clears; −1 per Action played; affliction type (Potency applies) | Doom.json; SpellEffects.Doom:13382-13399; StatusDamage:13794-13824 |
| Doom doubling idiom | `inflict:[[other(status)Doom]]` shipped — Monster-side only (Strangling Lashes); player-side poison twins prove the pattern | Strangling_Lashes.json; Toxic_Salve/Aura_of_Venom (poison); Dark_Omen.json ships the token player-side |
| Reversed faces via changethiscard | **cut** — `changethiscard` transforms a card in place; it cannot mark a conjured copy "reversed", and the conjure-the-revealed axis is occupied (Clear/Arcane Visions, Passing of Time — `conjurerevealed`) | effect-commands.txt:122,157; Clear_Visions.json ENCH T72 |
| Deck-top placement | `placeontopfromhand` shipped at EndAction inside a self-enchant with pauseQueue | Sight_Beyond.json (verbatim skeleton) |
| Foretell-count condition ("Foretold 3+ this combat") | no such ConditionValue; counting exists only as enchantment stacks from the card's own play onward | ConditionChecker.cs enum (129 values, none foretell-related) |
| "Start next combat with Foretell 2" (power) | **impossible** — foretell early-returns unless it is the player's combat turn; ActivateWeapon fires in exploration | SpellEffects.cs:4466-4469; WEAPON-SPEC §1 |
| Doom "no longer decays" (Inevitable) | **cut** — no decay-suppression command exists in the 565-command vocabulary | effect-commands.txt (closed vocabulary) |

## Deviations from the proposal

1. **The Cut**: "bury one card you Foretold" cut — no command buries from the revealed
   selection, and foretell natively bottom-decks every refused future (the mechanic
   already IS the fiction). Rebuilt as the seed-shaped called shot (foretell 1 + 2 dmg,
   +2 when Foretold).
2. **Turn the Card**: "draw if you've Foretold 3+ this combat" cut (no counter
   condition); rebuilt as the Focus bridge (0-cost foretell 1 + 1 Focus).
3. **Called Shot** cut as a separate card — Prophetic Bolt (C, 1 hybrid, 3+5-when-
   Foretold + Doom rider) already owns that decision; its role folded into The Cut at
   starting-card numbers.
4. **Ask the Bones**: "conjure your next Foretold card free, reversed" cut — collides
   with shipped Clear/Arcane Visions (`conjurerevealed`) and inverse faces are
   unsupported; rebuilt as the Focus-per-revealed-card ramp (Desert Rush skeleton).
5. **Cheat Fate**: kept, order flipped to read-then-place (both halves shipped-verbatim
   via Torch-family foretell + Sight Beyond placement).
6. **The Significator**: "while a Foretold card waits on top of your deck" cut (no
   deck-top state condition); rebuilt as the foretell-count damage engine
   (Moonwell counting + Cruel Intent payout idioms).
7. **Sealed Fate**: "Doom equal to cards Foretold this combat" cut — shipped **Call of
   the Void** is exactly that card; rebuilt as the player-side-first Doom doubler at L.
8. **Doomwritten**: "when the foe's Doom detonates, they take it again" (enchantment)
   rebuilt as the active trigger-now Action (Combust skeleton) — Doom's detonation
   arrives as generic arcane status damage, so a detonation-reaction cannot be gated to
   Doom specifically; the active form gives the same "takes it twice" outcome
   (trigger now + natural detonation later) with zero inference.
9. **Inevitable** (L, Doom no longer decays) **cut** — unsupported; L slot goes to
   Sealed Fate.
10. **NEW — Stack the Deck**: the `foretellenemy` discovery (0 users) replaced the cut
    cards with the pack's flagship: reading and rigging the foe's deck.
11. **Diviner's Weapon**: both proposal halves impossible (see table); rebuilt as Doom
    opener (Baneful cd-2 shape; 0 of 66 talents touch Doom) + Barrier-on-Foretell
    passive. Focus opener rejected — Focused Weapon (cd 3) and Psychic Weapon (cd 4)
    already own next-combat Focus in the same class menus.
12. **Turn of Fate**: `adaptinhand` misread in the proposal (Adaptation = socketing
    system, GAME-MECHANICS Part IV) — the "spread position" concept is expressed through
    the shipped **Ambush** condition as a mode-switch instead; fully self-explanatory
    via a keyword players already know.

## Card-by-card

*(Full Budget/Nearest/Difference/DSL evidence lives in each item's `meta` block in
pack.json — summarized here; trigger/condition ints decoded against EventHandler.cs /
ConditionChecker.cs declaration order.)*

### 700000600 Turn the Card — C, 0-cost, Utility/Action — BRIDGE #1
- **Role:** cheapest fuel; Foretell↔Focus on-ramp. **Deck:** vanilla Focus Seeker
  (Mindstrike/Profess) or any foretell deck.
- **Budget:** 0-cost C par 2–3 value: foretell 1 + 1 Focus (Gaze into the Beyond prices
  1–2 Focus at 0; Recon prices foretell 5 + gated draw at 0 — neither dominates).
- **Nearest:** Gaze_into_the_Beyond.json. **Difference (Mode 1):** only card that both
  reads and banks the read-size dial.
- **DSL:** `foretell:1` (Torch), `bless:1`+Focus (Gaze/Mindstrike). No flags.

### 700000601 Omen of Ruin — C, 1 INT, Utility/Action
- **Role:** fate payload that fires from inside the deck. **Deck:** vanilla Doom shells;
  in-pack the reason Edge swings feel like attacks.
- **Budget:** 2 Doom played + 2 Doom on Reveal (paid by the foretell setup —
  METHODOLOGY 4.1.3). Anchors: Prophetic Bolt (C), Wicker Crux (R, 6 flat).
- **Nearest:** Seers_Orb.json. **Difference (Mode 2):** first *acquirable* card with a
  board-affecting Reveal effect (Seers Orb is canBeAcquired:0/excludeFromRewards:1).
- **DSL:** `inflict`+Doom (Dark_Omen), Reveal-inflict (Seers_Orb T72 verbatim). No flags.

### 700000602 Stack the Deck — C, 1 INT, Utility/Action — FLAGSHIP
- **Role:** the unused axis: see the foe's top 3, choose their next draw.
- **Budget:** 1 energy of pure information + one rigged draw ≈ soft Dazed when it
  matters; whiff (uniform decks) priced in meta.
- **Nearest:** Reconnaissance.json (fiction only). **Difference (Mode 2, pure):**
  `foretellenemy` has 0 users in 2,525 cards.
- **UNVERIFIED (QA #1):** command verified in code, never shipped on a card. Fallback:
  codeLine → `foretell:3` (own deck, Torch-verbatim) + retext.

### 700000603 Turn of Fate — U, 1 INT, Magic/Action
- **Role:** spread position: led = reading (Foretell 2 + draw), late = strike (4 arcane).
- **Budget:** either mode ≈ 1-cost C par; U pays for the mode choice. Anchor: Recon.
- **Nearest:** Reconnaissance.json. **Difference (Mode 4):** every shipped Ambush card
  is bonus-shaped; this is the first mode-switch.
- **DSL:** Ambush Is (Daggers/Recon `[26:2]`), Ambush IsNot (Quickchange/Sleight_of_Hand
  `[26:3]`). No flags.

### 700000604 Cheat Fate — U, 1 INT, Utility/Action
- **Role:** author the future: read the top card, then overwrite it from hand — arming
  any When-Foretold card (Meteor Storm literally becomes free).
- **Budget:** card-neutral sequencing + 1-read at U; strictly less raw value than Sight
  Beyond (C, draw 2 + tuck), different function.
- **Nearest:** Sight_Beyond.json. **Difference (Mode 3):** no shipped card chooses WHICH
  card gets Foretold next.
- **DSL:** foretell at PlayAction (Torch-family); `placeontopfromhand` in ENCH
  EndAction + pauseQueue (Sight Beyond verbatim incl. flag). No flags.

### 700000605 Ask the Bones — U, 1 INT, Utility/Action — BRIDGE #1b
- **Role:** compounding ramp: Focus per card revealed → bigger future reads.
- **Budget:** base 2 Focus + 2-read at U (Desert Rush prices foretell 3 + ~3 Momentum
  at C; Focus is the stronger permanent currency).
- **Nearest:** Desert_Rush.json. **Difference (Mode 3):** first Focus-per-revealed-card
  payoff; converts read-count into the engine's own size dial.
- **DSL:** Desert Rush counting skeleton verbatim (T1 enchant:self+foretell; ENCH T72
  addstack:1; ENCH T44 `bless:[[enchantmentStacks]];removeenchant:this`) with Focus
  referenceStatus (Gaze idiom). No flags.

### 700000606 Doomwritten — R, 1 INT, Magic/Action — BRIDGE #2
- **Role:** on-demand detonation: Doom triggers now AND still detonates at end of turn.
- **Budget:** Combust translation at the identical slot (R, 1 INT); Doom self-clears at
  EndTurnPhase so the economy is strictly tighter than Combust's never-decaying Burning.
  Sim: **ON-CURVE** (band P5–P25).
- **Nearest:** Combust.json (skeleton verbatim). **Difference (Mode 3):** 0 shipped
  cards trigger or read Doom for damage; decouples Doom from the end-turn clock.
- **DSL:** `statusdamage:[[other(status)Doom]]:arcane` (Combust's exact form,
  status-token swapped — token shipped in Dark_Omen). No flags. QA note: Potency adds to
  the trigger (affliction-type) — shipped-consistent, watch magnitude.

### 700000607 The Significator — R, 2 INT, Utility/Enchantment
- **Role:** build-around: foretell-count → permanent +damage.
- **Budget:** +1 dmg per 2 cards Foretold from play onward; Edge-only floor = +1 per 2
  turns (slow); dedicated readers reach +3–5 mid-combat ≈ rare 2-cost engines.
- **Nearest:** Moonwell.json / Cruel_Intent.json. **Difference (Mode 3):** shipped
  counters cash as resources (heal/Barrier/Momentum/Doom); none as damage scaling.
- **DSL:** ENCH Reveal `addstack:1` (Desert Rush/Moonwell), ENCH CalculateOutgoingDamage
  `adddamage:[[enchantmentStacks]]/2` (Cruel Intent ungated form + `/N` token division,
  Reaver/Blightpin). No flags.

### 700000608 Sealed Fate — L, 3 INT, Magic/Action — CAPSTONE
- **Role:** the sentence: Foretell 3, inflict 4 Doom, then double the foe's Doom.
- **Budget:** floor 8 Doom + 3-read (L 3-cost par); ceiling 2X+8 paid by multi-turn ramp
  plus Doom's built-in anti-synergy (decay per action, EndTurnPhase race). No loop.
- **Nearest:** Strangling_Lashes.json (Monster-only doubling). **Difference (Mode 3 /
  player-side-first):** the finisher the shipped ramp engines lack; proposal version cut
  for Call-of-the-Void collision.
- **DSL:** doubling `inflict:[[other(status)Doom]]` (Strangling Lashes verbatim; poison
  twins prove player-safety). No flags.

## Loadout (weapons/powers/starting-cards addenda)

**Loadout coherence (one sentence):** Cartomancer's Edge turns a card every swing; when
it turns The Cut, The Cut arrives calling +2 and its own read arms the next turn's
future; Diviner's Doom opener means the foe starts every fight already sentenced.

### Weapon: Cartomancer's Edge — 700000699, Magic/BasicAttack, Seeker+Arcanist
- Deal 2 arcane, Foretell 1. Statline = the 6-weapon curve exactly (damage 2, 1 Neutral,
  Common, one rider — CORPUS-STATS §2).
- **78-weapon sweep:** the only foretell basics are **Torch** (Arcanist keystone menu:
  0 dmg — foretell 3 + bury + burn) and **Seers Orb** (Seeker keystone menu: 0 dmg —
  Doom applicator). Both sacrifice ALL damage; the Edge is the missing damage-keeping
  point — not strictly better than Torch (⅓ the read, no engine), not worse (it
  fights). Rider decision is new: each swing chooses which future to arm (Reveal
  triggers fire every turn from the basic-attack slot — no shipped weapon does this).
- Foretell 1 cannot reorder (single card returns to top — DeckViewer.Foretell); the
  rider is knowledge + arming, scaling to real reorders via Focus. Priced as a
  permanent engine per the weapons addendum.

### Power: Diviner's Weapon — talentID 700000698, cd 2, Seeker+Arcanist
- ActivateWeapon: `startstatus:[[myLevel]]*2:false` → Doom (foe starts next combat
  sentenced). Passive: Foretell trigger (96) → `bless:1` Barrier.
- **Budget vs the 66-talent ladder:** cd 2 = level-scaled next-combat status opener
  (Baneful Weapon = Poison 2×lvl at cd 2 — shape verbatim); Doom 2×lvl is weaker per
  stack than Poison (one detonation minus decay vs repeating ticks). Passive = 1
  stack/trigger, once per completed reading (T96 fires post-selection, not per card) ≈
  1–2 Barrier/turn with the Edge — shipped small-rider scale (Acrobatic's bless:1).
- **Uniqueness:** 0 of 66 tier-0 talents touch Doom; Focus openers (Focused/Psychic
  Weapon) deliberately avoided.
- **UNVERIFIED ×2** (QA list): startstatus+Doom pairing; Foretell as a talent rider
  trigger. Fallback: Warding Weapon's Barrier-opener shape, rider kept.

### Starting card: The Cut — 700000697, Magic/Action, C, 1 INT, Seeker+Arcanist
- Foretell 1; deal 2+tempValue arcane; When Foretold +2 (stacking, Starmap-style).
- **Starting-card curve (CORPUS-STATS §4):** 1-cost (81% mode), Common, 3 lines,
  archetype-seed shape, normal reward-pool card (not excluded — 62/63 rule).
  Reliability floor: never dead (2 dmg + read every draw). 0 of 63 shipped starting
  cards reference Foretell (scan).
- **Nearest:** Prophetic_Bolt.json — deliberately smaller (2+2 vs 3+5, no Doom rider,
  mono-INT) with a self-read attached; metronome vs haymaker, neither dominates.

## Honest uncertainty ranking (drives gate-4 QA — play these first)

1. **Stack the Deck / `foretellenemy`** — 0 shipped users ever. Code-verified
   (selection UI + E_deck insert) but the enemy-side viewer has never shipped on a
   card. QA: play it vs a normal mob and an elite; confirm the UI opens, the pick lands
   on top, and the foe draws it next. Fallback: `foretell:3` own-deck retext.
2. **Diviner's Weapon opener** — `startstatus` with Doom referenceStatus has no shipped
   user (command shipped ×20, status shipped, pairing new). QA: activate in
   exploration, confirm foe opens with 2×lvl Doom and it detonates at first
   EndTurnPhase. Fallback: Warding-shape Barrier opener.
3. **Diviner's Weapon passive** — Foretell(96) as a *talent* rider trigger has no
   tier-0 precedent (card-side T96 shipped in Exploration.json; talents run the same
   FindTriggeredEvents scan). QA: confirm Barrier lands once per reading. Also confirm
   whether `foretellenemy` fires T96 (DeckViewer hides via CardOptions.foretell on the
   enemy path — SpellEffects.cs:4535 — so it *may*; either reading is acceptable, text
   says "After you Foretell").
4. **Cheat Fate sequencing** — foretell resolves at PlayAction, placement at EndAction
   (both shipped positions, Sight Beyond skeleton + pauseQueue). QA: confirm the two
   modal UIs never overlap and the placed card is revealed by the NEXT foretell, not
   this one (intended behavior; text reads in that order).
5. **The Significator payout** — ungated `adddamage` at CalculateOutgoingDamage boosts
   all outgoing attack damage (Cruel Intent precedent); QA: confirm it does not leak
   into status ticks, and that `/2` floors (NumberParser; division==2 → 2.01 quirk is
   irrelevant at these values but watch +2 breakpoints).
6. **Doomwritten + Potency** — Doom is affliction-typed, so Potency adds to the
   triggered tick (shipped-consistent with Burning/Combust). QA: magnitude check only.
7. **Sealed Fate ordering** — inflict:4 must resolve before the doubling read (effects
   run in list order — VL Weeping Blade precedent). QA: confirm 0-Doom foe takes 8.

Fully verified, no flags: Turn the Card, Omen of Ruin, Turn of Fate, Ask the Bones,
The Cut, Cartomancer's Edge (all codeLines + idioms shipped verbatim).

## Validation done offline

- **Phase-0 decompile pass:** foretell/Reveal/Foretell-trigger plumbing
  (SpellEffects.cs:1898-1903, 4464-4551; DeckViewer.cs:26-56, 306-311, 1025-1035,
  1489-1497), GetForetell/Focus (GameTextTranslator.cs:312, 723-737), Doom
  (Doom.json; SpellEffects.cs:13382-13399, 13794-13824), GameTriggers order decode
  (EventHandler.cs:8-123 — Reveal=72, Foretell=96), ConditionValue/Operator decode
  (ConditionChecker.cs:10-141 — Ambush=26, IsNot=3).
- **Scripted scans over `tools/out/data/`** (2026-07 extraction): 34 foretell-
  referencing cards enumerated (Surveillance, Torch, Profess, Battle Sense, Observe,
  Starmap, Seeker's Wisdom, Prophetic Bolt, Pierce the Veil, Gaze into the Beyond,
  Augury, Astral/Arcane/Clear Visions, Sifting Time, Passing of Time, Exploration,
  Desert Rush, Televan Insight/Compass, Aries' Reflection I–III, Set Sail, Moonwell,
  Houroboros, Dark Inquiry, Reconnaissance, Providence, Meteor Storm, Foresight,
  Skillshot, Seers Orb, Call of the Void); `foretellenemy` users = **0**;
  non-Monster Doom-status referencers = **12** (Call of the Void, Dark Omen, Dark
  Starfall, Form of the Willow, Impending Doom, Inevitable End, Ire of Serem-Pek,
  Prophetic Bolt, Quicksand, Scorpion Strike, Seers Orb, Wicker Crux); Doom
  trigger/damage-readers = **0**; mono-INT foretell casters = **0**; starting cards
  referencing Foretell = **0**/63; tier-0 talents touching Doom = **0**/66; Ambush
  IsNot users = 2 (Quickchange, Sleight of Hand).
- **Every codeLine command** present in `docs/research/reference/effect-commands.txt`
  (foretell:290, foretellenemy:291, conjurerevealed:157 unused-by-us, placeontop
  family:371-373, statusdamage, inflict, bless, adddamage, addstack, addtempvalue,
  enchant, removeenchant, draw, damage) and `startstatus` in talent-commands.txt.
- **All 12 names collision-free** vs 2,476 extracted card `m_Name`s + 383 talents +
  68 sibling-pack names (case-insensitive scripted check).
- **IDs**: cards 700000600–700000608 sequential bottom-up; loadout 699/698/697
  top-down per registry (block allocated 2026-07-20; next free top-down 700000696).
- **Keystone menu audit** (for art adjacency + weapon sweep): Seeker weapon menu =
  Knuckles + Triglaive/Quarterstaff/Moonclaws/Shuriken/**Seers Orb**/Dragon Wraps/
  Chakram (+ mod Tickwright's Baton); Arcanist = Forcewand + **Torch**/Wintersprig/
  Caduceus/Dark Spire/Arcane Weaver/Gilded Staff/Spellbinders (+ Tickwright's Baton,
  Cinderbough Wand). Starting-card menus resolved likewise (Keystone JSONs →
  data-index path_ids).
- **Gates:** `dmk validate` 0 errors / 0 warnings; `dmk artcheck --distinctness` 0/0;
  `dmk sim` → BALANCE-REPORT.md: Doomwritten ON-CURVE, no DEGENERATE-RISK, no OVER,
  remainder INSUFFICIENT-SIM (expected — foretell/enchantment mechanics are outside
  the sim's 91-card calibration; budget math + gate-4 QA carry per METHODOLOGY 4.1.5).

## Art direction

**Treatment: candlelit reading room.** Midnight indigo is the pack's ground
(#10122a–#14162f), every piece pulled through an indigo→slate-blue→candle-gold gradient
map with a warm glow_spot as the "turned card" light source; violet accents mark the
Doom pieces (Omen of Ruin, Doomwritten, Sealed Fate get #8f7bff–#a883ff glows — fate's
cold side) while the Focus/reading pieces glow gold (#ffd98c–#ffe9b0 — the candle side).
All sources mirrored + rotated/zoomed (composition-level change) before grading; shared
vignette finisher.

**Sources** (thematically-nearest shipped art, none = its card's `meta.nearestExisting`,
each used once, none used by sibling packs — scripted check against all 5 packs'
recipes): Turn the Card ← Observe (CARD_UNLOCKS_36); Omen of Ruin ← Dark Omen
(SIEGEOFSHADOWS-DARK-OMEN); Stack the Deck ← Abracardabra (CARDS_METAMORPHOSIS_2_48 —
card-trick imagery); Turn of Fate ← Chronomend (CHRONOMEND); Cheat Fate ← Houroboros
(INFINITE_HOURGLASS_ECLYPSE — rewriting time); Ask the Bones ← Roll the Bones
(ROLL-THE-BONES); Doomwritten ← Doomsay (DOOMSAY); The Significator ← Oracle's Pendant
(ORACLE-PENDANT); Sealed Fate ← Inevitable End (INEVITABLE_END_ECLYPSE); The Cut ←
Skillshot (SKILLSHOT).

**Same-menu adjacency (ART-MUTATION-SPEC §2):** Cartomancer's Edge appears in the
Seeker AND Arcanist weapon menus → excluded sources = both menus' full rosters (audit
above: KNUCKLES, TRIGLAIVE, QUARTERSTAFF, MOONCLAW_*, SHURIKEN, **SEERSORB**,
DRAGONWRAPS, CHRAKRAM_CHARGED, FORCEWAND, TORCH, WINTERSPRIG, CADUCEUS, SPIRE,
ARCANE_WEAVER, GILDED staff, SPELLBINDERS_* + mod sources ORB-OF-LIGHTNING,
STAFFOFTHUNDER, GLOWINGBRANCH, ORB-OF-FIRE) — source chosen outside the set: **STYLUS**
(mid-run weapon, no class menu; the pen that writes fate), built portrait 512×873.
Diviner's Weapon: all 66 tier-0 talent sheets excluded (VL convention) + sibling
Seeker/Arcanist power sources (TINKERER_S_TOOLBOX, MOM_BINDINGCHAINS, REWIND,
GRIMOIRE_OF_FLAMES_ECLYPSE, ROOTLIMB_STRIKE_INFERNAL) — source: **KNOW-THE-UNKOWN**
(Astral Visions card art, divination burst). The Cut sits in the Seeker/Arcanist
starting-card menus → excluded: CARDART_3_43 (Mindstrike), CARDS_METAMORPHOSIS_2_50,
CARDART_5_43, CARD_UNLOCKS_28, DEATH_OF_DIVINITY_24, PIERCE-THE-VEIL,
SIEGEOFSHADOWS-SPLIT (Seeker keystones), CARDS_METAMORPHOSIS_2_52 (Shocking Grasp),
FIERY-BARRAGE, FROZEN-IN-TIME, CARD_UNLOCKS_5, CARD_UNLOCKS_11, DEATH_OF_DIVINITY_32,
CARDART_4_63, ARCANE-AMPLIFICATION (Arcanist keystones), CARDART_4_4 (Calibrated
Strike), CARDS_METAMORPHOSIS_8 (Stoke the Coals) — source chosen outside the set:
**SKILLSHOT**.
