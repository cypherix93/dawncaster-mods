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


def test_talent_and_profession_loaders():
    assert len(gd.pool_talents()) == 383
    assert len(gd.pool_talent_ids()) == 383
    assert gd.talent_commands() >= {"resetweaponcooldown", "addtalent", "startstatus",
                                    "addnextcardfree", "addnextbykeyword"}
    # union: talent extras + full SpellEffects fall-through (TalentHandler.cs:510)
    union = gd.talent_effect_commands()
    assert "damage" in union and "resetweaponcooldown" in union
    assert len(union) == len(gd.effect_commands() | gd.talent_commands())
    assert gd.profession_names() == {"Arcanist", "Hunter", "Knight", "Rogue",
                                     "Scion", "Seeker", "Warrior"}
    assert gd.resolve_talent_reference("Acrobatic Weapon")
    assert gd.resolve_talent_reference("Acrobatic_Weapon.json (weapon rider)")
    assert not gd.resolve_talent_reference("Totally Made Up Talent")


def test_dialogue_loaders():
    # 99 dialogue-action commands (DialogueActionHandler.RunActionCode switch)
    assert len(gd.dialogue_commands()) == 99
    assert {"gold", "addcard", "combat"} <= gd.dialogue_commands()
    # 148 shipped Dialogue SOs; both name namespaces are scanned
    assert len(gd.pool_events()) == 148
    assert "mimic" in gd.pool_event_names_lower()
    assert "mimic" in gd.pool_event_textfile_names_lower()
    assert gd.textasset_pathid_map()  # PPtr resolution table non-empty
    # collision helper covers both namespaces + space/underscore variants
    assert gd.event_pool_collision("Mimic")
    assert gd.event_pool_collision("abandoned village")
    assert not gd.event_pool_collision("Hello Wayfarer")


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


def good_weapon(**overrides):
    weapon = good_card(
        name="Zz Tithing Blade", cardID=700009990, type="Melee",
        category="BasicAttack", cost={"STR": 1},
        description="Deal 5 damage.",
        meta={"archetype": "weapon", "nearestExisting": "Longsword",
              "whyDifferent": "test fixture"},
    )
    weapon["classes"] = ["Knight", "Warrior"]
    weapon.update(overrides)
    return weapon


def good_power(**overrides):
    power = {
        "name": "Zz Bloodletting", "talentID": 700009991,
        "description": "Inflict 2 Bleeding at the start of your next combat.",
        "flavortext": "", "cooldown": 3, "keywords": [],
        "effects": [{"trigger": "ActivateWeapon", "codeLine": "startstatus:2",
                     "forecast": False, "referenceStatus": "Bleeding",
                     "referenceCards": [], "conditions": []}],
        "requirements": {"rDEX": 0, "rINT": 0, "rSTR": 0},
        "classes": ["Knight"], "art": "art/Missing.png",
        "meta": {"archetype": "weapon power", "nearestExisting": "Acrobatic Weapon",
                 "whyDifferent": "test fixture"},
    }
    power.update(overrides)
    return power


def good_starting_card(**overrides):
    card = good_card(
        name="Zz First Spark", cardID=700009985, cost={"INT": 1},
        description="Deal 3 fire damage.",
        meta={"archetype": "burn starting card", "nearestExisting": "Shocking Grasp",
              "whyDifferent": "test fixture"},
    )
    card["effects"][0]["codeLine"] = "damage:3:fire"
    card["classes"] = ["Arcanist", "Knight"]
    card.update(overrides)
    return card


def run(cards, id_block=(700009900, 700009999), tmp_path=None,
        weapons=None, powers=None, starting_cards=None):
    manifest = {"pack": "TestPack", "idBlock": list(id_block), "cards": cards}
    if weapons is not None:
        manifest["weapons"] = weapons
    if powers is not None:
        manifest["weaponPowers"] = powers
    if starting_cards is not None:
        manifest["startingCards"] = starting_cards
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


# ================================================================ v1.1 weapons


def test_clean_weapon_and_power_pass(tmp_path):
    findings = run([good_card()], tmp_path=tmp_path,
                   weapons=[good_weapon()], powers=[good_power()])
    assert not errors(findings), findings


def test_weapons_only_pack_is_legal(tmp_path):
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power()])
    assert not errors(findings, "no_cards")


def test_empty_manifest_rejected(tmp_path):
    findings = run([], tmp_path=tmp_path)
    assert errors(findings, "no_cards")


def test_weapon_must_be_basicattack(tmp_path):
    findings = run([], tmp_path=tmp_path,
                   weapons=[good_weapon(category="Action")])
    assert errors(findings, "weapon_not_basicattack")


def test_weapon_classes_validated(tmp_path):
    findings = run([], tmp_path=tmp_path,
                   weapons=[good_weapon(classes=["Paladin"])])
    assert errors(findings, "bad_class")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon(classes=[])])
    assert errors(findings, "missing_classes")
    w = good_weapon()
    del w["classes"]
    assert errors(run([], tmp_path=tmp_path, weapons=[w]), "missing_classes")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon(classes=["all"])])
    assert not errors(findings, "bad_class")


def test_weapon_id_topdown_advisory(tmp_path):
    # weapon ID below a regular card's ID -> advisory only
    findings = run([good_card(cardID=700009950)], tmp_path=tmp_path,
                   weapons=[good_weapon(cardID=700009910)])
    assert any(f["check"] == "weapon_id_not_topdown" for f in findings)
    assert not errors(findings, "weapon_id_not_topdown")  # advisory, not an error
    findings = run([good_card(cardID=700009901)], tmp_path=tmp_path,
                   weapons=[good_weapon(cardID=700009990)])
    assert not any(f["check"] == "weapon_id_not_topdown" for f in findings)


def test_weapon_shares_card_id_namespace(tmp_path):
    findings = run([good_card(cardID=700009990)], tmp_path=tmp_path,
                   weapons=[good_weapon(cardID=700009990)])
    assert errors(findings, "id_collision")


# =========================================================== v1.1 weapon powers


def test_power_talent_id_separate_namespace(tmp_path):
    # a talentID may share a number with a cardID (different lookup spaces, §3)
    findings = run([good_card(cardID=700009991)], tmp_path=tmp_path,
                   powers=[good_power(talentID=700009991)])
    assert not errors(findings), findings


def test_power_talent_id_collides_with_shipped(tmp_path):
    shipped_id = next(iter(gd.pool_talent_ids()))
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(talentID=shipped_id)])
    assert errors(findings, "talent_id_collision")


def test_power_name_collides_with_shipped_talent(tmp_path):
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(name="acrobatic weapon")])  # case-insensitive
    assert errors(findings, "talent_name_collision")


def test_power_talent_id_in_block_and_unique(tmp_path):
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(talentID=700000001)])
    assert errors(findings, "id_outside_block")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(), good_power(name="Zz Other Power")])
    assert errors(findings, "talent_id_collision")


def test_power_codeline_uses_union_vocabulary(tmp_path):
    # talent-switch command + SpellEffects fall-through command both legal
    p = good_power()
    p["effects"][0]["codeLine"] = "addnextcardfree:1; heal:5:self"
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p])
    assert not errors(findings, "unknown_command"), findings
    p2 = good_power()
    p2["effects"][0]["codeLine"] = "summonvoltron:9000"
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p2])
    assert errors(findings, "unknown_command")


def test_card_codeline_does_not_get_talent_vocabulary(tmp_path):
    # talent-only commands stay illegal on regular cards
    c = good_card()
    c["effects"][0]["codeLine"] = "addnextcardfree:1"
    assert errors(run([c], tmp_path=tmp_path), "unknown_command")


def test_cooldown_degeneracy_hard_error(tmp_path):
    p = good_power()
    p["effects"][0]["codeLine"] = "startstatus:2; resetweaponcooldown"
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p])
    assert errors(findings, "weapon_cooldown_degeneracy")
    # every cooldown-reducing spelling is caught
    for cmd in ("lowercooldown", "reducecooldown:1", "setcooldown:0"):
        p2 = good_power()
        p2["effects"][0]["codeLine"] = cmd
        findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p2])
        assert errors(findings, "weapon_cooldown_degeneracy"), cmd
    # the same command on a non-ActivateWeapon rider is legal
    p3 = good_power(effects=[
        {"trigger": "ActivateWeapon", "codeLine": "startstatus:2", "forecast": False,
         "referenceStatus": "Bleeding", "referenceCards": [], "conditions": []},
        {"trigger": "VictoryPhase", "codeLine": "resetweaponcooldown",
         "forecast": False, "referenceStatus": None, "referenceCards": [],
         "conditions": []}])
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p3])
    assert not errors(findings, "weapon_cooldown_degeneracy")


def test_power_schema_errors(tmp_path):
    p = good_power()
    del p["cooldown"]
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p])
    assert errors(findings, "missing_field")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(cooldown=0)])
    assert errors(findings, "bad_cooldown")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(requirements={"rLUCK": 1})])
    assert errors(findings, "bad_requirements")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()],
                   powers=[good_power(classes=["Paladin"])])
    assert errors(findings, "bad_class")
    p4 = good_power()
    p4["meta"]["nearestExisting"] = "Totally Made Up Talent"
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p4])
    assert errors(findings, "meta_bad_reference")


def test_power_without_activateweapon_warns(tmp_path):
    p = good_power(effects=[{"trigger": "StartCombat", "codeLine": "bless:1",
                             "forecast": False, "referenceStatus": "Evasion",
                             "referenceCards": [], "conditions": []}])
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon()], powers=[p])
    assert any(f["check"] == "no_activateweapon" for f in findings)
    assert not errors(findings, "no_activateweapon")


def test_starting_card_clean_pass(tmp_path):
    findings = run([good_card()], tmp_path=tmp_path,
                   starting_cards=[good_starting_card()])
    assert not errors(findings), findings
    # no budget warnings on the corpus-conformant fixture (1-cost Common)
    assert not any(f["check"].startswith("startingcard_") for f in findings)


def test_starting_cards_only_pack_is_legal(tmp_path):
    findings = run([], tmp_path=tmp_path, starting_cards=[good_starting_card()])
    assert not errors(findings, "no_cards")


def test_starting_card_any_category_legal(tmp_path):
    # NO BasicAttack pinning: the shipped corpus spans Action/Enchantment/Equipment
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(category="Enchantment")])
    assert not errors(findings), findings
    assert not any(f["check"] == "weapon_not_basicattack" for f in findings)


def test_starting_card_classes_validated(tmp_path):
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(classes=["Paladin"])])
    assert errors(findings, "bad_class")
    sc = good_starting_card()
    del sc["classes"]
    assert errors(run([], tmp_path=tmp_path, starting_cards=[sc]),
                  "missing_classes")
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(classes=["all"])])
    assert not errors(findings, "bad_class")


def test_starting_card_shares_card_id_namespace(tmp_path):
    findings = run([good_card(cardID=700009985)], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(cardID=700009985)])
    assert errors(findings, "id_collision")
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon(cardID=700009985)],
                   starting_cards=[good_starting_card(cardID=700009985)])
    assert errors(findings, "id_collision")


def test_starting_card_id_topdown_advisory(tmp_path):
    # below the pack's regular cards -> advisory only
    findings = run([good_card(cardID=700009950)], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(cardID=700009910)])
    assert any(f["check"] == "startingcard_id_not_topdown" for f in findings)
    assert not errors(findings, "startingcard_id_not_topdown")   # advisory
    # at/above the weapon/power allocations -> advisory
    findings = run([], tmp_path=tmp_path, weapons=[good_weapon(cardID=700009990)],
                   powers=[good_power(talentID=700009989)],
                   starting_cards=[good_starting_card(cardID=700009991)])
    assert any(f["check"] == "startingcard_id_not_topdown" for f in findings)
    # directly below them -> silent
    findings = run([good_card(cardID=700009901)], tmp_path=tmp_path,
                   weapons=[good_weapon(cardID=700009990)],
                   powers=[good_power(talentID=700009989)],
                   starting_cards=[good_starting_card(cardID=700009988)])
    assert not any(f["check"] == "startingcard_id_not_topdown" for f in findings)


def test_starting_card_budget_lints_warn_only(tmp_path):
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(cost={"INT": 2})])
    assert any(f["check"] == "startingcard_cost_curve" for f in findings)
    assert not errors(findings, "startingcard_cost_curve")
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(rarity="Rare")])
    assert any(f["check"] == "startingcard_rarity" for f in findings)
    assert not errors(findings, "startingcard_rarity")
    findings = run([], tmp_path=tmp_path,
                   starting_cards=[good_starting_card(flags=["excludeFromRewards"])])
    assert any(f["check"] == "startingcard_reward_excluded" for f in findings)
    assert not errors(findings, "startingcard_reward_excluded")


def test_cross_pack_starting_card_collision(tmp_path, monkeypatch):
    sibling_dir = tmp_path / "OtherPack"
    sibling_dir.mkdir()
    sibling = {"pack": "OtherPack", "idBlock": [700009900, 700009999],
               "cards": [], "startingCards": [good_starting_card()]}
    (sibling_dir / "pack.json").write_text(json.dumps(sibling), encoding="utf-8")
    monkeypatch.setattr(gd, "PACKS_DIR", tmp_path)

    mine_dir = tmp_path / "MinePack"
    mine_dir.mkdir()
    manifest = {"pack": "MinePack", "idBlock": [700009900, 700009999],
                "cards": [], "startingCards": [good_starting_card()]}
    findings = vp.validate_pack(manifest, mine_dir / "pack.json")
    assert errors(findings, "id_collision")
    assert errors(findings, "name_collision")


def test_cross_pack_weapon_and_power_collisions(tmp_path, monkeypatch):
    sibling_dir = tmp_path / "OtherPack"
    sibling_dir.mkdir()
    sibling = {"pack": "OtherPack", "idBlock": [700009900, 700009999],
               "cards": [], "weapons": [good_weapon()], "weaponPowers": [good_power()]}
    (sibling_dir / "pack.json").write_text(json.dumps(sibling), encoding="utf-8")
    monkeypatch.setattr(gd, "PACKS_DIR", tmp_path)

    mine_dir = tmp_path / "MinePack"
    mine_dir.mkdir()
    manifest = {"pack": "MinePack", "idBlock": [700009900, 700009999],
                "cards": [], "weapons": [good_weapon()],
                "weaponPowers": [good_power()]}
    findings = vp.validate_pack(manifest, mine_dir / "pack.json")
    assert errors(findings, "id_collision")          # weapon card namespace
    assert errors(findings, "name_collision")
    assert errors(findings, "talent_id_collision")   # power talent namespace
    assert errors(findings, "talent_name_collision")


# ------------------------------------------------------------------ events

GOOD_STORY = {
    "inkVersion": 20,
    "root": [["^A wayfarer waves at you.", {"#": "A Wayfarer"}, "\n",
              "^>>>>gold:50", "\n", "end", {"#f": 1}], "done", {"#f": 1}],
    "listDefs": {},
}


def good_event(**overrides):
    ev = {"name": "Zz Test Wayfarer", "storyFile": "events/ZzTestWayfarer.ink.json",
          "minLevel": 0, "maxLevel": 0, "unique": False}
    ev.update(overrides)
    return ev


def event_pack(tmp_path, story=None, **event_overrides):
    """Write a minimal events-only pack folder; returns its pack.json Path."""
    pack_dir = tmp_path / "DC.ZzEvents"
    (pack_dir / "events").mkdir(parents=True, exist_ok=True)
    (pack_dir / "events" / "ZzTestWayfarer.ink.json").write_text(
        json.dumps(story if story is not None else GOOD_STORY), encoding="utf-8")
    manifest = {"schemaVersion": 2, "pack": "Zz Events",
                "events": [good_event(**event_overrides)]}
    pack_json = pack_dir / "pack.json"
    pack_json.write_text(json.dumps(manifest), encoding="utf-8")
    return pack_json


def run_validator(pack_json):
    manifest = json.loads(pack_json.read_text(encoding="utf-8"))
    return vp.validate_pack(manifest, pack_json)


def test_good_event_pack_passes(tmp_path):
    findings = run_validator(event_pack(tmp_path))
    assert errors(findings) == []


def test_events_only_pack_needs_no_idblock(tmp_path):
    findings = run_validator(event_pack(tmp_path))
    assert errors(findings, "bad_id_block") == []


def test_events_require_schema_version_2(tmp_path):
    pack_json = event_pack(tmp_path)
    manifest = json.loads(pack_json.read_text(encoding="utf-8"))
    del manifest["schemaVersion"]
    pack_json.write_text(json.dumps(manifest), encoding="utf-8")
    findings = run_validator(pack_json)
    assert errors(findings, "schema_version")


def test_event_unknown_command_did_you_mean(tmp_path):
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>>glod:50"
    findings = run_validator(event_pack(tmp_path, story=story))
    errs = errors(findings, "unknown_command")
    assert errs and "gold" in errs[0]["msg"]


def test_event_command_case_insensitive(tmp_path):
    # shipped stories use uppercase (Mimic: >>>>DIRECTCOMBAT) — the game
    # lowercases before dispatch (DialogueActionHandler.cs:29), so GOLD is clean
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>>GOLD:50"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors(findings, "unknown_command") == []


def test_event_ink_version_pin(tmp_path):
    story = dict(GOOD_STORY, inkVersion=21)
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors(findings, "bad_ink_version")


def test_event_name_collision_with_shipped(tmp_path):
    findings = run_validator(event_pack(tmp_path, name="Mimic"))
    assert errors(findings, "event_name_collision")


def test_event_goto_and_storyfunction(tmp_path):
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>goto:no_such_knot"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors(findings, "goto_unknown_knot")

    story["root"][0][3] = "^>>>STORYFUNCTION:foo:imbueCost"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors(findings, "storyfunction_reserved")


def test_event_level_gate_shape(tmp_path):
    findings = run_validator(event_pack(tmp_path, minLevel=5, maxLevel=3))
    assert errors(findings, "bad_levels")
    # maxLevel 0 = uncapped is legal
    findings = run_validator(event_pack(tmp_path, minLevel=5, maxLevel=0))
    assert errors(findings, "bad_levels") == []


def test_event_missing_story_file(tmp_path):
    findings = run_validator(event_pack(tmp_path, storyFile="events/Nope.ink.json"))
    assert errors(findings, "story_missing")
