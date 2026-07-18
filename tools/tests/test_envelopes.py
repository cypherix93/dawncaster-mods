"""Envelope math + scoring plumbing (no full corpus rebuild in unit tests)."""

from sim import engine, envelopes
from sim.dsl import card_from_pack_json
from sim.envelopes import Envelope, percentile


def test_percentile_interpolation():
    vals = [1.0, 2.0, 3.0, 4.0, 5.0]
    assert percentile(vals, 0) == 1.0
    assert percentile(vals, 50) == 3.0
    assert percentile(vals, 100) == 5.0
    assert percentile(vals, 25) == 2.0
    assert percentile([7.0], 95) == 7.0


def test_envelope_positions_and_bands():
    e = Envelope("Common", 100, p5=-2.0, p25=0.0, p50=2.0, p75=5.0, p95=10.0)
    assert e.position(-3.0) == "UNDER"
    assert e.position(0.5) == "ON-CURVE"
    assert e.position(11.0) == "OVER"
    assert e.band(1.0) == "P25-P50"
    assert e.band(11.0) == ">P95"


def test_rarity_fallback_maps_epic_to_legendary():
    data = {"envelopes": {"Legendary": {"n": 50, "p5": 0, "p25": 1, "p50": 2,
                                        "p75": 3, "p95": 4}}}
    e = envelopes.envelope_for("Epic", data)
    assert e is not None and e.rarity == "Legendary"


def test_score_card_is_deterministic():
    card = card_from_pack_json({
        "name": "DetCheck", "cardID": 700000000, "type": "Melee",
        "category": "Action", "rarity": "Common", "cost": {"Neutral": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "damage:2%8"}],
    })
    a = engine.score_card(card)
    b = engine.score_card(card)
    assert a.power == b.power
    assert a.per_turn_dmg_delta == b.per_turn_dmg_delta


def test_stronger_card_scores_higher():
    def mk(dmg):
        return card_from_pack_json({
            "name": f"D{dmg}", "cardID": 1, "type": "Melee", "category": "Action",
            "rarity": "Common", "cost": {"Neutral": 1},
            "effects": [{"trigger": "PlayAction", "codeLine": f"damage:{dmg}"}]})
    weak = engine.score_card(mk(2))
    strong = engine.score_card(mk(12))
    assert strong.power > weak.power
    # on-rate common (damage:4 == the baseline strike) should sit near zero delta
    on_rate = engine.score_card(mk(4))
    assert abs(on_rate.power) < abs(strong.power)


def test_infinite_scaler_has_high_scaling_ratio():
    # A card that inflicts Burning every play keeps accelerating: Burning never
    # decays, so late-window damage outgrows the early window.
    scaler = card_from_pack_json({
        "name": "BurnStack", "cardID": 2, "type": "Magic", "category": "Action",
        "rarity": "Common", "cost": {"Neutral": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "inflict:4",
                     "referenceStatus": "Burning"}]})
    vanilla = card_from_pack_json({
        "name": "Van", "cardID": 3, "type": "Melee", "category": "Action",
        "rarity": "Common", "cost": {"Neutral": 1},
        "effects": [{"trigger": "PlayAction", "codeLine": "damage:4"}]})
    s1 = engine.score_card(scaler)
    s2 = engine.score_card(vanilla)
    assert s1.scaling_ratio > s2.scaling_ratio
