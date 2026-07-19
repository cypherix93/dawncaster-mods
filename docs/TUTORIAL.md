# Your first card in 15 minutes

The data path, start to finish: scaffold a pack, reshape its starter card into
a real one, run the offline gates, and install it — **no programming, no
compiler**. A pack is one `pack.json` file (plus optional art); the DawnKit
engine does everything else at game boot.

Every command below was run while writing this page and the outputs shown are
real, with one exception: the **install-to-game step (§6) is described from
the specs and loader source, not re-verified on the day this was written** —
it is the one step that needs the game running. Everything up to it is fully
offline.

## 0. One-time setup (~5 minutes, once ever)

1. **Dawncaster** installed (Steam).
2. **BepInEx 5.4.23.2 + the DawnKit DLLs** installed into the game —
   follow [`../DC.DawnKit/src/README.md`](../DC.DawnKit/src/README.md)
   (Prerequisites → Build → Install: three copy commands).
3. **This repo + Python 3.10+** for the authoring toolchain:

   ```powershell
   cd D:\src\mods\dawncaster-mods
   pip install --user -r tools\requirements.txt
   ```

4. **Extract the game's content pool** (once, and again after game updates) —
   the validator checks your card against the 2,525 shipped cards, so it needs
   them extracted (reads the game folder read-only, writes `tools\out\`):

   ```powershell
   python tools\extract_data.py
   ```

Sanity check — this prints the toolchain map (worth reading once):

```powershell
python tools\dmk.py -h
```

## 1. Scaffold your pack

Pick a pack name and put your (nick)name as author — the pair matters, it
derives your permanent ID block:

```powershell
python tools\dmk.py new "Frost Reverie" --author "Alex"
```

Output (your numbers will differ — they are hashed from *your* name pair):

```
AutoId block: [767140400, 767140499]
  formula:  700,000,000 + (FNV1a32(owner) % 1,000,000) x 100   (SPEC.md §4.3, ...)
  owner:    'alex/frost reverie'  (lower(author + '/' + pack))
  ...
created D:\src\mods\dawncaster-mods\DC.FrostReverie
  pack.json
  art-recipes.json
  DESIGN-NOTES.md
  art/

self-check (tools/validate_pack.py):
== ...\DC.FrostReverie\pack.json — 0 error(s), 0 warning(s)
```

You now have a **complete, valid pack** containing one placeholder card
("Frost Reverie Strike": 1 energy, 4 damage — deliberately boring). The
100-ID block is yours: it's a hash of `author/packname`, so two mods only
share a block if they share both names — and if that ever happens, the engine
refuses at boot with both owners named instead of corrupting anything.

## 2. Open `pack.json` in your editor

Open `DC.FrostReverie\pack.json` in VS Code (or any schema-aware editor). The
first line — `"$schema": "../schemas/pack.schema.json"` — gives you
autocomplete for every field, the legal values for every enum, docs on hover,
and red squiggles for mistakes *while you type*.

One warning you'll meet immediately: **the game's internal spellings are law,
typos included.** The rarity enum is literally called `CardRariry` in the
game's code, and the physical suffix is spelled `Phyisical`. The schema
autocompletes the correct-wrong spellings for you; never "fix" them by hand.

## 3. Make the card yours

We'll turn the starter into **Glacial Lance** — an INT mage card that deals
ice damage and Freezes. This exact diff was validated and simmed while
writing this page:

```diff
     {
-      "name": "Frost Reverie Strike",
+      "name": "Glacial Lance",
       "cardID": 767140400,
       "expansion": "Extended",
-      "type": "Melee",
+      "type": "Magic",
       "category": "Action",
-      "suffix": "None",
+      "suffix": "Frost",
       "rarity": "Common",
-      "cost": { "STR": 1 },
-      "description": "Deal 4 damage.",
+      "cost": { "INT": 1 },
+      "description": "Deal 3 ice damage. Inflict 2 Frozen.",
       "keywords": [],
       "cardKeywords": [],
       "utilityNumber": "",
       "charges": 0,
       "flags": [],
       "playConditions": [],
       "effects": [
         {
           "trigger": "PlayAction",
-          "codeLine": "damage:4",
+          "codeLine": "damage:3:ice",
           "forecast": true,
           "referenceStatus": null,
           "referenceCards": [],
           "hideReferenceCards": false,
           "conditions": []
+        },
+        {
+          "trigger": "PlayAction",
+          "codeLine": "inflict:2",
+          "forecast": true,
+          "referenceStatus": "Frozen",
+          "referenceCards": [],
+          "hideReferenceCards": false,
+          "conditions": []
         }
       ],
       "enchantment": null,
       "art": "",
       "meta": {
-        "archetype": "starter scaffold (replace me)",
-        "nearestExisting": "Bash.json",
-        ...
+        "archetype": "frost tempo (tutorial card)",
+        "nearestExisting": "Frost_Arrow.json",
+        "whyDifferent": "Tutorial card - mirrors Frost Arrow on the INT color
+         to learn the idioms. A real pack card must introduce a NEW decision
+         here (CARD-PACK-SPEC §5) or it gets cut in review.",
+        "intendedDeck": "Tutorial only."
       }
     }
```

### The three ideas that make a card work

**`codeLine` is what the card does** — a tiny command language:
`command:argument:argument`, chained with `;`. Some vocabulary, straight from
shipped cards:

| codeLine | meaning |
|---|---|
| `damage:3:ice` | deal 3 damage, typed ice (type changes resist/synergy interactions) |
| `damage:5; draw:1` | two things in one effect: 5 damage, then draw a card |
| `inflict:[[lastDamage]]` | inflict stacks equal to the damage just dealt (Frostbolt does exactly this — `[[...]]` tokens are live values) |

Every command must exist in the game — the full list is
[`research/reference/effect-commands.txt`](research/reference/effect-commands.txt)
(565 commands). Invent one and the validator rejects it with a
"did you mean …?" hint.

**`trigger` is when it happens.** `PlayAction` = when you play the card.
Anything else makes the card *reactive*: e.g. `"trigger": "StartCombat"`
fires when combat starts, `"Draw"` when it's drawn. (Reactive cards have zone
subtleties — copy the trigger from a shipped card that behaves like yours;
[GAME-MECHANICS](research/GAME-MECHANICS.md) Part VI has the map.)

**Statuses go through `referenceStatus`.** To apply Frozen, the code is
`inflict:2` (just a number!) and the *status name* rides in
`"referenceStatus": "Frozen"` — that's how 441 shipped cards do it. Writing
`Frozen:2` as a command instead is a trap (exactly one card in the whole game
does that; the validator warns you off it). `bless:X` works the same for
buffs on yourself. The 49 legal status names autocomplete from the schema.

## 4. Gate 1 — validate

```powershell
python tools\dmk.py validate DC.FrostReverie\pack.json
```

```
== DC.FrostReverie\pack.json — 0 error(s), 0 warning(s)
```

Errors point at the exact card and field, with suggestions
(`command 'dmg' not in effect-commands.txt (did you mean 'damage'?)`). Fix
until 0 errors — everything it checks (spellings, ID/name collisions vs every
shipped card, status names, art) would otherwise surface as a bug in-game.

## 5. Gate 3 — the balance sim

```powershell
python tools\dmk.py sim DC.FrostReverie\pack.json
```

```
== Frost Reverie: 1 cards -> DC.FrostReverie\BALANCE-REPORT.md
  ON-CURVE         Glacial Lance (sim 100% power=6.9 band=P25-P50)
```

`ON-CURVE` = your card's simulated power sits inside the band the game's own
commons occupy. `OVER`/`UNDER` = numbers off the curve (the report says by
how much); `DEGENERATE-RISK` = an infinite-ish loop got sniffed; 
`INSUFFICIENT-SIM` = the card does things the sim can't model — not
necessarily bad, it just moves the burden to in-game testing. Details land in
`DC.FrostReverie\BALANCE-REPORT.md`.

(There's also gate 2 — `dmk artcheck` — once you give cards real art. With
`"art": ""` the engine renders a generated placeholder, which is fine for
testing. One command runs all gates on a pack: `python tools\dmk.py all
DC.FrostReverie`.)

## 6. Install into the game

> **Honesty note:** this section is written from the loader's spec and source
> and the pack READMEs — it was **not re-verified in a live game session on
> the day this tutorial was written**. If reality disagrees, the pack READMEs
> and `DC.DawnKit/src/README.md` win; please report the mismatch.

The pack loader scans `BepInEx\plugins\DawncasterPacks\` — every subfolder
with a `pack.json` is a pack. So installing is one copy:

```powershell
Copy-Item -Recurse DC.FrostReverie "E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\plugins\DawncasterPacks\DC.FrostReverie"
```

(Alternative for active development: set `Packs.PacksPath` in
`BepInEx\config\dcmods.dawnkit.packs.cfg` to this repo's root — then your
checkout IS the install, no copying. That's how this repo's four packs run.)

## 7. What you should see

- **Boot log** (`BepInEx\LogOutput.log`):
  `[DawnKit.Packs] Frost Reverie: registered 1 cards, 0 weapons, 0 weapon
  powers (applied at asset load)`, followed by the engine's boot report
  counting your mod. Errors, if any, name your pack and card — no silent
  failures.
- **In-game**: the run-settings **card-sets screen** shows a new toggleable
  row named "Frost Reverie" (your pack is its own card set, with the eye icon
  previewing its cards). Start a run with it enabled and Glacial Lance is in
  the INT reward pool; with `"art": ""` it wears a generated cost-colored
  placeholder. The Codex lists it under your set.
- **Uninstalling** degrades safely: delete the folder and saves keep working
  (mid-run copies of your card vanish on the next reload; the engine's
  degradation rules are part of its spec, not your problem).

## 8. Where to go next

- **Design a real pack** — [METHODOLOGY](design/METHODOLOGY.md) is the design
  loop and the four review bars your cards will be judged against (with
  [CORPUS-STATS](design/CORPUS-STATS.md) as the budget anchor);
  [GAME-MECHANICS](research/GAME-MECHANICS.md) is the mechanics bible (timing
  rules, the archetype synergy web, number grammar);
  [CARD-PACK-SPEC](../DC.DawnKit/CARD-PACK-SPEC.md) §4–5 anchors the budget
  and uniqueness rules on the contract side. Write your rationale into
  `DESIGN-NOTES.md` as you go (the scaffold has the skeleton).
- **Weapons, weapon powers and starting cards** — same manifest, three more
  arrays (`weapons`, `weaponPowers`, `startingCards` — the full character-
  creation loadout): [WEAPON-SPEC](../DC.DawnKit/WEAPON-SPEC.md).
- **Real card art** — recipes that mutate the game's own art into new,
  clearly-derived pieces, built locally and never redistributed:
  [ART-MUTATION-SPEC](../DC.DawnKit/ART-MUTATION-SPEC.md), then `dmk art`,
  `dmk artcheck`, `dmk sheet`.
- **Prefer C#?** The same card is ~20 lines against the typed builder API
  (correct spellings included): [DC.DawnKit/API.md](../DC.DawnKit/API.md) and
  the [MyFirstMod example](../DC.DawnKit/examples/MyFirstMod/README.md).
