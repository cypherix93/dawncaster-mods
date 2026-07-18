# Card ID Registry

Mod range: **700,000,000–799,999,999** (verified collision-free vs shipped pool, max shipped
ID 41,020,045). Blocks of 100. Allocate here BEFORE using; never reuse a freed block.

| Block | Pack | Owner/status |
|---|---|---|
| 700000000–700000099 | EmberweaveGrove (fire/graveyard) | allocated 2026-07-18 |
| 700000100–700000199 | VenomousLegacy (attrition) | allocated 2026-07-18 |
| 700000200–700000299 | Clockwork Cadence (tempo) | allocated 2026-07-18 |
| 700000300–700000399 | CrimsonLedger (sacrifice/artifice) | allocated 2026-07-18 |

Legacy: `SandboxStrike` id 900001 (pre-policy hello-card; migrate into a block when the
pack loader lands).

## Weapons & weapon powers (manifest v1.1, allocated 2026-07-18)

Weapon cardIDs are allocated **top-down from each block's end** (WEAPON-SPEC §3);
weapon-power talentIDs reuse the same block numbers in the **separate Talent namespace**
(a talentID may equal a cardID safely). Allocations are contiguous top-down; the next
free top-down number per block is one below the lowest listed.

| Block | Pack | Weapon cardIDs (top-down) | Power talentIDs (top-down) |
|---|---|---|---|
| …000099↓ | EmberweaveGrove | 700000099 Cinderbough Wand | 700000098 Cindersong Weapon, 700000097 Pyregraft Weapon, 700000096 Hearthguard Weapon |
| …000199↓ | VenomousLegacy | 700000199 Blightpin Stiletto, 700000198 Flaying Hook | 700000197 Festering Weapon, 700000196 Hemorrhaging Weapon |
| …000299↓ | Clockwork Cadence | 700000299 Tickwright's Baton | 700000298 Ratchetwind Weapon, 700000297 Escapement Weapon, 700000296 Flywheel Weapon |
| …000399↓ | CrimsonLedger | 700000399 Bloodprice Falchion | 700000398 Bloodtithe Weapon, 700000397 Soulscribe Weapon, 700000396 Pawnbroker's Weapon |

## Starting cards (manifest v1.2, allocated 2026-07-18)

Starting cards are plain cards in the pack's cardID block; they **continue the same
top-down counter** below the weapons/powers allocations above (WEAPON-SPEC §3). Engine
attachment (`Profession.startingCards`) landed in DawnKit 0.8.0 (`startingCards`
manifest array / `DawnKit.StartingCards` builder).

| Block | Pack | Starting-card cardIDs (top-down) | Next free (top-down) |
|---|---|---|---|
| …000099↓ | EmberweaveGrove | 700000095 Stoke the Coals | 700000094 |
| …000199↓ | VenomousLegacy | 700000195 Twin Fangs, 700000194 First Blood | 700000193 |
| …000299↓ | Clockwork Cadence | 700000295 Calibrated Strike | 700000294 |
| …000399↓ | CrimsonLedger | 700000395 Repossession | 700000394 |
