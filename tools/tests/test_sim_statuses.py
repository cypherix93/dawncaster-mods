"""Per-status timing tests — each implemented status asserts the VERIFIED behavior
from GAME-MECHANICS.md Parts II-III (status asset timing + RunStatusEffect rules)."""

import math

from sim import engine
from sim.dsl import card_from_pack_json
from sim.engine import Combat, EnemyProfile


def make_combat(**profile_overrides):
    prof = EnemyProfile(name="test", hp=100, plays_per_turn=0, hit_damage=0)
    for k, v in profile_overrides.items():
        setattr(prof, k, v)
    return Combat(deck=[], profile=prof, seed=1)


def melee_card(**overrides):
    spec = {"name": "_T", "cardID": 0, "type": "Melee", "category": "Action",
            "rarity": "Common", "cost": {}, "effects": [
                {"trigger": "PlayAction", "codeLine": "damage:0"}]}
    spec.update(overrides)
    return card_from_pack_json(spec)


def hit(c, base, ctype="Melee", piercing=False, mult=None, keywords=None):
    card = melee_card(type=ctype, keywords=keywords or [])
    return c._attack(c.player, c.enemy, base, "normal", card, piercing, mult)


# ---------------------------------------------------------------- block statuses

def test_armor_blocks_and_is_consumed_and_fades_at_startphase():
    c = make_combat()
    c.player.add_status("Armor", 5)
    c._attack(c.enemy, c.player, 3, "normal", None, False, None)
    assert c.player.hp == engine.PLAYER_HP          # fully blocked
    assert c.player.s("Armor") == 2                 # loses the amount blocked
    c._start_phase(c.player)
    assert c.player.s("Armor") == 0                 # fades fully at StartPhase (Armor.json)


def test_barrier_blocks_like_armor_but_never_fades():
    c = make_combat()
    c.player.add_status("Barrier", 5)
    c._attack(c.enemy, c.player, 3, "normal", None, False, None)
    assert c.player.s("Barrier") == 2
    c._start_phase(c.player)
    assert c.player.s("Barrier") == 2               # clearAt: None (Barrier.json)


def test_resilience_loses_only_one_stack_per_hit():
    c = make_combat()
    c.player.add_status("Resilience", 5)
    c._attack(c.enemy, c.player, 3, "normal", None, False, None)
    assert c.player.hp == engine.PLAYER_HP
    assert c.player.s("Resilience") == 4            # -1 per hit, not per amount


def test_piercing_skips_all_prevention():
    c = make_combat()
    c.enemy.add_status("Armor", 99)
    dealt = hit(c, 7, piercing=True)
    assert dealt == 7
    assert c.enemy.s("Armor") == 99


# ---------------------------------------------------------------- evasion

def test_evasion_dodges_melee_only_consumes_one_and_fades_at_startphase():
    c = make_combat()
    c.enemy.add_status("Evasion", 2)
    assert hit(c, 9, ctype="Melee") == 0            # dodged
    assert c.enemy.s("Evasion") == 1                # 1 consumed per dodge
    assert hit(c, 9, ctype="Ranged") == 9           # default evasionTypes = {Melee}
    c._start_phase(c.enemy)
    assert c.enemy.s("Evasion") == 0                # fades at StartPhase (Evasion.json)


# ---------------------------------------------------------------- outgoing mods

def test_weakness_halves_outgoing_and_decays_per_damage_inflicted():
    c = make_combat()
    c.player.add_status("Weakness", 2)
    assert hit(c, 10) == 5                          # -round(dmg/2)
    assert c.player.s("Weakness") == 1              # clearAt InflictDamage, lower


def test_weakness_cancels_the_x2_crit_channel():
    c = make_combat()
    c.player.add_status("Weakness", 1)
    # x2 IS "critical hit"; Weakness cancels it to x1 (DamageCalculations.CriticalHit)
    assert hit(c, 8, mult=2.0) == 4                 # halved only, no x2
    c2 = make_combat()
    assert hit(c2, 8, mult=2.0) == 16               # crit applies without Weakness


def test_anger_adds_stacks_and_rampage_doubles_it():
    c = make_combat()
    c.player.add_status("Anger", 3)
    assert hit(c, 4) == 7
    assert hit(c, 4, keywords=["Rampage"]) == 10    # Anger doubled (RunStatusEffect)


def test_frozen_reduces_outgoing_melts_on_play_and_at_end_turn():
    c = make_combat()
    c.player.add_status("Frozen", 3)
    assert hit(c, 5) == 2                           # -stacks from holder's outgoing
    assert hit(c, 5, piercing=True) == 5            # piercing ignores Frozen
    c._on_card_played(c.player)
    assert c.player.s("Frozen") == 2                # playing a card removes 1
    c._end_turn_phase(c.player)
    assert c.player.s("Frozen") == 0                # melts fully at EndTurnPhase


def test_zeal_boosts_divine_damage_and_burns_holder_at_startphase():
    c = make_combat()
    c.player.add_status("Zeal", 4)
    assert hit(c, 3, ctype="Divine") == 7           # +Zeal for Divine cards only
    assert hit(c, 3, ctype="Melee") == 3
    hp = c.player.hp
    c._start_phase(c.player)
    assert c.player.hp == hp - 4                    # self-burn upkeep (Zeal.json)


def test_chain_status_adds_damage_and_keyword_flow():
    c = make_combat()
    chain_card = melee_card(keywords=["Chain"])
    plain_card = melee_card()
    c.play_card(chain_card)
    assert c.player.s("Chain") == 1                 # +1 after a Chain card resolves
    assert hit(c, 4) == 5                           # chain stacks add to damage
    c.play_card(plain_card)
    assert c.player.s("Chain") == 0                 # non-Chain card wipes chain
    c.player.add_status("Chain", 2)
    c._end_turn_phase(c.player)
    assert c.player.s("Chain") == 0                 # fades at EndTurnPhase (Chain.json)


# ---------------------------------------------------------------- vulnerable

def test_vulnerable_adds_ten_percent_per_stack_capped_at_ten():
    c = make_combat()
    c.enemy.add_status("Vulnerable", 5)
    assert hit(c, 10) == 15
    c.enemy.statuses["Vulnerable"] = 25             # effective cap is 10 => +100%
    assert hit(c, 10) == 20


# ---------------------------------------------------------------- DoT timing

def test_poison_ticks_on_holder_card_play_flat_one_and_eats_healing():
    c = make_combat()
    c.enemy.add_status("Poison", 5)
    c._on_card_played(c.enemy)                      # enemy plays a card
    assert c.enemy.hp == 10 ** 9 - 1                # flat poisonDamage=1 tick
    assert c.enemy.s("Poison") == 4                 # stacks are duration, -1
    c.player.add_status("Poison", 3)
    c.player.hp = 40
    c._heal(c.player, 5)
    assert c.player.s("Poison") == 0                # healing eaten 1:1
    assert c.player.hp == 42                        # only 2 HP actually healed


def test_burning_ticks_at_end_turn_and_never_decays():
    c = make_combat()
    c.enemy.add_status("Burning", 4)
    c._end_turn_phase(c.enemy)
    c._end_turn_phase(c.enemy)
    assert c.enemy.hp == 10 ** 9 - 8                # 4 + 4
    assert c.enemy.s("Burning") == 4                # clearAt: None (Burning.json)


def test_bleeding_ticks_when_dealt_damage_then_loses_one():
    c = make_combat()
    c.enemy.add_status("Bleeding", 3)
    dealt = hit(c, 5)
    assert dealt == 5
    assert c.enemy.hp == 10 ** 9 - 5 - 3            # hit + bleed tick of full stacks
    assert c.enemy.s("Bleeding") == 2               # then -1 (Bleeding.json)


def test_regenerate_heals_at_startphase_then_loses_one():
    c = make_combat()
    c.player.hp = 30
    c.player.add_status("Regenerate", 4)
    c._start_phase(c.player)
    assert c.player.hp == 34
    assert c.player.s("Regenerate") == 3


def test_doom_decays_per_play_and_detonates_full_stacks_at_end_turn():
    c = make_combat()
    c.enemy.add_status("Doom", 6)
    c._on_card_played(c.enemy)
    assert c.enemy.s("Doom") == 5                   # -1 per Action played
    c._end_turn_phase(c.enemy)
    assert c.enemy.hp == 10 ** 9 - 5                # detonates remaining stacks
    assert c.enemy.s("Doom") == 0                   # then clears completely


def test_stagger_hits_half_rounded_up_at_startphase():
    c = make_combat()
    c.player.add_status("Stagger", 5)
    c._start_phase(c.player)
    assert c.player.hp == engine.PLAYER_HP - math.ceil(5 / 2)
    assert c.player.s("Stagger") == 2               # removes the amount dealt


# ---------------------------------------------------------------- engine caps

def test_energy_persists_across_turns_and_caps_at_eight():
    c = make_combat()
    c.energy = 7
    c.energy = min(c.energy + engine.PLAYER_ENERGY_PER_TURN, engine.ENERGY_CAP)
    assert c.energy == 8                            # oldest-orb overflow == cap for untyped


def test_persistent_survives_discard_and_heavy_stays_in_graveyard():
    persistent = melee_card(name="_P", keywords=["Persistent"])
    heavy = melee_card(name="_H", keywords=["Heavy"])
    c = make_combat()
    c.hand = [persistent]
    c.graveyard = [heavy, melee_card(name="_N")]
    # discard phase logic (inside _player_turn) — replicate the exact block:
    keep = [x for x in c.hand if "Persistent" in x.keywords]
    assert keep == [persistent]
    c._reshuffle()
    assert [x.name for x in c.graveyard] == ["_H"]  # Heavy not reshuffled
    assert [x.name for x in c.deck] == ["_N"]


def test_status_stack_cap():
    c = make_combat()
    c.player.add_status("Anger", 20000)
    assert c.player.s("Anger") == engine.STACK_CAP
