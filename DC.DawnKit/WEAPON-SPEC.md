# Weapon Spec v1 — starting weapons & activatable weapon powers

Contract for adding modded **starting weapons** and **activatable weapon abilities**
("weapon powers") via card packs. Grounded in the decompiled source; every mechanism
below is verified at the cited file/method. Companion to `CARD-PACK-SPEC.md` (this spec
extends the same pack manifest).

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

## 2. Product shape — manifest extension (pack.json v1.1)

Two new optional top-level arrays in the existing pack manifest:

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
  ]
}
```

## 3. Identity policy

- **Weapon cards** use the pack's existing cardID block, allocated **top-down from the
  block's end** (…390-399) so they never collide with the pack's bottom-up card IDs.
- **Weapon powers are Talents** — `Talent.ID` is a separate lookup space
  (`talentLookupCache`). Verified: shipped talent IDs max out at 6,403,702 with zero at
  ≥700,000,000 → mods reuse the **same 700M+ block numbers** as their card block
  (a talentID and cardID may share a number safely; different namespaces).
- Names must be unique vs all 383 shipped talents (case-insensitive,
  `talentNameLookupCache`) and vs sibling packs.

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
