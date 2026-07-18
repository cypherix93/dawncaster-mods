"""DSL parsing, value grammar, simulability classification."""

import random

import gamedata as gd
from sim import dsl
from sim.dsl import ValueContext, analyze_statement, eval_expression


def ctx(**kw):
    return ValueContext(random.Random(7), **kw)


# ------------------------------------------------------------- value grammar

def test_integer_arithmetic_left_to_right():
    assert eval_expression("4+2*3", ctx()) == 18      # NumberParser is L2R, not PEMDAS
    assert eval_expression("7/2", ctx()) == 3         # integer division


def test_random_range_is_seed_deterministic():
    a = eval_expression("2%6", ValueContext(random.Random(42)))
    b = eval_expression("2%6", ValueContext(random.Random(42)))
    assert a == b and 2 <= a <= 6


def test_last_damage_token():
    assert eval_expression("[[lastDamage]]/2", ctx(last_damage=6)) == 3  # Fireball idiom


def test_status_reader_tokens():
    c = ctx(my_statuses={"Anger": 3}, other_statuses={"Burning": 5})
    assert eval_expression("[[my(status)Anger]]+1", c) == 4
    assert eval_expression("[[other(status)Burning]]", c) == 5


def test_unknown_token_is_not_simulable():
    assert not dsl.expression_simulable("[[sinister]]")
    assert dsl.expression_simulable("[[lastDamage]]/2")


# ------------------------------------------------------- statement analysis

def test_unknown_command_flagged():
    st = analyze_statement("frobnicate:3", None)
    assert not st.simulable and st.reason == "unknown command"


def test_real_but_unsupported_command_flagged_distinctly():
    st = analyze_statement("delverevelation", None)
    assert not st.simulable and st.reason == "command not in sim subset"


def test_inflict_requires_modeled_reference_status():
    assert not analyze_statement("inflict:3", None).simulable
    assert not analyze_statement("inflict:3", "Charmed").simulable  # not modeled
    assert analyze_statement("inflict:3", "Poison").simulable


def test_damage_with_unmodeled_target_flagged():
    assert not analyze_statement("damage:4:fire:all", None).simulable
    assert analyze_statement("damage:4:fire:other:2", None).simulable


def test_conjure_is_token_only():
    st = analyze_statement("conjure:potion:1:1", None)
    assert st.simulable and "token" in st.reason


# ------------------------------------------------------- card-level analysis

def _pack_card(**overrides):
    spec = {
        "name": "T", "cardID": 700000000, "type": "Magic", "category": "Action",
        "suffix": "None", "rarity": "Common", "cost": {"INT": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "damage:4"}],
    }
    spec.update(overrides)
    return dsl.card_from_pack_json(spec)


def test_fully_simulable_card():
    assert _pack_card().simulability() == 1.0


def test_reactive_trigger_is_not_scored():
    card = _pack_card(effects=[{"trigger": "Bury", "codeLine": "damage:4"}])
    assert card.simulability() < 1.0
    assert any("reactive" in n for n in card.sim_notes())


def test_unsupported_conditions_block_simulation():
    card = _pack_card(effects=[{
        "trigger": "PlayAction", "codeLine": "damage:4",
        "conditions": [{"value": "Scholar", "op": "Is", "target": ""}]}])
    assert card.simulability() < 1.0


def test_supported_conditions_do_not_block():
    for cond in (
        {"value": "Frenzy", "op": "Is", "target": ""},
        {"value": "Ambush", "op": "Is", "target": ""},
        {"value": "Chance", "op": "IsChance", "target": "50"},
        {"value": "AnyValue", "op": "IsMoreThan", "target": "[[lastDamage]]:4"},
        {"value": "StatusOnOpponent", "op": "Is", "target": "Poison"},
    ):
        card = _pack_card(effects=[{"trigger": "PlayAction", "codeLine": "damage:4",
                                    "conditions": [cond]}])
        assert card.simulability() == 1.0, cond


def test_play_conditions_and_unknown_keywords_penalize():
    card = _pack_card(playConditions=[{"value": "Empower", "op": "Is", "target": ""}])
    assert card.simulability() < 1.0
    card2 = _pack_card(keywords=["Recall"])
    assert card2.simulability() < 1.0
    card3 = _pack_card(keywords=["OneUse", "Piercing"])
    assert card3.simulability() == 1.0


def test_pool_card_normalization_fireball():
    import json
    d = json.loads((gd.DATA_DIR / "Card" / "Fireball.json").read_text(encoding="utf-8"))
    card = dsl.card_from_pool_json(d)
    assert card.name == "Fireball" and card.cost == 2 and card.rarity == "Common"
    assert card.keywords == {"Firecast"}
    assert card.effects[1].reference_status == "Burning"   # PPtr 15570 resolved
    assert card.fully_simulable()                          # Firecast inert in harness


# ------------------------------------------------------- degeneracy sniffing

def test_unbounded_replay_loop_flagged():
    card = _pack_card(effects=[{"trigger": "PlayAction", "codeLine": "playcopy:1"}])
    assert dsl.degeneracy_flags(card)


def test_oneuse_bounds_the_loop():
    card = _pack_card(keywords=["OneUse"],
                      effects=[{"trigger": "PlayAction", "codeLine": "playcopy:1"}])
    assert not dsl.degeneracy_flags(card)


def test_self_feeding_trigger_flagged():
    card = _pack_card(effects=[{"trigger": "Draw", "codeLine": "draw:1"}])
    assert dsl.degeneracy_flags(card)


def test_conditioned_self_feed_not_flagged():
    card = _pack_card(effects=[{
        "trigger": "Draw", "codeLine": "draw:1",
        "conditions": [{"value": "Chance", "op": "IsChance", "target": "25"}]}])
    assert not dsl.degeneracy_flags(card)
