"""Shared ground-truth tables + extracted-pool loaders for the pack validator and sim.

Enum tables are HARDCODED copies of the decompiled enums (decompiled/Card.cs,
EventHandler.cs, ConditionChecker.cs, AssetManager.cs) — they are stable API surface,
including the canonical typos (`CardRariry`, `Phyisical`). Do not "fix" spellings.

Pool loaders read `tools/out/data/**` (produced by tools/extract_data.py) and
`docs/research/reference/effect-commands.txt`. Everything is read-only and cached
per process.
"""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
REPO_DIR = TOOLS_DIR.parent
DATA_DIR = TOOLS_DIR / "out" / "data"
DATA_INDEX = TOOLS_DIR / "out" / "data-index.json"
REFERENCE_DIR = REPO_DIR / "docs" / "research" / "reference"
EFFECT_COMMANDS_FILE = REFERENCE_DIR / "effect-commands.txt"
TALENT_COMMANDS_FILE = REFERENCE_DIR / "talent-commands.txt"
DIALOGUE_COMMANDS_FILE = REFERENCE_DIR / "dialogue-action-commands.txt"
# Content packages live at the repo root as DC.<Name>/ dirs; a dir is a content
# pack iff it contains pack.json (DC.DawnKit has none — engine, not content).
PACKS_DIR = REPO_DIR

# --------------------------------------------------------------------------- enums

# EventHandler.cs — declaration order matters (asset ints index into this).
GAME_TRIGGERS = [
    "None", "PlayAction", "Pause", "GainEnergy", "SpendEnergy", "DealtDamage",
    "CalculateIncomingDamage", "GainHealth", "GainStatusStack", "LoseStatusStack",
    "Shuffle", "Kill", "Draw", "Bury", "Discard", "Remove", "Conjure", "Return",
    "StartCombat", "StartPhase", "EnergyPhase", "DrawPhase", "PlayPhase",
    "DiscardPhase", "EndTurnPhase", "EnemyPhase", "VictoryPhase", "NoCombat",
    "GainGold", "GainFood", "GainRestingHealing", "RollTreasure", "CalculateGoldCost",
    "CalculateOutgoingDamage", "CalculateCardCost", "ResolveDamage", "AcquireCard",
    "RemoveCard", "ActivateWeapon", "EnemyPlaysAction", "GainUniqueStatus",
    "EncounterOpportunity", "UpgradeAction", "ImbueEnchantment", "EndAction",
    "Rewards", "EnterCombat", "VisitCampsite", "Death", "PickEvent", "CriticalHit",
    "TakeStatusDamage", "AfterTakenDamage", "EncounterShrine", "Reshuffle", "Shout",
    "CompanionPlays", "EndRun", "FinalVictory", "FinalDefeat", "Highscore",
    "ReachCanto", "Perform", "Playerdeath", "Shapechange", "Gainsouls", "Deepwound",
    "FinishPerformance", "CardReward", "EndDamage", "AfterHealing", "EnemyEndsAction",
    "Reveal", "SpendCharge", "InflictStatus", "InflictUniqueStatus", "InflictDamage",
    "QueueAction", "InflictStatusDamage", "ResolveDamageTaken", "SummonClone",
    "ChangeTide", "AttuneTrigger", "ShuffleCard", "EnterStance", "GainEnchantment",
    "IncomingDamage", "LoseGold", "AddCardToEnemyDeck", "Interrupt", "ResetCard",
    "PostAction", "StartGame", "BelowZeroHealth", "UpdateAreaSpecial", "Defile",
    "Foretell", "ChangeMaxHealth", "LoseStatus", "GainAffliction", "MutateCard",
    "Reaping", "EnemyGainsStatus", "PlayerBelowZeroHealth", "GainFaith", "FinishHymn",
    "Adapt", "ScavengeAdd", "ScavengeDraw", "Scavenge", "AddCardToScrap",
    "AddCompanionCard", "ActivateEquipment",
]

# Card.cs
CARD_TYPES = ["Melee", "Magic", "Ranged", "Utility", "Divine", "Move", "Corruption", "Monster"]
CARD_CATEGORIES = [
    "Action", "Item", "Artifact", "Conjuration", "Enchantment", "BasicAttack",
    "Summon", "Performance", "Form", "Hymn", "Revelation", "Affix", "Attunement",
    "Equipment", "Code", "Ingredient", "Path", "Location", "Offering", "Mantra",
    "Adaptation",
]
CARD_RARITIES = ["Common", "Uncommon", "Rare", "Legendary", "Monster", "Epic", "Mythic", "Ultra"]
CARD_SUFFIXES = ["None", "Fire", "Frost", "Lightning", "Phyisical", "Ebb", "Flow",
                 "Infernal", "Companion", "Junk"]
# AssetManager.cs
CARD_EXPANSIONS = ["None", "Core", "Metaprogress", "Metamorphosis", "Extended",
                   "Infinitum", "Catalyst", "Eclypse", "Synthesis"]

# Card.cs CardProperties — explicit, non-contiguous values (name -> int).
CARD_PROPERTIES = {
    "Chain": 1, "Cursed": 2, "Conjured": 3, "Defiled": 4, "Echo": 6, "Figmented": 9,
    "Firecast": 11, "Lifedrain": 13, "Untempered": 14, "Grounded": 16, "Heavy": 21,
    "Memorized": 26, "OneUse": 31, "Piercing": 33, "Psionic": 34, "Persistent": 36,
    "Reliable": 37, "Recall": 38, "Reaping": 39, "Rampage": 40, "Unique": 41,
    "Scrap": 42, "Sinister": 43, "Unplayable": 46, "Valuable": 51,
}
CARD_PROPERTIES_BY_INT = {v: k for k, v in CARD_PROPERTIES.items()}

# ConditionChecker.cs
CONDITION_OPERATORS = ["IsLessThan", "IsMoreThan", "Is", "IsNot", "IsHigherPercent",
                       "IsLowerPercent", "IsChance"]

CONDITION_VALUES = [
    "None", "MyHealth", "OtherHealth", "ActionType", "ActionCategory",
    "StatusOnSelf", "StatusOnOpponent", "TypeOfEnemyCardInHand", "Chance", "IsBoss",
    "CardsInDeckTotal", "CardsInGraveyard", "CardsBurned", "CardsInDeckRemaining",
    "CardsPlayedThisTurn", "CategoryPlayedThisTurn", "CategoryPlayedLastTurn",
    "TypePlayedThisTurn", "TypePlayedLastTurn", "CategoryPlayedByEnemy",
    "TypePlayedByEnemy", "CardsPlayedLastTurn", "CardsPlayedThisCombat",
    "HealthGainedThisTurn", "DamageTakenThisTurn", "AnyValue", "Ambush",
    "StatusType", "CurrentCardIsConjured", "StatusCategory", "myDeckContains",
    "otherDeckContains", "CurrentPhase", "EnchantmentStacks", "EnemyTurn",
    "EnergyType", "EnemyType", "Flanking", "IsEven", "IsOdd", "LastChanceSuccess",
    "EnchantmentOnOther", "Frenzy", "Bloodlust", "HasPreviousCombat", "Continuity",
    "Finale", "Flow", "Cascade", "Shout", "Corrupted", "Performing", "Precision",
    "Unscathed", "EnchantmentOnSelf", "InCombat", "PlayerFirstTurn",
    "LastEnergySpend", "CardContainsKeyword", "Balance", "Scholar", "CardRarity",
    "CardInEnemyDeck", "DamageIsPiercing", "Ascended", "InSunforge", "Synergy",
    "Empower", "Imprints", "CombatDeckContains", "DamageType", "DiscardContains",
    "Suffix", "Attuned", "IsOneUse", "IsUnique", "IsPersistent", "Channeling",
    "DeckContainsNoDuplicates", "DeckContainsOnlyCardsOfRarity",
    "DeckContainsOnlyCardsOfType", "EnchantmentType", "HasTalent",
    "TypeOfLastPlayed", "MalignanceActive", "EnemyName", "IsElite", "TextValue",
    "IsChain", "CardName", "Questflag", "CardColor", "Swift", "HasKeyword",
    "DeckContainsOneCost", "DeckContainsOnlyCardsWithKeyword", "IsSwift",
    "PlayedCardHasKeyword", "EnemyHasObtainableEnchantment", "EffigyActive",
    "AlreadyPlayedThisTurn", "CanBeUpgraded", "TideIsEbb", "EvadedLastTurn",
    "IsImprintedCard", "LastDrawnCardHasKeyword", "IsUpgraded", "TotalCardCost",
    "IsFinalBoss", "AreaState", "Imbued", "DeckContainsOnlySuffix", "Advantage",
    "HasCompanion", "PlayedCardWasPlayedBefore", "ClawEquipped", "CardUpgrades",
    "PermaValue",
]  # 118 members, exact declaration order from ConditionChecker.cs

# Manifest-only surfaces (CARD-PACK-SPEC.md §2)
COST_KEYS = ["DEX", "INT", "STR", "HOLY", "Neutral", "DEXINT", "DEXSTR", "INTSTR", "Life"]
CARD_FLAGS = ["uniqueInHand", "cantbeupgraded", "requireAllConditions", "pauseQueue",
              "excludeFromConjurations", "excludeFromSunforge", "excludeFromRewards",
              "excludeFromCodex", "hideConditionGlow", "resetTempValues"]
ENCHANT_TYPES = ["blessing", "affliction", "other", "stance", "attunement",
                 "performance", "equipment", "mantra"]

MOD_ID_RANGE = (700_000_000, 799_999_999)

# ------------------------------------------------------------------- pool loaders


@lru_cache(maxsize=1)
def effect_commands() -> frozenset[str]:
    """All 565 command spellings from docs/research/reference/effect-commands.txt
    (case-sensitive)."""
    lines = EFFECT_COMMANDS_FILE.read_text(encoding="utf-8").splitlines()
    return frozenset(ln.strip() for ln in lines if ln.strip())


@lru_cache(maxsize=1)
def talent_commands() -> frozenset[str]:
    """The TalentHandler.RunTalentEffect switch labels
    (docs/research/reference/talent-commands.txt).

    Talent codeLines fall through to the SpellEffects DSL for anything else
    (TalentHandler.cs:510-516), so validate talent effects against
    talent_effect_commands(), the union."""
    lines = TALENT_COMMANDS_FILE.read_text(encoding="utf-8").splitlines()
    return frozenset(ln.strip() for ln in lines if ln.strip())


@lru_cache(maxsize=1)
def talent_effect_commands() -> frozenset[str]:
    """Full vocabulary legal in a Talent effect codeLine (talent switch ∪ SpellEffects)."""
    return effect_commands() | talent_commands()


@lru_cache(maxsize=1)
def status_names() -> frozenset[str]:
    """The 49 shipped StatusEffect asset names."""
    return frozenset(p.stem.replace("_", " ") for p in (DATA_DIR / "StatusEffect").glob("*.json"))


@lru_cache(maxsize=1)
def status_assets() -> dict[str, dict]:
    """StatusEffect name -> decoded asset fields (theStatus block)."""
    out = {}
    for p in sorted((DATA_DIR / "StatusEffect").glob("*.json")):
        d = json.loads(p.read_text(encoding="utf-8"))
        out[d["m_Name"]] = d["theStatus"]
    return out


@lru_cache(maxsize=1)
def status_pathid_map() -> dict[int, str]:
    """resources.assets path_id -> StatusEffect name (for referenceStatus PPtrs)."""
    idx = json.loads(DATA_INDEX.read_text(encoding="utf-8"))
    out = {}
    for key, meta in idx.items():
        if key.startswith("StatusEffect/"):
            name = key.split("/", 1)[1].replace("_", " ")
            out[meta["path_id"]] = name
    return out


@lru_cache(maxsize=1)
def card_pathid_map() -> dict[int, str]:
    """resources.assets path_id -> Card m_Name (for referenceCard PPtrs)."""
    idx = json.loads(DATA_INDEX.read_text(encoding="utf-8"))
    out = {}
    for key, meta in idx.items():
        if key.startswith("Card/"):
            out[meta["path_id"]] = key.split("/", 1)[1]
    return out


@lru_cache(maxsize=1)
def pool_cards() -> list[dict]:
    """All extracted Card asset dicts (raw JSON, enum ints)."""
    return [json.loads(p.read_text(encoding="utf-8"))
            for p in sorted((DATA_DIR / "Card").glob("*.json"))]


@lru_cache(maxsize=1)
def pool_card_names_lower() -> frozenset[str]:
    return frozenset(c["m_Name"].lower() for c in pool_cards())


@lru_cache(maxsize=1)
def pool_card_name_keys() -> frozenset[str]:
    """Lowercased display names AND extracted filename stems (underscored)."""
    keys = set()
    for p in (DATA_DIR / "Card").glob("*.json"):
        keys.add(p.stem.lower())
    keys |= pool_card_names_lower()
    return frozenset(keys)


def resolve_pool_reference(ref: str) -> bool:
    """True if `ref` plausibly names a real extracted card. Accepts display names,
    filename stems, a trailing `.json`, and annotated forms like
    `Fireball.json (closest burn payoff); Combust.json (...)` — the FIRST
    reference must resolve."""
    if not isinstance(ref, str) or not ref.strip():
        return False
    first = ref.split(";")[0].split(" (")[0].strip()
    if first.lower().endswith(".json"):
        first = first[:-5]
    keys = pool_card_name_keys()
    return (first.lower() in keys
            or first.replace(" ", "_").lower() in keys
            or first.replace("_", " ").lower() in keys)


@lru_cache(maxsize=1)
def pool_card_ids() -> frozenset[int]:
    return frozenset(c["cardID"] for c in pool_cards())


# ---- talents & professions (WEAPON-SPEC.md) ----


@lru_cache(maxsize=1)
def pool_talents() -> list[dict]:
    """All extracted Talent asset dicts (raw JSON, enum ints)."""
    return [json.loads(p.read_text(encoding="utf-8"))
            for p in sorted((DATA_DIR / "Talent").glob("*.json"))]


@lru_cache(maxsize=1)
def pool_talent_ids() -> frozenset[int]:
    """Talent.ID values of the 383 shipped talents (separate namespace from cardIDs)."""
    return frozenset(t["ID"] for t in pool_talents())


@lru_cache(maxsize=1)
def pool_talent_names_lower() -> frozenset[str]:
    return frozenset(t["m_Name"].lower() for t in pool_talents())


@lru_cache(maxsize=1)
def pool_talent_name_keys() -> frozenset[str]:
    """Lowercased display names AND extracted filename stems (underscored)."""
    keys = {p.stem.lower() for p in (DATA_DIR / "Talent").glob("*.json")}
    keys |= pool_talent_names_lower()
    return frozenset(keys)


def resolve_talent_reference(ref: str) -> bool:
    """True if `ref` plausibly names a real extracted talent (same lenient
    formats as resolve_pool_reference)."""
    if not isinstance(ref, str) or not ref.strip():
        return False
    first = ref.split(";")[0].split(" (")[0].strip()
    if first.lower().endswith(".json"):
        first = first[:-5]
    keys = pool_talent_name_keys()
    return (first.lower() in keys
            or first.replace(" ", "_").lower() in keys
            or first.replace("_", " ").lower() in keys)


@lru_cache(maxsize=1)
def profession_names() -> frozenset[str]:
    """The 7 shipped Profession asset names (Arcanist, Hunter, Knight, Rogue,
    Scion, Seeker, Warrior) — the legal `classes` entries besides "all"."""
    return frozenset(p.stem for p in (DATA_DIR / "Profession").glob("*.json"))


# ---- opportunity events (EVENT-SPEC.md) ----


@lru_cache(maxsize=1)
def dialogue_commands() -> frozenset[str]:
    """The 99 dialogue-action command spellings (DialogueActionHandler.RunActionCode
    switch labels, docs/research/reference/dialogue-action-commands.txt). The game
    lowercases commands before dispatch (DialogueActionHandler.cs:29) — the file is
    lowercase; compare candidate commands lowercased."""
    lines = DIALOGUE_COMMANDS_FILE.read_text(encoding="utf-8").splitlines()
    return frozenset(ln.strip() for ln in lines if ln.strip())


@lru_cache(maxsize=1)
def pool_events() -> list[dict]:
    """All extracted Dialogue asset dicts (raw JSON, PPtr fields)."""
    return [json.loads(p.read_text(encoding="utf-8"))
            for p in sorted((DATA_DIR / "Dialogue").glob("*.json"))]


@lru_cache(maxsize=1)
def pool_event_names_lower() -> frozenset[str]:
    """Shipped Dialogue asset names (the eventLookupCache namespace)."""
    return frozenset(d["m_Name"].lower() for d in pool_events())


@lru_cache(maxsize=1)
def textasset_pathid_map() -> dict[int, str]:
    """resources.assets path_id -> TextAsset stem (for Dialogue.textFile PPtrs)."""
    idx = json.loads(DATA_INDEX.read_text(encoding="utf-8"))
    out = {}
    for key, meta in idx.items():
        if key.startswith("TextAsset/"):
            out[meta["path_id"]] = key.split("/", 1)[1]
    return out


@lru_cache(maxsize=1)
def pool_event_textfile_names_lower() -> frozenset[str]:
    """Names of the TextAssets shipped Dialogues point at — the area-deck
    eventContent / doneEvents namespace (EVENT-SPEC §1: deck entries carry
    textFile.name, not Dialogue.name)."""
    ta = textasset_pathid_map()
    out = set()
    for d in pool_events():
        pid = (d.get("textFile") or {}).get("m_PathID", 0)
        if pid and pid in ta:
            out.add(ta[pid].lower())
    return frozenset(out)


def event_pool_collision(name: str) -> bool:
    """True when `name` collides (case-insensitive; space/underscore variants —
    extracted stems are SAFE_NAME-underscored) with a shipped Dialogue name or a
    shipped dialogue TextAsset name."""
    if not isinstance(name, str):
        return False
    keys = pool_event_names_lower() | pool_event_textfile_names_lower()
    n = name.lower()
    return n in keys or n.replace(" ", "_") in keys or n.replace("_", " ") in keys


def other_pack_manifests(exclude: Path | None = None) -> list[tuple[Path, dict]]:
    """(path, manifest) for every sibling <PACKS_DIR>/*/pack.json except `exclude`."""
    out = []
    if not PACKS_DIR.is_dir():
        return out
    for p in sorted(PACKS_DIR.glob("*/pack.json")):
        if exclude is not None and p.resolve() == Path(exclude).resolve():
            continue
        try:
            out.append((p, json.loads(p.read_text(encoding="utf-8"))))
        except (OSError, json.JSONDecodeError):
            continue  # a broken sibling pack is its own validation problem
    return out


def parse_statements(code_line: str) -> list[str]:
    """Split a codeLine into trimmed `command:arg:...` statements."""
    return [s.strip() for s in code_line.split(";") if s.strip()]


def statement_command(statement: str) -> str:
    return statement.split(":", 1)[0].strip()


# ------------------------------------------------------------------- autoid
# AutoId block formula (DC.DawnKit/SPEC.md §4.3) — the Python twin of the C#
# implementation in DC.DawnKit/src/DawnKit/Core/AutoId.cs. Both sides pin the
# AUTOID_TEST_VECTORS below (tools/tests/test_autoid.py; C# boot self-check):
# the same owner string MUST map to the same block on every machine, forever —
# synthetic set values and save data depend on it.

FNV1A32_OFFSET = 2166136261
FNV1A32_PRIME = 16777619
AUTOID_MOD_RANGE_START = 700_000_000
AUTOID_BLOCK_SIZE = 100


def fnv1a32(text: str) -> int:
    """FNV-1a 32-bit over the UTF-8 bytes of `text`."""
    h = FNV1A32_OFFSET
    for byte in text.encode("utf-8"):
        h ^= byte
        h = (h * FNV1A32_PRIME) & 0xFFFFFFFF
    return h


def autoid_owner_string(author: str, mod_name: str) -> str:
    """Canonical owner string: lower(author + "/" + modName)."""
    return f"{author}/{mod_name}".lower()


def autoid_block(author: str, mod_name: str) -> int:
    """First cardID of the mod's deterministic 100-ID block:
    700,000,000 + (FNV1a32(owner) % 1,000,000) * 100."""
    owner = autoid_owner_string(author, mod_name)
    return AUTOID_MOD_RANGE_START + (fnv1a32(owner) % 1_000_000) * AUTOID_BLOCK_SIZE


def autoid_set_value(block: int) -> int:
    """Synthetic CardExpansions value for a block: 1000 + (block − 700M) / 100."""
    return 1000 + (block - AUTOID_MOD_RANGE_START) // AUTOID_BLOCK_SIZE


# (author, modName, expected fnv1a32(owner), expected block) — shared with the
# C# boot self-check. NEVER change these without changing both sides.
AUTOID_TEST_VECTORS = [
    ("DCMods", "Example", 3497143552, 714355200),
    ("alice", "frostpack", 1346137019, 713701900),
    ("Bob", "VenomWorks", 4193822216, 782221600),
    ("dcmods.example", "My First Mod", 2376734, 737673400),
    ("Zoë", "Æther Deck", 718123484, 712348400),
]


# -------------------------------------------------------------- did-you-mean
# Nearest-match suggestions for validation errors, mirrored by the engine's
# DawnKit.Core.Lifecycle.DidYouMean (same rule: case-insensitive equality,
# then prefix ≥3 chars, then Levenshtein distance ≤2; top 3).

def _bounded_levenshtein(a: str, b: str, max_distance: int = 2) -> int:
    """Levenshtein distance if ≤ max_distance, else -1."""
    if abs(len(a) - len(b)) > max_distance:
        return -1
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        curr = [i]
        for j, cb in enumerate(b, 1):
            curr.append(min(curr[j - 1] + 1, prev[j] + 1, prev[j - 1] + (ca != cb)))
        if min(curr) > max_distance:
            return -1
        prev = curr
    return prev[-1] if prev[-1] <= max_distance else -1


def did_you_mean(word: str, candidates, limit: int = 3) -> list[str]:
    """The closest candidates to `word` (case fix > prefix > Levenshtein ≤2)."""
    if not word:
        return []
    scored = []
    lower = word.lower()
    for cand in candidates:
        if not cand:
            continue
        cl = cand.lower()
        if cl == lower:
            score = 0
        elif (len(lower) >= 3 and cl.startswith(lower)) or (len(cl) >= 3 and lower.startswith(cl)):
            score = 1
        else:
            dist = _bounded_levenshtein(lower, cl)
            if dist < 0:
                continue
            score = 1 + dist
        scored.append((score, cand.lower(), cand))
    scored.sort()
    return [cand for _, _, cand in scored[:limit]]
