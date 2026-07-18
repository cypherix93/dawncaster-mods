# Card Pack Spec v1

Contract for designing and shipping new Dawncaster card packs. Grounded in:
`../docs/research/GAME-MECHANICS.md` (mechanics + synergy web — **required reading**),
`../docs/research/GROUND-TRUTH.md` (architecture),
`../docs/research/asset-extraction-notes.md` (real card JSON idioms), and pool
statistics computed from all 2,525 extracted cards (`tools/card_stats.py` →
`tools/out/card-stats.json`).

## 1. Product shape

A **pack** = one JSON manifest + an `art/` folder, loaded at runtime by the
`Dawncaster.Sandbox` plugin (pack-loader spike: consumes this format; until it lands,
packs are validated offline by the sim harness + validators).

```
DC.<PackName>/        # top-level package dir (repo root); dir name has no spaces
  pack.json           # manifest (schema below)
  art/<CardName>.png  # 512×512 RGBA full-bleed (see ART-PIPELINE.md)
  DESIGN-NOTES.md     # per-card rationale: archetype, nearest existing card, why different
```

## 2. Manifest schema

Field names map 1:1 onto the verified `Card` ScriptableObject (`decompiled/Card.cs`);
enum values are the **exact enum member spellings** (including `CardRariry`-typo values
like `Phyisical` — the enums are API surface). References are by asset **name**, resolved
at load via `AssetManager.GetStatus` / `GetCard` (both verified public).

```jsonc
{
  "pack": "PackName",              // unique, PascalCase
  "idBlock": [700000000, 700000099], // reserved cardID block (see §3)
  "cards": [
    {
      "name": "Cinder Tithe",      // ScriptableObject name = display name (EN).
                                   // MUST be unique vs all 2,525 existing names (lowercase-keyed cache).
      "cardID": 700000001,
      "expansion": "Extended",     // CardExpansions member. Use "Extended" for mod packs:
                                   // in-pool, but not Core (Core is the base-game identity).
      "type": "Magic",             // CardType: Melee|Magic|Ranged|Utility|Divine|Move|Corruption|Monster
      "category": "Action",        // CardCategory (Action|Item|Artifact|Conjuration|Enchantment|...)
      "suffix": "None",            // Suffix: None|Fire|Frost|Lightning|Phyisical|Ebb|Flow|Infernal|Companion|Junk
      "rarity": "Uncommon",        // CardRariry: Common|Uncommon|Rare|Legendary|Epic|Mythic|Ultra (never Monster for player cards)
      "cost": { "INT": 1 },        // keys: DEX INT STR HOLY Neutral DEXINT DEXSTR INTSTR Life (omitted = 0)
      "description": "Deal 4 fire damage. If a card was buried this turn, apply 3 Burning.",
      "keywords": [],              // CardProperties members (Chain, OneUse, Piercing, Persistent, ...)
      "cardKeywords": ["firecast"],// free-string tags (lowercase; reuse existing tags where possible)
      "utilityNumber": "",         // string; NumberParser grammar (see GAME-MECHANICS Part VII)
      "charges": 0,
      "flags": [],                 // any of: uniqueInHand, cantbeupgraded, requireAllConditions,
                                   // pauseQueue, excludeFromConjurations, excludeFromSunforge,
                                   // excludeFromRewards, excludeFromCodex, hideConditionGlow, resetTempValues
      "playConditions": [],        // [{ "value": "<ConditionValue>", "op": "<ConditionOperator>", "target": "<string>", "ignoreForDisplay": false }]
      "effects": [
        {
          "trigger": "PlayAction", // GameTriggers member
          "codeLine": "damage:4:fire",
          "forecast": true,
          "referenceStatus": "Burning",   // status asset name or null
          "referenceCards": [],           // card asset names (for conjureref/delveref/etc.)
          "hideReferenceCards": false,
          "conditions": []                // same shape as playConditions
        }
      ],
      "enchantment": null,         // OR { "text": "...", "type": "blessing", "combat": true,
                                   //      "showstacks": true, "effects": [ <effect> ... ] }
      "art": "art/CinderTithe.png",
      "meta": {                    // stripped before load; required for review gates
        "archetype": "burn-graveyard",
        "nearestExisting": "Fireball",
        "whyDifferent": "Burn payoff conditioned on bury, bridging two archetypes that ...",
        "intendedDeck": "2-3 lines of how it plays",
        "powerNotes": "budget math vs baselines"
      }
    }
  ]
}
```

**Idiom rules (verified against shipped cards — see asset-extraction-notes):**
- Apply statuses with `inflict:X` / `bless:X` **+ `referenceStatus`** (441 shipped cards do
  this). Do NOT use the `Poison:X` status-name-command form (1 card in the whole game).
- Multi-statement lines use `;` (`damage:5; draw:1`). Value tokens `[[...]]` per
  GAME-MECHANICS Part VI (e.g. `[[lastDamage]]`, `[[my(status)Burning]]`, `/2` arithmetic).
- Every command used MUST appear in `../docs/research/reference/effect-commands.txt`; every trigger/
  condition/enum value must exist in the decompiled enums. No invented vocabulary.
- All list fields ship non-null; `enchantment: null` becomes an empty `Enchantment` at load.

## 3. Identity & collision policy

- **cardID**: mods own **700,000,000–799,999,999** (verified zero collisions; existing max
  41,020,045). Blocks of 100 per pack, allocated in `../docs/ID-REGISTRY.md`. First blocks:
  - 700000000–700000099 `EmberweaveGrove` (fire/graveyard cluster)
  - 700000100–700000199 `VenomousLegacy` (attrition cluster)
  - 700000200–700000299 `Clockwork Cadence` (tempo cluster)
  - 700000300–700000399 `CrimsonLedger` (sacrifice/artifice cluster)
  - (SandboxStrike id 900001 predates this policy; migrate when the pack loader lands.)
- **Names** must not collide with any existing card name (case-insensitive) — check
  `tools/out/data/Card/` filenames. Loader refuses colliding names/IDs.
- **Synthetic card sets** (amendment, loader ≥ 0.3.0): each pack is surfaced in-game as
  its own card set. The loader assigns every card in the pack a synthetic
  `AssetManager.CardExpansions` value derived deterministically from the pack's ID block:

  ```
  expansion = 1000 + (idBlock.start − 700,000,000) / 100
  ```

  e.g. EmberweaveGrove (block 700000000) → `(CardExpansions)1000`, VenomousLegacy →
  `1001`, Clockwork Cadence → `1002`, CrimsonLedger → `1003`. The value is stable across
  sessions, saves and machines because block allocations in `../docs/ID-REGISTRY.md` are
  permanent. Values 1000+ can never collide with official sets (enum tops out at
  `Synthesis = 8`; the game stores the enum as int everywhere it persists). The
  manifest's `expansion` field remains required as documentation/fallback: it is used
  only when the pack has no valid `idBlock` (or when the emergency config
  `Packs.ExpansionOverride` is set, which disables synthetic sets entirely).

## 4. Power budgets (derived from the live pool)

From `card-stats.json` + single-statement damage-card regression (player pool, n=1,243):

| Total cost | Common | Uncommon | Rare | Legendary |
|---|---|---|---|---|
| 0 | ~2–3 dmg | 3 | — | — |
| 1 | 3–5 (median 4) | 4–5 | ~5 | ~6 |
| 2 | ~8 | — | 5–8 | — |
| 3 | up to 12 | — | 8–15 | — |

Working rules:
- **~4 damage per energy** at common; each rarity step buys roughly +1 damage-equivalent
  or one rider. Pure vanilla is rare in this pool (only ~20 single-statement damage cards
  in 1,243) — **shipped cards earn their slot with text, not stats**. Match that.
- Cost distribution to respect: the pool is 15% cost-0, 57% cost-1, 16% cost-2, 11% cost-3,
  2% cost-4. Packs should skew the same way (this is a low-cost, high-tempo game).
- Rarity mix per pack (mirrors pool 35/22/25/19): ≈ 3–4 C / 2–3 U / 2–3 R / 1 L per 10.
- Conditional effects may exceed baseline by the value of the setup cost (see the
  archetype's enabling condition in GAME-MECHANICS Part VI). Statuses use the verified
  timing rules (Part III) — e.g. Burning never decays: budget it as repeating damage.
- Hard ceilings (engine): damage clamp 999,999; stack cap 9,999; hand cap 10; energy cap 8.

## 5. The uniqueness bar

The game ships 2,525 cards across 21 categories. A pack card earns its slot only if it
introduces a **new decision**, not new numbers. Enforced per card via `meta`:

1. **Nearest-neighbor audit**: before finalizing, grep `tools/out/data/Card/*.json` for
   cards with similar codeLines/keywords; name the closest one in `meta.nearestExisting`.
2. **Difference test** (`meta.whyDifferent`) — at least one of:
   - bridges two archetypes that currently don't talk (check Part VI synergy web),
   - uses a verified trigger/condition combination no shipped card uses,
   - creates a new payoff for an existing setup (or new setup for existing payoff),
   - meaningfully new mode of an existing mechanic (e.g. a payoff that *wants* a status
     on yourself).
3. **Rejection rule**: "existing card X with +1/-1 numbers" → cut. "Strictly better than
   existing X at same cost/rarity" → cut (power creep gate).

## 6. Design constraints (engine-verified)

- Player-pool legality: `expansion != None/Metaprogress`, `rarity != Monster`,
  `suffix != Companion` (ProcessCard filter, AssetManager.cs).
- Frame color derives from cost unless `colorCard` set — cost profile IS color identity;
  keep archetype color identities coherent (GetColor rules: DEX green, INT blue, STR red,
  HOLY gold, hybrids aqua/orange/purple, life/corruption black).
- Respect timing truths from GAME-MECHANICS: energy persists across turns; oldest-orb
  overflow at cap 8; Persistent survives discard phase; Firecast plays on bury; Heavy
  stays in graveyard on reshuffle; poison stacks are duration (flat 1/tick) and eat
  healing 1:1.
- Reactive cards = non-`PlayAction` triggers; they fire from wherever
  `FindTriggeredEvents` scans — confirm the trigger actually scans the zone you expect
  (in-hand vs in-deck vs enchantment) against a shipped card that does the same.
- Localization: inline `description` is the EN text (fallback path verified). No string
  tables needed.

## 7. Validation gates (in order)

1. `tools/validate_pack.py` *(to build — sim-harness track)*: schema shape, enum spellings,
   command vocabulary vs `effect-commands.txt`, ID/name collisions vs extracted pool,
   art file presence/dimensions, cost-color coherence.
2. **Uniqueness audit**: `meta.nearestExisting` populated for every card; reviewer
   spot-checks the claims.
3. **Sim harness** (`tools/sim/`): balance envelopes — flag cards outside §4 budgets and
   degenerate loops (see sim-harness brief).
4. **In-game QA**: pack loaded via plugin; every card played at least once in a test
   combat (RunStaticEffect harness + real run); Codex render check (art, text overflow).

## 8. Archetype clusters (v1 assignments)

Each design agent owns a cluster, reads GAME-MECHANICS Part VI for its archetypes' exact
triggers/conditions/commands, and delivers 10–12 cards spanning both archetypes plus at
least 2 explicit bridge cards. All four packs together should feel like one "wave":
cross-pack synergies welcome but never required.

| Pack | Cluster | Archetypes (Part VI) |
|---|---|---|
| VenomousLegacy | Attrition | poison/affliction stacking; bleed/Deep Wound; Infected angle |
| EmberweaveGrove | Fire & ash | burn/Firecast; graveyard (bury/Reaping/Heavy) |
| Clockwork Cadence | Tempo | chain/combo (CardsPlayedThisTurn); echo/copy; conjuration engines |
| CrimsonLedger | Sacrifice & artifice | blood (costLife/self-damage/souls); enchantment/imbue + equipment charges |

Out of scope v1: performance, tide, companions (each wants class-UI context — v2).
