# Weapon Spec v1 — starting weapons, weapon powers & starting cards

Contract for adding modded **starting weapons**, **activatable weapon abilities**
("weapon powers") and **starting cards** via card packs. Grounded in the decompiled
source; every mechanism below is verified at the cited file/method. Companion to
`CARD-PACK-SPEC.md` (this spec extends the same pack manifest).

## 1. Ground truth — how the shipped system works

**A starting weapon is a `Card`.** It becomes the player's repeating basic attack:
- Character creation offers `Profession.weapons` (List\<Card\>) plus Keystone unlocks of
  `KeystoneType.Weapon` (`CharacterBuilder.LoadWeapons`, CharacterBuilder.cs:915-940 —
  locked keystone weapons render greyed).
- The pick becomes the basic attack in the starting deck
  (`CreateCharacterFunctions.CreateStartingDeck(..., selectedWeapon)`,
  CharacterBuilder.cs:1754) and persists **by card name** (`PlayerData.weaponName`,
  CharacterBuilder.cs:1888; reloaded via `AssetManager.GetCard(playerData.weapon)` with a
  **Longsword fallback** if missing — PlayerHandler.cs:344-359).
- Shipped base weapons are one per class (Knight=Longsword, Rogue=Daggers,
  Arcanist=Forcewand, Hunter=Hatchet, Seeker=Knuckles, Warrior=Warmace; Scion none —
  extracted Profession JSONs). Longsword: `BasicAttack` category, Melee, 1-cost, Common.

**A weapon power is a tier-0 `Talent`.** The "activatable ability" is not on the weapon
card at all:
- Selection pool at character creation = `Profession.talents` + Keystone Talent unlocks
  (`CreateCharacterFunctions.GetRandomWeaponPower`, CreateCharacterFunctions.cs:352-364);
  `SetWeaponPower(player, talent)` = `AddTalent(t)` + `weaponPowerCooldown = t.cooldown`
  (CreateCharacterFunctions.cs:111-115). Persisted by name (`weaponPowerName`).
- **Activation**: the weapon button works only in exploration
  (`AreaHandler.areaState == exploration`; blocked in dialogue/merchant —
  PlayerUIHandler.cs:1600-1645). It finds the player's tier-0 talent
  (`talents.FindLast(x => x.tier == 0)`) and calls `TalentHandler.ActivateWeapon()`
  (TalentHandler.cs:83-91): sets `weaponCooldown = weaponPowerCooldown`, then
  `EventHandler.FindTriggeredEvents(GameTriggers.ActivateWeapon)` runs every talent
  effect with `cardTrigger = ActivateWeapon` (enum int 38 in extracted JSONs).
- **Cooldown**: decrements by 1 after each combat victory with rewards
  (CombatHandler.cs:1069-1075). Modifiable at runtime (talent DSL `resetweaponcooldown`
  TalentHandler.cs:446; dialogue actions `weaponpower`/`resetweaponpower`; permanent
  ±cooldown TalentHandler.cs:1364/1843, floor-clamped to 1).
- **Effect language**: talent codeLines go through `TalentHandler.RunTalentEffect`
  (TalentHandler.cs:107+), which handles a talent-specific command set (e.g. `addtalent`,
  `addnextcard`, `addnextcardfree`, `startstatus`, `addtreasuremap`, `addnextbykeyword`,
  `resetweaponcooldown`, ...) and **falls through to the full SpellEffects DSL for
  everything else** (TalentHandler.cs:510-516). So weapon powers may use the 565-command
  vocabulary plus the talent extras.
- Weapon powers may carry **passive riders** on other triggers alongside the
  ActivateWeapon effect — shipped examples do (66 tier-0 talents in the extracted pool,
  e.g. `Deadly_Weapon`: StartCombat rider + ActivateWeapon payoff).
- UI: `Talent.powerImage` is the ability icon; the weapon *card's* artwork is shown as
  the weapon-power button sprite when the basic attack changes (TalentHandler.cs:900-910).

**A starting card is a plain `Card` — the third slot of the character-creation loadout**
(weapon + weapon power + starting card):
- Selection pool at character creation = `Profession.startingCards` (List\<Card\>,
  Profession.cs:24) plus Keystone unlocks of `KeystoneType.StartingCard`
  (`CharacterBuilder.LoadStartingCards`, CharacterBuilder.cs:1229-1300 — the exact mirror
  of `LoadWeapons`, phase 5 of the creation flow). Each Profession entry is re-resolved
  live via `AssetManager.GetCard(startingCard.cardID) ?? startingCard`
  (CharacterBuilder.cs:1242); locked keystone cards render greyed
  (`SpawnStartingcard(c, active: false)`, :1254-1258). Random characters roll
  `CreateCharacterFunctions.GetRandomStartingCard(p)` (CreateCharacterFunctions.cs:316-332,
  same pool).
- **How the pick enters the deck** — `CreateStartingDeck(p, surges, startingcard, weapon)`
  (CreateCharacterFunctions.cs:251-267) builds: stat surges + weapon ×6 (also set as basic
  attack) + Block ×2 + **the starting card ×1**. One copy, as an ordinary deck card — after
  creation the engine gives it no special combat handling at all.
- **Persistence differs from weapons**: `SetStartingCard(p, c)` records
  `p.startingCardID = c.cardID` (int — CreateCharacterFunctions.cs:117-120; PlayerData.cs:83,
  244; PlayerHandler.cs:49, 343). This is a *record* (history/weekly-challenge surface), not
  a re-lookup path: the card itself round-trips inside `playerDeck` like any other card, so
  there is **no Longsword-style fallback** and none is needed. "Run it back" stores
  `startingCardName` (CharacterBuilder.cs:1889) and degrades to the first available pool
  entry if the name is gone (CharacterBuilder.cs:1982-1986).
- Shipped defaults are one per class (Knight=Bolstered Strike, Rogue=Sneak Attack,
  Arcanist=Shocking Grasp, Hunter=Feral Strikes, Seeker=Mindstrike, Warrior=Backswing;
  Scion none — extracted Profession JSONs → `tools/out/data-index.json` path_ids), all
  exactly 1-cost in class colors. Unlike weapons, starting cards are **normal acquirable
  pool cards**: of the full 63-card shipped starting corpus (6 defaults + 57 distinct
  `KeystoneType.StartingCard` keystone cards in `tools/out/data/Keystone`), 62/63 have
  `canBeAcquired: 1` and `excludeFromRewards: 0` (sole exception: Battleclaw, a
  starts-in-play equipment) — do NOT default-exclude them the way §5 does for weapons.

## 2. Product shape — manifest extension (pack.json v1.1)

Three new optional top-level arrays in the existing pack manifest (`weapons` and
`weaponPowers` landed with v1.1; `startingCards` is the v1.2 addition):

```jsonc
{
  "pack": "CrimsonLedger",
  "idBlock": [700000300, 700000399],
  "cards": [ ... ],

  "weapons": [
    {
      // A weapon IS a card: full card schema from CARD-PACK-SPEC §2 applies.
      // Category MUST be "BasicAttack"; cost/statline per §4 budgets below.
      "name": "Tithing Blade",
      "cardID": 700000390,              // from the pack's IDblock, top-down (see §3)
      "category": "BasicAttack",
      "type": "Melee",
      "rarity": "Common",
      // ... rest of card fields ...
      "classes": ["Knight", "Warrior"]  // NEW: which Professions offer it at char creation
                                        // (exact class asset names; "all" allowed)
    }
  ],

  "weaponPowers": [
    {
      "name": "Bloodletting",           // Talent SO name = display name (EN fallback)
      "talentID": 700000391,            // Talent.ID — SEPARATE id space from cards, see §3
      "description": "Inflict 2 Bleeding on the foe at the start of your next combat.",
      "flavortext": "",
      "cooldown": 3,                    // combats between uses (see §4)
      "keywords": [],
      "effects": [
        { "trigger": "ActivateWeapon", "codeLine": "...", "forecast": false,
          "referenceStatus": "Bleeding", "referenceCards": [], "conditions": [] }
        // optional passive riders on other triggers, like shipped weapon talents
      ],
      "requirements": { "rDEX": 0, "rINT": 0, "rSTR": 0 },  // stat gates (Talent fields)
      "classes": ["Knight", "Warrior"],
      "art": "art/Bloodletting.png",    // powerImage, 512×512 (abilityart-sourced mutation)
      "meta": { ... same review fields as cards ... }
    }
  ],

  "startingCards": [
    {
      // A starting card IS a card: full card schema from CARD-PACK-SPEC §2 applies,
      // unrestricted category/type (shipped corpus: 56 Action / 4 Enchantment / 3
      // Equipment). Budget per §4's starting-card curve. Do NOT set excludeFromRewards
      // (62/63 shipped starting cards are normal reward-pool cards — §1).
      "name": "First Blood",
      "cardID": 700000194,              // pack cardID block, top-down below the
                                        // weapons/powers allocations (see §3)
      "type": "Melee",
      "rarity": "Common",
      "cost": { "STR": 1 },
      // ... rest of card fields ...
      "classes": ["Hunter", "Warrior"]  // which Professions offer it at char creation.
                                        // Engine attachment target: Profession.startingCards
                                        // (Profession.cs:24) — the char-creation UI reads
                                        // that list live (LoadStartingCards,
                                        // CharacterBuilder.cs:1240), same pattern as
                                        // profession.weapons. "all" allowed.
    }
  ]
}
```

**Engine status:** the `startingCards` loader (Profession.startingCards attachment, §5
step 3 analogue) is **pending — a separate engine task implements it**. This section is
the design contract that task builds against; `validate_pack.py` does not yet know the
array either (§7).

## 3. Identity policy

- **Weapon cards** use the pack's existing cardID block, allocated **top-down from the
  block's end** (…390-399) so they never collide with the pack's bottom-up card IDs.
- **Weapon powers are Talents** — `Talent.ID` is a separate lookup space
  (`talentLookupCache`). Verified: shipped talent IDs max out at 6,403,702 with zero at
  ≥700,000,000 → mods reuse the **same 700M+ block numbers** as their card block
  (a talentID and cardID may share a number safely; different namespaces).
- Names must be unique vs all 383 shipped talents (case-insensitive,
  `talentNameLookupCache`) and vs sibling packs.
- **Starting cards** use the pack's cardID block and **continue the same top-down
  allocation**, directly below the pack's weapon cardIDs and power talentIDs (the two
  namespaces share one top-down counter per block purely for bookkeeping — see
  `docs/ID-REGISTRY.md`). Example: a pack whose weapons/powers occupy …399-396 allocates
  its first starting card at …395. Bottom-up card IDs and top-down loadout IDs must never
  meet. Name uniqueness: same bar as cards (2,525 extracted `m_Name`s + sibling packs).

## 4. Power budgets

- **Weapons**: baseline = the shipped class weapons (BasicAttack, ~1-cost, Common;
  repeating every turn). A mod weapon must stay statline-comparable to the 6 shipped
  base weapons **for its classes** — study their extracted JSONs; the weapon's identity
  should come from a *rider or condition*, not a bigger number (same "text, not stats"
  rule as cards). Remember basic attacks restore per the BasicAttack category rules
  (GAME-MECHANICS Part IV) — any on-hit rider repeats every single turn, all run.
- **Weapon powers**: baseline = the 66 shipped tier-0 talents
  (`tools/out/data/Talent/*_Weapon.json` and similar; derive the cooldown-vs-payoff curve
  from them before designing — do not guess). Two shipped patterns to respect:
  ActivateWeapon effects fire in **exploration context** (out of combat — payoffs are
  deck-prep, healing, statuses for *next* combat, economy), and passive riders carry the
  in-combat identity. An always-on rider must be budgeted as a permanent talent, not as
  part of the cooldown payoff.
- **Starting cards**: baseline = the 63-card shipped starting corpus (6 Profession
  defaults + 57 distinct `KeystoneType.StartingCard` keystone cards; derivation in §1).
  The measured curve — cite it when budgeting:
  - **Cost:** 1 total energy on 51/63 (81%); 2-cost 7, 3-cost 3, 0-cost 2. All six
    Profession defaults are exactly 1-cost in class colors. Mod starting cards should be
    1-cost unless the pack has a corpus-anchored reason.
  - **Rarity:** 22 Common / 19 Uncommon / 18 Rare / 4 Legendary. Defaults are 5 C + 1 U —
    Rare is the keystone-unlock build-around tier; mod starting cards are always-available
    (no keystone gate in v1), so hold them to Common/Uncommon.
  - **Type/category:** Utility 40, Melee 10, Magic 5, Divine 5, Corruption 2, Ranged 1;
    Action 56, Enchantment 4, Equipment 3.
  - **Complexity:** 1-3 effect codeLines on 60/63 (median 2) — one clean idea, usually
    one gate.
  - **Shape:** an archetype *seed* — one cheap card that declares the run's identity on
    turn 1 and leans on the weapon (7/63 literally "Make a Basic Attack"; 3 of the 6
    defaults do). A mod starting card should complete the pack's loadout — weapon +
    power + starting card as one coherent turn-1 story — not be a stray good card.
  - Deck math: the pick is **one card in a ~13-card starting deck** (surges + weapon ×6 +
    Block ×2 + it, §1) — it is seen turn 1-2 of nearly every combat early on.
    Reliability, not raw power, is the real budget axis.

## 5. Loader design (implementation contract)

Injection extends the existing phase-1 hook (after `AllClasses()` has populated
`AssetManager.allClasses` — same `SetPlayerAssetsLoaded`/`LoadPlayerAssets` postfixes):

1. Build weapon `Card`s (existing card factory) and weapon-power `Talent`s
   (`ScriptableObject.CreateInstance<Talent>()`: ID, tier **0**, cooldown, description/
   flavortext inline, effectList, requirements, `unique = true`, exclusion flags false,
   `requiredTalents`/`requiredProfessions` empty — class gating via list membership, §5.2).
2. Register: cards → `allCards` (weapons are NOT `playercards` pool material if
   `expansion` handling would add them — weapons enter play via char creation, not
   rewards; set `excludeFromRewards: true` by default in the factory for weapon cards).
   Talents → `AssetManager.allTalents` + `RefreshCaches()`.
3. **Class attachment**: for each target class name, find the live `Profession` in
   `AssetManager.allClasses` and append to `profession.weapons` / `profession.talents`
   (idempotent: check membership first). The character-creation UI reads these lists
   live (`LoadWeapons` reads `activeProfession.weapons`) — **no UI patching required**,
   unlike card sets.
   **Starting cards (v1.2, engine task pending)**: identical pattern — build the `Card`
   via the existing card factory (registered as a *normal* acquirable card, NOT
   reward-excluded — §1 corpus rule), then append to `profession.startingCards`.
   `LoadStartingCards` reads that list live (CharacterBuilder.cs:1240) and re-resolves
   entries through `AssetManager.GetCard(cardID)`, so registration must precede the
   character-creation scene. No UI patching, no persistence work: the pick serializes
   inside `playerDeck` and `startingCardID` is a plain int record (§1).
4. Art: weapon card art via the existing card pipeline; `Talent.powerImage` via the same
   placeholder/mutation loader (source corpus for powers: `abilityart_*` sheets, 512×512
   — see asset-extraction-notes).
5. Re-injection after `ForceReloadAssets` must also re-append to the freshly reloaded
   Profession assets (they are re-fetched by `AllClasses()` — membership checks must key
   on ID/name, not object identity).

## 6. Verified degradation & save safety

- Weapon missing on load (mod removed): engine falls back to Longsword with an error log
  (PlayerHandler.cs:344-359) — run continues. Verified shipped behavior, not ours.
- Weapon power missing: `talents.FindLast(tier == 0)` returns null → activation button
  errors gracefully ("Could not find a Weapon power at tier 0",
  PlayerUIHandler.cs:1636-1640). Mid-run mod removal degrades, doesn't corrupt.
- `LastCharacterConfig.json` stores `weaponName`/`weaponPowerName` strings — stale mod
  names simply fail lookup on the next "run it back".

## 7. Validation gates

0. **v1.2 gap (current state)**: `validate_pack.py` v1.1 does not yet know the
   `startingCards` array — it validates `cards`/`weapons`/`weaponPowers` and ignores
   unknown top-level keys, so packs carrying `startingCards` pass today without any
   checks on that array. The pending engine task extends the validator: full card-schema
   checks + `classes` name validation + top-down ID placement + the §4 starting-card
   budget lint (cost-1/Common-Uncommon defaults), and adds the array to
   `schemas/pack.schema.json` (whose root `additionalProperties: false` would otherwise
   flag it in schema-aware editors).
1. `validate_pack.py` v1.1: schema for the two new arrays; weapon category forced
   `BasicAttack`; talentID collision vs extracted talents + sibling packs; `classes`
   entries must match extracted Profession asset names; talent effect codeLines against
   the same 565-command vocabulary **plus** the talent-specific command list (extract the
   `RunTalentEffect` switch labels into `../docs/research/reference/talent-commands.txt` as part of the
   tooling change).
2. Sim harness: weapon cards simulate like any card; weapon powers are exploration-time →
   mostly `INSUFFICIENT-SIM` by design; static degeneracy checks still apply (e.g.
   `resetweaponcooldown` inside an ActivateWeapon effect = infinite-activation loop —
   hard error).
3. In-game QA: weapon appears in char creation for target classes (locked/unlocked
   render), picked weapon lands in starting deck, power activates in exploration, cooldown
   ticks after combat victories, riders fire, Codex/tooltip text renders.

## 8. Out of scope v1

- New Keystone meta-unlocks for mod weapons (shipped weapons unlock via Keystones; mod
  weapons are always-available to their classes in v1).
- New classes (Profession UI roster is fixed — separate future spec).
- In-combat activatable abilities (the shipped activation surface is exploration-only;
  changing that is engine modification, not content).
