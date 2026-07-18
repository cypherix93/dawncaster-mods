# Dawncaster game mechanics — ground truth (decompile-verified)

Compiled 2026-07-17 from `decompiled/` (562 C# files, ilspycmd 10.1.1, byte-accurate mirror of
`Assembly-CSharp.dll`); updated 2026-07-18 with the extracted ScriptableObject dumps in
`tools/out/data/` (2525 Cards, 49 StatusEffects, 383 Talents, 170 Battles, ... — see
`asset-extraction-notes.md` for enum decoding). Purpose: make card/event/talent
designers experts in the strategic system. **Every mechanical claim cites the decompiled file
(and method for load-bearing claims) or the specific asset JSON.** Enum ints in asset JSONs are
decoded against the decompiled enums (e.g. `Status.effectTrigger`/`clearAt` against
`EventHandler.GameTriggers` declaration order, `LastingEffect.EffectType` 0=blessing
1=affliction 2=other...). Anything not traceable is marked **UNVERIFIED**. Exact enum spellings
(including typos like `CardRariry`, `Phyisical`) are API surface — never "fix" them.

Architecture recap (see GROUND-TRUTH.md): all card/talent/status/event behavior is the string DSL
(`command:arg:...` statements joined by `;`) interpreted by `SpellEffects.PlayEffect` (565
commands, `reference/effect-commands.txt`), gated by `Condition` lists evaluated in
`ConditionChecker`, fired by `EventHandler.GameTriggers`. Statuses are `Status` assets with **one**
`effectTrigger` each (49 ship — `tools/out/data/StatusEffect/`); the `Status` class carries no
effect script of its own — behavior is hardcoded C#, dispatched on the status *name* in
`SpellEffects.RunStatusEffect`'s switch (plus name-checks scattered through combat code), with
per-asset data controlling only type, trigger, and expiry.

---

## Part I — Core resources & turn structure

### Energy

- Energy types: `EnergyHandler.energyTypes = { DEX, INT, STR, HOLY, NONE, BLOOD }`
  (EnergyHandler.cs). Player energy is a *list of typed orbs* (`playerEnergy`,
  `List<EnergyRecord>`), not four counters.
- **9-slot cost array** (order fixed everywhere): `[0]=DEX, [1]=INT, [2]=STR, [3]=HOLY,
  [4]=DEXINT, [5]=DEXSTR, [6]=INTSTR, [7]=Neutral, [8]=Life` (Card.GetCostArray, Card.cs;
  consumed in EnergyHandler.CostArray / CalculateCardCost).
- Hybrid costs (slots 4–6) resolve at pay time into whichever of the two pools has more free
  energy (player can drag-select distributions — `GetHybridDistributions`); Neutral (slot 7) pays
  from any pool, defaulting to the largest pool, player-selectable
  (`GetNeutralDistributions`) (EnergyHandler.CostArray).
- **Life cost (slot 8)**: `EnergyHandler.PayAllEnergy` → `PlayerHandler.ChangeLife(-cost[8],
  "bleeding")`, fires `DealtDamage` (targetSelf) + `SpendEnergy` with v=4 (BLOOD), and counts
  into `TurnStat.damageTakenTurn` — life costs ARE self-damage and feed Frenzy/self-damage
  payoffs. Monsters pay life costs from their own HP (`PayMonsterEnergy`).
- Energy per turn (`CombatHandler.EnergyPhase`): DEX = `PlayerHandler.Dexterity`, INT =
  `Intelligence`, STR = `Strength`, **HOLY has no per-turn stat** — it only arrives via
  carryover bonuses (`carryOverData.bonusHOLY`) and effects (`energyHOLY`, `faith` commands).
  Carryover bonuses (`bonusDEX/INT/STR/HOLY`, set by `NextTurnBonus`-type effects) are added
  then zeroed each EnergyPhase.
- Energy cap: `PlayerHandler.maxEnergy = 8` default; effective cap =
  `Math.Clamp(maxEnergy - CombatStat.defile, 0, maxEnergy)` (PlayerHandler.GetMaxEnergy).
  **Gaining energy at the cap deletes the OLDEST orb first** (`playerEnergy[0]` removed,
  EnergyHandler.AddEnergy / InstantlyAddEnergy) — energy "cycles", it doesn't just clamp.
- Energy does NOT reset to zero between turns: `EnergyHandler.SetEnergyToZero()` is an **empty
  method** — leftover energy persists (this is what "channel" and `Empower` build on).
- Each orb gained fires `GameTriggers.GainEnergy`; each orb spent fires `SpendEnergy` with
  `v` = type index (0..3; 4 = blood) (EnergyHandler.AddEnergy / RemoveEnergy / PayAllEnergy).
- Monster energy is a single untyped int `E_energy`, cap `MonsterHandler.GetMaxEnergy()`
  (`AddMonsterEnergy` hardcodes a 5 cap on one path), refilled `E_energyPerTurn` per turn;
  `monsterenergy:X` raises `E_energyPerTurn` (SpellEffects.MonsterEnergy).

### Turn & phase flow

Phases are `GameTriggers` values; `CombatHandler.GoToPhase` drives the loop and
`EventHandler.FindTriggeredEvents(phase)` fires at each step, so cards/talents/statuses can hook
any phase.

1. **StartCombat** (`CombatHandler.StartCombat` + `SetStartingEffects` coroutine):
   `EnterCombat` trigger → combat deck built (`DeckHandler.CreateCombatDeck`; **Memorized cards
   are moved to the top of the deck**, CombatHandler.StartCombat) → Code-category cards in deck
   run their `None`-trigger effects (`RunCodeEffects`) → imbued cards run → carried-over
   castables (Enchantment/Attunement/Form/Equipment/Summon/Performance in `castList`) re-cast →
   companion starting enchantments → area effects → affixes (player-affixes become an `affix`
   Enchantment container) → battle `enchantmentAura`/`startingEnchantments` → enemy deck
   `StartCombat` effects → battle `startingStatus` → player Scars applied as a status →
   `StartCombat` trigger (fired once per side) → enemy draws `Battle.cardsPerTurn`.
2. **StartPhase** (`CombatHandler.StartPhase`): `turnNumber++`, `roundNumber++`, turn stats
   reset (`ResetAllTurnStats`; `damageTakenLastTurn`/`evadedLastTurn` carried over). If the
   enemy has the "First Strike" status on turn 1, the player's first turn is skipped entirely.
   `StartPhase` trigger fires.
3. **DrawPhase**: draw `CombatStat.cardsPerTurn` (default 5, from
   `PlayerHandler.GetCardsPerTurn`) + `carryOverData.bonusCards` (`DeckHandler.DrawCards`).
   Skippable via `skipphase`/`CombatStat.skiptriggers`.
4. **EnergyPhase**: energy gain as above.
5. **PlayPhase**: **the tide flips here** — first player turn = `Ebb`, then alternates
   Ebb↔Flow every player turn (`CombatHandler.SetTurnTide` → `SpellEffects.SetTide`).
   `Dominated` on the player forces auto-play (`SpellEffects.Dominated`). Player plays cards
   until pressing end turn (`EndPlayerTurn` → DiscardPhase).
6. **DiscardPhase** (`CombatHandler.PlayerDiscard`): whole hand discarded
   (`SpellEffects.Discarding`) **except Persistent cards** (SpellEffects.Discarding, keyword
   check). If the player ends the turn with **0 energy, all "channel" lasting effects are
   removed** (`cardKeywords.Contains("channel")` check in PlayerDiscard) — keeping ≥1 energy
   sustains channels.
7. **EndTurnPhase**: statuses whose `Status.clearAt == EndTurnPhase` are removed (or lowered by
   1 if `lowerInsteadOfClear`); **non-`combatEnchantment` enchantments expire here**
   (EventHandler.RemoveLastingEffects).
8. **EnemyPhase**: `playerTurn = false`, `MonsterHandler.StartMonsterTurn()`, `turnNumber++`
   again (turnNumber counts half-turns; roundNumber counts full rounds).
9. **VictoryPhase** on enemy HP < 1 (`CombatHandler.VictoryPhase`) → XP/gold → Rewards phase;
   player HP < 1 → `PlayerHandler.Defeat()` (souls revive may intercept, see Part V).

- Hand size: `PlayerHandler.maxHandSize = 10` default; effective =
  `maxHandSize + combatData.bonusMaxHandSize` (`PlayerMaxHandsize`); DSL `maxhand:X` adds,
  `setmaxhand:X` sets exactly. Draw stops at max hand ("MAXIMUM_CARDS") unless a draw call
  passes `ignoreMaxHandSize` (DeckHandler.DrawCards).
- Deck exhaustion: drawing from an empty deck triggers `DeckHandler.ResetDeck` →
  **`SpellEffects.AddFatigue()`** then graveyard reshuffles into deck (**Heavy cards stay in
  the graveyard**, DeckHandler.ReshuffleGraveyard) and `Reshuffle` trigger fires.
- **Combat Fatigue** (SpellEffects.AddFatigue): each reshuffle: `fatigue++` (only on
  difficulty > 0); when `fatigue − fatigueResistance == 0` a fatigue enchantment (card ID
  778954) is applied; then player takes
  `round(maxHealth × (fatigue − fatigueResistance)/10)` unpreventable damage.
  `fatigueResistance` default 1 (PlayerHandler.cs) — i.e. the second reshuffle starts hurting.
  Fatigue resets each combat (`ResetFatigue` from `ResetCombatMetrics`).

### The action queue (SpellManager.cs)

Three nested queues, all pausable:

- **Card queue** (`cardQueue`, `AddToSpellQueue` → `RunSpellQueue`): one entry per card played;
  fires `QueueAction` trigger on enqueue; pays energy (`PayAllEnergy`) then
  `SpellEffects.ExecuteCard(..., PlayAction, playedFromHand: true)`. Cards wait on
  `ExecutionBlocker` flags: `SystemPaused, HardPause, DeckViewerOpen, DrawInProgress,
  PerformanceSelection, AnotherCardExecuting, ActionQueueRunning, PlayActionRunning`.
- **Action queue** (`actionList`, `AddActionToQueue` → `RunActionList`): individual
  `CardEffect` lines from triggered effects, executed via `PlayEffectPublic` with per-command
  delays (`CalculateDelay`: draw 0.35s, discard 0.3s, adddamage/multiplydamage 0s, basicattack
  0.2s, default 0.1s).
- **Play-action list** (`playActionList`, `AddToPlayActionList` → `ExecuteActions`): "play this
  card N times" loops (used by `play`, `playcopy`, Echo, Dominated etc.). Runs the card's
  `PlayAction` effects, `Kill` effects when the enemy dies mid-loop, and (SpellManager line
  ~685) **grants 1 stack of the `chain` status after resolving a Chain-keyword card**.
- Pause model: `SetPause` (soft; phase saved to `previousPhase` and restored), `SetHardPause`,
  `Card.pauseQueue` field pauses the queue while that card resolves; the deck viewer being open
  blocks everything. `CombatHandler.WaitForGameActions` has a ~3s watchdog (`waitTime`) that
  force-clears stuck pauses.
- `interrupt` (SpellEffects.Interrupt, `GameTriggers.Interrupt`): strips the target's remaining
  energy and ends channel effects; against enemies it replaces Deadly attacks (in-game glossary
  text, GameTextTranslator.cs keywordDescriptions; code path in MonsterHandler) — see Part V.

---

## Part II — Damage & defense pipeline

### Outgoing damage (DamageCalculations.CalculateDamage — exact order)

For `damage:X[:type[:target[:attacks]]]` the coroutine `SpellEffects.Damage` runs each attack
through `DamageCalculations.CalculateDamage(base, cc, ...)`:

1. **Base** = `NumberParser.TotalNumber(X)` (supports `a%b` = uniform random a..b inclusive,
   `+ - * /` integer arithmetic — NumberParser.cs).
2. `+ cc.damageBonus` (**upgrades: +1 damage each** — see Part IV).
3. `+` every conditional **`adddamage:` effect on the card itself and its adaptations**
   (conditions checked per hit).
4. `GameTriggers.CalculateOutgoingDamage` fires → statuses/enchantments/talents on that trigger
   mutate `SpellEffects._globalDamageModifier` (this is where Anger/Chain/Frozen/Weakness-class
   modifiers land); Divine-type cards additionally get `+Zeal stacks`
   (DamageCalculations.ZealDamage). Modifier is then folded in.
5. **`multiplydamage` / `dividedamage`** applied in a fixed order: active *enchantments* →
   *talents* → the card's own effects. `[[enchantmentStacks]]` is substituted per container.
   `a%b` forms roll a random multiplier. `dividedamage` = `ceil(damage / N)` and is **skipped
   entirely for piercing damage**.
6. **Crits**: `CriticalHit()` — a multiplier of **exactly 2** is the crit channel:
   sets `damageTypeOverwrite = "crit"`, refuses to stack (second ×2 becomes ×1), and is
   cancelled to ×1 if the attacker's side has **Weakness** (non-piercing). Because of this,
   `NumberParser` deliberately turns any division result equal to 2 into 2.01 — *you cannot
   express a non-crit ×2 with `multiplydamage:2`; that IS "critical hit"* (NumberParser.cs
   TotalNumberFloat; DamageCalculations.CriticalHit). Ferocity: if the final type is "crit",
   `+2 × Ferocity stacks` damage, heals the attacker `stacks`, consumes all Ferocity
   (DamageCalculations.Ferocity).
7. Clamp to ≥ 0 and to `SpellEffects._maxDamage` (999999; also used as a kill-switch — defensive
   statuses set `_maxDamage = 0` to void the hit).

### Incoming damage (DamageCalculations.DamagePreventionEffects)

- Non-piercing: `GameTriggers.CalculateIncomingDamage` + `ResolveDamageTaken` fire on the
  *defender's* status list; each defensive status adjusts `_globalDamageModifier`; result added.
- **Piercing skips all of it** (`piercing == true` → return damage unchanged) with ONE
  exception: **Brittle** still adds its stacks (see below). Piercing comes from the `Piercing`
  keyword, `piercingdamage:`, or `directdamage:`/status damage paths
  (SpellEffects.Damage; DamageCalculations.GetAllDamage: codeLine containing
  "piercing"/"direct"/"status" ⇒ piercing).
- After application: `DealtDamage` (any damage), `InflictDamage` (non-self), `CriticalHit` (type
  crit), `ResolveDamage` + `AfterTakenDamage` (if damage > 0), then `EndDamage` triggers fire, in
  that order (SpellEffects.Damage coroutine).
- On-hit keywords resolve inside the Damage coroutine when damage > 0: **Reaping** = target
  loses that much *max* health (`ChangeTempMaxHealth(-dmg)` + `Reaping` trigger);
  **Lifedrain** = attacker heals the damage dealt (SpellEffects.Damage keyword block).

### Defensive statuses — exact formulas (SpellEffects.RunStatusEffect switch + helpers)

All of these run when their `Status.effectTrigger` fires during damage calc; blocked amounts
are applied via `AdjustGlobalDamageModifier(-n)`. Trigger/expiry columns are decoded from the
extracted status assets (`tools/out/data/StatusEffect/<Name>.json`, fields
`effectTrigger`/`clearAt` as `GameTriggers` ints):

| Status | Effect (verified method) | Stack consumption | Trigger / natural expiry (asset) |
|---|---|---|---|
| `Armor` | Blocks `min(stacks, damage)` | loses the amount blocked (`Armor`) | CalculateIncomingDamage; **fades fully at StartPhase** (Armor.json; type `other`) |
| `Barrier` | Identical formula to Armor (`Barrier`) | loses amount blocked | CalculateIncomingDamage; **never fades** (`clearAt: None`) but is a `blessing` ⇒ dispellable (Barrier.json). This is the whole Armor-vs-Barrier difference. |
| `Darkness` | Blocks like Armor; **if it absorbs the whole hit, attacker gets Stunned 1** (player with Daywalker talent: Dazed = level instead) (`Darkness`) | loses amount blocked | CalculateIncomingDamage; never fades (Darkness.json; type `other`; also subtracts from max HP — Part VII) |
| `Resilience` | Blocks `min(stacks, damage)` | loses only **1** stack per hit (`Resilience`) | CalculateIncomingDamage; never fades (Resilience.json) |
| `Evasion` | Voids the entire hit (`_maxDamage = 0`, type "miss") — but **only vs card types in `TurnStat.evasionTypes`, default `{ Melee }`** (TurnStat.cs); `setevasion:` changes the set; `Swiftness` extends it to Magic + Ranged (Swiftness.json description) | 1 per dodge (`Evasion`) | CalculateIncomingDamage; **fades at StartPhase** (Evasion.json); Swiftness also fades at StartPhase |
| `Impervious` | Voids any non-piercing hit ≥ 1 (type "immune"); also zeroes direct/status damage (checked again inside `DirectDamage`/`StatusDamage`) | 1 per hit (`Impervious`) | IncomingDamage; fades at StartPhase (Impervious.json) |
| `Insight` | Voids the hit **only when incoming damage exactly equals its stacks** (`Insight(i)` called only `if (i == se.stacks)`) | not consumed in code | CalculateIncomingDamage; never fades (Insight.json) |
| `Ward` | Negates an entire incoming **affliction application** (stacks forced to 0) — anti-status, not anti-damage (SpellEffects.AddStatus) | 1 per block (`Ward`) | code hook (`effectTrigger: None`); fades at StartPhase (Ward.json) |
| `Weakness` | Attacker deals **half damage** (−`round(dmg/2)`), and cannot crit (see above); piercing ignores it (`Weakness`) | — | CalculateOutgoingDamage; **loses 1 stack each time the holder inflicts damage** (`clearAt: InflictDamage`, `lowerInsteadOfClear: 1` — Weakness.json) |
| `Frozen` | −stacks flat from the **holder's outgoing** damage (`Frozen`; trigger confirms direction); **playing any card removes 1 Frozen** (EndActionStep `removestatus:Frozen:1:self`); piercing ignores it | 1 per card played | CalculateOutgoingDamage; melts fully at EndTurnPhase (Frozen.json) |
| `Vulnerable` | Defender takes `+10% × stacks` (stacks capped at 10 ⇒ max +100%) (`Vulnerable`) | — | CalculateIncomingDamage; never fades (Vulnerable.json) |
| `Brittle` | Melee hits ≥1 damage on the holder get `+stacks` **after** all prevention (pending-resolve pattern `ResolvePendingBrittle`; works even vs piercing) | fully consumed per proc | CalculateIncomingDamage; never fades (Brittle.json) |
| `Reflect` | When triggered by a hit ≥1: deals back `damage + stacks` as status damage, then all Reflect is removed (`Reflect`) | all | IncomingDamage; fades at StartPhase (Reflect.json) |
| `Charmed` | Uncapped stacks; **if stacks ever exceed the holder's current HP the holder dies instantly** (`CheckAllStatusEffects`); reduced by damage taken (`Charmed` removes `i` stacks after a non-piercing hit) | = damage taken | AfterTakenDamage; never fades (Charmed.json) |

### Offensive / tempo statuses (same switch)

- `Anger`: +stacks damage on CalculateOutgoingDamage (Anger.json); **doubled if the played
  card has the `Rampage` keyword** (`Anger`); never fades. `Chain` status: +stacks damage on
  CalculateOutgoingDamage; fades at EndTurnPhase (Chain.json); built by playing Chain-keyword
  cards, wiped when a non-Chain card resolves (EndActionStep). `Zeal`: +stacks to Divine card
  damage (DamageCalculations.ZealDamage) **and at StartPhase the holder takes fire damage
  equal to Zeal** — a self-burn upkeep (`Zeal`; Zeal.json `effectTrigger: StartPhase`,
  description). `Ferocity`: crit payoff (Part II step 6), trigger CalculateOutgoingDamage
  (Ferocity.json). `Focus`: at EnergyPhase runs `draw:<GetFocusDraw()>` = **draw
  `floor(stacks/6)` extra cards** (GameTextTranslator.GetFocusDraw; Focus.json description
  agrees: +1 draw per 6 stacks, +1 Foretell reveal per 3 stacks); Focus stacks are also
  the alternate cost pool for **Psionic** cards (Draggable.cs: a Psionic card's full cost can
  be paid by removing that many Focus stacks instead of energy).
- `Haste`: the next card **drawn** costs 1 less (`effectTrigger: Draw` — Haste.json; removes
  one point from its cost array; consumed 1 stack; **Grounded cards unaffected**) (`Hasted`).
  `Slow`: the next card drawn costs +1 neutral (trigger Draw — Slow.json) (`Slowed`).
  `Insight` above. `Rally`: **when the opponent plays a card**, holder's side heals `stacks`,
  then −1 stack (`effectTrigger: EnemyPlaysAction` — Rally.json; `Rally`). `Regenerate`: at
  StartPhase heal stacks, −1 stack (Regenerate.json; `Regenerate`).
- `Momentum`: hardcoded in AddStatus — on reaching **> 4 stacks**: draw 1, reset to 0.
  `Sated`: at `CombatStat.allowedSatedStacks` (default 10): reset + self-Stun. `Fear`: at
  ≥ 100 stacks: reset, Stunned, and (player) discard phase skipped (SpellEffects.AddStatus).

### DoT / status damage (SpellEffects.StatusDamage)

- `statusdamage`/DoT ticks bypass the whole damage pipeline — flat HP loss, only stoppable by
  `Impervious` (zeroes it) or cleansing. Fires `InflictStatusDamage` + `TakeStatusDamage`
  triggers with `damageTypeOverwrite` temporarily set to the tick type
  (`poison`/`fire`/`bleeding`/`arcane`/`normal`).
- **`Potency`** status: +stacks to every *affliction-type* status tick the holder inflicts
  (StatusDamage). Enchantments with a `vulnerable`-codeLine referencing a specific status
  **double** that status's tick damage (StatusDamage) — the engine's "X-vulnerability" hook.
- Tick formulas (RunStatusEffect; timing from `tools/out/data/StatusEffect/*.json`):
  `Burning` = stacks fire damage **at EndTurnPhase; no natural decay** (Burning.json:
  `clearAt: None`) — burn persists until cleansed; `Bleeding` = takes stacks bleeding damage
  **whenever the holder is dealt damage** (`effectTrigger: DealtDamage`), then −1 stack
  (`clearAt: AfterTakenDamage`, lower — Bleeding.json); `Poison` = ticks **after the holder
  plays a card** (`effectTrigger: PlayAction` — Poison.json): `CombatStat.poisonDamage`
  (default 1, raise via `addpoisondamage`) damage, −1 stack — poison stacks are duration, not
  magnitude, and **poison eats incoming healing 1:1** (DoHealing removes poison stacks instead
  of healing); `Infected` = stacks poison-type damage **per card discarded**
  (`effectTrigger: Discard`) and is **removed entirely when the holder gains health**
  (`clearAt: GainHealth` — Infected.json); `Doom` = at EndTurnPhase **detonates for its full
  stacks** as arcane damage and then clears completely (`ClearOnTrigger: 1`); until then it
  decays 1 per Action played (`clearAt: PlayAction`, lower — Doom.json); `Stagger` = at
  StartPhase deals `ceil(stacks/2)` and removes that many (self-halving; Stagger.json);
  `Decay` = at EndTurnPhase, temp max-health loss (`ChangeTempMaxHealth(-stacks)`;
  Decay.json); `Shocked` = decays 1 at StartPhase, Stun when it reaches 0 (Shocked.json);
  `Dazed` = −1 per card queued (`effectTrigger: QueueAction`), Stun when it hits 1
  (Dazed.json); `Sickened` = removes 1 random energy at PlayPhase, fades 1/turn
  (Sickened.json); `Delirious` = after each card played, shuffles the costs of cards in hand;
  1 round per stack (`ShuffleCost`; Delirious.json); `Jinxed` = the next non-Reliable Action
  fizzles (damage zeroed; consumed 1; fades at EndTurnPhase — Jinxed.json); `Dominated` =
  holder can only play the randomly selected card (`Dominated`; trigger QueueAction, fades 1
  per EndAction — Dominated.json); `Dazzled` = next card draw is cancelled (1 stack per
  cancelled draw, `Dazzled()` in DeckHandler.DrawCards; trigger Draw — Dazzled.json).
- Pure hand-lock afflictions (no hardcoded case — enforced via play-gating; asset-verified,
  all "1 round per stack" via `clearAt: EndTurnPhase` + lower): **`Disarmed`** = Basic Attack
  cards can't be played; **`Silenced`** = Magic Actions can't be played; **`Pinned`** = only
  the leftmost and rightmost cards in hand are playable (anti-synergy with Flanking)
  (Disarmed.json / Silenced.json / Pinned.json descriptions). **`Stunned`** = no cards at all;
  fades at EndTurnPhase, `canNotBeIncreased` (Stunned.json; enforcement in
  Draggable.ReturnCard).
- `Deep Wound` (not a Status asset — its own container, SpellEffects.DeepWound): stacks of
  bleeding pressure; enemy is **instantly slain at `E_maxwounds` stacks** (per-battle data),
  player at **5** stacks; `Deepwound` trigger fires on application.
- Healing pipeline (SpellEffects.DoHealing): `addhealing`/`reducehealing`/`multiplyhealing`
  effects on the card, then GainHealth-triggered enchantment modifiers, then talents; `Bolster`
  status adds +stacks to each heal; Poison then eats it (above); `ConditionChecker.CanHeal()`
  can veto healing entirely.

### Damage types

- Damage "type" strings (`normal`, `fire`, `poison`, `bleeding`, `arcane`, `crit`, `miss`,
  `immune`, `charm`, `fatigue`, `Healing`...) are **cosmetic + trigger-metadata**: they pick
  animation/color (`PlayerHandler.ChangeLife(amount, type)` → DamageAnimation;
  GameTextTranslator damageLibrary) and are readable by conditions
  (`ConditionValue.DamageType` vs `SpellEffects.damageTypeOverwrite`). There is **no
  type-vs-resistance matrix in code** — elemental identity with rules-weight comes from card
  *Suffix* (Fire/Frost/Lightning/`Phyisical` (sic)) used by conditions (`ConditionValue.Suffix`)
  and from statuses (Burning/Frozen/Shocked) individually.
- `damageTypeOverwrite` (SpellEffects) is a transient global: "crit" (enables Ferocity, fires
  `CriticalHit`), "miss" (Evasion), "immune" (Impervious/Insight); `OverwriteDamageType` resets
  after each hit.
- Statuses with `theType == affliction` vs `blessing` vs `other` (LastingEffect.EffectType) is
  the rules-relevant taxonomy: affliction applications are Ward-blockable, Bane-amplified,
  cleanse/dispel target by this type, and `GainAffliction` fires.

---

## Part III — Status & keyword reference

### Status machinery (applies to ALL statuses)

- Application (SpellEffects.AddStatus): order of interception = enchantment/talent `immune`
  effects (referencing the status or blanket) → `changestacks:` effects (add/subtract to the
  incoming stack count; enchantment- and talent-hosted) → **`Bane`** (holder's Bane stacks are
  added to every incoming *affliction* except Bane/Jinxed/`canNotBeIncreased`) → **`Ward`**
  (voids affliction application, consumes 1) → stack merge. Stacks cap at
  `SpellEffects._maxstacks = 9999` (Charmed exempt). New statuses fire
  `GainUniqueStatus`/`InflictUniqueStatus`; every application fires
  `GainStatusStack`/`InflictStatus` (+ `GainAffliction`, `EnemyGainsStatus` as applicable).
- Decay/removal is **data-side per status**: `Status.clearAt` (a GameTrigger) removes it, or
  drops 1 stack if `lowerInsteadOfClear`; `ClearOnTrigger` removes after its own effect fires;
  `triggerOnClear` fires the effect once on removal; `canNotBeIncreased` freezes stacks
  (Status.cs fields; EventHandler.RemoveLastingEffects / ProcessLastingEffects). Talents/
  enchantments with a `noreset` codeLine + referenceStatus exempt that status from clearing
  (EventHandler.NoResetStatusList).
- `cleanse`/`dispel`/`convert*`/`swap*`/`double*`/`increase*` DSL commands manipulate statuses
  by `EffectType` (affliction/blessing) — see effect-commands.txt.

### Hardcoded status catalogue

Fully covered in Part II tables. Statuses seen in `RunStatusEffect` but **not** in Part II:
`Fear` (its per-trigger body is effectively empty — pressure comes from the ≥100-stack stun in
AddStatus; ticks at EndTurnPhase per Fear.json). Statuses referenced by name elsewhere in
code: `Swiftness` (Evasion enabler), `Potency`, `Bolster`, `Bane`, `Scar` (StartCombat),
`Stunned` (blocks playing cards entirely, Draggable.ReturnCard), `First Strike` (skips player
turn 1), `Ferocity`, `Momentum`, `Sated`, `Essential Oils` (potions go to graveyard instead
of vanishing, EndActionStep), `Time's Up` (cleared at victory), `Dominated`, `Charmed`,
`Deep Wound`.

**The shipped status roster is exactly 49 assets** (`tools/out/data/StatusEffect/`): Anger,
Armor, Bane, Barrier, Bleeding, Bolster, Brittle, Burning, Chain, Charmed, Darkness, Dazed,
Dazzled, Decay, Delirious, Disarmed, Dominated, Doom, Evasion, Fear, Ferocity, Focus, Frozen,
Haste, Impervious, Infected, Insight, Jinxed, Momentum, Pinned, Poison, Potency, Rally,
Reflect, Regenerate, Resilience, Sated, Scar, Shocked, Sickened, Silenced, Slow, Stagger,
Stunned, Swiftness, Vulnerable, Ward, Weakness, Zeal. Pure-hook statuses with
`effectTrigger: None` (all behavior lives in C# hooks or play-gating, not the trigger loop):
Bane, Bolster, Momentum, Potency, Sated, Scar, Swiftness, Ward, Disarmed, Pinned, Silenced.
`excludeFromRandom: 1` (never rolled by `randomstatus`/`randomaffliction`): Bane, Chain,
Dazed, Dazzled, Decay, Disarmed, Dominated, Impervious, Infected, Insight, Jinxed, Pinned,
Potency, Resilience, Sated, Scar, Shocked, Sickened, Silenced, Stunned, Swiftness, Zeal, Fear
(per-file `excludeFromRandom` fields). `canNotBeIncreased` (stack-frozen, also Bane-immune):
Dazed, Dazzled, Shocked, Stunned.

### Card keywords (`Card.CardProperties`) — verified rules-weight

Enum values are explicit and non-contiguous (Card.cs): `Chain=1, Cursed=2, Conjured=3,
Defiled=4, Echo=6, Figmented=9, Firecast=11, Lifedrain=13, Untempered=14, Grounded=16,
Heavy=21, Memorized=26, OneUse=31, Piercing=33, Psionic=34, Persistent=36, Reliable=37,
Recall=38, Reaping=39, Rampage=40, Unique=41, Scrap=42, Sinister=43, Unplayable=46,
Valuable=51`.

| Keyword | Verified mechanics |
|---|---|
| `Chain` | After the card resolves, gain 1 `chain` status stack (SpellManager.ExecuteActions; SpellEffects.EndActionStep); playing any non-Chain card removes all chain stacks (EndActionStep). Chain status = +stacks damage (RunStatusEffect). |
| `Conjured` | Temporary: never returned to the run deck after combat (DeckHandler.SetPlayerDeck filters it out); excluded from AcquireCard triggers and deck value; counts `conjuredCardsTurn/Combat`. |
| `Cursed` | Mutually exclusive with `Untempered` (CardContainer.AddKeyword swaps them); Cursed cards are excluded from most removal/transform selections (DeckViewer/DialogueActionHandler filters) — the "can't get rid of it" tag. A Cursed+Untempered/Conjured card *stays* in the deck (SetPlayerDeck) and Cursed Memorized cards escape perma-removal (SpellEffects ~7644). |
| `Defiled` | Playing it with an energy cost **defiles** energy slots equal to its non-life cost: `CombatStat.defile += cost`, shrinking max energy this combat; at full defile the player is force-`Ascend(defiled: true)`ed unless already Ascended (Draggable.cs; SpellEffects.Defile). |
| `Echo` | Card is cast twice when played from hand (EndActionStep → `CastEcho` replays it once; suppressed by cardKeyword `skipecho` or `Unique`). |
| `Figmented` | After playing, conjure a Conjured copy (without Figmented) into hand — the card "replicates" as a temp copy (EndActionStep). |
| `Firecast` | When Buried on your turn, the card is **played instead of exiled** (its PlayAction effects execute) and lands in graveyard/unique pile (SpellEffects.BuryCard coroutine; BuryLastPlayed). `firesideblessing` gives lastDrawn Firecast or casts it if it already has it. |
| `Grounded` | Immutable card: cannot gain keywords (CardContainer.AddKeyword/AddTempKeyword), cannot be upgraded (ConditionChecker.CanUpgrade), excluded from Recall/Figmented processing, from most select/copy/imbue/reward-mutation lists (TalentHandler, GenerateCardsRewards, DeckViewer filters), unaffected by Haste/Slow cost changes. |
| `Heavy` | Not reshuffled from graveyard when the deck resets (DeckHandler.ReshuffleGraveyard) — dead weight after first use. Classified negative (CardContainer.GetRandomKeyword). |
| `Lifedrain` | Heal = damage dealt, per hit (SpellEffects.Damage). |
| `Memorized` | Always starts on top of the combat deck (CombatHandler.StartCombat moves all Memorized to deck front). `altermemorized` swaps them. |
| `OneUse` | After play the card is not returned to deck or scrap — permanently gone (EndActionStep; DeckHandler.SetPlayerDeck). Exception: `Essential Oils` + potion → graveyard instead. Rewards: `GenerateCardsRewards` marks some categories OneUse (see RunCardAlterations). |
| `Piercing` | All the card's damage is piercing — ignores prevention/dividedamage (SpellEffects.Damage; Part II). |
| `Persistent` | Not discarded in DiscardPhase (Discarding); when Buried goes to graveyard instead of exile (BuryCard); protected from `BuryLastPlayed`. |
| `Psionic` | Payable with Focus stacks: if Focus ≥ total cost, cost is paid by removing Focus instead of energy (Draggable.cs OnEndDrag). |
| `Reliable` | Auto-passes the conditional keywords: Ambush, Bloodlust, Cascade, Continuity, Finale, Flanking, Flow, Frenzy checks return true (ConditionChecker.Cascade/Continuity/Flow/Finale/Bloodlust/Frenzy each early-return); immune to Jinxed (RunStatusEffect.Jinxed). |
| `Recall` | After play, shuffle a **free** Conjured copy (minus Recall) into the deck (EndActionStep). |
| `Reaping` | Damage dealt also reduces the target's **max** health (SpellEffects.Damage → `Reaping()` → ChangeTempMaxHealth; fires `Reaping` trigger). |
| `Rampage` | Doubles the `Anger` status contribution when this card attacks (RunStatusEffect.Anger). |
| `Scrap` | Card lives in the **scrap pile**, a side-deck: scrap cards return to the scrap pile after play instead of the graveyard (EndActionStep; DeckHandler.SetPlayerDeck/AddCardToScrap); accessed via `scavenge`, `delvefromscrap`, `toolbox`, `drawfromscrap` selection; Junk-suffix cards go to scrap without gaining the keyword (AddCardToScrap). |
| `Sinister` | Playing it increments the `CombatStat.sinister` counter (EndActionStep; also `sinister`/`addsinister` commands); the counter is readable via the `[[sinister]]` value token — a build-around accumulator. |
| `Unique` | One copy per run: reward pools and add-card effects exclude cards you already own with Unique (GenerateCardsRewards.GetLimitedList; Card.GetTransformedCard; DialogueActionHandler); in combat, castable once (`ConditionChecker.CanCastUnqiue`; played copies land in `combatData.uniquecards`); Unique blocks Echo/Recall/Figmented. All Enchantments are Unique (glossary). |
| `Unplayable` | Cannot be played from hand (Draggable.ReturnCard; MonsterHandler skips it too). |
| `Untempered` | "Not yet forged": mutually exclusive with Cursed; Untempered cards are **removed from the deck after combat** unless Cursed (SetPlayerDeck) — temporary acquisitions; `adduntempered`, `untemperedcopy`, `doublealluntemperedupgrades` interact; also excluded from AcquireCard trigger (DeckHandler.AddCardToDeck). |
| `Valuable` | Worth ×4 in `DeckValue()` score/economy calc (PlayerHandler.DeckValue; rarity base values: Common 5, Uncommon 10, Rare 25, Epic 35, Legendary 50, Monster 0). |
| (random keyword pools) | `CardContainer.GetRandomKeyword`: never grants Unique/Grounded/Conjured/Echo/OneUse/Scrap; negative set = {Untempered, Cursed, Unplayable, Defiled, Heavy}; damage-only set = {Reaping, Lifedrain, Rampage, Piercing}; stat-gated: Sinister needs DEX≥1, Firecast/Psionic INT≥1, Rampage STR≥1; fallback = Memorized. |

### Conditional keywords (conditions, not CardProperties — ConditionChecker.cs)

`Ambush` = first card of the round (`CombatStat.ambushOverwrites`, cleared on first
hand-play — SpellEffects.SetAmbush/EndActionStep). `Flanking` = played from leftmost or
rightmost hand position (Draggable.cs sets `CombatStat.flanking`). `Cascade` = previous card
this turn cost exactly ±1 of this one. `Continuity` = previous card this turn shares CardType.
`Flow:N` = ≥N cards already played this turn. `Finale` = ≤2 cards in hand (counting this one;
enemy: hand ≤1). `Frenzy` = took damage this turn (`damageTakenTurn > 0` — self-damage and
blood costs count). `Bloodlust` = dealt bleeding damage this turn or target is Bleeding.
`Unscathed` = no damage taken last turn (always false on turn 1). `Precision` = target HP
divisible by 5. `Balance` = current HP and deck count both even. `Scholar` = deck ≥ 20 cards.
`Empower` = total energy (incl. this card's cost) > 4. `Synergy` = shares category AND type
with the currently-resolving card. `Corrupted` = 5+ Corruption cards owned (glossary; ConditionValue.Corrupted).
All of the above are auto-true on `Reliable` cards.

---

## Part IV — Card-type & archetype systems

*(Section grounded by a dedicated pass over CardCategory usages; see also Part VI.)*

- `Card.CardType` = `{Melee, Magic, Ranged, Utility, Divine, Move, Corruption, Monster}`:
  rules-weight = Evasion only dodges types in `evasionTypes` (default Melee); Brittle is
  Melee-only; Zeal boosts Divine; Continuity/type-counting conditions and `[[type]]Played`
  tokens key off it; Corruption = penalty cards (Black color, `Corrupted` condition,
  `removecorruption`/`convertcorruptions`).
- `Card.GetColor()` (Card.cs, verified): explicit `colorCard` override wins; else from costs —
  DEX→Green, INT→Blue, STR→Red, HOLY→Gold, DEX+INT (or costDEXINT)→Aqua, DEX+STR→Orange,
  INT+STR→Purple; no colored cost → **Black** if `costLife>0` or Corruption type, else
  **Brown**. Color gates reward pools via stats (GenerateCardsRewards.GetAvailableColors).
- Rarity ladder: `HigherRarity`: Common→Uncommon→Rare→Legendary (Legendary caps; Monster→Common;
  Epic/Mythic/Ultra outside the ladder → Common) (Card.cs).

### Category rules-weight (all claims verified in the cited methods)

- **Play routing**: playing a card rewrites its trigger by category —
  `Performance → GameTriggers.Perform`, `Form → Shapechange`, `Hymn → Perform`
  (SpellEffects.AddCardToQueue). Everything else queues as `Conjure`/`PlayAction`
  (SpellManager.ExecuteActions).
- **The cast list** (CombatHandler.castList): `{Enchantment, Attunement, Form, Equipment,
  Summon, Performance}` — carried-over cards of these categories are auto-re-cast at the start
  of the next combat, then removed from `carryOverData.startingCards`
  (CombatHandler.SetStartingEffects). These are the "board state" categories.
- **BasicAttack**: two mechanisms. (a) `CombatStat.basicAttack` — a permanent, always-playable
  container rebuilt each combat from `PlayerHandler.basicAttack` (CombatHandler
  .ResetCombatMetrics; PlayerHandler.GetBasic/RestoreBasic; `restorebasic` command re-derives
  it, `setasbasic`/`settempbasic`/`setrandombasic` replace it). (b) BasicAttack-*category*
  cards in the deck: `costrestore` resets their costs across deck/hand/graveyard
  (SpellEffects.CostRestore). There is **no automatic per-turn basic restore** — it's all
  effect-driven.
- **Conjuration**: `CardHandler.CreateCardContainer` stamps `Conjured` on every
  Conjuration-category container at creation — conjurations are temporary by construction;
  after play they are filed nowhere (skipped by AddCardToGY/ReturnCardToScrap/Decharge) and
  dropped at combat end (DeckHandler.SetPlayerDeck).
- **Enchantment**: playing one adds a `LastingEffectContainer` wrapping
  `Card.CardEnchantments` to the target's `P_lastingEffects`/`E_lastingEffects`
  (SpellEffects.Enchantment). **No slot limit exists** — the only gate is `Unique` (a
  same-name active enchantment is refused, "ENCHANTMENT_ACTIVE"). Non-`combatEnchantment` ones
  expire at EndTurnPhase (Part I).
- **Performance/Hymn**: a single active `EffectType.performance` container. `perform:X`
  upticks its stacks (`UptickPerformance: les.stacks += X`, fires the `Perform` trigger) or,
  if none is active, delves a new performance from all performance-enchantment cards
  (SpellEffects.ExecutePerform; `performancetype:X` sets `CombatStat.performance`, default
  "song"; a "ritual" performance type is one-shot — reset to "song" after selection).
  **Completion: when stacks reach the performance card's `utilityNumber`, its
  `CardEnchantments` payload fires, the performance is removed, and `FinishPerformance`
  triggers** (UptickPerformance). Asset-verified thresholds: Songs cost 2–6
  (e.g. Song of Aldour 2, Song of Sages 5, Song of Slaughter 6), Rituals 2–10 (Ritual of
  Vodou 10) — `tools/out/data/Card/Song_of_*.json`, `Ritual_of_*.json` `utilityNumber`;
  payloads are the `cardTrigger: None` effects in their `CardEnchantments` (e.g. Song of
  Sages → `draw:3`). Uptick cards: 34 cards use `perform:` (e.g. Crescendo `perform:6`,
  Chorus `perform:1` — Crescendo.json/Chorus.json). **"finale"/"uptick" are NOT command
  strings** — Finale is a *condition* (Part III), uptick is internal. **`ReachCanto` is not a
  performance trigger** — it fires on entering a new area with `v = areaLevel`
  (AreaHandler.cs:135); "Canto" = chapter.
- **Form/stance**: only one stance active — `stance` replaces the existing
  `EffectType.stance` container in place (SpellEffects.Stance; re-entering the same stance is
  a no-op); fires `EnterStance`. Forms route through `Shapechange`; `shift`/`transmorph`/
  `moonclawtransform` are the shapeshift commands.
- **Attunement**: same single-active pattern as stance (`EffectType.attunement`,
  SpellEffects.Attune); fully blocked by a "Singular Focus" lasting effect. See tide, Part VI.
- **Mantra**: persists as `EffectType.mantra`; its number scales via
  `CombatStat.cardCountValues` (Dictionary<Card,int>) — `increaseeffectsoftype:mantra:X`
  raises every mantra's stored value (SpellEffects.IncreaseCardValuesOfType).
- **Equipment**: `equip` creates an `EffectType.equipment` container with
  `stacks = cc.charges` (durability); duplicate-name refused ("EQUIPMENT_ACTIVE")
  (SpellEffects.Equip). `repair`/`repairself` refill stacks up to `theCard.charges`.
  Charges on non-Equipment cards: each hand-play runs `Decharge` (−1 charge; fires
  `SpendCharge` for non-Equipment); at 0 charges the card is exiled ("Depleted")
  (SpellEffects.EndActionStep). `ActivateEquipment` trigger + enable/disable equipment
  commands manage them.
- **Adaptation**: `CardContainer.Adapt` sockets Adaptation-category cards into a host card up
  to `adaptationSlots` (**hard cap 5** — `AddAdaptationSlots` clamps 0..5); adaptations merge
  their keywords into the host (`GetKeywords`), can add attacks (`addattacks` scanned in the
  Damage coroutine) and their `Adapt`-trigger effects fire on socketing
  (CardContainer.AddAdaptiation (sic); `GameTriggers.Adapt`).
- **Revelation**: `addrevelation` adds random valid revelations
  (non-Monster-rarity, deduped vs deck); `delverevelation` = pick 1 of 3 to obtain
  (SpellEffects.GetValidRevelations/DelveRevelation).
- **Code**: permanent deck cards whose `GameTriggers.None` effects run at every combat start
  (CombatHandler.RunCodeEffects); `delveholycode` offers Code cards with `costHOLY > 0`
  (SpellEffects.DelveHolyCode) — the Holy build-around.
- **Offering**: reward-screen picks of category Offering grant a **Talent**
  (`AssetManager.GetTalent(rewardCard.permaValue)` — CombatHandler.EndCombat); Offering cards
  refuse all keyword changes (CardContainer.AddKeyword/AddTempKeyword);
  `addoffering`/`Talent.infernalOffering` feed the "offeringsaccepted" meta-stat and
  OfferingManager persistence (TalentHandler.AddInfernalOffering).
- **Ingredient/potion**: `createingredient` conjures an enemy-type-flavored Ingredient (e.g.
  undead → "Salty Femur", 10% "Unknown Meat") (SpellEffects.CreateIngredient); potions are
  `cardKeywords.Contains("potion")` cards; `setnextpotion` internally runs
  `conjure:potion:1:1`.
- **Item/Artifact/Path/Location**: mostly display/pool tags; Artifact has a random-artifact
  pool + `discarduntemperedartifact`; Path/Location are exploration-display categories.
  Monster-hint enum `MonsterHint = {Attack, Hex, Heal, Deadly, Other, Summon, Curse}` is the
  enemy-intent tag (Part V).

### Card values & upgrade system (CardContainer.cs; TalentHandler.UpgradeCard)

- `CardContainer` mutable state: `cost[9]`, `permaCost[9]`, `preSwapCost[9]`, `keywords` +
  `tempkeywords`, `charges`, **`damageBonus`** (= upgrade count), `tempValue`/`permaValue`
  (generic per-card counters: `addtempvalue`/`addpermavalue`/`setcardvalue`, clamp 0..9999,
  read back via `[[tempValue]]/[[permaValue]]`), `adaptationSlots`/`permaAdaptationSlots`,
  `imprints`, `adaptations`.
- **Upgrading changes ONLY `damageBonus`** — +1 per upgrade, clamp 0..9999; no cost/rarity
  change (TalentHandler.UpgradeCard). Applied as flat `+damageBonus` to non-self damage
  (DamageCalculations.CalculateDamage; not applied to self-damage). `damageBonus` persists
  across combats (ResetCardTempValues does not clear it); `tempValue` resets each combat;
  cards with `Card.resetTempValues` also zero tempValue whenever filed to graveyard/scrap
  (SpellEffects.AddCardToGY/ReturnCardToScrap).
- Upgrade gating: `ConditionChecker.CanUpgrade` — `overwriteUpgradable` forces yes;
  `cantbeupgraded`/`Grounded` force no; else must be a damage card. `removeupgrades`,
  `removehalfupgrades`, `stealupgrades`, `doublealluntemperedupgrades`, `setupgrades` mutate
  it.
- **Imbue** = permanent passive: `PlayerHandler.Imbue(c)` adds to `imbuedCards` (blocked by
  affix 821590), fires `ImbueEnchantment`; imbued cards' effects auto-run at every combat
  start without deck presence (CombatHandler.SetStartingEffects). Player default
  `maxImbues = 1`.
- `utilityNumber` (string, parsed by NumberParser) = free per-card design constant, read via
  `GetCardUtilityNumber`/`[[utilitynumber]]`; used e.g. as `Wane`'s permaValue cap and mantra
  tick values.
- `uniqueInHand` is **monster-only**: the enemy won't draw a second copy while one is in hand
  (MonsterHandler.CardDrawing/UniqueCardInHand).

---

## Part V — Combat vs overworld loop

### Enemy model (MonsterHandler.cs, Battle.cs)

- Enemies mirror the player: `E_deck`, `E_hand`, `E_discard`, `E_energy`, `E_cardsPerTurn`,
  `E_lastingEffects`. `GenerateMonsterDeck`: deck of `decksize = 20` built by cloning
  `Battle.deckList` (`20 / deckList.Count` copies each, min 1), each copy given
  `damageBonus = DifficultyManager.GetDamageBonus(...)`.
- **Intent = `Card.monsterHint`** (`MonsterHint = {Attack, Hex, Heal, Deadly, Other, Summon,
  Curse}`), shown per card in the enemy hand (HintCardDisplay; CombatHandler.StartPhase
  accessibility readout). `foretellenemy`/`interrupt` interact: Interrupt makes the enemy
  replace Deadly attacks (`DrawNonDeadly` filters `monsterHint != Deadly`).
- Enemy turn (MonsterHandler.StartMonsterTurn → PlayAbilities): gain energy
  (`DifficultyManager.GetEnergyPerTurn(E_energyPerTurn)`, added as a single pool; **enemy max
  energy = `Clamp(5 − defiledEnergy, 0, 5)`**, MonsterHandler.GetMaxEnergy) → loop the hand
  playing every card that passes cost + playConditions (`TryProcessCard`; safety cap
  `MAX_ITERATIONS = 20`) → discard → draw `E_cardsPerTurn` (hand cap
  `Battle.maximumHandSize = 5` default) → EndTurnPhase. Enemy pays life costs from own HP and
  can be Defiled/Dazzled/Jinxed like the player.
- `Battle` (ScriptableObject): `EnemyType` = `{none, undead, demon, humanoid, beast, spirit,
  elemental, construct, seraph, arachnid, court, companion, summon}`; `maxHealth`,
  `energyPerTurn`, `cardsPerTurn`, `maximumHandSize = 5`, `maxwounds = 5` (Deep Wound
  threshold), `startingStatus`, `startingEnchantments`, `enchantmentAura`, `affixes`,
  `forcedRewards`, and **`battleStages`** — multi-phase fights are *sequential* sub-Battles
  (`increasebattlestage` → `IncreaseMonsterStage` → `LoadMonster(stage)` fully replaces
  health+deck). **There is no simultaneous multi-enemy combat** — one MonsterHandler
  singleton.
- Health scaling: `GetMonsterMaxHealth` = base + `DifficultyManager.GetHealthBonus`; `invader`
  enemies scale +45% per area level above 1.
- **Effigy** (voodoo-doll archetype): a player-side lasting effect named "Effigy" —
  **created by the "Effigy" Corruption enchantment card** (`enchant:self`;
  `tools/out/data/Card/Effigy.json` — its enchantment also conjures a free "pin" card each
  StartPhase and converts damage dealt into temp max HP). `damageEffigy:X` /
  `damageEffigyPercentage:X` (X% of enemy max HP) reduce its stacks, and at 0 stacks the
  **foe** is slain (SpellEffects.DamageEffigy → `Slay("other")` on the player's turn);
  `EffigyActive` condition = any player lasting effect whose name contains "Effigy"
  (ConditionChecker.EffigyActive). Orbit: pin cards (`keyword=pin` pools, Pinspitter.json),
  Ritual of Strings/Vodou performances.
- `Slay` = set HP to exactly 0 (SpellEffects.Slay); "overkill" = negative HP past 0
  (`[[overkill]]`).

### Companions & summons

- The runtime companion is a **`Battle`** (`PlayerHandler.companion`), not the `Companion`
  ScriptableObject: exactly **one** Companion asset ships ("Wolf" —
  `tools/out/data/Companion/Wolf.json`: portrait, `health: 22`, a 1-card decklist), i.e. a
  legacy/summon-display shape, while all real companions are `Battle` assets with
  `EnemyType.companion`. Do not build companion logic on `Companion.cs`. Only one
  `EnemyType.companion` companion at a time (SpellEffects.SetCompanion refuses replacement);
  `summon`-type units also live in the `companion` slot but are **cleared after combat**
  (CombatHandler.ResetCombatMetrics).
- Companions have their own deck: `Battle.deckList` → `companionDeck` (persistent for
  companion-type; rebuilt fresh for summons) (PlayerHandler.GetCompanionDeck;
  DeckHandler.CreateCompanionDeck). Companion-suffix cards acquired as rewards route into
  `companionDeck` and auto-bind the matching companion Battle (DeckHandler.AddCardToDeck).
  Companion "level" = companion deck size (PlayerHandler.GetCompanionLevel).
- Companions do NOT take turns: `companionaction` plays **one random card** from the companion
  deck (`CompanionPlaysAction`, fires `GameTriggers.CompanionPlays`).
  `summoncompanion:<name>` = specific Battle; `summonfollower` = random Battle whose name
  contains "Follower"; `dismisscompanion` clears; **`subjugate` converts the current enemy
  into your companion and ends the fight** (SpellEffects.Subjugate). `advantage` grants the
  `TurnStat.advantage` flag (condition `Advantage`), cleared when a Companion-suffix card
  resolves (EndActionStep).

### Overworld (AreaHandler.cs, Area.cs)

- **The map is a shuffled event deck, not a spatial graph**: `GenerateAreaDeck(Area)` builds
  `areaDeck` from the Area's counts (`enemies, opportunities, campfires (difficulty-adjusted),
  npcs, shrines`), one random miniboss, level-gated shrine/opportunity Dialogues
  (`minimumLevel`/`maxLevel` vs `areaLevel`), using the seeded `RNGcategory.areadeck` stream.
  `DrawAreaEvents` always presents **up to 3 choices** (dead-ends pad an empty deck);
  picking one fires `GameTriggers.PickEvent` and dispatches on `EventTypes`.
- `AreaHandler.EventTypes` (exact): `npc, combat, opportunity, quest, boss, portal, deadend,
  campfire, exploration, miniboss, shrine, merchant, none`.
- Progression: non-passive events advance `questProgress`; when it reaches the area's count
  the **boss event is injected** (`AddAreaBoss`); after the boss a `portal` event leads to
  area selection (`InvasionHandler.GetNextAreas`). `areaLevel` 1–10; level 10 boss =
  "pinnacle" endgame fight (CombatHandler.StartCombat).
- **Campfires**: fire `VisitCampsite` trigger, add questflag "visitedcampfire", run the
  Campfire dialogue whose `campfire` action = **heal 20% max HP**
  (DialogueActionHandler `case "campfire"` → HealPercentage(20); fires
  `GainRestingHealing`). Campfire count per area = `campfires − difficulty + 1`
  (DifficultyManager.Campfires).
- **Merchants** (MerchantHandler.cs): buy prices = `ceil(base × merchantPrice)` with base
  Common 10 / Uncommon 20 / Rare 40 / Legendary 100 (fallback 50); stock rarity roll uses
  `luck` (Legendary if roll > 100 − luck×0.15, Rare > 100 − luck×1.25, Uncommon >
  100 − luck×2.5); **reroll costs 1 Fateshard** (meta currency). Talents "Emporium Discount"/
  "Stormscarred" zero the first purchase.
- **Shrines**: `EncounterShrine` trigger → Dialogue asset with `eventType == shrine`
  (transform/blessing events; `Card.GetTransformedCard` uses the seeded
  `shrines_opportunities` stream).
- **Quests/questflags**: `PlayerHandler.questflags` (List<string>, lowercased);
  `addquestflag`/`removequestflag` commands; `ConditionChecker.CheckQuestflag`
  (`ConditionValue.Questflag`) reads them — the cross-event memory designers use for
  multi-step stories.
- Souls: run currency 0..`maxsouls` (default 100); `changesouls` fires `Gainsouls`; **at
  death with ≥100 souls: −100 souls, revive at 50 HP** (PlayerHandler
  .PostHealtHChangeUpdate). Gold: 0..9999, `GainGold`/`LoseGold` triggers.

### Difficulty & meta

- **Difficulty is 5 discrete levels** (PlayerPrefs "difficulty" 0–4: Story, Normal,
  Challenging, Hard, Impossible — DifficultyManager). Exact modifiers: enemy card damage
  `+round((diff−1) × areaLevel × 0.35)` for diff ≥ 2 (cap 2 at level 1); enemy energy +1 at
  diff > 2; enemy health `+base × 0.1 × diff × (areaLevel × 0.75)` for diff > 1; campfires
  −1 per difficulty step; Story (0) = free full revive on death; fatigue only at diff > 0.
  **`ConditionValue.Ascended` is NOT meta-difficulty** — it checks the in-combat
  "Ascension"/"Demonform" transformation states (angel/demon; `ascend`/`removeascend`
  commands, `[[ascensionlevel]]`, glossary: up to 3 levels).
- **Metaprogression** (KeystoneManager.cs, Keystone.cs, RewardTrack.cs, MetaHandler.cs):
  Fateshards = meta currency (earned from bounties, run score, dailies, Sunforge; spent on
  keystones, merchant rerolls, class unlock ≥5, Sunforge). Keystones sit on per-class
  `RewardTrack`s; cost by rarity: Common 10, Uncommon 25, Rare 50, Legendary 125, Epic 50,
  Mythic 250, Ultra 2500, Monster 0 (KeystoneManager.GetKeyStoneCost); refunds return half.
  `KeystoneType` = `{StartingCard, Weapon, Card, Portrait, Banner, Talent, BonusHealth,
  BonusLuck, BonusUpgrade, BonusGold, BonusExperience, BonusReroll, BonusCampfire,
  BonusIncome, HealingPotion, RemoveBasic, MajorHealthBonus, BonusRoll, BonusScore}` —
  unlocked cards/weapons/talents feed future runs (GetActiveKeystones/GetClassWeapons;
  "metacards" via AssetManager.AddMetaCards).
- Combat entry: area events `combat/miniboss/boss/quest` fire `EnterCombat` and call
  `CombatHandler.StartCombat(AssetManager.GetBattle(eventContent), e)`; dialogues via
  `combat`/`directcombat` actions. Exit: victory → XP/gold (Part VII formulas) → level-up
  interception (`talentpoints > 0` → `Levelup`) → reward screen (`GenerateCardsRewards`) →
  `EndCombat` (Offering→talent, Companion-suffix→companion deck, else deck/scrap) →
  `TransitionToExplore`. Defeat: `Playerdeath` → `Defeat()` (run deleted, history saved);
  `endrun` dialogue action force-kills. `NoCombat` trigger fires on combat teardown;
  `TalentHandler.ReduceCooldown` ticks talent cooldowns per combat.

---

## Part VI — Synergy web (design payload)

Each archetype: enablers (exact triggers/conditions), payoffs (commands), orbit
(statuses/keywords). All names are exact API spellings.

- **Poison / affliction stacking.** Enablers: `GainAffliction`, `InflictStatus`,
  `InflictUniqueStatus`, `InflictStatusDamage`, `TakeStatusDamage` triggers;
  `StatusOnOpponent`/`StatusType`/`StatusCategory` conditions. Payoffs: `Poison:X`,
  `inflictaffliction`, `inflictafflictions`, `randomaffliction`, `doubleafflictions`,
  `increaseafflictions`, `copyaffliction`, `swapaffliction`, `convertaffliction`,
  `addpoisondamage` (raises the global poison tick), `caustic`. **Primary application idiom
  (asset-verified): `inflict:X` / `bless:X` with the status wired via the effect's
  `referenceStatus` PPtr — 441 shipped cards use `inflict*`; the status-name-as-command form
  (`Poison:X`) appears on only 1 card** (e.g. Poison Dart = `damage:1; inflict:5`→Poison —
  `tools/out/data/Card/Poison_Dart.json`). Orbit: `Potency` (+tick),
  `Bane` (+stacks on every affliction), `Ward`/`cleanse` as counterplay, `Infected`, `Sickened`,
  `Decay`, `hex` cards (`hexcard` cardKeyword pool, `triggerhex`, `removehex`). Note Poison's
  tick is flat (`poisonDamage`) — poison decks scale via *duration + Potency + tick frequency*,
  not stack size.
- **Burn / Firecast.** Enablers: Bury events (`GameTriggers.Bury`, `CardsBurned` /
  `[[cardsBuried]]`), `Firecast` keyword. Payoffs: `Burning:X` (stacks = damage per tick),
  `bury`, `burydrawn`, `burylowest`, `buryplayed`, `buryselect`, `firesideblessing`,
  `conjurelastburied`. Orbit: `Zeal` (fire ticks + Divine damage), `Brittle`. Design lever:
  Firecast turns your *deck* into a spellbook — bury effects become casts.
- **Bleed.** Enablers: `Bloodlust` condition, `wasDealtBleedingDamage` (same-turn bleed
  tracking), `Deepwound` trigger. Payoffs: `Bleeding:X` (stacks = tick), `deepwound:X`
  (threshold-execution: `E_maxwounds`/5), `caustic`. Orbit: Lifedrain, `Reaping`.
- **Chain / combo tempo.** Enablers: `Chain` keyword + chain status, `IsChain` condition,
  `CardsPlayedThisTurn`, `Flow:N`, `Cascade`, `Continuity`, `Synergy`,
  `AlreadyPlayedThisTurn`, `[[actionsPlayed]]`-family tokens. Payoffs: `addchain`, cheap
  cards, `Haste` (cost −1), `bullettime`, `extraturn`, `setswift`/`Swift` conditions,
  `startturn`. Breaking rule: any non-Chain card wipes chain — design "links" as low-cost.
- **Echo / copy.** Enablers: `Echo`, `Figmented`, `Recall` keywords; `SummonClone` trigger.
  Payoffs: `copy`, `clone`, `playcopy`, `playlastselected`, `conjurelastplayed`,
  `addcopywithupgrades`, `duplicatedeck`, `repeat`. Guard: `Unique` and cardKeyword
  `skipecho` suppress Echo; copies arrive `Conjured` (gone after combat).
- **Conjuration engines.** Enablers: `Conjure` trigger, `CurrentCardIsConjured` condition,
  `[[conjuredTurn]]/[[conjuredCombat]]` tokens, `conjuredCardsCombat`/`legendaryConjuredCombat`
  counters. Payoffs: the `conjure*` family (25+ commands: `conjure:pool:choices:amount`,
  `conjurefree`, `conjureupgraded`, `conjurerandom`, `conjureblood`...),
  `conjurationsinhandpersistent`. Conjured = temporary by construction (CardHandler adds
  `Conjured` to conjured containers).
- **Discard / graveyard.** Enablers: `Discard` trigger, `DiscardContains`/`CardsInGraveyard`
  conditions, `[[discardTurn]]/[[discardCombat]]/[[cardsInGraveyard]]`; graveyard cards with
  `EnterStance`/`AttuneTrigger`/`FinishPerformance` triggers fire **from the graveyard**
  (EventHandler.HandleDiscardTriggers — the only triggers that scan the graveyard!). Payoffs:
  `discard`, `forcediscard`, `discardfromdeck`, `playlastdiscarded`, `imprintdiscard`,
  `delvecategoryfromdiscard`, `removeupgradefromdiscard`, `reaping` (command), `decktogy`,
  `topgy`. "Ancestral" = 4+ cards in discard (glossary). Orbit: `Heavy` (graveyard-sticky),
  Persistent (survives bury as graveyard card).
- **Lifedrain / self-damage / blood.** Enablers: life costs fire `DealtDamage(targetSelf)` +
  `SpendEnergy(v=4)` and count as `damageTakenTurn` ⇒ `Frenzy` is the natural blood condition;
  `PlayerBelowZeroHealth`, `BelowZeroHealth` triggers. Payoffs: `bloodcard`/`bloodcost`/
  `convertcardtoblood`/`conjureblood` (convert costs to life), `swaplife`, `setlife`,
  `maxhealth`, `souls`. Orbit: `Lifedrain`, `Reaping`, souls revive (Part V), `Charmed`
  (HP-threshold kill), `Decay`.
- **Performance / finale (bard).** Enablers: `Perform`, `FinishPerformance` triggers
  (NOT `ReachCanto` — that is area entry, Part IV); `Performing`, `Finale` conditions;
  `[[performanceStacks]]`; completion at the performance card's `utilityNumber` (Part IV);
  `CombatStat.performance` (default "song"). Payoffs: `perform:X` (**adds X stacks to the
  single active performance, or delves a new one** — SpellEffects.ExecutePerform/
  UptickPerformance), `performancetype`; Hymns route through the `Perform` trigger too
  (AddCardToQueue maps Hymn/Performance→`Perform`). Performances are `LastingEffect.EffectType
  .performance` containers; selection UI pauses the queue (`pickingPerformance`).
- **Tide (Ebb/Flow).** Mechanics: tide flips every player turn (Ebb on turn 1)
  (CombatHandler.SetTurnTide); `SetTide` runs `tidecheck` on every Ebb/Flow-suffixed deck card
  **not in hand**, swapping it to its counterpart reference card (SpellEffects.TideCheck —
  cards literally transform with the tide). Enablers: `ChangeTide` trigger, `TideIsEbb`
  condition, `[[ebbcards]]/[[flowcards]]`. Payoffs: `changetide`, `delvetidal`, `attune`,
  `attunetrigger` (`AttuneTrigger` fires from graveyard too); `Attuned` condition = any
  attunement-type effect active.
- **Souls economy.** `Gainsouls` trigger, `[[souls]]` token, `souls`/`changesouls`/`maxsouls`
  commands; 100 souls = auto-revive at 50 HP (PlayerHandler.PostHealtHChangeUpdate). Design:
  souls are an over-cap HP battery for greed decks.
- **Companions & summons.** `CompanionPlays`, `AddCompanionCard`, `SummonClone` triggers;
  `HasCompanion`, `Advantage` conditions; `companionaction`, `summoncompanion`,
  `summonfollower`, `dismisscompanion`, `subjugate` commands; Companion-suffix cards live in a
  separate `companionDeck` (DeckHandler.AddCardToDeck routes them; a Companion card acquisition
  auto-binds the matching companion `Battle`). Advantage: `GrantAdvantage`/`RemoveAdvantage`
  — cleared whenever a Companion-suffix card resolves (EndActionStep).
- **Enchantment stacking.** Enchantments = `LastingEffectContainer`s whose effects run on
  their `cardTrigger` with `[[enchantmentStacks]]` substitution (EventHandler
  .ProcessLastingEffects); imbue family (`imbue`, `directimbue`, `imbueenchantment`,
  `imbueenchantmentwithfocus`, `ImbueEnchantment` trigger, `[[imbues]]`, `maxImbues = 1`
  default) permanently attaches an enchantment to the run (PlayerHandler.imbuedCards);
  `GainEnchantment` trigger; `EnchantmentStacks`/`EnchantmentOnSelf`/`EnchantmentOnOther`
  conditions; `destroyenchantment`, `duplicateenchantments`, `enchant`, `enchantref`,
  `gainenemyenchantment`, `spellsteal`.
- **Equipment / charges / toolbox.** `charges` field (Card.cs) — each play consumes 1 charge
  (`Decharge`; `SpendCharge` trigger for non-Equipment); at 0 charges the card is "Depleted"
  and exiled (EndActionStep buries it); `repair`/`repairself`/`decharge`/`restock`,
  `ActivateEquipment`/`ActivateWeapon` triggers, `[[charges]]/[[chargesBaseValue]]`. Scrap
  pile as ammo bag: `Scrap` keyword + `scavenge` (pre-combat scrap seeding in
  DeckHandler.CreateCombatDeck), `toolbox`/`toolboxswap`, `delvefromscrap`, `addcopytoscrap`,
  `ScavengeAdd`/`ScavengeDraw`/`Scavenge`/`AddCardToScrap` triggers.
- **Basic-attack decks.** `CombatStat.basicAttack` container (from `PlayerHandler.GetBasic`);
  `basicattack`/`addbasic`/`restorebasic`/`setasbasic`/`settempbasic`/`setrandombasic`
  commands; `[[basicCards]]/[[basicsinhand]]/[[basicsPlayed]]`; `ActivateWeapon` trigger;
  weapon cooldown (`[[weaponCooldown]]`, TalentHandler.ReduceCooldown at combat end).
- **Counting/condition surface for designers.** Value tokens (GameTextTranslator
  .NumericReplacement, usable in any DSL numeric slot AND in `Condition.targetValue`): ~230
  `[[tokens]]` including per-type played counts (`[[meleeActionsPlayed]]`,
  `[[spellsPlayedCombat]]`...), deck composition (`[[cursedCards]]`, `[[uniqueCards]]`,
  `[[differentCards]]`, `[[differentCostCards]]`, `[[corruptionsInHand]]`), economy
  (`[[gold]]`, `[[souls]]`, `[[deckvalue]]`, `[[wealth]]`), state (`[[fatigue]]`,
  `[[overkill]]`, `[[lastDamage]]`, `[[damageTaken]]`, `[[myMissingHealth]]`,
  `[[ascensionlevel]]`, `[[sinister]]`, `[[quickvalue]]`, `[[imprints]]`,
  `[[enchantmentStacks]]`), and **status-stack readers `[[my(status)<Name>]]` /
  `[[other(status)<Name>]]`** (GameTextTranslator.cs:192,2156; live example: Combust's
  `statusdamage:[[other(status)Burning]]:fire` — `tools/out/data/Card/Combust.json`). Conditions: 129 `ConditionValue`s (Part III lists the named ones);
  operators `IsLessThan, IsMoreThan, Is, IsNot, IsHigherPercent, IsLowerPercent, IsChance`.
  `CheckAllConditions` is **AND** (Condition.cs list; any failure aborts).

---

## Part VII — Design-relevant constants & guardrails

Verified constants (file: field/method):

- `SpellEffects._maxDamage = 999999` (mutable static; `= 0` is the "void this hit" latch;
  `setmaxdamage`/`setlastdamage` commands manipulate it).
- `SpellEffects._maxstacks = 9999` — status stack cap (Charmed exempt);
  `Mathf.Clamp(stacks, 0, _maxstacks)` on stack edits.
- Upgrades: `CardContainer.damageBonus` clamped 0–9999 (TalentHandler.UpgradeCard); **+1 damage
  per upgrade**, no other stat changes; talent ID 900000 randomizes upgrade amounts
  (−2..+3 with a luck reroll); affix 141965 ("brittle weapons") disables upgrading entirely;
  `Card.cantbeupgraded` / `Grounded` / `overwriteUpgradable` gate `ConditionChecker.CanUpgrade`.
- Hand: `maxHandSize = 10`, draw-per-turn `cardsPerTurn = 5` (PlayerHandler.cs);
  `ConditionChecker.cardsInHand = 5` (static mirror used by conditions).
- Energy: `maxEnergy = 8` (PlayerHandler.cs), defile-reduced, oldest-orb overflow.
- Gold hard cap **9999**, floor 0 (PlayerHandler.ChangeGold). `maxsouls = 100` default
  (uncapped growth via `maxsouls` command), `maxfaith = 10`, `luck = 5`,
  `fatigueResistance = 1`, `goldPercentage/experiencePercentage = 100`, `cardRewards = 3`,
  `maxImbues = 1`, `talentRolls = 2`, `talentShown = 3` (PlayerHandler.cs fields).
- Deck size cap: `DeckHandler.maxcards = 999` (also guards vs adding beyond 999).
- XP: `GainExperience` capped by `GlobalValues.MaxExperience`; level XP =
  `level² × 50 − 50` (GetLevelXP); level-up = +5 max HP, +5 current HP, +3 luck, pick a talent
  (Levelup). Combat XP = `creatureLevel × 10 × experiencePercentage%`; gold =
  `rand(level, 2×level) × goldPercentage%`; `creatureLevel = round(maxHealth/10)` **capped at
  10** (CombatHandler.CalculateXPReward/CalculateGoldReward/CalculateCreatureLevel).
- Max health: `GetPlayerMaxHealth() = (maxhealth − caustic − Darkness + bonusMaxHealth) ×
  (1 − 0.2×scars)` — **each Scar −20% max HP (cap 100%)**; Darkness stacks subtract directly
  (PlayerHandler.GetPlayerMaxHealth).
- Poison tick default `CombatStat.poisonDamage = 1`; `allowedSatedStacks = 10`; Momentum
  trips at >4; Fear at ≥100; Vulnerable stacks effective-capped at 10; player Deep Wound
  threshold 5 (SpellEffects.DeepWound).
- Rewards: `GenerateCardsRewards.MAX_CARD_REWARDS = 6` (amount clamped 0–6, default 3).
  Rarity roll (GetRandomRarity, 0–100): Legendary width `luck×0.1`%, Rare `luck×0.5 (+35 vs
  boss)`%, Uncommon `luck×2 (+35 vs boss)`%, remainder Common (split with Monster rarity if
  monster cards enabled). Class-color chance 45 (65 in Sunforge); Gold color 15% when
  `accessToHoly`. Unique-owned and imbued cards excluded from pools. Reward-mutating DSL:
  `forcerarity` (locks `RewardLimits.rarity`), `alterrewards:color|type|category|rarity|
  suffix|cost:X`, `setrewardstokeyword`, `upgraderewards` (combat-scoped),
  `playerupgraderewards` (run-scoped), `forcereward`/`setcardreward` (forced cards),
  `disablecardrewards`/`disablerewards`/`canskiprewards`.
- RNG (RNG/Randomness.cs): seeded per-category streams from run seed — `RNGcategory =
  { none, card_rewards, talents, shrines_opportunities, merchant, areadeck }`; seed = SHA256 of
  `section+seed` (Functions.ConvertSeedStringToInt); draw counts serialized for save-restore.
  **Combat shuffles and most in-combat rolls use the `none` (unseeded) stream**
  (DeckHandler.ShuffleDeck passes no category); card rewards/transforms/talent rolls are
  seeded. `ConditionChecker.GetPureRandom` is a separate System.Random for pure gameplay
  chance.
- `NumberParser` value grammar: int arithmetic `+ - * /`, `a%b` random inclusive range
  (projection shows `a`), **division that equals exactly 2 becomes 2.01** to dodge the
  crit-multiplier channel.

---

## Part VIII — Dead & negative findings

- **Misleading spellings that are load-bearing API** (do NOT correct): enum `CardRariry`;
  suffix `Phyisical`; `Condition.conditonOperator` field; `ConditionChecker.CanCastUnqiue`;
  `GameTriggers.Gainsouls`/`Playerdeath` casing; command spellings `dispell` (alias of
  `dispel` — both are cases), `replacecardinenemeyhand`.
- `EnergyHandler.SetEnergyToZero()` is an empty body — energy carries over by design; do not
  assume end-of-turn energy loss.
- `multiplydamage:2` is not "double damage" — it is the crit channel (Weakness cancels it, it
  refuses to stack, Ferocity keys off it). Use fractions ≠ 2 (engine itself nudges `/`-results
  off 2.0) for non-crit doubling.
- `RandomSeed.cs` is an empty class (dead file); seeding lives in `RNG/`.
- Damage type strings have **no resistance table** — "Fire/Frost/Lightning" identity is suffix
  + status driven, not a type chart. Don't design around elemental resistances.
- `Card.CardProperties` numbering has gaps (5, 7–8, 10, 12, 15, 17–20...) — likely removed
  keywords; only the 25 listed members exist at runtime.
- `HigherRarity` never produces Epic/Mythic/Ultra — those rarities also get **no band in the
  reward rarity roll** (GenerateCardsRewards.GetRandomRarity): they are reachable only by
  explicit placement (forced rewards, event grants). Monster rarity transforms *down* to
  Common.
- `SpellEffects.Fear()` per-trigger body is a no-op (reads health, does nothing) — Fear's real
  effect is the ≥100-stack stun in AddStatus. Its `RunStatusEffect` case is effectively dead
  code.
- The `Damage` coroutine contains a vestigial keyword check (`if (!keywords.Contains(Piercing))
  { _ = !keywords.Contains(Reliable); }` — result discarded; SpellEffects.cs ~8220): dev
  leftover, no effect.
- `DamageCalculations.rangeNumber` cycling (GetAllDamage) only affects UI forecasting of
  `a%b` ranges, not resolution.
- `ConditionChecker.CheckAllConditions` is strict AND; `Card.requireAllConditions` does NOT
  switch it to OR — it only changes display/eligibility handling on the card
  (GROUND-TRUTH.md; Card.cs field). UNVERIFIED beyond that.
- **"finale" and "uptick" are not DSL commands** — no `case "finale"`/`case "uptick"` exists
  in SpellEffects.cs. Finale is a `ConditionValue`; uptick is the internal
  `UptickPerformance` method reached via `perform:X`.
- `MonsterHandler.IncreaseMonsterStage`'s bounds guard reads
  `if (battleStages.Count < currentStage + 1)` — the apparent **inverse** of a normal bounds
  check (same in `SetMonsterStage`). Reported verbatim; intentional-vs-bug UNVERIFIED. Design
  battle stages exactly like shipped ones do.
- `Companion.cs` (the ScriptableObject with portrait/health/decklist) is **not** the runtime
  companion — `PlayerHandler.companion` is a `Battle`. Asset dump confirms: exactly one
  Companion asset exists (`tools/out/data/Companion/Wolf.json`) vs 170 Battles — legacy;
  don't build companion logic on it.
- `GameTriggers.ReachCanto` sounds like a performance/bard trigger; it is actually fired on
  **entering a new area** with `v = areaLevel` (AreaHandler.cs:135).
- "Enchantment slots" as a fixed count do not exist in code — enchantments are unbounded
  lasting effects gated only by `Unique`.
- `ConditionValue.Ascended` ≠ meta-difficulty ascension. There is **no ascension/heat ladder**;
  difficulty is the 0–4 PlayerPrefs setting. `Ascended` means the angel/demon transformation.
- Status-name-as-command lines (`Poison:X`, `Burning:X`, ...) are a nearly-dead idiom in
  shipped content: real cards apply statuses via `inflict:`/`bless:` + `referenceStatus`
  PPtr (441 cards vs 1 literal `Poison:` user — asset dump, Part VI). Prefer the
  referenceStatus idiom for new content.

### Resolved in the 2026-07-18 asset pass (formerly UNVERIFIED)

- Per-status `effectTrigger`/`clearAt` timing — **closed** for all 49 statuses via
  `tools/out/data/StatusEffect/*.json` (decoded against `GameTriggers` order): see Part II
  tables and Part III catalogue. Headline: Armor fades at StartPhase / Barrier never fades
  (but is a dispellable blessing); Weakness decays per damage inflicted; Frozen reduces
  *outgoing* damage; Poison ticks on the holder's card plays; Burning never decays.
- Performance canto/completion numbers — **closed**: threshold = per-card `utilityNumber`
  (Songs 2–6, Rituals 2–10), payload = `CardEnchantments` None-trigger effects (Part IV).
- Effigy container creation — **closed**: the "Effigy" Corruption enchantment card
  (`tools/out/data/Card/Effigy.json`) creates it (Part V).
- `Companion.cs` live role — **closed**: single legacy asset (Wolf.json); runtime companions
  are Battles.

### Still UNVERIFIED

- Exact consumers of the seeded `talents`/`merchant`/`areadeck` RNG streams (beyond the call
  sites already cited).
- Enemy AI card-selection weighting beyond what Part V documents (hand played in order,
  cost/condition-gated; no priority weighting found).
- Whether `MonsterHandler.IncreaseMonsterStage`'s inverted-looking bounds guard is
  intentional (see above).
