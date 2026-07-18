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
