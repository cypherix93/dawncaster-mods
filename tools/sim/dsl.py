"""Effect-DSL subset: card model, statement parsing, value grammar, simulability.

The interpreter implements ONLY commands whose semantics were verified in
decompiled/SpellEffects.cs (case sites cited inline). Anything else marks the
statement non-simulable — we never guess semantics (honesty rule).

Two card sources are normalized into the same CardModel:
  - extracted pool JSON (enum ints, referenceStatus/referenceCard PPtrs)
  - pack manifest JSON (enum member spellings, references by name)
"""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import gamedata as gd  # noqa: E402

# Statuses the engine implements with verified timing (GAME-MECHANICS Part II/III).
IMPLEMENTED_STATUSES = frozenset({
    "Armor", "Barrier", "Resilience", "Evasion", "Weakness", "Vulnerable",
    "Anger", "Chain", "Zeal", "Frozen", "Poison", "Burning", "Bleeding",
    "Regenerate", "Doom", "Stagger",
})

# Status EffectType taxonomy straight from the extracted assets
# (theStatus.theType: 0=blessing, 1=affliction, 2=other — LastingEffect.EffectType).
AFFLICTION_STATUSES = frozenset(
    n for n, s in gd.status_assets().items() if s.get("theType") == 1)
BLESSING_STATUSES = frozenset(
    n for n, s in gd.status_assets().items() if s.get("theType") == 0)

# Commands the engine interprets (SpellEffects.cs case sites, line refs approximate):
#   damage/piercingdamage (~1532/1555), directdamage (~1730), statusdamage (~1688),
#   draw (~1790), energy (~1853), energyDEX.. (~6415), heal (~1916),
#   inflict (~1949), bless (~1204), bury (~1213), conjure (~1341, token only),
#   addstack (~1139, tracked counter, no payload), adddamage (~1064),
#   multiplydamage (damage-calc step 5).
#   caustic/reaping (~1258, max-HP loss), removestatus (~2263),
#   discard (~1772, random self-discard form), addtempvalue/addpermavalue
#   (~1078, per-card counters read back via [[tempValue]]/[[permaValue]]).
SIMULABLE_COMMANDS = frozenset({
    "damage", "piercingdamage", "directdamage", "statusdamage",
    "draw", "heal", "inflict", "bless", "bury", "conjure",
    "energy", "energyDEX", "energyINT", "energySTR", "energyHOLY",
    "addstack", "adddamage", "multiplydamage",
    "caustic", "reaping", "removestatus", "discard",
    "addtempvalue", "addpermavalue",
}) | IMPLEMENTED_STATUSES  # bare status-name commands (nearly-dead idiom, 1 shipped card)

# Keywords whose combat behavior the engine models. Cards carrying other keywords
# are penalized in simulability (their behavior would silently be wrong otherwise).
SIMULATED_KEYWORDS = frozenset({
    "Chain", "Echo", "OneUse", "Piercing", "Persistent", "Lifedrain", "Reaping",
    "Heavy", "Rampage",
})
# Keywords that are inert under this harness's standardized setup: Firecast/
# Psionic never trigger (nothing buries hand/deck copies of the focus card; no
# Focus stacks exist), Grounded/Cursed/Untempered/Memorized are deck-lifecycle or
# ordering effects, Valuable/Sinister/Unique are counters/economy.
INERT_KEYWORDS = frozenset({"Valuable", "Sinister", "Unique", "Reliable", "Memorized",
                            "Firecast", "Psionic", "Grounded", "Cursed", "Untempered"})

# Value identifiers the engine can resolve inside [[...]] (everything else ->
# non-simulable statement). Token content may itself contain arithmetic
# ([[damageBonus+3]], [[tempValue+1]] — GameTextTranslator resolves identifiers
# before NumberParser runs).
KNOWN_SIMPLE_TOKENS = frozenset({
    "lastDamage", "tempValue", "permaValue", "damageBonus", "cardsInHand",
})


@dataclass
class Statement:
    raw: str
    command: str
    args: list[str]
    simulable: bool
    reason: str = ""


@dataclass
class CondModel:
    value: str    # ConditionValue member name
    op: str       # ConditionOperator member name
    target: str


# Conditions the engine evaluates (ConditionChecker.cs, verified helpers):
#   AnyValue  — targetValue "exprA:exprB" compared with the operator (AnyValue())
#   Chance    — roll 1..100 <= NumberParser(target); operator ignored (CheckChance)
#   Ambush    — no cards played by this side this round yet (CheckAmbush)
#   Frenzy    — damage taken this turn > 0 (Frenzy; life costs/self-damage count)
#   StatusOnSelf/StatusOnOpponent — target "any"/"blessing"/"affliction"/<name>
#                                   with Is/IsNot (CheckEffect)
def condition_simulable(c: CondModel) -> bool:
    if c.value == "AnyValue":
        if c.op not in ("Is", "IsNot", "IsMoreThan", "IsLessThan"):
            return False
        parts = c.target.split(":")
        return len(parts) == 2 and all(expression_simulable(p) for p in parts)
    if c.value == "Chance":
        return expression_simulable(c.target)
    if c.value in ("Ambush", "Frenzy"):
        return c.op in ("Is", "IsNot")
    if c.value in ("StatusOnSelf", "StatusOnOpponent"):
        return (c.op in ("Is", "IsNot")
                and (c.target in ("any", "blessing", "affliction")
                     or c.target.title() in IMPLEMENTED_STATUSES
                     or c.target in IMPLEMENTED_STATUSES))
    return False


@dataclass
class EffectModel:
    trigger: str
    statements: list[Statement]
    reference_status: str | None = None
    has_conditions: bool = False
    conditions: list[CondModel] = field(default_factory=list)  # only when simulable


@dataclass
class CardModel:
    name: str
    card_id: int
    rarity: str
    category: str
    ctype: str
    suffix: str
    cost: int              # total energy cost (all pools except Life)
    life_cost: int
    keywords: set[str] = field(default_factory=set)
    charges: int = 0
    effects: list[EffectModel] = field(default_factory=list)
    has_play_conditions: bool = False
    play_conditions: list[CondModel] = field(default_factory=list)  # only when simulable
    has_enchantment_payload: bool = False
    source: str = "pool"

    def _play_conds_unsupported(self) -> bool:
        return self.has_play_conditions and not self.play_conditions

    # ---- simulability ------------------------------------------------------
    def sim_notes(self) -> list[str]:
        notes = []
        if self._play_conds_unsupported():
            notes.append("playConditions not evaluated")
        if self.has_enchantment_payload:
            notes.append("enchantment payload not interpreted")
        unknown_kw = self.keywords - SIMULATED_KEYWORDS - INERT_KEYWORDS
        if unknown_kw:
            notes.append("keywords not simulated: " + ", ".join(sorted(unknown_kw)))
        for eff in self.effects:
            for st in eff.statements:
                if not st.simulable:
                    notes.append(f"{st.raw!r}: {st.reason}")
        return notes

    def simulability(self) -> float:
        """Fraction of behavioral units the sim actually models (0..1)."""
        units = 0
        ok = 0
        for eff in self.effects:
            for st in eff.statements:
                units += 1
                if st.simulable:
                    ok += 1
        # non-statement behavior the sim can't see also costs a unit each
        penalties = 0
        if self._play_conds_unsupported():
            penalties += 1
        if self.has_enchantment_payload:
            penalties += 1
        penalties += len(self.keywords - SIMULATED_KEYWORDS - INERT_KEYWORDS)
        units += penalties
        if units == 0:
            return 0.0
        return ok / units

    def fully_simulable(self) -> bool:
        return self.simulability() >= 0.999


# ------------------------------------------------------------- value expressions

class ValueContext:
    """Resolution context for [[tokens]] during a sim run."""

    def __init__(self, rng, last_damage=0, my_statuses=None, other_statuses=None,
                 cards_in_hand=0, temp_value=0, perma_value=0):
        self.rng = rng
        self.last_damage = last_damage
        self.my_statuses = my_statuses or {}
        self.other_statuses = other_statuses or {}
        self.cards_in_hand = cards_in_hand
        self.temp_value = temp_value
        self.perma_value = perma_value


def _resolve_identifier(name: str, ctx: ValueContext) -> int | None:
    if name == "lastDamage":
        return ctx.last_damage
    if name == "tempValue":
        return ctx.temp_value
    if name == "permaValue":
        return ctx.perma_value
    if name == "damageBonus":
        return 0  # cards are evaluated unupgraded
    if name == "cardsInHand":
        return ctx.cards_in_hand
    if name.startswith("my(status)"):
        s = name[len("my(status)"):]
        return ctx.my_statuses.get(s, 0) if s in IMPLEMENTED_STATUSES else None
    if name.startswith("other(status)"):
        s = name[len("other(status)"):]
        return ctx.other_statuses.get(s, 0) if s in IMPLEMENTED_STATUSES else None
    return None


def _identifier_known(name: str) -> bool:
    return (name in KNOWN_SIMPLE_TOKENS
            or (name.startswith("my(status)") and name[len("my(status)"):] in IMPLEMENTED_STATUSES)
            or (name.startswith("other(status)") and name[len("other(status)"):] in IMPLEMENTED_STATUSES))


def _substitute_token(content: str, ctx: ValueContext | None) -> str | None:
    """Resolve a [[...]] token's content. Content may be a bare identifier or an
    identifier followed by arithmetic (e.g. `damageBonus+3`). Returns the numeric
    replacement string, or None if unresolvable. ctx=None means static check."""
    # longest matching identifier prefix
    match = None
    for ident in sorted(KNOWN_SIMPLE_TOKENS, key=len, reverse=True):
        if content.startswith(ident):
            match = ident
            break
    if match is None:
        for prefix in ("my(status)", "other(status)"):
            if content.startswith(prefix):
                rest = content[len(prefix):]
                for s in sorted(IMPLEMENTED_STATUSES, key=len, reverse=True):
                    if rest.startswith(s):
                        match = prefix + s
                        break
            if match:
                break
    if match is None:
        return None
    tail = content[len(match):]
    if tail and _parse_arith("0" + tail) is None:  # tail must be pure arithmetic
        return None
    if ctx is None:
        return "0" + tail
    val = _resolve_identifier(match, ctx)
    if val is None:
        return None
    return str(val) + tail


def _substitute_all(expr: str, ctx: ValueContext | None) -> str | None:
    rest = expr
    while "[[" in rest:
        i = rest.index("[[")
        j = rest.find("]]", i)
        if j < 0:
            return None
        rep = _substitute_token(rest[i + 2:j], ctx)
        if rep is None:
            return None
        rest = rest[:i] + rep + rest[j + 2:]
    return rest


def expression_simulable(expr: str) -> bool:
    """Static check: can eval_expression resolve this at run time?"""
    rest = _substitute_all(expr, None)
    return rest is not None and _parse_arith(rest) is not None


def _parse_arith(s: str):
    """Tokenize `a op b op c` / `a%b` int arithmetic; None if unparseable."""
    s = s.strip()
    if not s:
        return None
    if s[0] == "-":  # leading negation (e.g. `-[[tempValue]]` after substitution)
        s = "0" + s
    out = []
    num = ""
    for ch in s:
        if ch.isdigit():
            num += ch
        elif ch in "+-*/%":
            if not num:
                return None
            out.append(int(num))
            out.append(ch)
            num = ""
        elif ch == " ":
            continue
        else:
            return None
    if not num:
        return None
    out.append(int(num))
    return out


def eval_expression(expr: str, ctx: ValueContext) -> int:
    """NumberParser.TotalNumber subset: [[token]] substitution, then left-to-right
    integer + - * /, and a%b = uniform random in [a, b] (NumberParser.cs)."""
    rest = _substitute_all(expr, ctx)
    if rest is None:
        raise ValueError(f"unresolvable token in {expr!r}")
    toks = _parse_arith(rest)
    if toks is None:
        raise ValueError(f"unparseable expression {expr!r}")
    acc = toks[0]
    i = 1
    while i < len(toks):
        op, rhs = toks[i], toks[i + 1]
        if op == "+":
            acc += rhs
        elif op == "-":
            acc -= rhs
        elif op == "*":
            acc *= rhs
        elif op == "/":
            acc = acc // rhs if rhs else 0
        elif op == "%":
            lo, hi = (acc, rhs) if acc <= rhs else (rhs, acc)
            acc = ctx.rng.randint(lo, hi)
        i += 2
    return acc


# ----------------------------------------------------------- statement analysis

_VALID_TARGETS = {"other", "self"}


def analyze_statement(raw: str, reference_status: str | None) -> Statement:
    parts = [p.strip() for p in raw.split(":")]
    cmd = parts[0]
    args = parts[1:]

    def bad(reason: str) -> Statement:
        return Statement(raw, cmd, args, simulable=False, reason=reason)

    if cmd not in SIMULABLE_COMMANDS:
        if cmd in gd.effect_commands():
            return bad("command not in sim subset")
        return bad("unknown command")

    if cmd in IMPLEMENTED_STATUSES:  # bare status-name form: Status:X
        if len(args) != 1 or not expression_simulable(args[0]):
            return bad("status-name command with unsupported args")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("damage", "piercingdamage", "directdamage", "statusdamage"):
        if not args or not expression_simulable(args[0]):
            return bad("damage amount not resolvable")
        if len(args) >= 3 and args[2] not in _VALID_TARGETS:
            return bad(f"damage target {args[2]!r} not modeled")
        if len(args) >= 4 and not expression_simulable(args[3]):
            return bad("attack count not resolvable")
        if len(args) > 4:
            return bad("extra damage args not modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "draw":
        if not args or not expression_simulable(args[0]):
            return bad("draw amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "heal":
        if not args or not expression_simulable(args[0]):
            return bad("heal amount not resolvable")
        if len(args) >= 2 and args[1] not in _VALID_TARGETS:
            return bad(f"heal target {args[1]!r} not modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("inflict", "bless"):
        if reference_status is None:
            return bad(f"{cmd} without referenceStatus")
        if reference_status not in IMPLEMENTED_STATUSES:
            return bad(f"status {reference_status!r} not modeled")
        if not args or not expression_simulable(args[0]):
            return bad("stack amount not resolvable")
        if len(args) >= 2 and not expression_simulable(args[1]):
            return bad("application count not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "bury":
        if not args or not expression_simulable(args[0]):
            return bad("bury count not resolvable")
        if len(args) >= 2 and args[1] != "self":
            return bad("bury target other than self not modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "conjure":
        # conjure:pool:choices:amount — modeled as generic inert-token add ONLY.
        return Statement(raw, cmd, args, simulable=True,
                         reason="token value not modeled (counts as card added)")

    if cmd == "energy":
        if len(args) < 2 or not expression_simulable(args[1]):
            return bad("energy amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("energyDEX", "energyINT", "energySTR", "energyHOLY"):
        if not args or not expression_simulable(args[0]):
            return bad("energy amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "addstack":
        # Tracked counter on the card's own enchantment; payloads are never
        # interpreted, so this is semantically a no-op for scoring.
        if not args or not expression_simulable(args[0]):
            return bad("stack amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("caustic", "reaping"):
        # Reaping(N, targetOther=true) — only the 1-arg enemy-targeting form.
        if len(args) != 1 or not expression_simulable(args[0]):
            return bad("only `caustic:N`/`reaping:N` (enemy max-HP loss) modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "removestatus":
        if not args or args[0] == "ref" or args[0] not in IMPLEMENTED_STATUSES:
            return bad("removestatus of unmodeled status")
        if len(args) >= 2 and args[1] != "all" and not expression_simulable(args[1]):
            return bad("removestatus amount not resolvable")
        if len(args) >= 3 and args[2] not in _VALID_TARGETS:
            return bad(f"removestatus target {args[2]!r} not modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd == "discard":
        # Discard(count, which="random", self=true) — random self-discard only.
        if not args or not expression_simulable(args[0]):
            return bad("discard count not resolvable")
        if len(args) >= 2 and args[1] != "random":
            return bad(f"discard selector {args[1]!r} not modeled")
        if len(args) >= 3 and args[2] != "self":
            return bad("discarding the enemy hand not modeled")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("addtempvalue", "addpermavalue"):
        if not args or not expression_simulable(args[0]):
            return bad("value amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    if cmd in ("adddamage", "multiplydamage"):
        if not args or not expression_simulable(args[0].replace(".", "")):
            return bad("modifier amount not resolvable")
        return Statement(raw, cmd, args, simulable=True)

    return bad("unreachable")  # pragma: no cover


# ----------------------------------------------------------- model constructors

_SCORED_TRIGGERS = {"PlayAction", "None"}


def _effect_model(trigger: str, code_line: str, reference_status: str | None,
                  conditions: list[CondModel]) -> EffectModel:
    conds_ok = all(condition_simulable(c) for c in conditions)
    stmts = []
    for raw in gd.parse_statements(code_line):
        st = analyze_statement(raw, reference_status)
        if st.simulable and trigger not in _SCORED_TRIGGERS:
            st = Statement(st.raw, st.command, st.args, simulable=False,
                           reason=f"trigger {trigger} not simulated (reactive)")
        if st.simulable and trigger == "None" and st.command not in ("adddamage", "multiplydamage"):
            st = Statement(st.raw, st.command, st.args, simulable=False,
                           reason="None-trigger effect outside damage-calc hooks")
        if st.simulable and conditions and not conds_ok:
            st = Statement(st.raw, st.command, st.args, simulable=False,
                           reason="effect conditions not evaluated")
        stmts.append(st)
    return EffectModel(trigger, stmts, reference_status,
                       has_conditions=bool(conditions),
                       conditions=list(conditions) if conds_ok else [])


def _conds_from_pool(raw: list) -> list[CondModel]:
    out = []
    for c in raw or []:
        v = c.get("valueToCheck", 0)
        o = c.get("conditonOperator", 0)  # (sic) — canonical field typo
        value = gd.CONDITION_VALUES[v] if 0 <= v < len(gd.CONDITION_VALUES) else f"#{v}"
        op = gd.CONDITION_OPERATORS[o] if 0 <= o < len(gd.CONDITION_OPERATORS) else f"#{o}"
        out.append(CondModel(value, op, c.get("targetValue", "")))
    return out


def _conds_from_pack(raw: list) -> list[CondModel]:
    return [CondModel(c.get("value", "None"), c.get("op", "Is"), c.get("target", ""))
            for c in raw or []]


def card_from_pool_json(d: dict) -> CardModel:
    """Normalize an extracted Card asset dict (enum ints, PPtr references)."""
    status_map = gd.status_pathid_map()
    cost = sum(d.get("cost" + k, 0) for k in
               ("DEX", "INT", "STR", "HOLY", "Neutral", "DEXINT", "DEXSTR", "INTSTR"))
    keywords = {gd.CARD_PROPERTIES_BY_INT[k] for k in d.get("keywords", [])
                if k in gd.CARD_PROPERTIES_BY_INT}
    effects = []
    for e in d.get("CardEffectList", []):
        trig_i = e.get("cardTrigger", 0)
        trigger = gd.GAME_TRIGGERS[trig_i] if 0 <= trig_i < len(gd.GAME_TRIGGERS) else f"#{trig_i}"
        ref = None
        pptr = e.get("referenceStatus") or {}
        if pptr.get("m_PathID"):
            ref = status_map.get(pptr["m_PathID"])
        effects.append(_effect_model(trigger, e.get("codeLine", ""), ref,
                                     _conds_from_pool(e.get("effectConditions"))))
    ench = d.get("CardEnchantments") or {}
    play_conds = _conds_from_pool(d.get("playConditions"))
    return CardModel(
        name=d["m_Name"],
        card_id=d.get("cardID", 0),
        rarity=gd.CARD_RARITIES[d.get("cardRarity", 0)],
        category=gd.CARD_CATEGORIES[d.get("cardCategory", 0)],
        ctype=gd.CARD_TYPES[d.get("cardType", 0)],
        suffix=gd.CARD_SUFFIXES[d.get("cardSuffix", 0)],
        cost=cost,
        life_cost=d.get("costLife", 0),
        keywords=keywords,
        charges=d.get("charges", 0),
        effects=effects,
        has_play_conditions=bool(play_conds),
        play_conditions=(play_conds if play_conds
                         and all(condition_simulable(c) for c in play_conds) else []),
        has_enchantment_payload=bool(ench.get("CardEffectList")),
        source="pool",
    )


def card_from_pack_json(c: dict) -> CardModel:
    """Normalize a pack-manifest card entry (CARD-PACK-SPEC §2)."""
    cost_d = c.get("cost") or {}
    cost = sum(v for k, v in cost_d.items() if k != "Life" and isinstance(v, int))
    effects = []
    for e in c.get("effects", []):
        effects.append(_effect_model(e.get("trigger", "None"), e.get("codeLine", ""),
                                     e.get("referenceStatus"),
                                     _conds_from_pack(e.get("conditions"))))
    ench = c.get("enchantment")
    play_conds = _conds_from_pack(c.get("playConditions"))
    return CardModel(
        name=c.get("name", "?"),
        card_id=c.get("cardID", 0),
        rarity=c.get("rarity", "Common"),
        category=c.get("category", "Action"),
        ctype=c.get("type", "Melee"),
        suffix=c.get("suffix", "None"),
        cost=cost,
        life_cost=cost_d.get("Life", 0) if isinstance(cost_d, dict) else 0,
        keywords=set(c.get("keywords") or []),
        charges=c.get("charges", 0),
        effects=effects,
        has_play_conditions=bool(play_conds),
        play_conditions=(play_conds if play_conds
                         and all(condition_simulable(c2) for c2 in play_conds) else []),
        has_enchantment_payload=bool(ench and (ench.get("effects"))),
        source="pack",
    )


# --------------------------------------------------------- degeneracy sniffing

_LOOP_COMMANDS = {
    "play", "playcopy", "playlastselected", "playlastdiscarded", "playcardfromdeck",
    "copy", "clone", "repeat", "extraturn", "startturn", "conjurelastplayed",
    "addcopywithupgrades", "duplicatedeck",
}
_SELF_FEEDING = {  # trigger -> commands that re-fire the same trigger
    "Draw": {"draw", "drawtype", "drawhighest", "drawspecific", "drawtarget"},
    "Conjure": {c for c in gd.effect_commands() if c.startswith("conjure")},
    "Discard": {"discard", "forcediscard", "discardfromdeck"},
    "Bury": {"bury", "burydrawn", "burylowest", "buryplayed", "buryselect"},
    "GainEnergy": {"energy", "energyDEX", "energyINT", "energySTR", "energyHOLY", "fillenergy"},
    "PlayAction": set(),  # PlayAction+play handled via _LOOP_COMMANDS
}


def degeneracy_flags(card: CardModel, raw_effects: list[dict] | None = None) -> list[str]:
    """Static sniff for unbounded loops. `raw_effects` = manifest effect dicts
    (used to see commands on cards regardless of simulability)."""
    flags = []
    bounded = bool(card.keywords & {"OneUse", "Conjured"}) or card.charges > 0
    for eff in card.effects:
        cmds = {s.command for s in eff.statements}
        loops = cmds & _LOOP_COMMANDS
        if loops and not bounded:
            flags.append(f"replay/copy command(s) {sorted(loops)} on a card without "
                         "OneUse/Conjured/charges bound")
        feeder = _SELF_FEEDING.get(eff.trigger)
        if feeder:
            selffeed = cmds & feeder
            if selffeed and not eff.has_conditions and not bounded:
                flags.append(f"trigger {eff.trigger} fires command(s) {sorted(selffeed)} "
                             "that re-fire the same trigger (unbounded feedback)")
    return flags
