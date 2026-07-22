---
date: 2026-07-20
slug: pack-lineup-qa
stage: gate-4 in-game QA (post-build; METHODOLOGY pipeline complete through gate 3 for all 7 packs)
status: in-progress
---

# Pass the torch: in-game QA + iteration on the new pack lineup

## Next action (start here)

Verify the first modded boot, then triage the user's QA findings. Concretely: read
`E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\LogOutput.log` and confirm
(a) the boot report shows **6 mods** with the three new packs registering their kits
and VenomousLegacy registering **cards only** (no weapons line), and (b) when
`[Sandbox] ModOnlyRewards = true` is set in `BepInEx\config\dcmods.dawnkit.cfg`, the
line `[DawnKit] [Sandbox] ModOnlyRewards ACTIVE: removed N native cards` appears on
run start. Then work the user's QA reports against each pack's **uncertainty
ranking** in its DESIGN-NOTES (those lists ARE the QA order; every entry has a
documented fallback design — apply the fallback rather than inventing a new fix).

## Objective

The user is play-testing the new pack lineup in isolation (`xdctest0` sandbox +
mod-only rewards) and will report gate-4 QA findings to fix/iterate next session.

## Pipeline stage

- **Done:** methodology audit of the 4 original packs (all fixed, gates 1–3 green);
  persona-crew ideation → `docs/design/PACK-LINEUP-PROPOSAL.md`; three new packs
  built through the full pipeline (gates 1–3 green); VenomousLegacy loadout retired
  (cards-only v2.0); DawnKit 0.9.0 `[Sandbox] ModOnlyRewards` knob; sim parser
  fixes; everything committed AND pushed; DLLs deployed.
- **Pending:** gate 4 (in-game QA) for all packs — manual, user-driven; the two
  refits specced in the proposal but NOT built (CrimsonLedger blood-debt,
  EmberweaveGrove pyre-clock); ModOnlyRewards knob never exercised in a live boot.

## State of the world

- **Branch:** `master`   **Worktree:** `D:/src/mods/dawncaster-mods`
- **Committed, not pushed:** none (`git log @{upstream}..HEAD` empty this session;
  HEAD = `9e8bd46 refactor(venomouslegacy): retire the poison loadout`)
- **PR:** no PR — this repo pushes directly to `origin/master`
- **Working tree:** clean (`git status -sb` = `## master...origin/master`, no entries)
- **CI / checks (all run this session, 2026-07-20):** `python -m pytest tools/tests`
  = 245 passed; `python tools/dmk.py validate --all` = 7/7 packs, 0 errors;
  `dmk all` green (gates 1–3) for VenomousLegacy, EmberweaveGrove,
  ClockworkCadence, CrimsonLedger, FinalEncore, PowderAndPatience, Cartomancer.
- **Deployed game state:** packs load live from this repo
  (`PacksPath = D:\src\mods\dawncaster-mods` in `dcmods.dawnkit.packs.cfg` — the
  checkout IS the install); the three DLLs in
  `E:\...\Dawncaster\BepInEx\plugins\` were rebuilt (0.9.0) and copied
  2026-07-20 22:29 (verified by `ls -l` after copy).

## Decisions made this session

- **VenomousLegacy is cards-only (v2.0).** Its whole loadout (Blightpin Stiletto,
  Flaying Hook, Festering/Hemorrhaging Weapon, Twin Fangs, First Blood) retired
  because shipped **Khanjali** (keyword 43 = Sinister, so it self-compounds its own
  poison) owns the poison-weapon lane; two stiletto redesigns (maintenance rider,
  compound stacks) were tried and rejected before retirement. The 12 cards stay —
  they feed Khanjali decks rather than compete. User directed the archetype pivot.
- **Rogue loadout slot → DC.FinalEncore (Perform)**; **Warrior/Hunter →
  DC.PowderAndPatience (fuses)**; **Seeker → DC.Cartomancer (Foretell/Doom)** —
  the three adversarial-round survivors from the persona-crew ideation. Rejected
  alternatives recorded in the proposal: Poison Fed, Reversals-by-arrival,
  Intent Thief/Mine Now, Loaded Dice, Questflag Ledger, shared reshuffle clock;
  **benched with live disagreement** (revivable): Momentum Mint, Stolen Hourglass.
- **Sapper classes = Warrior/Hunter** (narrative argument; user did not override).
- **ClockworkCadence: hold** — no changes this wave ("wind the spring" unseconded).
- Weapon budget anchors to the 6 starting weapons; **uniqueness sweeps all 78**
  basic-keyword weapons (CORPUS-STATS §2, corrected this session).

## Artifacts

- Lineup spec (user-approved direction): `docs/design/PACK-LINEUP-PROPOSAL.md`
- New packs: `DC.FinalEncore/`, `DC.PowderAndPatience/`, `DC.Cartomancer/`
  (each: `pack.json`, `DESIGN-NOTES.md` with uncertainty ranking + Phase-0
  decompile citations, `BALANCE-REPORT.md`, `art-recipes.json`; PNGs in `art/`
  are gitignored — regenerate from recipes per `DC.DawnKit/ART-PIPELINE.md`)
- Corpus correction: `docs/design/CORPUS-STATS.md` §2 (78-weapon corpus)
- Test loop docs: `docs/TUTORIAL.md` §7b (xdctest warp codes, ModOnlyRewards,
  log tracing); knob spec: `DC.DawnKit/SPEC.md` §5
- Knob implementation: `DC.DawnKit/src/DawnKit/Core/RunListProbe.cs`,
  `DC.DawnKit/src/DawnKit/DawnKitPlugin.cs`

## Open findings (undispositioned — the gate-4 QA queue)

None from review tribunals. The open items are the packs' UNVERIFIED QA flags
(each with a documented fallback in its DESIGN-NOTES uncertainty ranking):

- 🟡 [qa] FinalEncore: Dead Man's Encore fires `damage:3` from a graveyard
  container — only `return:this` is shipped from that zone. Suggested fallback:
  Chump Change `return:this` payload (DC.FinalEncore/DESIGN-NOTES.md).
- 🟡 [qa] FinalEncore: The Final Encore is the first mod Performance-category card
  through the loader — must appear in the Song delve and complete at threshold 4.
  Suggested fallback: demote to Rare Action reading `[[cardsInGraveyard]]`.
- 🟡 [qa] FinalEncore: Showstopper Weapon's FinishPerformance tier-0 rider has no
  shipped precedent. Suggested fallback: StartCombat bless rider.
- 🟡 [qa] PowderAndPatience: Chain Reaction's `decharge:1` in enchantment context —
  0 shipped users. Fallback written in DESIGN-NOTES.
- 🟡 [qa] PowderAndPatience: One Last Job `decharge:3` mass-tick incl. same-turn
  Blasting Barrel maturity. Fallback written in DESIGN-NOTES.
- 🟡 [qa] PowderAndPatience: Fuse-Cutter swing-tick negative case — commanded
  `basicattack:N` swings must NOT tick (not hand-played); check charge depletion
  feel in long fights.
- 🟡 [qa] Cartomancer: Stack the Deck ships `foretellenemy` (0 shipped card
  users). Fallback: `foretell:3` own-deck retext.
- 🟡 [qa] Cartomancer: Diviner's Weapon — both halves are unshipped pairings
  (startstatus+Doom opener; Foretell trigger as talent rider). Fallback:
  Warding-shape Barrier opener.
- 🟡 [qa] Cartomancer: Cheat Fate's two modal UIs (PlayAction foretell + EndAction
  placement) — confirm no overlap.
- 🟡 [qa] DawnKit: ModOnlyRewards knob never exercised live — confirm the ACTIVE
  log line and mod-only reward screens on first boot.
- 🟡 [qa] Original packs' standing flags (older, still open): VenomousLegacy
  uncertainty ranking items 1–6; EmberweaveGrove Communion cascade termination;
  ClockworkCadence Unbroken Cadence `addtempkeyword` binding.

## Landmines / blockers

- **DLLs can't be copied while the game runs** — Copy-Item fails with
  "user-mapped section open". Close Dawncaster first (a retry loop was needed
  this session).
- **The repo checkout is the live install** (PacksPath = repo root): any pack.json
  edit is live on next game boot — including uncommitted experiments.
- **Game dir is READ-ONLY by repo rule** except the sanctioned BepInEx install
  paths (`DC.DawnKit/src/README.md` §Install).
- **Art PNGs are gitignored**: a fresh clone fails `dmk artcheck` until art is
  rebuilt from each pack's `art-recipes.json` (ART-PIPELINE).
- **INSUFFICIENT-SIM is expected** for Perform/foretell/enchantment-heavy cards —
  the sim can't model them; don't chase it, gate-4 QA + budget math carry those
  (METHODOLOGY §4.1.5).
- **Retired IDs 700000194–700000199 are permanently reserved** (docs/ID-REGISTRY.md)
  — never reuse, even though the items are gone from the manifest.
- **`Sandbox.InjectSandboxCard` and `[Sandbox] ModOnlyRewards` live in DIFFERENT
  cfg files** (com.dawncastermods.sandbox.cfg vs dcmods.dawnkit.cfg) — the knob
  is in the ENGINE cfg; the config entry materializes on first boot with the
  0.9.0 DLL.
- Two proposal refits (CrimsonLedger blood-debt, Ember pyre-clock) are specced
  but unbuilt — don't mistake the proposal doc for shipped state.
