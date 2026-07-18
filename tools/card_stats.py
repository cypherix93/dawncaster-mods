"""Quick stats over extracted Card JSONs to ground pack-design power budgets.

Emits tools/out/card-stats.json:
- damage-per-total-cost quartiles for simple `damage:N` player cards, by rarity
- codeLine command frequency (top 60)
- keyword/category/rarity distributions for player cards
- cardID ranges in use
"""

from __future__ import annotations

import json
import re
import statistics
from collections import Counter
from pathlib import Path

DATA = Path(__file__).resolve().parent / "out" / "data" / "Card"
OUT = Path(__file__).resolve().parent / "out" / "card-stats.json"

DMG = re.compile(r"^damage:(\d+)$")


def main() -> None:
    cards = []
    for p in sorted(DATA.glob("*.json")):
        with open(p, encoding="utf-8") as f:
            cards.append((p.stem, json.load(f)))

    # player pool ~= cardexpansion != 0(None) and !=2(Metaprogress), rarity != 4(Monster)
    player = [(n, c) for n, c in cards
              if c.get("cardexpansion") not in (0, 2) and c.get("cardRarity") != 4]

    cmd_freq: Counter[str] = Counter()
    for _, c in cards:
        for ce in c.get("CardEffectList") or []:
            for stmt in (ce.get("codeLine") or "").split(";"):
                stmt = stmt.strip()
                if stmt:
                    cmd_freq[stmt.split(":")[0]] += 1

    def total_cost(c: dict) -> int:
        return sum(c.get(k, 0) for k in (
            "costDEX", "costINT", "costSTR", "costNeutral", "costHOLY",
            "costDEXINT", "costDEXSTR", "costINTSTR", "costLife"))

    # simple vanilla damage cards: exactly one effect, codeLine 'damage:N', PlayAction(=1)
    dmg_by_cost_rarity: dict[str, list[int]] = {}
    for n, c in player:
        eff = c.get("CardEffectList") or []
        if len(eff) != 1:
            continue
        m = DMG.match((eff[0].get("codeLine") or "").strip())
        if not m or eff[0].get("cardTrigger") != 1:
            continue
        key = f"cost{total_cost(c)}_r{c.get('cardRarity')}"
        dmg_by_cost_rarity.setdefault(key, []).append(int(m.group(1)))

    dmg_summary = {
        k: {"n": len(v), "min": min(v), "median": statistics.median(v), "max": max(v)}
        for k, v in sorted(dmg_by_cost_rarity.items())
    }

    ids = sorted(c.get("cardID", 0) for _, c in cards)
    out = {
        "total_cards": len(cards),
        "player_pool": len(player),
        "rarity_dist_player": Counter(c.get("cardRarity") for _, c in player),
        "category_dist_player": Counter(c.get("cardCategory") for _, c in player),
        "cost_dist_player": Counter(total_cost(c) for _, c in player),
        "vanilla_damage_by_cost_rarity": dmg_summary,
        "command_freq_top60": cmd_freq.most_common(60),
        "cardID_min": ids[0], "cardID_max": ids[-1],
        "cardID_under_1M": sum(1 for i in ids if i < 1_000_000),
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2, default=str)
    print(json.dumps(out, indent=2, default=str))


if __name__ == "__main__":
    main()
