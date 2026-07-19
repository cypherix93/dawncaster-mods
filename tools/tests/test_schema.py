"""schemas/pack.schema.json: valid vs the 5 real manifests, broken-manifest
rejection, and enum-table sync with gamedata (the schema is generated from the
same tables — these tests pin them together so they can never drift)."""

import copy
import json
from pathlib import Path

import pytest
from jsonschema import Draft7Validator

import gamedata as gd

SCHEMA_PATH = gd.REPO_DIR / "schemas" / "pack.schema.json"
REAL_MANIFESTS = sorted(gd.REPO_DIR.glob("DC.*/pack.json")) + [
    gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExamplePack" / "pack.json",
    gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExampleEventPack" / "pack.json",
]


@pytest.fixture(scope="module")
def schema() -> dict:
    return json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def validator(schema) -> Draft7Validator:
    Draft7Validator.check_schema(schema)
    return Draft7Validator(schema)


def errors(validator, manifest) -> list[str]:
    return [f"{'/'.join(map(str, e.absolute_path)) or '<root>'}: {e.message}"
            for e in validator.iter_errors(manifest)]


# ------------------------------------------------------------ real manifests

@pytest.mark.parametrize("path", REAL_MANIFESTS, ids=lambda p: p.parent.name)
def test_real_manifests_pass(validator, path):
    manifest = json.loads(path.read_text(encoding="utf-8"))
    assert errors(validator, manifest) == []


def test_manifests_carry_schema_ref():
    for path in REAL_MANIFESTS:
        manifest = json.loads(path.read_text(encoding="utf-8"))
        ref = manifest.get("$schema", "")
        assert ref.endswith("schemas/pack.schema.json"), path
        # the relative pointer must actually resolve from the manifest's dir
        assert (path.parent / ref).resolve() == SCHEMA_PATH.resolve(), path


# ------------------------------------------------------------- schemaVersion

def example_manifest() -> dict:
    return json.loads(
        (gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExamplePack" / "pack.json")
        .read_text(encoding="utf-8"))


def test_schema_version_optional_and_known_versions_only(validator):
    m = example_manifest()
    m.pop("schemaVersion", None)
    assert errors(validator, m) == []          # absent = 1
    m["schemaVersion"] = 1
    assert errors(validator, m) == []
    m["schemaVersion"] = 2                     # v2 = events (EVENT-SPEC §4)
    assert errors(validator, m) == []
    m["schemaVersion"] = 3
    assert any("schemaVersion" in e for e in errors(validator, m))


# ---------------------------------------------------------- broken manifests

def test_broken_manifest_fails(validator):
    m = example_manifest()
    card = m["cards"][0]
    card["rarity"] = "Comon"
    card["suffix"] = "Physical"            # corrected spelling must FAIL
    card["effects"][0]["trigger"] = "OnPlay"
    card["cost"] = {"MANA": 1}
    card["unknownField"] = True
    del card["meta"]
    m["idBlock"] = [1, 2]
    errs = "\n".join(errors(validator, m))
    for fragment in ("rarity", "suffix", "trigger", "MANA", "unknownField",
                     "meta", "idBlock"):
        assert fragment in errs, f"expected a {fragment} error in:\n{errs}"


@pytest.mark.parametrize("mutate,fragment", [
    (lambda m: m.pop("pack"), "pack"),
    (lambda m: m.pop("idBlock"), "idBlock"),
    (lambda m: m.__setitem__("cards", []), "anyOf|valid|cards"),
    (lambda m: m["cards"][0].__setitem__("effects", []), "effects"),
    (lambda m: m["cards"][0]["effects"][0].pop("codeLine"), "codeLine"),
    (lambda m: m["cards"][0].__setitem__("expansion", "None"), "expansion"),
    (lambda m: m["cards"][0].__setitem__("rarity", "Monster"), "rarity"),
    (lambda m: m["cards"][0].__setitem__("cardID", 900001), "cardID"),
], ids=["no-pack", "no-idBlock", "empty-content", "empty-effects",
        "no-codeLine", "expansion-None", "rarity-Monster", "id-outside-range"])
def test_single_break_is_caught(validator, mutate, fragment):
    m = example_manifest()
    mutate(m)
    errs = errors(validator, m)
    assert errs, "broken manifest passed the schema"
    joined = "\n".join(errs)
    assert any(f in joined for f in fragment.split("|")), joined


def test_weapon_category_pinned_and_classes_required(validator):
    m = example_manifest()
    w = copy.deepcopy(m["cards"][0])
    w["name"] = "Zz Schema Test Blade"
    w["cardID"] = 737673499
    m["weapons"] = [w]
    errs = "\n".join(errors(validator, m))
    assert "classes" in errs
    assert "BasicAttack" in errs           # category Action != const BasicAttack
    w["category"] = "BasicAttack"
    w["classes"] = ["Knight", "all"]
    assert errors(validator, m) == []
    w["classes"] = ["Paladin"]             # not a shipped Profession
    assert any("classes" in e or "Paladin" in e for e in errors(validator, m))


def test_starting_card_shape_and_classes(validator):
    # v1.2: a starting card is the full card schema + required classes,
    # with NO category pinning (any legal card shape).
    m = example_manifest()
    sc = copy.deepcopy(m["cards"][0])
    sc["name"] = "Zz Schema Test Opener"
    sc["cardID"] = 737673497
    m["startingCards"] = [sc]
    errs = "\n".join(errors(validator, m))
    assert "classes" in errs                   # classes is required
    assert "BasicAttack" not in errs           # category NOT pinned (stays Action)
    sc["classes"] = ["Knight", "all"]
    assert errors(validator, m) == []
    sc["category"] = "Enchantment"             # shipped corpus has 4 Enchantments
    assert errors(validator, m) == []
    sc["classes"] = ["Paladin"]                # not a shipped Profession
    assert any("classes" in e or "Paladin" in e for e in errors(validator, m))


def test_starting_cards_only_pack_is_legal(validator):
    m = example_manifest()
    sc = copy.deepcopy(m["cards"][0])
    sc["classes"] = ["Knight"]
    del m["cards"]
    m["startingCards"] = [sc]
    assert errors(validator, m) == []          # anyOf accepts startingCards alone


def test_weapon_power_shape(validator):
    m = example_manifest()
    m["weaponPowers"] = [{
        "name": "Zz Schema Test Power", "talentID": 737673498,
        "description": "Test.", "flavortext": "", "cooldown": 3,
        "keywords": [], "classes": ["Knight"], "art": "",
        "effects": [{"trigger": "ActivateWeapon", "codeLine": "healplayer:5",
                     "forecast": False, "referenceStatus": None,
                     "referenceCards": [], "conditions": []}],
        "requirements": {"rDEX": 0, "rINT": 0, "rSTR": 0},
        "meta": {"nearestExisting": "Deadly_Weapon"},
    }]
    assert errors(validator, m) == []
    m["weaponPowers"][0]["cooldown"] = 0   # floor-clamped to 1 by the engine
    assert any("cooldown" in e for e in errors(validator, m))
    del m["weaponPowers"][0]["cooldown"]
    assert any("cooldown" in e for e in errors(validator, m))


# ------------------------------------------------- enum sync with gamedata

def defs(schema, name):
    return schema["definitions"][name]


def test_enums_match_gamedata(schema):
    card = defs(schema, "card")["properties"]
    assert card["type"]["enum"] == gd.CARD_TYPES
    assert card["category"]["enum"] == gd.CARD_CATEGORIES
    assert card["rarity"]["enum"] == [r for r in gd.CARD_RARITIES if r != "Monster"]
    assert card["suffix"]["enum"] == [s for s in gd.CARD_SUFFIXES if s != "Companion"]
    assert card["expansion"]["enum"] == [e for e in gd.CARD_EXPANSIONS
                                         if e not in ("None", "Metaprogress")]
    assert card["keywords"]["items"]["enum"] == sorted(gd.CARD_PROPERTIES)
    assert card["flags"]["items"]["enum"] == gd.CARD_FLAGS
    assert sorted(card["cost"]["properties"]) == sorted(gd.COST_KEYS)

    eff = defs(schema, "effect")["properties"]
    assert eff["trigger"]["enum"] == gd.GAME_TRIGGERS
    assert defs(schema, "talentEffect")["properties"]["trigger"]["enum"] == gd.GAME_TRIGGERS

    cond = defs(schema, "condition")["properties"]
    assert cond["value"]["enum"] == gd.CONDITION_VALUES
    assert cond["op"]["enum"] == gd.CONDITION_OPERATORS

    ench = defs(schema, "enchantment")["properties"]
    assert ench["type"]["enum"] == gd.ENCHANT_TYPES

    for defname in ("weapon", "startingCard"):
        classes = defs(schema, defname)["properties"]["classes"]["items"]["enum"]
        assert classes == sorted(gd.profession_names()) + ["all"]

    # startingCard mirrors the card definition (full category enum, no pinning)
    sc = defs(schema, "startingCard")["properties"]
    assert sc["category"]["enum"] == gd.CARD_CATEGORIES
    assert defs(schema, "startingCard")["required"] == \
        defs(schema, "card")["required"] + ["classes"]

    for field in ("cardID",):
        assert card[field]["minimum"] == gd.MOD_ID_RANGE[0]
        assert card[field]["maximum"] == gd.MOD_ID_RANGE[1]


def test_canonical_typos_documented(schema):
    card = defs(schema, "card")["properties"]
    assert "Phyisical" in card["suffix"]["enum"]
    assert "sic" in card["suffix"]["description"]
    assert "CardRariry" in card["rarity"]["description"]
    assert "sic" in card["rarity"]["description"]


# ------------------------------------------------------------------- events

def events_manifest() -> dict:
    return {
        "$schema": "../../../schemas/pack.schema.json",
        "schemaVersion": 2,
        "pack": "Zz Events",
        "events": [{"name": "Hello Wayfarer",
                    "storyFile": "events/HelloWayfarer.ink.json",
                    "minLevel": 0, "maxLevel": 0, "unique": False}],
    }


def test_events_manifest_passes(validator):
    assert errors(validator, events_manifest()) == []


def test_events_require_schema_version_2(validator):
    m = events_manifest()
    del m["schemaVersion"]
    assert errors(validator, m)
    m["schemaVersion"] = 1
    assert errors(validator, m)


def test_events_only_pack_needs_no_idblock(validator):
    m = events_manifest()
    assert "idBlock" not in m and errors(validator, m) == []


def test_cards_still_require_idblock(validator):
    m = example_manifest()
    del m["idBlock"]
    assert errors(validator, m)


def test_event_entry_shape(validator):
    m = events_manifest()
    del m["events"][0]["storyFile"]
    assert errors(validator, m)
    m = events_manifest()
    m["events"][0]["bogusField"] = 1
    assert errors(validator, m)
    m = events_manifest()
    m["events"][0]["minLevel"] = -1
    assert errors(validator, m)
