# Content Design Methodology

How new Dawncaster content gets designed in this repo. **Every agent (or human)
drafting content follows this document** — cards, weapons, weapon powers, starting
cards, and every content type onboarded later. The contracts (schemas, IDs, loaders,
validation) live with the engine in `DC.DawnKit/CARD-PACK-SPEC.md` and
`DC.DawnKit/WEAPON-SPEC.md`; *this* document owns the judgment: what makes a piece of
content worth shipping.

**The core stance.** Dawncaster is a roguelike: every card a pack adds competes for
the same 3 reward slots after every combat, in every run that enables the pack. Added
content therefore has a *cost to the player* before it has any value. A design pays
that cost only by **extending the combo graph** — new decision points, new edges
between archetypes, new payoffs for existing setups — never by inflating the pool with
variations of decisions the game already offers. "Existing card X with different
numbers" is not content; it is dilution.

The methodology is a five-phase loop. Four **bars** — Budget, Uniqueness, Pull-Equity,
Flavor — are pass/fail tests applied in Phase 4; each requires *written evidence* in
the pack's DESIGN-NOTES.md, which reviewers diff against the checklist at the end of
this file. An item that cannot show its evidence is cut, not argued for.

---

## Phase 0 — Load the ground truth

Required reading before designing anything. Do not work from memory of the game or of
other card games — this engine is idiosyncratic and the traps are documented.

| Read | For |
|---|---|
| `docs/research/GAME-MECHANICS.md` | The strategic system. **Part VI (synergy web)** is the design payload; **Part VIII (dead & negative findings)** is the trap list — check it before every mechanical assumption. |
| `docs/research/GROUND-TRUTH.md` | Architecture: the DSL, triggers, conditions, load phases. |
| `docs/design/CORPUS-STATS.md` | The measured shipped-pool curves every budget argument anchors to. |
| The contract for your content type | `DC.DawnKit/CARD-PACK-SPEC.md` (cards), `DC.DawnKit/WEAPON-SPEC.md` (weapons / powers / starting cards). |
| `docs/research/reference/` | The **closed vocabularies**: 565 effect commands, 115 talent commands, 99 dialogue actions. If a command is not in these files, it does not exist. |
| `docs/ID-REGISTRY.md` | Your pack's allocated ID block. Never invent IDs. |
| A shipped pack's `DESIGN-NOTES.md` | The output format you must produce. `DC.VenomousLegacy/DESIGN-NOTES.md` is the reference example. |

## Phase 1 — Pack thesis

A pack is not a bag of cards; it is an argument. Before drafting a single item, write:

1. **Mechanical thesis** — 2–3 *decompile-verified* mechanics facts treated as design
   material, naming the untapped axis they open. Example (VenomousLegacy): "Poison
   ticks when the holder plays a card, flat 1/tick — so tick *frequency*, not stack
   size, is the untapped axis." Each fact must cite GAME-MECHANICS or an asset JSON.
   If the thesis doesn't survive Phase 2's verification, the pack changes, not the
   citation.
2. **Fictional thesis** — one line of fiction the whole pack serves (names, art,
   mechanics). Example: "a legacy of venom: sickness as inheritance, wounds that
   outlive the blade."
3. **Scope** — cluster/archetypes (per CARD-PACK-SPEC §8), color identity (cost
   profile IS frame color — GetColor rules), pack size (see Pull-Equity Bar), ID block
   from `docs/ID-REGISTRY.md`.

## Phase 2 — Corpus research

Claims about the shipped pool must be **scripted-scan verified, never remembered**.
The pool is 2,525 cards; your recollection of it is wrong somewhere.

- **Curves:** pull the relevant curves from CORPUS-STATS. If the game has updated
  since its last-measured date, regenerate first (provenance table in that file).
- **Gap verification:** every "no shipped card does X" claim gets a grep/scan over
  `tools/out/data/` with the result recorded in DESIGN-NOTES (e.g. "0 of 63 starting
  cards reference Poison — scripted scan"). A gap that turns out to be occupied
  usually means the game rejected the idea or solved it differently — investigate
  before filling it.
- **Nearest-neighbor sweep:** for each planned item, find the closest shipped cards by
  codeLine/keyword/trigger before finalizing anything. This feeds the Uniqueness Bar
  and supplies the budget anchors.
- **Idiom precedent:** every DSL construction you plan to use must be found verbatim
  (or structurally) in a *named* shipped asset (e.g. "`forcediscard:heal` verified
  verbatim in Silence.json"). Constructions with no shipped precedent are legal only
  with an explicit **UNVERIFIED** flag (Phase 5).

## Phase 3 — Draft

- **Slot plan first, cards second.** Lay out the pack's rarity mix, cost curve, color
  spread and bridge quota (Pull-Equity Bar) as empty slots, then design into them.
  This prevents the classic failure: eleven good ideas that are all 1-cost Uncommon
  engines.
- **One clean idea per item**, usually one gate — shipped complexity is 1–3 effect
  lines, median 2 (CORPUS-STATS). If an item needs four lines to state its idea, it is
  two items or none.
- **DSL discipline:** commands only from `reference/effect-commands.txt` (talent
  commands for powers); statuses applied via `inflict:X`/`bless:X` + `referenceStatus`
  (the 441-card idiom), never the status-name-command form; exact enum spellings
  including the canonical typos (`CardRariry`, `Phyisical`) — the enums are API
  surface.
- **Reactive cards:** a non-`PlayAction` trigger fires from wherever
  `FindTriggeredEvents` scans — confirm the trigger scans the zone you expect against
  a shipped card that does the same (only `EnterStance`/`AttuneTrigger`/
  `FinishPerformance` scan the graveyard, for instance).
- **Flag every inference.** Anything the decompile doesn't outright prove — trigger
  direction, target-argument semantics, an interaction between two verified rules —
  gets an UNVERIFIED note *at draft time*, not after review catches it.

## Phase 4 — The four bars

Every item passes all four, with evidence, or is cut.

### 4.1 Budget Bar (balance)

1. **Anchor to the corpus, not to intuition.** Each item's budget math names: the
   CORPUS-STATS curve it sits on, and at least one shipped card as precedent (e.g.
   "Precedent: Cleavers — C, 1 energy, 2 dmg + Frenzy-gated 2 dmg + 3 Bleeding").
2. **~4 damage-equivalent per energy at Common; each rarity step buys ~+1 or one
   rider.** Statuses budget by their *verified* timing (Burning never decays → price
   it as repeating damage; Bleeding −1/tick → overstacking is waste).
3. **Conditional effects may exceed baseline by the value of the setup cost** — the
   overage is paid by the enabling condition, and the whiff mode must be priced too
   (a card that is dead without its archetype is a Pull-Equity failure, not a budget
   discount).
4. **Text, not stats.** Identity comes from a rider, condition, or new mode — never
   from a bigger number at the same slot. Pure vanilla stat-cards are ~20 in 1,243;
   don't add to them.
5. **Respect engine ceilings and the sim's limits:** run gate 3; treat
   `INSUFFICIENT-SIM` as "budget math + gate-4 QA carry the burden", never as a pass.
   `DEGENERATE-RISK` (unbounded replay/copy/conjure loops, damage still accelerating
   at the cap) is a hard stop pending redesign.

### 4.2 Uniqueness Bar (new decisions, non-collision)

An item earns its slot only by introducing a **new decision**, not new numbers.

1. **Nearest-neighbor named:** `meta.nearestExisting` filled for every item, found by
   the Phase 2 sweep, spot-checkable by a reviewer.
2. **Difference test** (`meta.whyDifferent`) — at least one of:
   - **Mode 1 — bridge:** connects two archetypes that currently don't talk (check
     the Part VI synergy web for the missing edge);
   - **Mode 2 — unused combination:** a verified trigger/condition/zone combination
     no shipped card uses;
   - **Mode 3 — new payoff/setup:** a new payoff for an existing setup, or a new
     setup for an existing payoff;
   - **Mode 4 — new mode:** a meaningfully new mode of an existing mechanic (e.g. a
     payoff that *wants* an affliction on yourself).
3. **Rejection rules:** "existing card X with ±numbers" → cut. "Strictly better than
   existing X at same cost/rarity" → cut (power creep). "Strictly worse" → cut too
   (pool pollution).
4. **Literal non-collision:** names unique vs all 2,525 shipped names + all sibling
   packs (case-insensitive, scripted); IDs inside the allocated block per the
   registry's bottom-up/top-down rules.
5. **Decision-point non-collision:** if the play pattern the item creates already
   exists under another name ("when you discard, get value" et al.), it collides even
   with different text. The decision the player faces must be new.

### 4.3 Pull-Equity Bar (pool economics)

The roguelike rules. Every card is judged as a *reward pull in a run that didn't plan
for it*, because that is how most players will meet it.

1. **Pull-safe:** every card must have playable value when it is the *only* pack card
   seen all run. `meta.intendedDeck` must name at least one **vanilla** deck/archetype
   that wants it, not only the pack's internal engine. Cards that are dead without two
   other pack cards are cut or redesigned.
2. **Bridge quota:** ≥2 explicit bridge cards per 10–12 (CARD-PACK-SPEC §8), and **at
   least half the pack must read or feed at least one vanilla archetype** (Part VI
   web). Intra-pack-only synergy is capped at the remaining half — a pack must not be
   a disconnected clique in the combo graph.
3. **Pack size cap: 10–12 cards.** Every slot earns its place through the Uniqueness
   Bar; no filler to round out a curve. If only 8 items pass the bars, ship 8.
4. **Graph extension proof:** the pack as a whole must add at least one new *edge*
   between existing archetypes AND at least one new payoff for an existing enabler (or
   new enabler for an existing payoff). State these explicitly in the pack thesis
   section of DESIGN-NOTES.
5. **Commons carry the pack.** Rarity mix per CORPUS-STATS (≈3–4 C / 2–3 U / 2–3 R /
   1 L per 10); the commons — what players will actually see — must be the pack's most
   pull-safe cards. Build-around capstones live at Rare/Legendary where pull frequency
   is low.

### 4.4 Flavor Bar

1. **Names read native:** 2–4 words, evocative-concrete register matching the shipped
   pool ("Festering Wounds", "Cutting Words" — not "Ultra Venom Blast 3"), collision-
   free (scripted), no real-world or other-IP references.
2. **One fiction, every card:** each item's name + art + mechanics serve the pack's
   fictional thesis, and the mechanics *depict the name* (a card called Quarantine
   should isolate/deny, not deal burst damage).
3. **No canon invention:** never name official lore figures/places into new
   relationships or events. Stay in the pool's generic dark-fantasy register; the
   pack's own fiction is self-contained.
4. **Text templating:** card text follows shipped phrasing for the same commands —
   before writing a description, read 2–3 shipped cards using the same
   commands/keywords and match their sentence patterns and keyword casing.
5. **Art identity:** the pack defines a palette/treatment identity in DESIGN-NOTES
   ("sickly and wet; toxin green dominant, bruise-purple shadows" —
   VenomousLegacy) and builds art per `DC.DawnKit/ART-MUTATION-SPEC.md`, passing the
   distinctness gate (`validate_art.py --distinctness`).

## Phase 5 — Gates & evidence

**Gate chain, in order** (docs/CONVENTIONS.md): `dmk validate` (schema/vocabulary/
collisions) → `dmk artcheck --distinctness` → `dmk sim` (envelopes) → in-game QA. Or
`dmk all <pack>`. Nothing ships on a red gate.

**DESIGN-NOTES.md is the evidence artifact.** Required structure (VenomousLegacy is
the reference):

1. Header: cluster, ID block, colors, **pack thesis** (mechanical + fictional +
   graph-extension proof).
2. Slot plan: rarity mix and cost curve vs CORPUS-STATS.
3. Per-item blocks: **Role / Deck (incl. the vanilla deck that wants it) / Budget
   (anchors + math) / Nearest (+ Difference mode) / DSL (verified-in citations +
   UNVERIFIED flags)**.
4. **Honest uncertainty ranking:** every UNVERIFIED item ordered by risk, each with
   its fallback if the sim/QA check fails. This section *drives* gate 4 — QA plays
   these first.
5. Validation-done list: the scripted scans run (name collisions, gap checks,
   command-vocabulary membership) and their results.
6. Art direction: palette/treatment + per-card source recipes rationale.

A reviewer must be able to verify any bar from DESIGN-NOTES alone, without re-deriving
the research.

---

## Per-type addenda

The loop above is written against cards; these addenda override or extend specific
phases for the loadout types. (Contracts: WEAPON-SPEC.)

### Weapons

- Statline-comparable to the 6 shipped class weapons (CORPUS-STATS §2): damage 2,
  1 Neutral, Common. **Identity = exactly one rider or condition.** A stat *sacrifice*
  for archetype fuel is legal and interesting (Blightpin Stiletto: damage 1 + 2
  Poison); a stat surplus is not.
- The rider repeats every turn, all run — Budget Bar prices it as a permanent engine.
- Weapon cards default `excludeFromRewards: true` (they enter play via character
  creation, not pulls) — Pull-Equity rules 1–2 don't apply, but the weapon must still
  pass Uniqueness (its rider should read *archetype state*, not duplicate a shipped
  weapon's position/cost gimmick).

### Weapon powers

- Budget against the 66-talent cooldown ladder (CORPUS-STATS §3) — derive, don't
  guess. cd 2–3 is the shipped mode (79%).
- ActivateWeapon payoffs are **exploration-context** (next-combat prep, healing,
  economy); passive riders carry the in-combat identity and are budgeted as permanent
  talents, never as part of the cooldown payoff.
- Hard degeneracy rule: `resetweaponcooldown` inside an ActivateWeapon effect is an
  infinite-activation loop — validator hard error.

### Starting cards

- Budget against the 63-card corpus (CORPUS-STATS §4): 1-cost (81% mode), Common/
  Uncommon only (Rare is the keystone tier and mods are always-available), 1–3 lines,
  archetype *seed* shape.
- **Reliability, not raw power, is the budget axis** — the card is in a ~13-card deck
  and seen turn 1–2 of nearly every early combat. A conditional whiff mode that's
  acceptable in the reward pool is much more expensive here.
- Starting cards are **normal reward-pool cards** (62/63 shipped) — do NOT
  exclude-from-rewards, and Pull-Equity applies *doubly*: the card must work both as a
  loadout pick and as a mid-run pull for someone else's deck.
- **Loadout coherence rule:** weapon + power + starting card must form one coherent
  turn-1 story, stated in one sentence in DESIGN-NOTES (e.g. Stoke the Coals ignites
  Cinderbough Wand's bury gate and Cindersong's rider). A starting card that is merely
  a good card is a cut.

---

## Onboarding a new content type (the extension recipe)

When a new content type becomes shippable (opportunities/events at M3; monsters,
regions later), its addendum gets written by this recipe — not invented ad hoc:

1. **Ground truth first.** Decompile-verify the runtime path (how the type loads,
   fires, persists, degrades) and land the findings in `docs/research/` with citations
   — the GAME-MECHANICS/GROUND-TRUTH bar of quality.
2. **Derive the corpus curve.** Extract every shipped instance, measure the
   distributions that constrain design (cost/frequency/complexity/whatever the type's
   axes are), and add a section to CORPUS-STATS with provenance.
3. **Identify the uniqueness axes.** What decisions does this type create for the
   player? Which combinations are unused? The Difference-test modes get restated in
   the type's terms.
4. **Define the contract**: spec section in `DC.DawnKit/` (schema, IDs, loader,
   degradation), validator coverage in `tools/validate_pack.py`, and the type's gate
   chain.
5. **Write the addendum here** (which phases change, which bars need type-specific
   rules) and prove it with one worked example pack.

Known head-starts: **opportunities** — vocabulary already extracted
(`reference/dialogue-action-commands.txt`, 99 actions), corpus = Dialogue assets,
seeded RNG stream `shrines_opportunities`, questflags are the cross-event memory
(GAME-MECHANICS Part V). **Monsters/regions** — 170 Battle assets + Area event-deck
model documented in Part V; difficulty-scaling formulas in Parts V/VII; no corpus
curve derived yet.

---

## The checklist

The compressed audit card. Reviewers diff DESIGN-NOTES against this; drafting agents
self-check before requesting review. Every ✗ is a cut or a redesign, not a debate.

**Pack level**
- [ ] Mechanical thesis: 2–3 cited, verified facts naming the untapped axis
- [ ] Fictional thesis: one line; graph-extension proof: ≥1 new edge + ≥1 new payoff/enabler
- [ ] Slot plan vs CORPUS-STATS (rarity mix, cost curve, colors); size ≤ 12
- [ ] ≥2 bridge cards; ≥half the pack reads/feeds a vanilla archetype
- [ ] IDs inside the registered block (bottom-up cards, top-down loadout); names scripted-checked

**Per item**
- [ ] Budget: CORPUS-STATS curve + named shipped precedent + conditional/whiff math
- [ ] Uniqueness: nearestExisting named; Difference mode 1–4 stated; not strictly better/worse
- [ ] Pull-safe: names a vanilla deck that wants it (`meta.intendedDeck`)
- [ ] Flavor: native name; mechanics depict the name; text matches shipped templating
- [ ] DSL: every command in the closed vocabulary; every idiom cited to a shipped asset
- [ ] Every inference flagged UNVERIFIED with a fallback

**Pack exit**
- [ ] Uncertainty ranking present and driving the gate-4 QA list
- [ ] `dmk all <pack>` green: validate → artcheck → sim → in-game QA
- [ ] DESIGN-NOTES complete enough to verify every box above without re-research
