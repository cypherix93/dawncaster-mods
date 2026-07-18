"""Validator tests: enum tables, schema checks, collision policy, DSL vocabulary."""

import json

import pytest

import gamedata as gd
import validate_pack as vp


# ------------------------------------------------------------- table integrity

def test_enum_tables_shape():
    assert len(gd.GAME_TRIGGERS) == 113
    assert len(gd.CONDITION_VALUES) == 118
    assert len(gd.CONDITION_OPERATORS) == 7
    assert len(gd.CARD_PROPERTIES) == 25
    for table in (gd.GAME_TRIGGERS, gd.CONDITION_VALUES, gd.CARD_RARITIES,
                  gd.CARD_SUFFIXES, gd.CARD_TYPES, gd.CARD_CATEGORIES):
        assert len(table) == len(set(table)), "duplicate enum member"


def test_canonical_typos_are_preserved():
    assert "Phyisical" in gd.CARD_SUFFIXES          # sic — API surface
    assert "Playerdeath" in gd.GAME_TRIGGERS
    assert "Gainsouls" in gd.GAME_TRIGGERS


def test_pool_loaders():
    assert len(gd.pool_cards()) == 2525
    assert len(gd.status_names()) == 49
    assert len(gd.effect_commands()) == 565
    assert "damage" in gd.effect_commands()
    assert gd.status_pathid_map()  # PPtr resolution table non-empty


# ------------------------------------------------------------------ fixtures

def good_card(**overrides):
    card = {
        "name": "Zz Test Cinder", "cardID": 700009901, "expansion": "Extended",
        "type": "Magic", "category": "Action", "suffix": "Fire", "rarity": "Common",
        "cost": {"INT": 1}, "description": "Deal 4 fire damage.",
        "keywords": [], "cardKeywords": ["firecast"], "utilityNumber": "",
        "charges": 0, "flags": [], "playConditions": [],
        "effects": [{"trigger": "PlayAction", "codeLine": "damage:4:fire",
                     "forecast": True, "referenceStatus": None,
                     "referenceCards": [], "conditions": []}],
        "enchantment": None, "art": "art/Missing.png",
        "meta": {"archetype": "burn", "nearestExisting": "Fireball",
                 "whyDifferent": "test fixture"},
    }
    card.update(overrides)
    return card


def run(cards, id_block=(700009900, 700009999), tmp_path=None):
    manifest = {"pack": "TestPack", "idBlock": list(id_block), "cards": cards}
    pack_path = (tmp_path or gd.PACKS_DIR / "_nonexistent") / "pack.json"
    return vp.validate_pack(manifest, pack_path)


def errors(findings, check=None):
    return [f for f in findings if f["level"] == "ERROR"
            and (check is None or f["check"] == check)]


# ------------------------------------------------------------------ happy path

def test_clean_card_has_no_errors(tmp_path):
    findings = run([good_card()], tmp_path=tmp_path)
    assert not errors(findings), findings


# ------------------------------------------------------------------ enums

def test_bad_enum_spellings_rejected(tmp_path):
    findings = run([good_card(suffix="Physical")], tmp_path=tmp_path)  # the typo IS canon
    assert errors(findings, "bad_enum")
    findings = run([good_card(rarity="common")], tmp_path=tmp_path)
    assert errors(findings, "bad_enum")


def test_player_pool_illegal_combos(tmp_path):
    assert errors(run([good_card(rarity="Monster")], tmp_path=tmp_path), "pool_illegal")
    assert errors(run([good_card(expansion="Metaprogress")], tmp_path=tmp_path), "pool_illegal")
    assert errors(run([good_card(suffix="Companion")], tmp_path=tmp_path), "pool_illegal")


def test_unknown_trigger_and_condition(tmp_path):
    c = good_card()
    c["effects"][0]["trigger"] = "OnPlay"           # invented — not a GameTrigger
    assert errors(run([c], tmp_path=tmp_path), "bad_trigger")
    c2 = good_card(playConditions=[{"value": "IsAngry", "op": "Is", "target": ""}])
    assert errors(run([c2], tmp_path=tmp_path), "bad_condition")
    c3 = good_card(playConditions=[{"value": "Frenzy", "op": "Equals", "target": ""}])
    assert errors(run([c3], tmp_path=tmp_path), "bad_condition")


# ------------------------------------------------------------------ commands

def test_unknown_command_rejected_with_case_hint(tmp_path):
    c = good_card()
    c["effects"][0]["codeLine"] = "damage:4; Draw:1"
    errs = errors(run([c], tmp_path=tmp_path), "unknown_command")
    assert errs and "case-sensitive" in errs[0]["msg"]


def test_multi_statement_codeline_parses(tmp_path):
    c = good_card()
    c["effects"][0]["codeLine"] = "damage:5; draw:1; inflict:3"
    c["effects"][0]["referenceStatus"] = "Burning"
    assert not errors(run([c], tmp_path=tmp_path))


def test_bad_reference_status(tmp_path):
    c = good_card()
    c["effects"][0]["referenceStatus"] = "Burnination"
    assert errors(run([c], tmp_path=tmp_path), "bad_reference_status")


def test_inflict_without_reference_status_warns(tmp_path):
    c = good_card()
    c["effects"][0]["codeLine"] = "inflict:3"
    findings = run([c], tmp_path=tmp_path)
    assert any(f["check"] == "missing_reference_status" for f in findings)


# ------------------------------------------------------------------ identity

def test_id_collisions(tmp_path):
    # outside declared block
    assert errors(run([good_card(cardID=700000001)], tmp_path=tmp_path), "id_outside_block")
    # outside mod range entirely
    findings = run([good_card(cardID=614322)], id_block=(700009900, 700009999),
                   tmp_path=tmp_path)
    assert errors(findings, "id_outside_block") or errors(findings, "id_outside_mod_range")
    # duplicate inside pack
    findings = run([good_card(), good_card(name="Zz Other Name")], tmp_path=tmp_path)
    assert errors(findings, "id_collision")


def test_name_collision_with_pool_case_insensitive(tmp_path):
    findings = run([good_card(name="fireball", cardID=700009902)], tmp_path=tmp_path)
    assert errors(findings, "name_collision")


def test_meta_nearest_existing_must_be_real(tmp_path):
    c = good_card()
    c["meta"]["nearestExisting"] = "Fireball But Cooler"
    assert errors(run([c], tmp_path=tmp_path), "meta_bad_reference")
    c2 = good_card()
    del c2["meta"]["nearestExisting"]
    assert errors(run([c2], tmp_path=tmp_path), "meta_missing")


# ------------------------------------------------------------------ warnings

def test_cost_warnings(tmp_path):
    findings = run([good_card(cost={"DEX": 1, "INT": 1, "STR": 1})], tmp_path=tmp_path)
    assert any(f["check"] == "color_muddy" for f in findings)
    findings = run([good_card(cost={"INT": 5})], tmp_path=tmp_path)
    assert any(f["check"] == "cost_outlier" for f in findings)


def test_bad_cost_key_rejected(tmp_path):
    assert errors(run([good_card(cost={"MANA": 1})], tmp_path=tmp_path), "bad_cost_key")


# ------------------------------------------------------------------ end-to-end

def test_cross_pack_collision(tmp_path, monkeypatch):
    sibling_dir = tmp_path / "OtherPack"
    sibling_dir.mkdir()
    sibling = {"pack": "OtherPack", "idBlock": [700009900, 700009999],
               "cards": [good_card()]}
    (sibling_dir / "pack.json").write_text(json.dumps(sibling), encoding="utf-8")
    monkeypatch.setattr(gd, "PACKS_DIR", tmp_path)

    mine_dir = tmp_path / "MinePack"
    mine_dir.mkdir()
    manifest = {"pack": "MinePack", "idBlock": [700009900, 700009999],
                "cards": [good_card()]}  # same id + name as sibling
    findings = vp.validate_pack(manifest, mine_dir / "pack.json")
    assert errors(findings, "id_collision")
    assert errors(findings, "name_collision")


def test_cli_exit_codes(tmp_path):
    bad = {"pack": "X", "idBlock": [700009900, 700009999],
           "cards": [good_card(rarity="Shiny")]}
    p = tmp_path / "pack.json"
    p.write_text(json.dumps(bad), encoding="utf-8")
    assert vp.main([str(p)]) == 1
    ok = {"pack": "X", "idBlock": [700009900, 700009999], "cards": [good_card()]}
    p.write_text(json.dumps(ok), encoding="utf-8")
    assert vp.main([str(p)]) == 0


def test_missing_required_fields(tmp_path):
    c = good_card()
    del c["description"]
    findings = run([c], tmp_path=tmp_path)
    assert errors(findings, "missing_field")


@pytest.mark.parametrize("kw,ok", [("Piercing", True), ("OneShot", False)])
def test_keyword_vocabulary(tmp_path, kw, ok):
    findings = run([good_card(keywords=[kw])], tmp_path=tmp_path)
    assert bool(errors(findings, "bad_keyword")) != ok
