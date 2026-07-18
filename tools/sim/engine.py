"""Deterministic combat-loop simulator (subset).

Implements the VERIFIED timing rules from GAME-MECHANICS.md Parts I-III:
  - energy persists across turns; cap 8 with oldest-orb overflow (EnergyHandler)
  - phase order StartPhase -> Draw -> Energy -> Play -> Discard -> EndTurn -> enemy turn
  - Persistent survives the discard phase; Heavy stays in the graveyard on reshuffle
  - damage pipeline order: base -> adddamage -> outgoing statuses (Anger/Chain/Zeal/
    Frozen/Weakness) -> multiplydamage (x2 == crit; refuses stacking; cancelled by
    Weakness) -> incoming (Vulnerable -> Evasion -> Armor/Barrier/Resilience) —
    piercing skips all prevention
  - status timing per asset: see _STATUS notes inline (each has a test in
    tools/tests/test_sim_statuses.py)

Deliberate simplifications (documented in tools/sim/README.md):
  - energy orbs are untyped (color identity is a deck-building constraint, not an
    in-archetype power constraint); Life costs are paid as self-damage
  - combat fatigue is OFF (symmetric noise between control and test decks)
  - the enemy is a scripted profile (HP-class + hits/turn from Battle/monster-card
    stats), not a real deck; it never dies — we accumulate damage for 16 turns
"""

from __future__ import annotations

import math
import random
import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from sim.dsl import (AFFLICTION_STATUSES, BLESSING_STATUSES,   # noqa: E402
                     IMPLEMENTED_STATUSES, CardModel, CondModel, ValueContext,
                     eval_expression)

ENERGY_CAP = 8          # PlayerHandler.maxEnergy
HAND_CAP = 10           # PlayerHandler.maxHandSize
STACK_CAP = 9999        # SpellEffects._maxstacks
POISON_TICK = 1         # CombatStat.poisonDamage default
PLAYER_HP = 60
PLAYER_ENERGY_PER_TURN = 3
CARDS_PER_TURN = 5      # PlayerHandler.GetCardsPerTurn default
SIM_TURNS = 16          # scoring window = first 8; 9-16 = scaling probe
SCORE_TURNS = 8


@dataclass
class EnemyProfile:
    """Scripted opponent derived from tools/out/data/Battle + monster-rarity cards.

    Grounding (recomputed 2026-07-18 from the extracted pool):
      Battle maxHealth quartiles p25=42 / p50=65 / p75=122; cardsPerTurn median 2;
      monster-rarity `damage:N` statements: median 3, p75 6, p90 7.
    """
    name: str
    hp: int
    plays_per_turn: int   # cards the enemy plays (drives Poison/Frozen interaction)
    hit_damage: int       # damage per attack card (Melee-type, non-piercing)


PROFILES = [
    EnemyProfile("fast", hp=42, plays_per_turn=3, hit_damage=2),
    EnemyProfile("standard", hp=65, plays_per_turn=2, hit_damage=3),
    EnemyProfile("tank", hp=150, plays_per_turn=1, hit_damage=7),
]


def baseline_deck() -> list[CardModel]:
    """10 basics: 6x Strike (cost 1, damage:4 — the ~4 dmg/energy common baseline
    from CARD-PACK-SPEC §4) + 4x Defend (cost 1, bless:4 Armor)."""
    from sim.dsl import card_from_pack_json
    strike = {
        "name": "_SimStrike", "cardID": 0, "type": "Melee", "category": "Action",
        "rarity": "Common", "cost": {"Neutral": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "damage:4"}],
    }
    defend = {
        "name": "_SimDefend", "cardID": 0, "type": "Utility", "category": "Action",
        "rarity": "Common", "cost": {"Neutral": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "bless:4",
                     "referenceStatus": "Armor"}],
    }
    return [card_from_pack_json(strike) for _ in range(6)] + \
           [card_from_pack_json(defend) for _ in range(4)]


@dataclass
class Fighter:
    hp: int
    max_hp: int
    statuses: dict[str, int] = field(default_factory=dict)

    def add_status(self, name: str, stacks: int) -> None:
        if stacks <= 0:
            return
        self.statuses[name] = min(self.statuses.get(name, 0) + stacks, STACK_CAP)

    def drop(self, name: str, stacks: int | None = None) -> None:
        if name not in self.statuses:
            return
        if stacks is None or self.statuses[name] <= stacks:
            del self.statuses[name]
        else:
            self.statuses[name] -= stacks

    def s(self, name: str) -> int:
        return self.statuses.get(name, 0)


@dataclass
class Metrics:
    damage_to_enemy: list[float] = field(default_factory=list)  # per turn
    cards_gained: int = 0        # draws/tokens beyond the scheduled 5/turn
    player_hp_end: int = 0
    player_hp_at: list[int] = field(default_factory=list)

    def dmg_total(self, turns: int) -> float:
        return sum(self.damage_to_enemy[:turns])


class Combat:
    """One deterministic run: player deck vs scripted enemy profile."""

    def __init__(self, deck: list[CardModel], profile: EnemyProfile, seed: int,
                 focus: CardModel | None = None):
        self.rng = random.Random(seed)
        self.profile = profile
        self.player = Fighter(PLAYER_HP, PLAYER_HP)
        self.enemy = Fighter(10 ** 9, 10 ** 9)  # punching bag; profile.hp kept for reference
        self.energy = 0
        self.deck = list(deck)
        self.rng.shuffle(self.deck)
        self.hand: list[CardModel] = []
        self.graveyard: list[CardModel] = []
        self.exiled: list[CardModel] = []
        self.focus = focus                  # the card under evaluation (played first)
        self.metrics = Metrics()
        self.last_damage = 0
        self.turn_damage = 0.0
        self.charges_left: dict[int, int] = {}
        self.cards_played_this_turn = 0
        self.player_dmg_taken_turn = 0     # resets at player StartPhase (TurnStat)
        self.temp_values: dict[int, int] = {}   # per-card counters (addtempvalue)
        self.perma_values: dict[int, int] = {}  # per-card counters (addpermavalue)

    # ------------------------------------------------------------ zones

    def _draw(self, n: int, scheduled: bool = False) -> None:
        for _ in range(n):
            if len(self.hand) >= HAND_CAP:
                return
            if not self.deck:
                self._reshuffle()
            if not self.deck:
                return
            self.hand.append(self.deck.pop(0))
            if not scheduled:
                self.metrics.cards_gained += 1

    def _reshuffle(self) -> None:
        # Heavy cards stay in the graveyard (DeckHandler.ReshuffleGraveyard).
        stay = [c for c in self.graveyard if "Heavy" in c.keywords]
        back = [c for c in self.graveyard if "Heavy" not in c.keywords]
        self.graveyard = stay
        self.rng.shuffle(back)
        self.deck.extend(back)
        # Combat fatigue deliberately not modeled (see module docstring).

    # ------------------------------------------------------------ statuses

    def _ctx(self, mine: Fighter, other: Fighter,
             card: CardModel | None = None) -> ValueContext:
        return ValueContext(
            self.rng, self.last_damage, mine.statuses, other.statuses,
            cards_in_hand=len(self.hand),
            temp_value=self.temp_values.get(id(card), 0) if card else 0,
            perma_value=self.perma_values.get(id(card), 0) if card else 0)

    def _conds_pass(self, conds: list[CondModel], card: CardModel | None) -> bool:
        """AND over supported conditions (ConditionChecker.CheckAllConditions)."""
        for c in conds:
            ctx = self._ctx(self.player, self.enemy, card)
            if c.value == "AnyValue":
                a_s, b_s = c.target.split(":")
                a, b = eval_expression(a_s, ctx), eval_expression(b_s, ctx)
                ok = {"Is": a == b, "IsNot": a != b,
                      "IsMoreThan": a > b, "IsLessThan": a < b}[c.op]
            elif c.value == "Chance":
                ok = self.rng.randint(1, 100) <= eval_expression(c.target, ctx)
            elif c.value == "Ambush":
                first = self.cards_played_this_turn == 0
                ok = first if c.op == "Is" else not first
            elif c.value == "Frenzy":
                frenzied = self.player_dmg_taken_turn > 0
                ok = frenzied if c.op == "Is" else not frenzied
            elif c.value in ("StatusOnSelf", "StatusOnOpponent"):
                holder = self.player if c.value == "StatusOnSelf" else self.enemy
                t = c.target
                if t == "any":
                    hit = bool(holder.statuses)
                elif t == "blessing":
                    hit = any(n in BLESSING_STATUSES for n in holder.statuses)
                elif t == "affliction":
                    hit = any(n in AFFLICTION_STATUSES for n in holder.statuses)
                else:
                    hit = any(n.lower() == t.lower() for n in holder.statuses)
                ok = hit if c.op == "Is" else not hit
            else:  # unsupported conditions never reach here (statement unsimulable)
                ok = False
            if not ok:
                return False
        return True

    def _status_damage(self, target: Fighter, amount: int) -> None:
        """DoT/status damage: bypasses the pipeline entirely (SpellEffects.StatusDamage)."""
        if amount <= 0:
            return
        target.hp -= amount
        if target is self.player:
            self.player_dmg_taken_turn += amount
        if target is self.enemy:
            self.turn_damage += amount

    def _start_phase(self, who: Fighter) -> None:
        # Armor fades fully at StartPhase (Armor.json); Evasion too (Evasion.json).
        who.drop("Armor")
        who.drop("Evasion")
        # Regenerate: heal stacks, -1 (Regenerate.json / RunStatusEffect).
        if who.s("Regenerate"):
            self._heal(who, who.s("Regenerate"))
            who.drop("Regenerate", 1)
        # Stagger: take ceil(stacks/2), remove that many (Stagger.json).
        if who.s("Stagger"):
            tick = math.ceil(who.s("Stagger") / 2)
            self._status_damage(who, tick)
            who.drop("Stagger", tick)
        # Zeal upkeep: holder takes fire damage equal to stacks (Zeal.json StartPhase).
        if who.s("Zeal"):
            self._status_damage(who, who.s("Zeal"))

    def _end_turn_phase(self, who: Fighter) -> None:
        # Burning: stacks fire damage at EndTurnPhase, never decays (Burning.json).
        if who.s("Burning"):
            self._status_damage(who, who.s("Burning"))
        # Doom: detonates full stacks as arcane damage, then clears (Doom.json).
        if who.s("Doom"):
            self._status_damage(who, who.s("Doom"))
            who.drop("Doom")
        # Chain wiped at EndTurnPhase (Chain.json); Frozen melts fully (Frozen.json).
        who.drop("Chain")
        who.drop("Frozen")

    def _on_card_played(self, who: Fighter) -> None:
        # Poison ticks after the HOLDER plays a card: poisonDamage flat, -1 stack.
        if who.s("Poison"):
            self._status_damage(who, POISON_TICK)
            who.drop("Poison", 1)
        # Frozen: playing any card removes 1 (EndActionStep removestatus:Frozen:1:self).
        if who.s("Frozen"):
            who.drop("Frozen", 1)
        # Doom decays 1 per Action the holder plays (clearAt: PlayAction, lower).
        if who.s("Doom"):
            who.drop("Doom", 1)

    def _heal(self, who: Fighter, amount: int) -> None:
        if amount <= 0:
            return
        # Poison eats incoming healing 1:1 (SpellEffects.DoHealing).
        poison = who.s("Poison")
        if poison:
            eaten = min(poison, amount)
            who.drop("Poison", eaten)
            amount -= eaten
        healed = min(amount, who.max_hp - who.hp)
        who.hp += healed
        if who is self.enemy:
            self.turn_damage -= healed  # healing the enemy is negative progress

    # ------------------------------------------------------------ damage pipeline

    def _attack(self, attacker: Fighter, defender: Fighter, base: int, dtype: str,
                card: CardModel | None, piercing: bool, multiplier: float | None) -> int:
        """DamageCalculations.CalculateDamage order (Part II). Returns damage applied."""
        dmg = float(max(base, 0))
        # step 4: outgoing statuses
        anger = attacker.s("Anger")
        if anger and card is not None and "Rampage" in card.keywords:
            anger *= 2  # RunStatusEffect.Anger
        dmg += anger + attacker.s("Chain")
        if card is not None and card.ctype == "Divine":
            dmg += attacker.s("Zeal")  # DamageCalculations.ZealDamage
        if not piercing:
            dmg -= attacker.s("Frozen")
        if attacker.s("Weakness") and not piercing:
            dmg -= round(dmg / 2)  # RunStatusEffect.Weakness (also cancels crit)
        dmg = max(dmg, 0.0)
        # step 5/6: multiplydamage; x2 is the crit channel
        if multiplier is not None:
            if abs(multiplier - 2.0) < 1e-9 and attacker.s("Weakness"):
                pass  # crit cancelled to x1 by Weakness (CriticalHit)
            else:
                dmg *= multiplier
        dmg = math.floor(dmg)
        # incoming prevention (piercing skips all of it)
        if not piercing and dmg > 0:
            vuln = min(defender.s("Vulnerable"), 10)
            if vuln:
                dmg += round(dmg * 0.10 * vuln)  # RunStatusEffect.Vulnerable
            if defender.s("Evasion") and card is not None and card.ctype == "Melee":
                defender.drop("Evasion", 1)     # dodge, hit voided
                return 0
            for blocker in ("Armor", "Barrier"):
                if dmg <= 0:
                    break
                blocked = min(defender.s(blocker), dmg)
                if blocked:
                    defender.drop(blocker, blocked)  # loses the amount blocked
                    dmg -= blocked
            if dmg > 0 and defender.s("Resilience"):
                blocked = min(defender.s("Resilience"), dmg)
                dmg -= blocked
                defender.drop("Resilience", 1)  # loses only 1 stack per hit
        dmg = int(max(dmg, 0))
        if dmg > 0:
            defender.hp -= dmg
            if defender is self.player:
                self.player_dmg_taken_turn += dmg
            if defender is self.enemy:
                self.turn_damage += dmg
            # on-damage triggers
            if attacker.s("Weakness"):
                attacker.drop("Weakness", 1)  # -1 per damage inflicted (Weakness.json)
            if defender.s("Bleeding"):
                self._status_damage(defender, defender.s("Bleeding"))
                defender.drop("Bleeding", 1)  # DealtDamage tick, then -1 (Bleeding.json)
            if card is not None and "Lifedrain" in card.keywords:
                self._heal(attacker, dmg)
            if card is not None and "Reaping" in card.keywords:
                defender.max_hp -= dmg
                if defender is self.enemy:
                    self.turn_damage += dmg  # max-HP loss counts as damage-equivalent
        self.last_damage = dmg
        return dmg

    # ------------------------------------------------------------ card execution

    def _card_modifiers(self, card: CardModel) -> tuple[int, float | None]:
        """(flat adddamage, multiplydamage) from the card's own None/PlayAction hooks."""
        add = 0
        mult = None
        for eff in card.effects:
            if eff.conditions and not self._conds_pass(eff.conditions, card):
                continue
            for st in eff.statements:
                if not st.simulable:
                    continue
                ctx = self._ctx(self.player, self.enemy, card)
                if st.command == "adddamage":
                    add += eval_expression(st.args[0], ctx)
                elif st.command == "multiplydamage":
                    try:
                        mult = float(st.args[0])
                    except ValueError:
                        mult = float(eval_expression(st.args[0], ctx))
        return add, mult

    def play_card(self, card: CardModel) -> None:
        self.energy -= card.cost
        if card.life_cost:
            self.player.hp -= card.life_cost  # life costs ARE self-damage (Part I)
            self.player_dmg_taken_turn += card.life_cost
        casts = 2 if "Echo" in card.keywords else 1
        for _ in range(casts):
            self._execute(card)
        self._on_card_played(self.player)
        # Chain keyword: +1 chain stack after resolving; non-Chain wipes chain.
        if "Chain" in card.keywords:
            self.player.add_status("Chain", 1)
        else:
            self.player.drop("Chain")
        self.cards_played_this_turn += 1

    def _execute(self, card: CardModel) -> None:
        add_dmg, mult = self._card_modifiers(card)
        for eff in card.effects:
            if eff.trigger != "PlayAction":
                continue
            if eff.conditions and not self._conds_pass(eff.conditions, card):
                continue
            for st in eff.statements:
                if st.simulable:
                    self._run_statement(card, eff, st, add_dmg, mult)

    def _run_statement(self, card: CardModel, eff, st, add_dmg: int,
                       mult: float | None) -> None:
        ctx = self._ctx(self.player, self.enemy, card)
        cmd, args = st.command, st.args

        if cmd in ("damage", "piercingdamage", "directdamage", "statusdamage"):
            amount = eval_expression(args[0], ctx)
            target = args[2] if len(args) >= 3 else "other"
            attacks = eval_expression(args[3], ctx) if len(args) >= 4 else 1
            defender = self.enemy if target == "other" else self.player
            if cmd == "statusdamage":
                for _ in range(max(attacks, 1)):
                    self._status_damage(defender, amount)
            else:
                piercing = cmd in ("piercingdamage", "directdamage") or \
                    "Piercing" in card.keywords
                for _ in range(max(attacks, 1)):
                    self._attack(self.player, defender, amount + add_dmg, args[1] if len(args) > 1 else "normal",
                                 card, piercing, mult)
        elif cmd == "draw":
            self._draw(eval_expression(args[0], ctx))
        elif cmd == "heal":
            target = self.player if (len(args) < 2 or args[1] == "self") else self.enemy
            self._heal(target, eval_expression(args[0], ctx))
        elif cmd in ("inflict", "bless"):
            stacks = eval_expression(args[0], ctx)
            times = eval_expression(args[1], ctx) if len(args) >= 2 else 1
            target = self.enemy if cmd == "inflict" else self.player
            for _ in range(max(times, 1)):
                target.add_status(eff.reference_status, stacks)
        elif cmd in ("energy", "energyDEX", "energyINT", "energySTR", "energyHOLY"):
            n = eval_expression(args[1] if cmd == "energy" else args[0], ctx)
            # oldest-orb overflow at cap == count clamps at cap for untyped orbs
            self.energy = min(self.energy + n, ENERGY_CAP)
        elif cmd == "bury":
            n = eval_expression(args[0], ctx)
            for _ in range(n):
                if self.deck:
                    buried = self.deck.pop(0)
                    # Persistent goes to graveyard instead of exile (BuryCard).
                    (self.graveyard if "Persistent" in buried.keywords
                     else self.exiled).append(buried)
        elif cmd == "conjure":
            n = 1
            if len(args) >= 3:
                try:
                    n = int(args[2])
                except ValueError:
                    n = 1
            for _ in range(n):
                if len(self.hand) < HAND_CAP:
                    self.metrics.cards_gained += 1
                    # inert token: occupies no simulated behavior
        elif cmd == "addstack":
            pass  # tracked-counter semantics; payloads never interpreted
        elif cmd in ("adddamage", "multiplydamage"):
            pass  # consumed by _card_modifiers
        elif cmd in ("caustic", "reaping"):
            n = eval_expression(args[0], ctx)
            if n > 0:
                self.enemy.max_hp -= n
                self.turn_damage += n  # max-HP loss counts as damage-equivalent
        elif cmd == "removestatus":
            target = self.player if (len(args) >= 3 and args[2] == "self") else self.enemy
            amount = None
            if len(args) >= 2 and args[1] != "all":
                amount = abs(eval_expression(args[1], ctx))
            target.drop(args[0], amount)
        elif cmd == "discard":
            n = eval_expression(args[0], ctx)
            for _ in range(max(n, 0)):
                if not self.hand:
                    break
                pick = self.rng.randrange(len(self.hand))
                self.graveyard.append(self.hand.pop(pick))
        elif cmd in ("addtempvalue", "addpermavalue"):
            store = self.temp_values if cmd == "addtempvalue" else self.perma_values
            n = eval_expression(args[0], ctx)
            store[id(card)] = max(0, min(store.get(id(card), 0) + n, 9999))
        else:
            # bare status-name command (Poison:X etc. — nearly-dead shipped idiom)
            if cmd in IMPLEMENTED_STATUSES:
                stacks = eval_expression(args[0], ctx)
                target = self.enemy if cmd in AFFLICTION_STATUSES else self.player
                target.add_status(cmd, stacks)

    # ------------------------------------------------------------ turn loop

    def _playable(self, card: CardModel) -> bool:
        if "Unplayable" in card.keywords:
            return False
        if card.charges > 0 and self.charges_left.get(id(card), card.charges) <= 0:
            return False
        if card.play_conditions and not self._conds_pass(card.play_conditions, card):
            return False
        return card.cost <= self.energy

    def _player_turn(self) -> None:
        self.turn_damage = 0.0
        self.cards_played_this_turn = 0
        self.player_dmg_taken_turn = 0  # ResetAllTurnStats at StartPhase
        self._start_phase(self.player)
        self._draw(CARDS_PER_TURN, scheduled=True)
        self.energy = min(self.energy + PLAYER_ENERGY_PER_TURN, ENERGY_CAP)
        # policy: focus card first (every copy), then strikes, then the rest
        progress = True
        while progress:
            progress = False
            ordered = sorted(
                self.hand,
                key=lambda c: (0 if (self.focus is not None and c.name == self.focus.name)
                               else (1 if c.name == "_SimStrike" else 2)))
            for card in ordered:
                if self._playable(card):
                    self.hand.remove(card)
                    self.play_card(card)
                    if card.charges > 0:
                        left = self.charges_left.get(id(card), card.charges) - 1
                        self.charges_left[id(card)] = left
                        if left <= 0:
                            self.exiled.append(card)  # "Depleted" (EndActionStep)
                        else:
                            self.graveyard.append(card)
                    elif "OneUse" in card.keywords:
                        self.exiled.append(card)  # permanently gone
                    else:
                        self.graveyard.append(card)
                    progress = True
                    break
        # DiscardPhase: whole hand except Persistent (SpellEffects.Discarding)
        keep = [c for c in self.hand if "Persistent" in c.keywords]
        for c in self.hand:
            if "Persistent" not in c.keywords:
                self.graveyard.append(c)
        self.hand = keep
        self._end_turn_phase(self.player)

    def _enemy_turn(self) -> None:
        self._start_phase(self.enemy)
        for _ in range(self.profile.plays_per_turn):
            enemy_card = CardModel(name="_EnemyHit", card_id=0, rarity="Monster",
                                   category="Action", ctype="Melee", suffix="None",
                                   cost=0, life_cost=0)
            self._attack(self.enemy, self.player, self.profile.hit_damage,
                         "normal", enemy_card, piercing=False, multiplier=None)
            self._on_card_played(self.enemy)
        self._end_turn_phase(self.enemy)

    def run(self, turns: int = SIM_TURNS) -> Metrics:
        for _ in range(turns):
            self._player_turn()
            if self.player.hp > 0:
                self._enemy_turn()
            # record AFTER the enemy turn so end-of-round DoT ticks (Burning, Doom)
            # and enemy-turn Poison ticks count toward this round's damage
            self.metrics.damage_to_enemy.append(self.turn_damage)
            self.player.hp = max(self.player.hp, 0)
            self.metrics.player_hp_at.append(self.player.hp)
        self.metrics.player_hp_end = self.player.hp
        return self.metrics


# ------------------------------------------------------------------ evaluation

SEEDS = [101, 202, 303, 404, 505]

# Power-score weights (HP-equivalent units). Card advantage is credited at 1 HP
# per net card gained — a deliberately conservative modeling constant, validated
# by the calibration self-check (see README).
CARD_ADVANTAGE_WEIGHT = 1.0


@dataclass
class CardScore:
    power: float                       # mean over profiles x seeds, 8-turn window
    dmg_delta: float
    sustain_delta: float
    cards_delta: float
    scaling_ratio: float               # late-window vs early-window marginal damage
    per_turn_dmg_delta: list[float]


def _control_metrics(profile: EnemyProfile, seed: int) -> Metrics:
    return Combat(baseline_deck(), profile, seed).run()


_CONTROL_CACHE: dict[tuple[str, int], Metrics] = {}


def control_metrics(profile: EnemyProfile, seed: int) -> Metrics:
    key = (profile.name, seed)
    if key not in _CONTROL_CACHE:
        _CONTROL_CACHE[key] = _control_metrics(profile, seed)
    return _CONTROL_CACHE[key]


def score_card(card: CardModel) -> CardScore:
    powers, dmgs, sustains, cardsds = [], [], [], []
    per_turn = [0.0] * SIM_TURNS
    n = 0
    for profile in PROFILES:
        for seed in SEEDS:
            ctrl = control_metrics(profile, seed)
            test = Combat(baseline_deck() + [card], profile, seed, focus=card).run()
            dmg_d = test.dmg_total(SCORE_TURNS) - ctrl.dmg_total(SCORE_TURNS)
            sus_d = test.player_hp_at[SCORE_TURNS - 1] - ctrl.player_hp_at[SCORE_TURNS - 1]
            car_d = test.cards_gained - ctrl.cards_gained
            powers.append(dmg_d + sus_d + CARD_ADVANTAGE_WEIGHT * car_d)
            dmgs.append(dmg_d)
            sustains.append(sus_d)
            cardsds.append(car_d)
            for t in range(SIM_TURNS):
                per_turn[t] += test.damage_to_enemy[t] - ctrl.damage_to_enemy[t]
            n += 1
    per_turn = [x / n for x in per_turn]
    early = sum(per_turn[1:SCORE_TURNS]) / (SCORE_TURNS - 1)
    late = sum(per_turn[SCORE_TURNS:]) / (SIM_TURNS - SCORE_TURNS)
    scaling = late / early if early > 1e-6 else (math.inf if late > 1.0 else 1.0)
    return CardScore(
        power=sum(powers) / n,
        dmg_delta=sum(dmgs) / n,
        sustain_delta=sum(sustains) / n,
        cards_delta=sum(cardsds) / n,
        scaling_ratio=scaling,
        per_turn_dmg_delta=per_turn,
    )
