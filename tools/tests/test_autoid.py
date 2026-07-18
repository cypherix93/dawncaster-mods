# -*- coding: utf-8 -*-
"""AutoId reference vectors (DC.DawnKit/SPEC.md §4.3/§8) + did-you-mean rule.

The AUTOID_TEST_VECTORS pinned here are shared verbatim with the C# engine
(DC.DawnKit/src/DawnKit/Core/AutoId.cs, checked at boot). If this test fails
after touching either implementation, the two sides drifted — that breaks
AutoId determinism (synthetic set values and save data depend on stable
blocks), so fix the implementation, never the vectors.
"""

import tools.gamedata as gd


# ------------------------------------------------------------------ vectors

def test_reference_vectors_pin_hash_and_block():
    assert len(gd.AUTOID_TEST_VECTORS) == 5
    for author, mod_name, expected_hash, expected_block in gd.AUTOID_TEST_VECTORS:
        owner = gd.autoid_owner_string(author, mod_name)
        assert gd.fnv1a32(owner) == expected_hash, owner
        assert gd.autoid_block(author, mod_name) == expected_block, owner


def test_fnv1a32_known_values():
    # Classic FNV-1a fixed points: empty string hashes to the offset basis;
    # "a" is a standard published value.
    assert gd.fnv1a32("") == 2166136261
    assert gd.fnv1a32("a") == 0xE40C292C


def test_owner_string_is_lowercased_author_slash_name():
    assert gd.autoid_owner_string("DCMods", "Example") == "dcmods/example"
    # Case variants of the same owner map to the same block (determinism).
    assert gd.autoid_block("DCMods", "Example") == gd.autoid_block("dcmods", "EXAMPLE")


def test_blocks_are_aligned_and_inside_the_mod_range():
    for author, mod_name, _hash, _block in gd.AUTOID_TEST_VECTORS:
        block = gd.autoid_block(author, mod_name)
        assert 700_000_000 <= block <= 799_999_900
        assert block % 100 == 0


def test_set_value_formula():
    # SPEC §4.3: set value = 1000 + (block − 700M) / 100; the explicit DC.*
    # blocks map to the shipped 1000–1003 values.
    assert gd.autoid_set_value(700_000_000) == 1000
    assert gd.autoid_set_value(700_000_300) == 1003
    assert gd.autoid_set_value(gd.autoid_block("DCMods", "Example")) == 144552


# -------------------------------------------------------------- did-you-mean

def test_did_you_mean_case_fix_ranks_first():
    assert gd.did_you_mean("Draw", ["draw", "drain", "burn"])[0] == "draw"


def test_did_you_mean_levenshtein_and_prefix():
    # distance 1
    assert "damage" in gd.did_you_mean("darnage", ["damage", "draw", "heal"])
    # prefix match (input is a prefix of the candidate)
    assert "damageall" in gd.did_you_mean("damagea", ["damageall", "heal"])
    # nothing within distance 2 and no prefix -> no suggestions
    assert gd.did_you_mean("xyzzy", ["damage", "draw", "heal"]) == []


def test_did_you_mean_limits_to_three():
    cands = ["draw", "drab", "dram", "drag", "dray"]
    assert len(gd.did_you_mean("draw", cands)) == 3
