# Clockwork Cadence — design notes

Tempo cluster (spec §8): **chain/combo** (CardsPlayedThisTurn family), **echo/copy**, **conjuration engines**.
ID block 700000200–700000299 (used: 200–211, 12 cards). Rarity mix 4C / 4U / 3R / 1L. Cost curve: 2× cost-0, 6× cost-1, 4× cost-2 (pool skew is 15/57/16 — we run slightly heavy on 2s because three cards are combat-long engines).

**Color identity (verified, corrects the brief's guess):** the chain archetype in the shipped pool is
**INT-blue / Lightning-suffix / 0-cost-neutral**, not DEX-green — all 40+ `Chain`-keyword player cards
are Magic/lightning-flavored (Zap, Conduit, Stormbolt, Stormcast, Chain_Lightning…), costing INT or
nothing. The pack follows that: INT costs, `suffix: Lightning`, `cardKeywords: ["lightning"]`, with one
neutral-cost Melee (Gearbite — precedent: Stormaxe, Buzzsword) and neutral Utility engines.

**Research base** (all in `tools/out/data/Card/`): read end-to-end — Conduit, Storm_Arrow, Stormbolt,
Blinkstrike, Dynamo, Faewand, Gathering_Storm, Recast, Recite, Twincasting, Twinlightning, Stormblade,
Charged_Blade, Zap, Chain_Lightning, Innervation, Energize, Shocking_Grasp, Flurry, Arcane_Lance,
Arcane_Ripple, Conjurers_Boon, Luck_of_the_Draw, Rapture, Materialize, Vigorating_Strike, Aura_of_Mind,
Many_Eyes, Spellcoil, Follow_Through, Dark_Spire, Wintersprig, Thunderclap, Eternity, Timestop,
Twisted_Echoes, Brainwarp, Stormcast, Aethershock. Conjuration-category token pool surveyed (19 cards:
Materialize, Bandage, Fury, Healing_Orb, the Shanties, Sabotage…) — `conjure:potion:1:1`-style pool
pulls resolve against these; our Cogspark joins that category deliberately.

**Decompile checks done for this pack** (beyond GAME-MECHANICS):
- `ConditionChecker.CheckCardsPlayedThisTurn` — counts PlayAction queue entries this turn,
  **excluding the resolving card** (Vigorating Strike encodes "third card" as `Is 2`).
- `ConditionChecker.CheckCategoryPlayedThisTurn` — targetValue is the category name,
  case-insensitive (`"conjuration"`); op `Is` = at least one this turn. **Zero shipped users** (full-pool scan).
- `ConditionChecker.AlreadyPlayedThisTurn` — same `cardID`, `handplayed == true`, this turn.
  Conjured copies share cardID ⇒ they count; **Echo auto-replays are not hand plays ⇒ don't count.**
- `ConditionChecker.Flow` — `count >= N`, auto-true on Reliable.
- `SpellEffects` cases: `conjure:N[:free]` (referenceCard, 3rd arg = free), `conjureupgraded:X[:free]`,
  `playcopy:N` → `PlayAction(cc, N)` re-runs the card's PlayAction lines per copy,
  `extraturn` → `TurnStat.endPlayerTurn = false`, `addtempkeyword` / `addkeyword` enum-parse (`Chain` valid).
- `adddamage` idiom: shipped cards put it on **trigger None** with conditions (Daggers, Claw__1, Deep_Slice);
  it's folded into damage calc (Part II step 3), not executed as an action. Gearbite copies this exactly.
- All commands used are present in `reference/effect-commands.txt`: damage, draw, adddamage, conjure,
  bless, playcopy, enchant, addtempkeyword, extraturn. (`noreset` is NOT in effect-commands.txt — an
  earlier "chain doesn't break this turn" design was cut for that reason and replaced by Unbroken Cadence.)
- Value tokens used, with a real-codeLine citation each: `[[actionsPlayed]]` (Daring_Dash
  `energy:DEX:[[actionsPlayed]]`), `[[my(status)Chain]]` (Shocking_Grasp
  `damage:[[my(status)Chain]]:lightning`).

Art files are not yet produced (ART-PIPELINE step); `art/` paths reserved in the manifest.

---

## Card-by-card

### 700000200 — Cogspark (Common, 0, Magic/Conjuration, Lightning, Chain)
- **Role:** the pack's token. 1 lightning damage, +1 chain (Chain keyword), **Cascade: draw 1**.
- **Deck:** everything here; conjured by Winding Key / Assembly Line, never a reward (`excludeFromRewards`).
- **Budget:** 0-cost par is 2–3 dmg (Zap = 2 dmg + 1 chain). Cogspark = 1 dmg + 1 chain + sequencing-gated cantrip. On budget.
- **Nearest:** `Charged_Blade.json` (0-cost Conjured Chain cantrip). **Difference:** Charged Blade cantrips unconditionally and lives in the run deck (BasicAttack added by Stormblade); Cogspark is Conjuration-category (temporary by construction, `CardHandler.CreateCardContainer` stamps Conjured) and its draw needs Cascade — 0-cost tokens don't cascade off each other (prev card must cost exactly 1), so the deck must weave 0-1-0-1. New decision: cost-weaving, not token-spam.
- **Loop-brake (token):** conjures/generates nothing; Conjured ⇒ gone after combat.

### 700000201 — Gearbite (Common, 1N, Melee, Phyisical)
- **Role:** copy-archetype payoff at common. 2 dmg; **6** if a Gearbite was already hand-played this turn.
- **Deck:** 2 copies, or 1 + copy effects (Fractal-style conjured duplicates, Recast, Recall) — conjured copies share cardID and count.
- **Budget:** 1-cost common median 4. Base 2 (−2), conditioned 6 (+2) — overage = value of the setup (a second same-name play), per spec §4 conditional rule.
- **Nearest:** `Aura_of_Mind.json` (only friendly AlreadyPlayedThisTurn user). **Difference:** condition moves onto the attack itself; no shipped damage card cares about its own name having been played. New payoff for the existing copy setup.
- **Honest note:** Echo replays do NOT trigger it (not handplayed) — description says "played", which matches.

### 700000202 — Sprocket Volley (Common, 0, Ranged, Lightning)
- **Role:** bridge conjuration↔combo. 2×1 dmg; +2×1 more if a **Conjuration** was played this turn.
- **Deck:** any conjure engine; multi-hit loves Chain status (+stacks per hit).
- **Budget:** 0-cost: 2 base / 4 conditional; conditional overage paid by playing a Conjuration-category card first (a real deck constraint — only 19 such cards exist plus Cogspark).
- **Nearest:** `Wintersprig.json` (Flow-gated extra attack). **Difference:** uses `CategoryPlayedThisTurn` — a verified condition with **zero shipped users**; asks "did your engine produce," not "how many cards."
- **UNVERIFIED flag:** condition has no shipped precedent (semantics read directly from `CheckCategoryPlayedThisTurn`); needs one sim pass to confirm the lowercase target matches.

### 700000203 — Winding Key (Common, 1 INT, Utility)
- **Role:** conjuration engine setup: conjure 2 Cogsparks; **Empower** (total energy > 4): a third.
- **Deck:** Cogspark tempo; feeds Tickwork Aura, Sprocket Volley, Assembly Line.
- **Budget:** ≈ Stormbolt (1 INT: 4 dmg + conditional Zaps). Pure-engine version: value arrives slowly, each token costs a play.
- **Nearest:** `Stormbolt.json`. **Difference:** first conjuration card whose bonus is gated on **banked energy** (Empower; energy persists between turns — EnergyHandler.SetEnergyToZero is empty). New decision: end turns holding energy to wind the engine.
- **Loop-brake:** tokens are Conjured and inert generators (Cogspark conjures nothing); hand cap 10.

### 700000204 — Mainspring Coil (Uncommon, 1 INT, Utility, Lightning, Chain)
- **Role:** chain battery scaling with cards played this turn (`bless:[[actionsPlayed]]`, refStatus Chain).
- **Deck:** mono-Chain lightning; play as link 4–6, then spend via Shocking Grasp / Stutterbolt / next hits.
- **Budget:** played 4th = 4+1 chain for 1 INT (Stormcast U gives 1+upgrades). Chain dies at EndTurnPhase — value must be converted same turn.
- **Nearest:** `Stormcast.json` / `Chain_Lightning.json`. **Difference:** chain gain scales with *sequencing* (play count), a scaling axis no chain battery uses (upgrades and damage-roll exist; play-count doesn't).

### 700000205 — Stutterbolt (Uncommon, 1 INT, Magic, Lightning — deliberately NOT Chain)
- **Role:** chain cash-out into the copy archetype: 3 dmg + `playcopy:[[my(status)Chain]]/3`; being non-Chain, casting it **wipes your chain**.
- **Deck:** chain decks as finisher; tension: the chain you spend was also +dmg on future hits.
- **Budget:** at 6 chain → 9 dmg for 1 INT, minus surrendered chain value; net ~+3 over baseline with full setup.
- **Nearest:** `Gathering_Storm.json` (`playcopy:[[tempValue]]`). **Difference:** copy count fueled by chain, and the loop-brake is the chain-wipe rule itself rather than a tempValue zero-out. Converts chain into *plays* — existing spenders convert only into damage (Shocking Grasp) or energy (Energize).
- **Loop-brake (engine):** `playcopy` re-runs PlayAction lines per copy, but the first resolution (non-Chain card) wipes all chain via EndActionStep ⇒ every replay computes `chain/3 = 0`. Self-extinguishing; verified against the Gathering Storm re-entry pattern.

### 700000206 — Fractal Bolt (Uncommon, 1 INT, Magic, Lightning, Figmented)
- **Role:** echo/copy via the dormant **Figmented** keyword: on play, a Conjured non-Figmented copy joins your hand (engine: EndActionStep).
- **Deck:** copy shell (Resonance Chamber, Gearbite-style payoffs — the copy is a same-cardID hand play).
- **Budget:** 6 dmg for 2 INT split over two plays; below Twinlightning's ~8-avg-for-1 burst, above it in flexibility/synergy.
- **Nearest:** `Twinlightning.json` (Chain+Echo). **Difference:** the second cast is a *card you hold and pay for*, not an auto-replay — and it interacts with AlreadyPlayedThisTurn payoffs, which Echo replays cannot (not handplayed).
- **UNVERIFIED flag:** **zero shipped cards carry Figmented** — mechanics are decompile-verified (Part III table) but content-untested; QA gate must confirm the copy spawns and drops the keyword.
- **Loop-brake:** copy loses Figmented (one generation), and each generation costs full energy.

### 700000207 — Tickwork Aura (Uncommon, 1 INT, Enchantment, Unique)
- **Role:** bridge conjure↔chain: combat enchantment, **Conjure trigger** → `bless:1` (Chain).
- **Deck:** conjure engines that also run chain spenders; upgrades shipped conjurerandom cards (Innervation, Evocation) into chain fuel.
- **Budget:** 1 perishable chain per conjure; dead without both engines. Cheap on purpose.
- **Nearest:** `Conjurers_Boon.json` (Rare 3 INT, heal 2 per Conjuration *played*). **Difference:** triggers on the **Conjure event** (creation — shipped enchantments use trigger 16 only for cost/keyword mutation, never a payoff) and pays in the tempo cluster's currency. First card that makes the conjure and chain archetypes talk.
- **Loop-brake (engine):** pure conversion — generates no cards or plays; chain expires each turn.

### 700000208 — Unbroken Cadence (Rare, 2 INT, Enchantment, Lightning, Unique)
- **Role:** chain build-around that removes the archetype's mono-deck constraint: cards you **draw or Conjure** gain Chain (temp) for the combat — any deck can chain.
- **Deck:** chain-splash: big non-Chain payoffs inside a chain engine; everything sustains + grows chain.
- **Budget:** ~+1 dmg-equiv ramp per card played, resetting each turn; ≈ Twincasting-class engine (Rare, 3 total cost) at 2 INT but with no immediate board effect.
- **Nearest:** `Twisted_Echoes.json` (affix: Draw+Conjure triggers, `addkeyword:Echo`) — exact structural sibling, player-side, granting Chain instead, `addtempkeyword` for combat scoping.
- **UNVERIFIED flag (biggest in the pack):** `addtempkeyword`'s card-context binding inside an enchantment trigger. Twisted Echoes proves the Draw/Conjure-trigger + keyword-grant shape works with `addkeyword`; the temp variant's `cc` fallback is first-card-in-hand when null (SpellEffects.AddtempKeyword). If sim shows wrong binding, fall back to the literal `addkeyword:Chain` (accepting permanent container mutation only for cards drawn in that combat — still bounded, but must then be QA'd for run-deck leakage).
- **Loop-brake:** generates nothing; chain expires at EndTurnPhase; affects only cards entering hand after cast.

### 700000209 — Assembly Line (Rare, 2 INT, Utility)
- **Role:** bridge combo↔conjure: `conjure:[[actionsPlayed]]:free` Cogsparks.
- **Deck:** long-turn refill: 4–5 links, then a fresh handful of free tokens (+chain with Tickwork Aura).
- **Budget:** late-turn 4–6 free 0-cost tokens ≈ 5 dmg-equiv + chain + cascade draws, inside the rare cost-2 envelope (5–8).
- **Nearest:** `Apocalypse.json` (`conjure:[[tempValue+1]]:free`, cross-turn accumulator). **Difference:** scales with this-turn play count — no shipped conjure amount reads `[[actionsPlayed]]`; the combo deck's tempo *is* the engine's fuel.
- **Loop-brake (the pack's scariest engine, analyzed):** Cogsparks count as plays, so chained Assembly Lines escalate — but (a) hand cap 10 bounds each batch, (b) every token must be individually played for only 1 dmg + 1 chain, (c) each Assembly Line costs 2 INT and a card; it never conjures itself and Cogspark conjures nothing, so there is no closed card-positive loop. Worst case is a long, energy-bounded turn, which is the archetype's fantasy — Overclock's Flow 8 gate is the intended payoff, made honest by OneUse there.

### 700000210 — Resonance Chamber (Rare, 1 INT, Enchantment, Lightning, Unique)
- **Role:** echo/copy engine payoff: whenever you play a card you've **already played this turn** → 2 Chain + 2 lightning damage.
- **Deck:** duplicate-dense shells: 2× commons, Fractal Bolt copies, second-Cogspark-per-turn, Recast/Recall output.
- **Budget:** per trigger ≈ 4 dmg-equiv (2 now + 2 chain), needing a same-cardID second hand-play — a real constraint; 2–3 triggers/turn only in a dedicated deck.
- **Nearest:** `Aura_of_Mind.json` (1 Focus per repeat play, common). **Difference:** pays in the tempo cluster's own currencies and turns *any* duplicate into a chain link even when the copy isn't a Chain card; repeat-play-as-damage-engine doesn't exist player-side. Rare because it's the archetype's central payoff.
- **Loop-brake:** purely reactive — creates no cards or plays.

### 700000211 — Overclock (Legendary, 2 INT, Utility, Lightning, OneUse)
- **Role:** combo capstone: draw 2; **Flow (8): take an extra turn**; OneUse.
- **Deck:** any Clockwork shell that can physically reach 8 plays (token swarms + links). Whiff mode still cantrips.
- **Budget vs shipped extra turns:** Timestop (Rare, 4 INT flat), Brainwarp (Rare, 3 INT, dump 20 Focus), Eternity (Revelation, free, OneUse, delve-luck). Overclock: stricter *activity* gate (8 real plays), cheaper cost, single use, Legendary.
- **Nearest:** `Brainwarp.json`. **Difference:** first extra turn gated on raw combo count — the reward *is* the archetype's core activity, no parallel resource.
- **Loop-brake (engine):** OneUse — permanently gone after one cast (EndActionStep), so no turn-recursion; the granted turn resets `[[actionsPlayed]]`, so even a second copy (there is none — but e.g. `duplicatedeck` edge cases) restarts from 0. Noted for sim: Flow auto-passes on Reliable — Overclock is not Reliable; watch for external Reliable grants (`addtempkeywordto:reliable:*`) in QA.

---

## Cross-pack hooks (welcome, never required)
- Cogspark/Winding Key feed any shipped `conjurerandom ... suffix=lightning` deck (Innervation, Call the Storm).
- Unbroken Cadence makes EmberweaveGrove burn spells chain-legal in a splash.
- Sprocket Volley pays off CrimsonLedger's conjureblood tokens (any Conjuration-category play counts). *(Only if their tokens are Conjuration-category — verify at wave integration.)*

## Risk register (honest)
1. **Unbroken Cadence** — `addtempkeyword` context binding in enchantment triggers: UNVERIFIED, fallback documented above.
2. **Fractal Bolt** — Figmented has zero shipped users; engine-verified only.
3. **Sprocket Volley** — CategoryPlayedThisTurn has zero shipped users; semantics read from decompile.
4. **Assembly Line** — most degenerate-loop-prone card; bounded but sim harness should fuzz Assembly Line × Tickwork Aura × Overclock turns.
5. Enchantment `type` fields set to `"blessing"` (dispellable, matching Twincasting theType 0); if review prefers undispellable engines, switch to `"other"` (Dynamo pattern) — flagged as a reviewer decision, not silently chosen.

## Art direction

Treatment: crackling machinery. The core set's lightning art is already electric blue on
storm grey, so distinctness comes from composition (every card is mirrored, tilted, or
reframed) plus a brass overlay wash (#c9973f) that reads as clockwork metal — the pack's
signature is blue voltage glinting off brass. Token/spark cards zoom tight on small focal
sparks (Zap, Shocking Grasp); engine cards keep wide ritual framing (Evocation, Stormcast).
Composites mark the two payoff engines: Assembly Line (Evocation's forging hands under
Stormcast's vortex, linear blend) and Overclock (Timestop's clock-rune rings ringed by
Innervation's lightning, radial blend). Stutterbolt alone gets a soft posterize for a
deliberate stutter/glitch texture.

---

## Weapons & powers (manifest v1.1)

**Shipped curve (derived, cited in EmberweaveGrove notes in full):** weapons = damage 2,
1 Neutral, Common, one rider. Powers (66 tier-0 talents): cooldowns 1x1, 2x20, 3x32,
4x8, 5x2, 6x1, 10x1, 20x1; cd 2 = small/level-scaled next-combat status (+ optional
passive), cd 3 = free/choice card or 2-3xlvl status, cd 4-5 = permanent deck edits,
cd 6+ = dramatic one-offs; riders are small per-trigger values, non-scaling with cd.

### Weapon: Tickwright's Baton - 700000299, Magic (Chain), Arcanist/Seeker
- **Role:** combo finisher in the basic-attack slot - Flow 2 mints a Cogspark for next
  turn's chain; carries Chain so the basic never wipes chain stacks (the archetype's
  breaking rule).
- **Budget:** damage 2 + Chain keyword (=1 chain on resolve, half Forcewand's feed);
  gated token = ~1 dmg + 1 chain + Cascade-gated draw of temporary value, priced by
  needing 2 prior plays. At/under Forcewand's unconditional +2 chain.
- **Nearest:** Forcewand / Knuckles. **Different:** opposite sequencing decision to
  Knuckles' Cascade opener-weave - play it LAST, not first; the output is the pack token.

### Power: Ratchetwind Weapon - 700000298, cd 3, Arcanist/Seeker
- **Role:** token opener; four 0-cost links let turn 1 hit Flow thresholds.
- **Budget vs curve:** conjure:ref:[[myLevel]]:4 is Thundering Weapon's cd-3 codeLine
  character-for-character; Cogspark per-copy value ~ Zap's. On-curve by construction.
- **Nearest:** Thundering Weapon / Quicksilver Weapon. **Different:** the tokens are
  Chain links with Cascade cantrips - seeds the sequencing engine, not burst damage.

### Power: Escapement Weapon - 700000297, cd 4, Arcanist/Seeker
- **Role:** permanent link-minting - grafts Chain onto a chain-breaking payoff card.
- **Budget vs curve:** permanent deck edits live at cd 4 (Coiling/Psychic); single lean
  line, deliberately under Psychic's triple package (the graft compounds every combat).
- **Nearest:** Coiling Weapon (upgrades existing links) - Escapement makes NEW links.
  **UNVERIFIED:** addkeywordindeck ships only with the psionic argument; chain (Chain=1)
  is on the same enum-parse path - in-game QA required.

### Power: Flywheel Weapon - 700000296, cd 3, Seeker/Arcanist
- **Role:** engine-on-turn-zero - choice of Winding Key / Tickwork Aura / Mainspring
  Coil at next combat start; fixes engine-stuck-on-bottom, the archetype's worst fail.
- **Budget vs curve:** choice-of-3 start-of-combat card is the modal shipped cd-3
  payoff (Artificer's/Deathdealers/Explosive); the refs are the pack's own 1-cost C/U
  cards, materially in line with shipped Contract/Equipment pulls. addnextcard:3 +
  referenceCard list shipped-verbatim. **Nearest:** Deathdealers Weapon /
  Artificer's Weapon.
