# CrimsonLedger — design notes

Sacrifice & artifice cluster: blood (costLife / self-damage / souls) × enchantment/equipment
charges. IDs 700000300–700000310 (11 cards: 4C / 3U / 3R / 1L).

## The life-for-power exchange rate (derived from shipped blood cards)

| Shipped card | Price | Payout | Implied rate |
|---|---|---|---|
| Umbral Lance (U) | 1 INT + 2 Life | 3 dmg + curse into foe's deck | curse ≈ 4 dmg-equiv ⇒ 2 Life ≈ 1 energy |
| Vile Blast (C) | 2 Life | affliction-scaling damage (~5–15 conditional) | ~1–1.5 energy of conditional value ⇒ ≈ 0.5 energy/Life |
| Hellforged Axe (L) | 2 N + 1 Life (+4 junk cards) | 15 dmg | 15 ≈ cost-3 top-end ⇒ 1 Life + junk ≈ 1 energy |
| Blood Ritual (C) | 1 Life, 3 charges | 3 Bleeding + Ancestral return | ≈ 0.5–0.75 energy/Life, charge-throttled |
| Soultap (R) | 1 DEXSTR + N self-damage | N Souls | **1 Soul ≈ 1 HP** |

**Working rules used everywhere below:**
- **2 Life ≈ 1 energy** of stat budget (never more generous than that, because a life cost
  is also a *synergy payment*: it fires `DealtDamage(targetSelf)` + `SpendEnergy(v=4 BLOOD)`
  and counts into `damageTakenTurn`, i.e. it self-enables Frenzy and every BLOOD/self-damage
  listener in this pack — GAME-MECHANICS Part I).
- **1 Soul ≈ 0.5–1 HP** (Soultap 1:1; 100 souls = revive at 50 HP ⇒ marginal soul ≤ 0.5 HP);
  I price soul *payouts* at the 1 HP end and soul *spends* at the 0.5–1 HP end.
- Repair value: `repair:1` ≈ half of Field Repairs' 1-energy package (3 Armor + Frenzy-gated
  repair-all).

Effective-energy cost curve of the pack (life counted as 0 tempo): 0-cost ×3, 1-cost ×7,
2-cost ×1 — matches the pool's low-cost skew (15/57/16/11). Counting Life as cost slots:
0×2, 1×3, 2×5, 3×1.

Color identity: pure-life-cost cards render **Black** (Flesh Installment, Debt Collector,
Ledger of Vitae, Vengeful Mainspring, Exsanguine Engine); STR+Life cards render **Red**
(Blood Retainer, Sanguine Tally); Soul Dynamo is Blue (INT engine), Tithe Censer / Usurer's
Bargain / Paper Cut are Brown neutrals. Black/Red core with neutral support — coherent per
Card.GetColor rules.

Structural idioms copied from shipped cards (all read end-to-end):
- Equipment skeleton `equip` → payload `PlayPhase: activate` / `None: <use>` /
  `DiscardPhase: disable`, durability = charges: **Umbral_Armor.json, Carving_Knife.json,
  Grimoire_of_Hate.json**.
- BLOOD-spend listener `SpendEnergy` + `EnergyType Is BLOOD`: **Bloodbank.json**.
- Turn-gated self-damage listener `DealtDamage` + `EnemyTurn IsNot` + `[[varValue]]`:
  **Levy_Pain.json / Shield_of_Sorrow.json / Sanguine_Shield.json**.
- `adddamage:[[enchantmentStacks]]` on `CalculateOutgoingDamage`: **Acquire_Target.json**.
- Souls gating target format `[[souls]]:N`: **Mojo.json / Reave.json**.
- Self-decharge `repairself:-1`: **Steel_Golem.json**; self-cleanup `removeenchant:this`:
  **Sharpen_Steel.json**.
- `inflict:X` + `referenceStatus` (441-card idiom), never status-name commands.

---

## Per-card rationale

### 1. Paper Cut — C, Melee, cost 0 — `damage:2` + `directdamage:1:bleeding:self`
- **Role:** free Frenzy/self-damage dial; the pack's "proc button".
- **Deck:** any blood list; with own Bleeding stacks the take-1 triggers extra Bleeding ticks
  (Bleeding fires on DealtDamage — Part II), which Ledger of Vitae monetizes.
- **Budget:** cost-0 common = 2–3 dmg; 2 dmg minus 1 HP is *below* curve — the slot is paid
  for by enabler text, matching "cards earn their slot with text".
- **Nearest:** `Fury.json` (take 1 + benefit cantrip). Difference: Fury is a conjured junk
  card; this is an acquirable attack whose self-damage is deliberate fuel.
- **Honesty flag:** least novel card in the pack; kept because every listener here needs a
  cheap trigger. If the reviewer cuts one card, cut this one.

### 2. Blood Retainer — C, Melee, 1 STR + 1 Life — `damage:5` + `repair:1`
- **Role:** attack that maintains equipment; bridge #1 (blood → charges).
- **Budget:** 1.5 energy-equiv ⇒ curve ≈ 6 dmg; 5 dmg + unconditional `repair:1` ≈ even.
  Self-enables Frenzy for the rest of the turn (life paid at PayAllEnergy, before resolution).
- **Nearest:** `Field_Repairs.json` — defensive chassis, Frenzy-gated repair. This is the
  offensive, blood-priced, unconditional version; different play pattern (attack every turn
  to keep engines fed vs hold a utility card).

### 3. Flesh Installment — C, Magic, 2 Life — `damage:4:blood` + `changesouls:2`
- **Role:** zero-energy damage that banks Souls; HP→Souls trickle.
- **Budget:** 2 Life ≈ 1 energy ⇒ 4 dmg on curve; +2 Souls (≈2 HP battery) compensates
  paying in HP. Not strictly better than any shipped 1-cost common (it costs 2 HP/cast).
- **Nearest:** `Vile_Blast.json` (same 2-Life common chassis). Difference: flat tempo + soul
  bank vs conditional affliction nuke — opposite deck (greed vs afflictions).
- Damage type string `blood` is cosmetic (Part II); precedent `Chalice_of_Blood.json`
  (`directdamage:...:blood:self`).

### 4. Debt Collector — C, Utility, 1 Life — `inflict:2`(Bleeding) + `changesouls:[[other(status)Bleeding]]`
- **Role:** bleed→souls payoff; cross-pack handshake with VenomousLegacy.
- **Budget:** 0.5 energy-equiv for 2 Bleeding (on rate); the souls read is conditional value
  (typically 3–8 Souls with setup ≈ 1–2 energy of battery), justified as archetype payoff
  per spec §4.
- **Token:** `[[other(status)Bleeding]]` — verified status-reader form (GameTextTranslator;
  live example Combust's `[[other(status)Burning]]`, `Combust.json`).
- **Nearest:** `Soulbolt.json`. Difference: Soulbolt *spends* souls on a copy; nothing in the
  pool *gains* souls by reading enemy afflictions.

### 5. Sanguine Tally — U, Enchantment, 1 STR + 1 Life — blood-spend → +1 dmg/stack
- **Role:** the blood-aggro engine; makes every life payment permanent offense.
- **Payload:** `SpendEnergy`+`EnergyType Is BLOOD → addstack:1` (Bloodbank's exact enabler);
  `CalculateOutgoingDamage → adddamage:[[enchantmentStacks]]` (Acquire_Target's exact payoff).
- **Budget:** dedicated decks add ~2–3 stacks/turn — Anger-like growth but only from paying
  HP; its own 1-Life cost seeds stack #1. Uncommon: engine, but needs a whole deck to beat a
  vanilla Anger package.
- **Enchantment type `other`** (Bloodbank precedent) so enemy dispel can't strip it — it's a
  counter, not a blessing.
- **Nearest:** `Bloodbank.json`. Same trigger, opposite payoff axis (offense vs sustain);
  passes "new payoff for an existing setup".

### 6. Tithe Censer — U, Equipment, 1 Neutral, 3 charges — BLOOD spend → heal 3, −1 durability
- **Role:** bridge #2 (artifice absorbs blood costs); durability as a heal battery.
- **Payload:** reactive `SpendEnergy`+`EnergyType Is BLOOD → heal:3; repairself:-1`
  (Bloodbank listener + Steel Golem self-decharge); cleanup `StartPhase → removeenchant:this`
  when stacks < 1 (Sharpen Steel idiom, EnchantmentStacks condition per Carving Knife's
  deplete gate).
- **Budget:** 1 energy → ≤9 HP refunded, only while paying life; repairable (Blood Retainer /
  Field Repairs) for more. Heals are poison-eaten as usual.
- **Nearest:** `Blood_Levy.json` — the inverse decision (Levy taxes blood spend with Bleeding;
  Censer refunds it), on an equipment chassis no shipped card uses reactively for BLOOD.
- **UNVERIFIED:** (a) whether a passive equipment container with no `activate` line behaves
  identically to shipped activated ones (all 3 read equipment use PlayPhase/activate);
  (b) whether the engine itself despawns an equipment container at 0 stacks — the explicit
  StartPhase cleanup is the guard. Sim-harness check requested.

### 7. Usurer's Bargain — U, Utility, cost 0, gate `[[souls]] > 11` — `changesouls:-12; draw:2; bless:4`(Barrier)
- **Role:** souls-liquidation for tempo; makes over-banking a real choice vs the 100-soul revive.
- **Budget:** 12 Souls ≈ 12 HP battery buys ~2 energy of effect (draw 2 + Barrier 4) at
  0 energy. Gated by the shipped `[[souls]]:N` AnyValue format (Mojo/Reave).
- **Nearest:** `Mojo.json` (souls dump → perform). Difference: tempo/defense payout, playable
  outside bard/crit shells — a payoff family souls never had.

### 8. Ledger of Vitae — R, Enchantment, 1 N + 1 Life — on-turn damage taken → that many Souls
- **Role:** pack spine; every chosen wound double-pays (effect + battery).
- **Payload:** `DealtDamage` + `EnemyTurn IsNot ""` + `changesouls:[[varValue]]` — exactly
  Levy Pain's trigger/gate and Shield of Sorrow's `[[varValue]]` read (damage amount).
  Turn-gate stops enemy chip from feeding it.
- **Budget:** 5–8 self-damage/turn in a committed list → 5–8 Souls/turn ≈ 1 energy/turn of
  value at the Soultap 1:1 rate; costs a card + 1E + 1 HP and does nothing without
  self-damage sources. Rare (Soultap's one-shot is already Rare).
- **Nearest:** `Soultap.json`. Difference: engine vs one-shot, and it counts *all* on-turn
  self-damage (Zeal upkeep, Chalice draws, own Bleeding ticks, life costs), creating a
  payoff that *wants* afflictions on yourself — spec §5 mode 4.
- **Note:** `[[varValue]]` is not in the Part VI token list; cited from live codeLines
  (`Sanguine_Shield.json`, `Levy_Pain.json`, `Shield_of_Sorrow.json`).

### 9. Soul Dynamo — R, Equipment, 1 INT, 3 charges — Use: −8 Souls → +2 INT energy, draw 1
- **Role:** bridge #3 (souls → artifice); first souls→energy conversion in the pool.
- **Payload:** verbatim Umbral Armor skeleton (`PlayPhase: activate` / `None: use` /
  `DiscardPhase: disable`); use-line gated `AnyValue IsMoreThan [[souls]]:7`.
- **Budget:** per use −8 Souls (≈8 HP battery) → ~2.5 energy of ramp; ×3 uses. Energy at cap
  cycles oldest orb (Part I) so no overflow abuse. Rare engine, dead without a souls package.
- **Nearest:** `Grimoire_of_Storms.json` (activated equipment engine). Difference: Grimoires
  burn HP (costLife) to make cards; this burns Souls to make energy — inverted fuel line.
- **UNVERIFIED:** whether an activation that fails the use-condition still consumes
  durability (shipped Grimoires put conditions on their use-lines the same way; behavior
  assumed identical). Flag for in-game QA.

### 10. Vengeful Mainspring — R, Enchantment, 1 N + 1 Life — Charge spent → 3 arcane damage
- **Role:** the charge-economy payoff; makes decks *want* to burn durability/charges.
- **Payload:** single `SpendCharge → damage:3:arcane` effect.
- **Budget:** 2–4 charge spends/turn in a charge-dense shell → 6–12 dmg/turn for 1.5
  energy-equiv; comparable Rare engines (Ascension line) pay similarly.
- **Nearest:** `Bomb.json` — the only shipped SpendCharge listener (in-hand, self-serving
  cost reduction). This is the first *enchantment* payoff on that trigger: new
  trigger/zone combination per spec §5 mode 2.
- **UNVERIFIED (the honest one):** `SpendCharge` fires for **non-Equipment** charge cards on
  Decharge (Part IV) and Bomb proves in-hand scanning; whether the *player enchantment zone*
  is scanned for SpendCharge has no shipped precedent. If the sim shows the zone isn't
  scanned, fallback design is moving the listener onto an Equipment payload.

### 11. Exsanguine Engine — L, Equipment, 2 N + 1 Life, 4 charges — Use: draw 1, convert it to BLOOD
- **Role:** Legendary build-around; converts any deck into blood-spend fuel, throttled by
  durability. Bridge #4.
- **Payload:** Umbral Armor skeleton; use = `draw:1; convertlastdrawntoblood` (command
  verified in `Coven_s_Sight.json`, `Blood_for_Blood.json`, `Sanguine_Casting.json`).
- **Budget:** 3 effective cost for 4 activations, each ≈ Bloodcast (Rare, 1E: draw +
  blood-copy) without the extra copy. Converted costs then feed Sanguine Tally (+stacks),
  Tithe Censer (heals), Ledger of Vitae (souls via the HP paid). The 4-charge cap is the
  anti-degenerate throttle vs Overdraft-style mass conversion.
- **Nearest:** `Sanguine_Casting.json` (one-shot action). Difference: repeatable
  equipment engine + draws the natural top card (no selection) — different risk profile.

---

## Pack-level checks

- **Bridges (required ≥2):** Blood Retainer (blood→charges), Tithe Censer (charges absorb
  blood), Soul Dynamo (souls→artifice), Exsanguine Engine (artifice→blood). ✔
- **Name uniqueness:** all 11 names grepped case-insensitively against the 2,525 extracted
  filenames — zero collisions ("Dynamo" and "Song of Vitae" exist; full names differ).
- **Commands:** every codeLine token checked against `../docs/research/reference/effect-commands.txt`:
  damage, directdamage, repair, repairself, inflict, bless, changesouls, enchant, addstack,
  adddamage, equip, heal, removeenchant, draw, activate, disable, energyINT,
  convertlastdrawntoblood. ✔
- **Triggers/conditions:** PlayAction, SpendEnergy, CalculateOutgoingDamage, DealtDamage,
  StartPhase, PlayPhase, DiscardPhase, SpendCharge, None — all GameTriggers members;
  EnergyType / EnemyTurn / AnyValue / EnchantmentStacks + Is/IsNot/IsMoreThan/IsLessThan —
  all ConditionValue/ConditionOperator members (decompiled ConditionChecker.cs).
- **Degenerate-loop scan:** Tithe Censer heal (3/blood-spend) cannot loop (charges cap);
  Ledger of Vitae + Paper Cut = 1 soul/card/turn, bounded by hand; Sanguine Tally has no
  self-feeding (stacks don't cause spends). Soul Dynamo → energy → more plays → more life
  paid → more souls is the intended engine; throughput bounded by 8-souls-per-use pricing
  (souls in << souls out unless heavily built).
- **Open UNVERIFIED summary:** (1) SpendCharge scanned from enchantment zone (Mainspring);
  (2) passive equipment container without activate line (Censer); (3) durability consumption
  on failed use-condition (Dynamo); (4) auto-despawn of 0-durability equipment (Censer —
  guarded by explicit cleanup). All flagged for sim harness / in-game QA gates 3–4.

## Art direction

Treatment: a blood-lit ledger room. Blacks are crushed, reds pushed arterial, and every
card carries a candle-gold accent (#e8b54a) via wash, gradient stop, or glow — the gold is
the "ledger" thread tying blood cards to soul cards. Blood sources keep their red but get
gold rim-light (Blood Levy, Blood Ritual, Bloodletting); soul sources keep the game's
signature teal soul-glow as the one permitted cold accent (Soul Dynamo, Debt Collector), so
Souls stay instantly readable against the red room. Composites mark the two conversion
engines: Soul Dynamo (Damnation's radiating figure noise-blended with Soulbolt's comet) and
Exsanguine Engine (Vampirism's hungry face over Bloodcast's blood-working hands).

Weapon & powers (same-menu adjacency, ART-MUTATION-SPEC §2): the Warrior/Knight menus hold
Warmace + Longsword and all 66 tier-0 talent sheets — all excluded. Bloodprice Falchion
composites Shamshir (a mid-run weapon card, no class menu) over Blood and Iron's bloody
grip rising from the bottom edge, so the blades stay the read and the life-cost shows in
the hilt. Bloodtithe keeps Bloodmoney's coins-in-a-blood-pool nearly literal (the card IS
the theme); Soulscribe dirties Red Ink's thorn quill into arterial black; Pawnbroker's
warms Treasure for Trash's hand-off with candle gold — three card arts, zero tier-0
powerImage sheets.

---

## Weapons & powers (manifest v1.1)

**Shipped curve (derived, cited in EmberweaveGrove notes in full):** weapons = damage 2,
1 Neutral, Common, one rider (Warmace: 2 Neutral). Powers (66 tier-0 talents): cooldowns
1x1, 2x20, 3x32, 4x8, 5x2, 6x1, 10x1, 20x1; cd 2 = small/level-scaled next-combat
status or minor conversion (+ optional passive), cd 3 = free/choice card or 2-3xlvl
status, cd 4-5 = permanent deck edits, cd 6+ = dramatic one-offs; riders are small
per-trigger values, non-scaling with cd.

### Weapon: Bloodprice Falchion - 700000399, Melee, Warrior/Knight
- **Role:** the ledger as a basic attack - costs 1 Life, zero energy; every swing fires
  the pack's listener stack (life payment = damage taken -> Frenzy; = BLOOD energy
  spend -> Bloodbank-pattern riders) and banks 1 Soul toward the 100-soul revive.
- **Budget:** at the pack's derived rate (2 Life ~ 1 energy) 1 Life buys ~0.5 energy;
  damage stays at the universal 2 and the soul (<=1 HP battery) roughly refunds the
  life paid - the real gain is tempo + trigger fuel, and the sustained ~1 HP/turn drain
  is a drawback no shipped weapon carries. Renders Black (pure life cost) per GetColor.
- **Nearest:** Longsword (statline) / Soultap (HP->souls). **Different:** the only
  weapon with no energy cost; identity is an economy loop, not a statline bump.

### Power: Bloodtithe Weapon - 700000398, cd 2, Warrior/Knight
- **Role:** HP->gold conversion; the pack thesis as a button.
- **Budget vs curve:** Jewelled (cd 3) nets ~lvl gold + a potion per 3 combats;
  Bloodtithe nets 2xlvl gold per 2 combats minus lvl HP (exploration damage buys no
  combat triggers, so the HP is a real price). Per-combat gold rate ~ Jewelled, the
  surplus paid in HP. Both codeLines shipped tier-0 verbatim (Reckless takedamage,
  Jewelled gold). **Nearest:** Reckless Weapon x Jewelled Weapon.

### Power: Soulscribe Weapon - 700000397, cd 3, Warrior/Knight
- **Role:** armor opener + permanent blood->souls dividend.
- **Budget vs curve:** armor half runs UNDER the shipped cd-2 baseline (2xlvl vs
  Fortified's 3xlvl + card) one cd step higher; the delta prices the rider, which pays
  1 resource per trigger event like shipped small riders (per SpendEnergy event, not
  per HP - big life costs don't multiply it). Rider encoding = Bloodbank.json's shipped
  SpendEnergy + EnergyType Is BLOOD. **Nearest:** Fortified Weapon + Bloodbank (card).
  First tier-0 talent listening to BLOOD spends.

### Power: Pawnbroker's Weapon - 700000396, cd 5, Warrior/Knight
- **Role:** deck-as-collateral - destroy a card, gain 2xlvl gold.
- **Budget vs curve:** card destruction is the shipped cd-5 effect (Voracious); a
  one-shot 2xlvl gold vs Voracious' repeatable heal-on-destroy passive is at-worst
  equal. Both codeLines shipped verbatim (remove, gold). **Nearest:** Voracious
  Weapon - same setup action, economy payoff instead of sustain.

---

## Starting cards (manifest v1.2)

**Shipped curve (derived, cited in EmberweaveGrove notes in full):** 63-card corpus
(6 Profession defaults + 57 KeystoneType.StartingCard keystone cards): cost 1 on 51/63
(81%), rarity 22C/19U/18R/4L (defaults 5C/1U), Utility 40 / Melee 10 dominant, 1-3
effect lines (median 2), 62/63 normal reward-pool cards (NOT excludeFromRewards),
7/63 "Make a Basic Attack". The pick enters the starting deck ONCE (surges + weapon x6 +
Block x2 + card x1) and is an ordinary card thereafter.

### Starting card: Repossession — 700000395, Melee, C, 1 Neutral, Warrior/Knight
- **Role in loadout:** closes the Falchion's bank→cash loop from combat 1. Bloodprice
  Falchion drips 1 Soul per swing and Soulscribe pays a souls dividend on every blood
  spend — but nothing in the starting kit *spends* souls before the 100-soul revive
  abstraction. Repossession is the pocket-change outlet: 3 dmg, and with 3+ Souls it
  spends 3 for 3 more. Three swings fund a boosted hit; the ledger becomes a decision
  on turn 3, not at soul 100. Weapon (banks) + power (dividend) + card (cash-out).
- **Budget vs curve:** cost 1 (81% mode), Common, 2 effect lines. Base 3 is under the
  4-dmg 1-cost common par; conditional 6 total is priced by 3 banked Souls (≈3 HP of
  battery at the pack's derived 1 Soul ≈ 0.5–1 HP rate) — net ≈ par with loadout
  support, never free. Deliberately no Life cost (Brown neutral per GetColor): the
  SPEND side of the ledger stays playable at low HP.
- **Nearest existing:** Soultap (Hunter keystone starting card — the ONLY souls card
  among the 63, and it only *gains*) / Mojo (souls-gated spend, bard shell, not a
  starting card). **Different:** 0 of the 63 spend Souls; first damage-payout souls
  spender at starting-card scale, sitting below in-pack Usurer's Bargain's 12-soul
  liquidation.
- **DSL:** gate + spend are Mojo-verbatim — `AnyValue IsMoreThan "[[souls]]:2"` and a
  ';'-joined payoff+spend line (Mojo ships `perform:4;souls:-3` under the same gate);
  `souls` is in effect-commands.txt (distinct from the pack's `changesouls` usage —
  both shipped forms).
