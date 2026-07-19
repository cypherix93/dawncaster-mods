# EmberweaveGrove — design notes

**Cluster:** Fire & ash — burn/Firecast + graveyard (bury/Reaping/Heavy).
**ID block:** 700000000–700000011 used (of 700000000–700000099).
**Art:** `art/*.png` paths reserved; 512×512 RGBA per ART-PIPELINE still to produce.

## Research summary (what exists today)

Read end-to-end: Fireball, Elite_Fireball, Combust, Searing_Ray, Cauterize, Sanctify,
Singe, Torch, Fan_the_Flames, Meteor_Storm, Ashes_to_Ashes, Awakening, Prayer,
Conviction, Censorship, Spellburn, Fleeting_Thoughts, Beseech, the four Halos,
Pyroclasm, Fireside_Blessings, Gravesite, Bury_Alive, Recycle, Roll_the_Bones,
Bone_Ritual, Blood_Ritual, Rapture, Soultap, Sanguine_Visions, Grave_Pact, Soulbinder,
Boneskewer, Suntree_Twig, Extraction_Listener, plus the enemy-side mill suite
(Vile_Consumption, Gleeful_Disdain, Divine_Ire, Entropy, Bone_Armor, Fury's_Blaze).

Verified enum decodes used throughout (decompiled/EventHandler.cs, ConditionChecker.cs):
trigger ints 1=PlayAction, 13=Bury, 44=EndAction, 54=Reshuffle, 78=InflictStatusDamage;
condition ints 11=CardsInGraveyard, 13=CardsInDeckRemaining, 70=DamageType.
Engine reads that shaped the pack (decompiled/SpellEffects.cs):

- `BuryCard`: buries from the **top of the deck**; a buried card's own `Bury`-trigger
  effects run before filing; **Firecast** ⇒ PlayAction effects execute, card →
  graveyard; **Persistent** ⇒ graveyard instead of exile; everything else → exile.
- `bury:N:other` on the player side calls `BuryMonsterCard`, which silently deletes
  enemy deck cards — **no Bury trigger, no `[[lastBuriedEnergy]]`**. Killed an
  enemy-mill card concept; the Vile_Consumption pattern only works enemy→player.
- `return:` args verified: `choose`, `this`, `last`, `ref`, `unique`, numeric-random.
- `topgy` is a *target selector* (graveyard.Last()) for `permaremove`, not a command
  of its own. Not used in this pack.
- `burydrawn` exists and is engine-implemented (buries `TurnStat.lastDrawnCard`) but
  has **zero shipped users** — avoided.

**Gap map (why these 12):** no Firecast cantrip; no self-Burning payoff; no player
card on the Reshuffle trigger; no Firecast+Heavy card; no burn payoff reading
graveyard *size* (only `[[cardsBuried]]`); no graveyard-count + Reaping combo; the
burn↔graveyard bridge is one-directional (bury→cast) with nothing flowing back.

**Color identity:** INT (blue) for burn, matching Fireball/Combust/Searing_Ray;
DEXSTR (orange) for graveyard, matching Recycle/Rapture/Bone_Ritual; INTSTR (purple)
for bridges, matching the Halo bury cycle.

---

## Per-card rationale

### 1. Emberseed — 0, Common, Magic/Fire, Firecast
- **Role:** burn/Firecast enabler; graveyard filler.
- **Deck:** any bury shell; makes `bury:N` effects card-neutral when they hit it.
- **Budget:** 0-cost common ≈ 2–3 dmg-equiv. 1 Burning (never decays) + cantrip ≈
  Evangelize (0-cost Firecast: Zeal + random blessing).
- **Nearest:** Evangelize. **Different:** the only Firecast card that replaces itself —
  no shipped Firecast draws (checked all 14 `"firecast"` cardKeyword files).
- All DSL verified (inflict+referenceStatus per Fireball; draw:1 ubiquitous).

### 2. Ashfeast — INT 1, Common, Magic/Fire
- **Role:** burn payoff keyed to graveyard size (bridge, C-level).
- **Deck:** INT burn with Firecast/discard fill; dislikes exile-heavy bury (texture).
- **Budget:** 3 dmg flat (1-cost common band 3–5) + Ancestral-gated 4 Burning; gate
  identical to Boneskewer/Blood_Ritual (`CardsInGraveyard IsMoreThan 3`).
- **Nearest:** Boneskewer. **Different:** first Ancestral card outside the DEXSTR
  shaman suite; first burn payoff reading discard-pile size (Ashes_to_Ashes reads
  `[[cardsBuried]]` — burials only, different resource).
- All DSL verified.

### 3. Gravemoss — 0, Common, Utility
- **Role:** bury enabler that is itself a bury *target* (graveyard archetype).
- **Deck:** bury-dense shells; you rig your own bury lottery.
- **Budget:** played = 2 Armor + bury (cf. Singe 0-cost: 2 Burning + bury); buried =
  5 Armor at a random time (Armor fades at StartPhase, so enemy-phase procs decay).
- **Nearest:** Suntree_Twig (only shipped card with a self Bury-trigger effect; hidden
  transform on a Grounded basic). **Different:** first player-pool card whose *stated
  value* is higher buried than played — Firecast-for-defense without the keyword.
- Card-level Bury trigger verified in SpellEffects.BuryCard + Suntree_Twig.json.

### 4. Everburning Censer — INT 1, Common, Magic/Fire, Persistent
- **Role:** repeatable bury payload; burn + graveyard stock in one card.
- **Deck:** bury+recursion hybrid; loops with return effects.
- **Budget:** 3 Burning at 1 (≈ Halo_of_Wrath's per-proc rate as a one-shot); bury
  proc is a conditional bonus 3.
- **Nearest:** Singe. **Different:** first card built on the verified
  Persistent-bury routing (buried Persistent → graveyard, not exile) — survives
  burial and stays retrievable; Persistent ships today only as hand-retention.
- All DSL verified.

### 5. Flameletting — INT 1, Uncommon, Magic/Fire
- **Role:** self-Burning payoff (new mode: wants an affliction on self).
- **Deck:** masochist INT fire with cleanse valves (Cauterize, Fire_Resistance,
  Aura_of_Fire); Zeal decks get accidental fuel.
- **Budget:** floor 4 dmg for 1 INT (par) at the price of a permanent 2/turn
  self-DoT; escalates with copies/Zeal because Burning never decays.
- **Nearest:** Cauterize (self-Burning as cost rider). **Different:** the payoff
  direction is new — nothing in 2,525 cards rewards Burning on yourself.
- DSL: `bless:2`+refstatus Burning self-application per Cauterize.json;
  `[[my(status)Burning]]` per Pyroclasm.json; `*2` is NumberParser base arithmetic,
  deliberately NOT `multiplydamage:2` (crit channel, Part VIII).

### 6. Chosen for the Pyre — INT 1, Uncommon, Magic/Fire
- **Role:** precision bury → burn (burn/bury archetype).
- **Deck:** INT burn wanting thinning + reach; aims buryselect at Firecast payloads.
- **Budget:** Burning = 2×cost (avg ~2–4) vs Censorship (U): Zeal 2× + heal 2× —
  comparable, offense-shifted.
- **Nearest:** Censorship (the only shipped choose-bury). **Different:** ports
  choose-bury from HOLY/defense to INT/offense and makes the *choice* the tension
  (junk = thin-but-0-burn; bomb = big burn but card loss; Firecast = both).
- Structure copied 1:1 from Censorship.json (enchant:self + buryselect;
  EndAction `[[lastBuriedEnergy]]` payload; `removeenchant:this`; playCondition
  CardsInDeckRemaining>0). **Mild UNVERIFIED:** Censorship's helper enchant fires as
  three separate EndAction lines; I merged payload+cleanup into one `;`-joined line
  (multi-statement lines verified elsewhere, e.g. Bone_Ritual's EndAction line).

### 7. Emberdredge — INTSTR 1, Uncommon, Magic/Fire
- **Role:** **bridge #1 (non-obvious): graveyard → burn** — retrieval that advances
  the burn clock (reverse of Firecast's deck → fire direction).
- **Deck:** pack glue; re-arms Buried Sun, loops Everburning Censer.
- **Budget:** Recycle (C, DEXSTR1) = bare return:choose; +1×cost Burning rider at
  U/INTSTR mirrors Bone_Ritual's rider pricing (2× Armor at Rare + 1 Life). Not
  strictly better than Recycle (different color, higher rarity).
- **Nearest:** Bone_Ritual. **Different:** only the second card ever to price
  retrieval by the returned card's cost, and the payoff crosses archetypes.
- DSL: `return:choose` + EndAction `[[lastpickedenergy]]` + `removeenchant:this`
  verified verbatim in Bone_Ritual.json.

### 8. Ashen Harvest — DEXSTR 2, Uncommon, Melee, Reaping
- **Role:** graveyard-count finisher (graveyard archetype, Reaping payoff).
- **Deck:** DEXSTR graveyard stocking Heavy/Firecast residents; boss killer.
- **Budget:** 2-cost par ~8; graveyard is ~5–10 midgame; Uncommon pays for the
  Reaping rider and the anti-recursion tension (returns shrink it).
- **Nearest:** Rapture (2 dmg × GY, lifedrain, 3-cost). **Different:** single
  Reaping hit = permanent max-HP shred scaling on graveyard size — combination
  absent from the pool; creates a spend-vs-harvest decision with its own pack.
- DSL: `[[cardsInGraveyard]]` in damage slot per Rapture.json/Soultap.json.

### 9. Cinder Archive — INTSTR 1, Rare, Enchantment, Unique
- **Role:** **bridge #2 (non-obvious): burn tick frequency → card advantage.**
- **Deck:** burn deck splashing graveyard, or vice versa; Combust chains = draws.
- **Budget:** ≈ Roll_the_Bones (R, DEXSTR1, flat 1 random return/turn); mine needs
  Burning online but scales past 1/turn with tick effects.
- **Nearest:** Sanctify (per-tick trigger host) / Roll_the_Bones (payoff shape).
  **Different:** no shipped card converts a burn resource into a graveyard resource.
- **UNVERIFIED (flagged in meta):** whether self-Burning ticks (Zeal upkeep,
  Flameletting) also fire the player-side InflictStatusDamage scan — Sanctify's
  extra EnemyTurn/CurrentPhase guards hint at direction subtleties I did not fully
  trace. If self-ticks proc it, it's a noted synergy, not a break. Sim-check please.

### 10. Cinder Cycle — DEXSTR 1, Rare, Enchantment, Unique
- **Role:** **bridge #3 (non-obvious): reshuffle/fatigue race → burn + armor.**
- **Deck:** thin bury decks and Gravesite/Orphic_Glance mill decks that hit 2–4
  reshuffles; Armor half offsets fatigue chip (reshuffle #2+ ≈ 10% maxHP).
- **Budget:** ~2–3 activations × (6 Burning + 6 Armor) per long fight for 1 energy
  at Rare — engine-card envelope.
- **Nearest:** Entropy (enemy-side Reshuffle enchantment; no player analogue).
  **Different:** first player payoff on the Reshuffle trigger; turns a global
  penalty (fatigue) into a priced strategy. Bury accelerates reshuffles — hidden
  archetype synergy no shipped card exploits.
- Reshuffle-trigger enchantment hosting verified in Entropy/Divine_Ire/Fatigue.json.

### 11. Buried Sun — INT 2, Rare, Magic/Fire, Firecast + Heavy
- **Role:** bridge payload — burn bomb / bury roulette hit / permanent graveyard
  resident, in one card.
- **Deck:** every deck in the cluster wants it for a different reason.
- **Budget:** 8 + 4 Burning at 2-cost Rare is top-of-band (5–8), paid by Heavy
  (once per combat unless Emberdredge re-arms it). Not strictly better than
  Elite_Fireball (Legendary, INT1, no drawback, reward-locked rarity aside).
- **Nearest:** Elite_Fireball. **Different:** the pool's first Firecast+Heavy
  keyword pair (verified zero cards carry 11+21 together); the lifecycle
  (detonate-on-bury → Heavy graveyard residency → paid re-detonation) is new.
- All DSL verified; keyword routing verified in SpellEffects.BuryCard and
  DeckHandler.ReshuffleGraveyard.

### 12. Emberweave Communion — INTSTR 2, Legendary, Enchantment, Unique
- **Role:** capstone engine welding bury→burn and reshuffle→bury into one loop.
- **Deck:** all-in bury inferno (Prayer/Singe/Deification + Firecast payloads +
  Cinder Cycle); wincon = Burning snowball vs fatigue/deck-death clock.
- **Budget:** per-bury 2+cost (~3 avg) ≈ Halo_of_Wrath (C) per-proc; the Legendary
  premium is the reshuffle clause + cost scaling.
- **Nearest:** Halo_of_Wrath. **Different:** scales per buried cost (player-positive
  use of the `[[lastBuriedEnergy]]`-on-Bury idiom that ships only as the enemy
  affliction Extraction_Listener) and closes the loop with auto-bury on reshuffle.
- **DANGER FLAG (also in meta):** reshuffle→`bury:3` can cascade — BuryCard calls
  ResetDeck when the deck empties mid-bury, which fires Reshuffle again. Termination
  path verified (NO_MORE_CARDS break; Heavy-only graveyard yields empty reshuffles),
  and each cycle adds fatigue, so it is a self-limiting greed spiral by design — but
  this is the pack's #1 sim-harness target (with Cinder Cycle in play especially).

---

## Honest UNVERIFIED ledger

1. **Cinder Archive** — direction/targeting of `InflictStatusDamage` for self-owned
   Burning ticks (see card 9). Trigger+DamageType condition pair itself is verified
   (Sanctify.json).
2. **Chosen for the Pyre / Emberdredge** — helper-enchant `combat: false` +
   same-action `removeenchant:this`: shipped analogues (Censorship, Bone_Ritual)
   self-remove at EndAction so the combat flag should be moot; flag value on the
   shipped assets not extracted (CardEnchantments.combatEnchantment was 0 on both,
   matching my `false`).
3. **Emberweave Communion** — cascade termination reasoned from decompiled BuryCard
   control flow, not observed in play. Sim before ship.
4. Everything else: every command, trigger, condition, token, and keyword routing
   in this pack is backed by a named shipped card or a cited decompiled method.

## Cross-pack notes (one wave)

- Ashen Harvest + CrimsonLedger self-damage: Reaping max-HP shred stacks with
  Grave_Pact-style steals — no dependency, pure gravy.
- Cinder Archive returns feed Clockwork Cadence's CardsPlayedThisTurn payoffs.
- Nothing in this pack requires another pack.

## Art direction

Treatment: heat over dead ground. Every source is pulled toward ember orange focal light
with ash-grey mids and charcoal darks (shared gradient stops around #1a1210 / #c2571f /
#f5c469); cool navy backdrops from the core set are allowed to survive only as charcoal.
Sources are fire and graveyard art exclusively — flame-consumption pieces (Combust, Fan the
Flames, Aura of Fire) for the burn cards, grave/bone pieces (Gravesite, Recycle, Reaper's
Approach) hue-shifted from teal/green into ember for the graveyard half, so the pack's two
archetypes read as one scorched world. Composites mark the two archetype-welding cards:
Cinder Archive (grimoire + flame breath) and Emberweave Communion (ritual pillar + winged
ascension, noise-woven for the "weave").

Weapon & powers (same-menu adjacency, ART-MUTATION-SPEC §2): the Arcanist/Knight menus
hold Forcewand + Longsword and all 66 tier-0 talent sheets — all excluded. Cinderbough
Wand composites Suntree Twig's flame-wrapped branch (a mid-run weapon card, no class menu)
with Orb of Fire's fireball relighting the tip — already a cinder-bough, it only needed
the pack gradient. Cindersong takes Grimoire of Fire's flaming songbook (distinct sheet
from Cinder Archive's Grimoire of Storms); Pyregraft hue-shifts Rootlimb Strike's grafted
root-claw from olive to ember; Hearthguard reframes Fiery Shield's burning hearth-stone.
No tier-0 powerImage sheets anywhere in the four.

Starting card (same-menu adjacency, ART-MUTATION-SPEC §2): Stoke the Coals appears in the
Arcanist and Knight starting-card menus, so all 47 sprites backing those menus (defaults
Shocking Grasp/Bolstered Strike + every keystone starting card either class can see) are
excluded — notably Fiery Barrage, the single best theme fit, sits in EVERY class's
starting-card menu (its keystone declares no profession filter) and was therefore never a
legal source. Source chosen instead: Eldritch Flame (CARDS_METAMORPHOSIS_8, normal pool
card, no starting menu) — a hand cradling a flame, hue-rotated from witch-purple to ember
orange with the pack gradient: the coal being stoked.

---

## Weapons & powers (manifest v1.1)

**Shipped curve derived first (required by WEAPON-SPEC §4).** Weapons: all 6 shipped
base weapons are `damage:2`, Common, BasicAttack, 1 Neutral (Warmace: 2 Neutral for an
ungated status rider) — identity lives in exactly one rider/condition. Powers: the 66
extracted tier-0 talents distribute cooldown 1×1, 2×20, 3×32, 4×8, 5×2, 6×1, 10×1, 20×1
(79% at cd 2–3), and payoff scales as:

- **cd 2** — one small/level-scaled next-combat status (Evasion 1; Armor 3×lvl;
  Poison 2×lvl) or minor conversion, often + a passive rider or StartGame talent grant.
- **cd 3** — a start-of-combat card (choice-of-3 or free) OR ~2–3×lvl statuses OR a
  mid heal (10% max HP, per-deck-composition).
- **cd 4–5** — permanent deck edits (upgraderandom, upgradeallchain, addkeywordindeck,
  destroy) or bigger heals.
- **cd 6+** — dramatic one-offs (reward-pool override 6, Scar removal 10, full heal 20).
- Passive riders do NOT scale with cooldown: small per-trigger values (heal 1–3, 1
  status stack, 1 resource) or one-time StartGame `addtalent`/`imbue` grants.

### Weapon: Cinderbough Wand — 700000099, Magic, Arcanist/Knight
- **Role:** graveyard→burn bridge in the basic-attack slot (the direction the gap map
  calls missing).
- **Budget:** statline par (2 fire dmg, 1 Neutral, Common). Rider = 1 Burning/turn gated
  on `AnyValue IsMoreThan [[cardsBuried]]:0` (NOT the `CardsBurned` condition value —
  that is an unimplemented always-true stub in ConditionChecker.cs, and no vanilla card
  uses it); Warmace ships an UNgated 2-status/turn rider at cost 2, so a
  bury-gated 1/turn at cost 1 is under the shipped ceiling.
- **Nearest:** Forcewand (statline twin) / Warmace (status rider). **Different:** first
  weapon whose rider reads archetype state (graveyard) instead of position/cost-weave.

### Power: Cindersong Weapon — 700000098, cd 3, Arcanist/Knight
- **Role:** burn opener + bury→burn passive.
- **Budget vs curve:** primary identical to Flaming Weapon's cd-3 Burning×lvl; second
  half is a bury-conditional rider instead of Flaming's Focus×lvl + talent — at/under.
- **Nearest:** Flaming Weapon. **UNVERIFIED:** `Bury` as a talent rider trigger has no
  shipped tier-0 precedent (legal enum member; rider-on-odd-trigger pattern shipped —
  Shaking Weapon rides ShuffleCard).

### Power: Pyregraft Weapon — 700000097, cd 4, Arcanist
- **Role:** permanent Firecast grafting (manufactures the pack's enabler).
- **Budget vs curve:** permanent single-card edits sit at cd 4 (Psychic/Coiling/Forging);
  runs lighter than Psychic (flat 1 Zeal, no passive) because Firecast compounds every
  reshuffle. **Nearest:** Psychic Weapon. **UNVERIFIED:** `addkeywordindeck` ships only
  with the `psionic` argument; `firecast` is a legal CardProperties member on the same
  enum-parse path — in-game QA required.

### Power: Hearthguard Weapon — 700000096, cd 2, Knight
- **Role:** defensive opener with a Burning pip (turns on foe-Burning conditions turn 1).
- **Budget vs curve:** Armor 2×lvl + 1 Burning vs Fortified's cd-2 Armor 3×lvl + free
  card — strictly inside. All codeLines shipped-verbatim startstatus forms. **Nearest:**
  Fortified Weapon.

---

## Starting cards (manifest v1.2)

**Shipped curve derived first (WEAPON-SPEC §4; this is the full derivation the other
three packs cite).** Corpus = **63 cards**: the 6 Profession defaults
(`Profession.startingCards` PPtrs resolved via `tools/out/data-index.json` —
Knight=Bolstered Strike, Rogue=Sneak Attack, Arcanist=Shocking Grasp, Hunter=Feral
Strikes, Seeker=Mindstrike, Warrior=Backswing; Scion has none) + **57 distinct
`KeystoneType.StartingCard` keystone cards** (type 0 scan of
`tools/out/data/Keystone/*.json`). Measured over the 63 extracted Card JSONs:

- **Cost:** total 1 energy on 51/63 (**81%**); 7× cost-2, 3× cost-3, 2× cost-0. All six
  defaults are exactly 1-cost in class colors.
- **Rarity:** 22 C / 19 U / 18 R / 4 L (defaults: 5 C + 1 U — Rare is the keystone
  build-around tier, so always-available mod starting cards stay C/U).
- **Type:** Utility 40, Melee 10, Magic 5, Divine 5, Corruption 2, Ranged 1.
  **Category:** Action 56, Enchantment 4, Equipment 3.
- **Complexity:** 1–3 effect codeLines on 60/63 (median 2).
- **Flags:** 62/63 are normal acquirable reward-pool cards (`canBeAcquired: 1`,
  `excludeFromRewards: 0`; sole exception Battleclaw, a starts-in-play equipment) —
  starting cards are NOT reward-excluded, unlike our weapon default.
- **Shape:** 7/63 literally "Make a Basic Attack" (3 of the 6 defaults) — the corpus
  leans on the weapon; each card is one cheap archetype seed with usually one gate.
- **Engine facts:** the pick enters the starting deck **once** (`CreateStartingDeck`:
  surges + weapon ×6 + Block ×2 + starting card ×1, CreateCharacterFunctions.cs:251-267)
  and persists as a plain int record (`startingCardID`) — an ordinary deck card
  thereafter, drawn turn 1–2 of nearly every early combat. Reliability, not raw power,
  is the budget axis.

### Starting card: Stoke the Coals — 700000095, Magic/Fire, C, 1 INT, Arcanist/Knight
- **Role in loadout:** Cinderbough Wand's rider needs `[[cardsBuried]] > 0`; Cindersong's
  rider needs burials. Stoke the Coals is the turn-1 ignition — 3 fire damage + bury a
  card — so the weapon and power are live from the first turn, and the buried card spins
  the Firecast roulette. Weapon (bury-gated burn basic) + power (burn opener) + card
  (the bury) = one coherent turn-1 identity.
- **Budget vs curve:** cost 1 (81% mode), Common, 2 effect lines. 3 dmg is the low end
  of the 1-cost common band (3–5); the bury rider is archetype-positive here and
  neutral-to-negative outside bury shells. Strictly under in-pack Ashfeast.
- **Nearest existing:** Dedication (the ONLY burier among the 63 — bury→HOLY economy,
  Divine) / Singe (0-cost 2 Burning + bury, not a starting card). **Different:** the
  attack version that stocks the graveyard as a resource; Dedication's burial is fuel
  for energy, not stock, and no shipped starting card feeds a graveyard archetype.
