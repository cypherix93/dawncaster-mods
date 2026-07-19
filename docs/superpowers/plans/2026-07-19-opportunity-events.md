# Opportunity Events (EVENT-SPEC E1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship EVENT-SPEC v0.1 milestone E1 — modded Ink-scripted opportunity events via pack.json (`schemaVersion: 2`) and a `DawnKit.Events.Build(...)` C# API, injected into `AssetManager.allEvents` at the world-asset phase and served by a `DialogueManagerINK.StartDialogue` prefix.

**Architecture:** Mirrors the existing content families: declarative durable registration (`Registry.Events`, ledger kind `"event"`, name-keyed — no numeric IDs), injection on the already-patched world-asset hooks (before the resolver's final pass), one NEW Harmony prefix that serves registered stories by name (vanilla names pass through). Offline, the Python validator gains an `events` section (ink version pin, closed 99-command dialogue vocabulary, collisions vs shipped Dialogue names AND their TextAsset names).

**Tech Stack:** C# (net472, BepInEx 5 + HarmonyX, Newtonsoft.Json + Ink-Libraries from the game's Managed dir), Python 3 toolchain (`tools/`, pytest), JSON Schema draft-07.

## Global Constraints

- Game dir `E:\Games\Steam\steamapps\common\Dawncaster` is READ-ONLY (referenced DLLs only).
- Canonical typos are API surface (`CardRariry`, etc.) — never "fix" them.
- Closed vocabulary: every dialogue action command must exist in `docs/research/reference/dialogue-action-commands.txt` (99 commands). Command matching is **case-insensitive** (`DialogueActionHandler.RunActionCode` lowercases at DialogueActionHandler.cs:29 — unlike the case-sensitive effect DSL). `goto` and `STORYFUNCTION` are matched **case-sensitively** upstream (DialogueManagerINK.cs:1291/1295) and are engine-reserved: `goto:<knot>` requires the knot to exist; `STORYFUNCTION` is rejected in v0.1.
- Ink version pin: compiled stories must declare `inkVersion` 18–20 (game runtime `inkVersionCurrent = 20`, minimum 18; inklecate v1.0.0 emits 20, v1.1+ emits 21 = rejected).
- The naming rule: `Dialogue.name == TextAsset.name == event name` (area deck keys by `textFile.name`, `eventLookupCache` by `Dialogue.name`).
- Events have **no numeric IDs** — no `docs/ID-REGISTRY.md` impact. Ledger/RegisterResult kind string: `"event"`.
- `events` in pack.json requires `"schemaVersion": 2`; `SchemaGate.SupportedSchemaVersion` bumps 1 → 2; plugin versions bump 0.8.0 → **0.9.0** (both `DawnKitPlugin.Version` and `PacksPlugin.Version`).
- Fail-safe rule: `[Events] Enabled` config knob (default true); if the `StartDialogue` prefix target or ANY tracked member is missing, the whole Events integration disables itself (no injection — a node whose story can't be served must never reach the map).
- Gates: `python -m pytest tools/tests` green before any toolchain change lands; `dotnet build -c Release DC.DawnKit\src\DawnKit.slnx` green for engine changes; `dmk validate` green on the example pack; in-game QA per EVENT-SPEC §8 is the final gate.
- C# has no unit-test harness in this repo — engine tasks verify by build + boot log + the §8 QA task. Python tasks are strict TDD.
- Commit style (from git log): `feat:` / `fix:` / `docs:` / `docs+tools:` prefixes, imperative subject.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `tools/gamedata.py` | modify | dialogue-command vocabulary loader + shipped Dialogue/TextAsset name loaders |
| `tools/validate_pack.py` | modify | `events` section validation (gate 1) |
| `tools/tests/test_validate_pack.py` | modify | tests for both of the above |
| `schemas/pack.schema.json` | modify | schemaVersion 2 + `events` definition |
| `tools/tests/test_schema.py` | modify | schema tests + ExampleEventPack manifest |
| `DC.DawnKit/examples/ExampleEventPack/` | create | pack.json + HelloWayfarer.ink + HelloWayfarer.ink.json |
| `DC.DawnKit/src/DawnKit/DawnKit.csproj` | modify | embed dialogue vocab; reference Newtonsoft.Json + Ink-Libraries |
| `DC.DawnKit/src/DawnKit/Content/CommandVocabulary.cs` | modify | `DialogueCommands` set |
| `DC.DawnKit/src/DawnKit/Core/Drafts.cs` | modify | `EventDraft`, `ParsedEvent` |
| `DC.DawnKit/src/DawnKit/Core/InkStoryLint.cs` | create | compiled-Ink JSON lint (version pin + action vocabulary) |
| `DC.DawnKit/src/DawnKit/Core/Validator.cs` | modify | `ParseEvent` |
| `DC.DawnKit/src/DawnKit/Core/Registry.cs` | modify | `EventRegistration`, `Events` list, `RegisterEvent` |
| `DC.DawnKit/src/DawnKit/Core/RegistrationLedger.cs` | modify | `FindEventSpaceConflict` |
| `DC.DawnKit/src/DawnKit/Api/Events.cs` | create | public `Events.Build(...)` builder |
| `DC.DawnKit/src/DawnKit/Content/Factories.cs` | modify | `EventFactory` |
| `DC.DawnKit/src/DawnKit/Core/InjectionEngine.cs` | modify | `OnWorldAssetsLoaded` + `InjectEvents` |
| `DC.DawnKit/src/DawnKit/Core/BootReport.cs` | modify | count events in applied/status/diagnostics |
| `DC.DawnKit/src/DawnKit/Integration/DialogueIntegration.cs` | create | `StartDialogue` prefix + tracked members + availability |
| `DC.DawnKit/src/DawnKit/Core/PatchManager.cs` | modify | prefix def + member resolution |
| `DC.DawnKit/src/DawnKit/DawnKitPlugin.cs` | modify | `[Events] Enabled` knob; version 0.9.0 |
| `DC.DawnKit/src/DawnKit.Packs/SchemaGate.cs` | modify | SupportedSchemaVersion 2 + self-check table |
| `DC.DawnKit/src/DawnKit.Packs/PackManifest.cs` | modify | `EventManifest` |
| `DC.DawnKit/src/DawnKit.Packs/PackScanner.cs` | modify | events loop, content check, idBlock warning gate |
| `DC.DawnKit/src/DawnKit.Packs/PacksPlugin.cs` | modify | version 0.9.0 |
| `DC.DawnKit/RELEASE-NOTES.md`, `DC.DawnKit/EVENT-SPEC.md`, `AGENTS.md` | modify | 0.9.0 notes; spec status; routing table |

---

### Task 1: Toolchain — dialogue vocabulary + shipped event-pool loaders (`gamedata.py`)

**Files:**
- Modify: `tools/gamedata.py`
- Test: `tools/tests/test_validate_pack.py`

**Interfaces:**
- Consumes: existing `tools/out/data/Dialogue/*.json` extractions (148 files, PPtr `textFile.m_PathID`), `tools/out/data-index.json` (`TextAsset/<stem>` entries carry `path_id`), `docs/research/reference/dialogue-action-commands.txt` (99 lowercase lines).
- Produces (used by Task 2): `gd.dialogue_commands() -> frozenset[str]`, `gd.pool_events() -> list[dict]`, `gd.pool_event_names_lower() -> frozenset[str]`, `gd.textasset_pathid_map() -> dict[int, str]`, `gd.pool_event_textfile_names_lower() -> frozenset[str]`, `gd.event_pool_collision(name: str) -> bool`.

No extractor change needed: the dialogue-textfile index derives from the existing extraction (same idiom as `status_pathid_map`).

- [ ] **Step 1: Write the failing tests** — append to `tools/tests/test_validate_pack.py` after `test_talent_and_profession_loaders`:

```python
def test_dialogue_loaders():
    # 99 dialogue-action commands (DialogueActionHandler.RunActionCode switch)
    assert len(gd.dialogue_commands()) == 99
    assert {"gold", "addcard", "combat"} <= gd.dialogue_commands()
    # 148 shipped Dialogue SOs; both name namespaces are scanned
    assert len(gd.pool_events()) == 148
    assert "mimic" in gd.pool_event_names_lower()
    assert "mimic" in gd.pool_event_textfile_names_lower()
    assert gd.textasset_pathid_map()  # PPtr resolution table non-empty
    # collision helper covers both namespaces + space/underscore variants
    assert gd.event_pool_collision("Mimic")
    assert gd.event_pool_collision("abandoned village")
    assert not gd.event_pool_collision("Hello Wayfarer")
```

- [ ] **Step 2: Run to verify failure**

Run: `python -m pytest tools/tests/test_validate_pack.py::test_dialogue_loaders -q`
Expected: FAIL with `AttributeError: module 'gamedata' has no attribute 'dialogue_commands'`

- [ ] **Step 3: Implement** — in `tools/gamedata.py`, add below `TALENT_COMMANDS_FILE`:

```python
DIALOGUE_COMMANDS_FILE = REFERENCE_DIR / "dialogue-action-commands.txt"
```

and add these loaders after `profession_names()`:

```python
# ---- opportunity events (EVENT-SPEC.md) ----


@lru_cache(maxsize=1)
def dialogue_commands() -> frozenset[str]:
    """The 99 dialogue-action command spellings (DialogueActionHandler.RunActionCode
    switch labels, docs/research/reference/dialogue-action-commands.txt). The game
    lowercases commands before dispatch (DialogueActionHandler.cs:29) — the file is
    lowercase; compare candidate commands lowercased."""
    lines = DIALOGUE_COMMANDS_FILE.read_text(encoding="utf-8").splitlines()
    return frozenset(ln.strip() for ln in lines if ln.strip())


@lru_cache(maxsize=1)
def pool_events() -> list[dict]:
    """All extracted Dialogue asset dicts (raw JSON, PPtr fields)."""
    return [json.loads(p.read_text(encoding="utf-8"))
            for p in sorted((DATA_DIR / "Dialogue").glob("*.json"))]


@lru_cache(maxsize=1)
def pool_event_names_lower() -> frozenset[str]:
    """Shipped Dialogue asset names (the eventLookupCache namespace)."""
    return frozenset(d["m_Name"].lower() for d in pool_events())


@lru_cache(maxsize=1)
def textasset_pathid_map() -> dict[int, str]:
    """resources.assets path_id -> TextAsset stem (for Dialogue.textFile PPtrs)."""
    idx = json.loads(DATA_INDEX.read_text(encoding="utf-8"))
    out = {}
    for key, meta in idx.items():
        if key.startswith("TextAsset/"):
            out[meta["path_id"]] = key.split("/", 1)[1]
    return out


@lru_cache(maxsize=1)
def pool_event_textfile_names_lower() -> frozenset[str]:
    """Names of the TextAssets shipped Dialogues point at — the area-deck
    eventContent / doneEvents namespace (EVENT-SPEC §1: deck entries carry
    textFile.name, not Dialogue.name)."""
    ta = textasset_pathid_map()
    out = set()
    for d in pool_events():
        pid = (d.get("textFile") or {}).get("m_PathID", 0)
        if pid and pid in ta:
            out.add(ta[pid].lower())
    return frozenset(out)


def event_pool_collision(name: str) -> bool:
    """True when `name` collides (case-insensitive; space/underscore variants —
    extracted stems are SAFE_NAME-underscored) with a shipped Dialogue name or a
    shipped dialogue TextAsset name."""
    if not isinstance(name, str):
        return False
    keys = pool_event_names_lower() | pool_event_textfile_names_lower()
    n = name.lower()
    return n in keys or n.replace(" ", "_") in keys or n.replace("_", " ") in keys
```

- [ ] **Step 4: Run tests to verify pass**

Run: `python -m pytest tools/tests -q`
Expected: all PASS (the new test and no regressions).

- [ ] **Step 5: Commit**

```bash
git add tools/gamedata.py tools/tests/test_validate_pack.py
git commit -m "tools: dialogue-action vocabulary + shipped event-name loaders (EVENT-SPEC gate 1 groundwork)"
```

---

### Task 2: Toolchain — `events` validation in `validate_pack.py`

**Files:**
- Modify: `tools/validate_pack.py`
- Test: `tools/tests/test_validate_pack.py`

**Interfaces:**
- Consumes: Task 1 loaders; existing `_finding`, `gd.did_you_mean`, `gd.other_pack_manifests`.
- Produces: `validate_event(event: dict, idx: int, pack_dir: Path, findings: list[dict]) -> None` and events handling inside `validate_pack(manifest, pack_path)`. Check-name strings used by tests: `schema_version`, `story_missing`, `story_invalid`, `bad_ink_version`, `unknown_command`, `goto_unknown_knot`, `storyfunction_reserved`, `bad_levels`, `event_name_collision`.

- [ ] **Step 1: Write the failing tests** — append to `tools/tests/test_validate_pack.py`:

```python
# ------------------------------------------------------------------ events

GOOD_STORY = {
    "inkVersion": 20,
    "root": [["^A wayfarer waves at you.", {"#": "A Wayfarer"}, "\n",
              "^>>>>gold:50", "\n", "end", {"#f": 1}], "done", {"#f": 1}],
    "listDefs": {},
}


def good_event(**overrides):
    ev = {"name": "Zz Test Wayfarer", "storyFile": "events/ZzTestWayfarer.ink.json",
          "minLevel": 0, "maxLevel": 0, "unique": False}
    ev.update(overrides)
    return ev


def event_pack(tmp_path, story=None, **event_overrides):
    """Write a minimal events-only pack folder; returns its pack.json Path."""
    pack_dir = tmp_path / "DC.ZzEvents"
    (pack_dir / "events").mkdir(parents=True)
    (pack_dir / "events" / "ZzTestWayfarer.ink.json").write_text(
        json.dumps(story if story is not None else GOOD_STORY), encoding="utf-8")
    manifest = {"schemaVersion": 2, "pack": "Zz Events",
                "events": [good_event(**event_overrides)]}
    pack_json = pack_dir / "pack.json"
    pack_json.write_text(json.dumps(manifest), encoding="utf-8")
    return pack_json


def run_validator(pack_json):
    manifest = json.loads(pack_json.read_text(encoding="utf-8"))
    return vp.validate_pack(manifest, pack_json)


def errors_of(findings, check):
    return [f for f in findings if f["level"] == "ERROR" and f["check"] == check]


def test_good_event_pack_passes(tmp_path):
    findings = run_validator(event_pack(tmp_path))
    assert [f for f in findings if f["level"] == "ERROR"] == []


def test_events_only_pack_needs_no_idblock(tmp_path):
    findings = run_validator(event_pack(tmp_path))
    assert errors_of(findings, "bad_id_block") == []


def test_events_require_schema_version_2(tmp_path):
    pack_json = event_pack(tmp_path)
    manifest = json.loads(pack_json.read_text(encoding="utf-8"))
    del manifest["schemaVersion"]
    pack_json.write_text(json.dumps(manifest), encoding="utf-8")
    findings = run_validator(pack_json)
    assert errors_of(findings, "schema_version")


def test_event_unknown_command_did_you_mean(tmp_path):
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>>glod:50"
    findings = run_validator(event_pack(tmp_path, story=story))
    errs = errors_of(findings, "unknown_command")
    assert errs and "gold" in errs[0]["msg"]


def test_event_command_case_insensitive(tmp_path):
    # shipped stories use uppercase (Mimic: >>>>DIRECTCOMBAT) — the game
    # lowercases before dispatch, so GOLD must validate clean
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>>GOLD:50"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors_of(findings, "unknown_command") == []


def test_event_ink_version_pin(tmp_path):
    story = dict(GOOD_STORY, inkVersion=21)
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors_of(findings, "bad_ink_version")


def test_event_name_collision_with_shipped(tmp_path):
    findings = run_validator(event_pack(tmp_path, name="Mimic"))
    assert errors_of(findings, "event_name_collision")


def test_event_goto_and_storyfunction(tmp_path):
    story = json.loads(json.dumps(GOOD_STORY))
    story["root"][0][3] = "^>>>goto:no_such_knot"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors_of(findings, "goto_unknown_knot")

    story["root"][0][3] = "^>>>STORYFUNCTION:foo:imbueCost"
    findings = run_validator(event_pack(tmp_path, story=story))
    assert errors_of(findings, "storyfunction_reserved")


def test_event_level_gate_shape(tmp_path):
    findings = run_validator(event_pack(tmp_path, minLevel=5, maxLevel=3))
    assert errors_of(findings, "bad_levels")
    # maxLevel 0 = uncapped is legal
    findings = run_validator(event_pack(tmp_path, minLevel=5, maxLevel=0))
    assert errors_of(findings, "bad_levels") == []


def test_event_missing_story_file(tmp_path):
    findings = run_validator(event_pack(tmp_path, storyFile="events/Nope.ink.json"))
    assert errors_of(findings, "story_missing")
```

- [ ] **Step 2: Run to verify failure**

Run: `python -m pytest tools/tests/test_validate_pack.py -q -k event`
Expected: the new tests FAIL (`validate_pack` emits `no_cards` for an events-only manifest / has no events handling).

- [ ] **Step 3: Implement** — in `tools/validate_pack.py`:

3a. Add after `REQUIRED_POWER_FIELDS` block (before `validate_pack`):

```python
REQUIRED_EVENT_FIELDS = ["name", "storyFile"]


def _ink_strings(node):
    """Every string anywhere in a compiled-Ink JSON tree."""
    if isinstance(node, str):
        yield node
    elif isinstance(node, list):
        for item in node:
            yield from _ink_strings(item)
    elif isinstance(node, dict):
        for v in node.values():
            yield from _ink_strings(v)


def _ink_knots(story: dict) -> set[str]:
    """Top-level knot names: keys of the trailing dict of the root container."""
    root = story.get("root")
    if isinstance(root, list) and root and isinstance(root[-1], dict):
        return {k for k in root[-1] if k != "#f"}
    return set()


def _validate_story_actions(story: dict, where: str, err) -> None:
    """Mirror DialogueManagerINK.RunDialogueAction (DialogueManagerINK.cs:1264-1303):
    a `^`-text line containing `>>>` is stripped to the first '>', has >>>>/>>> and
    newlines removed, and is ';'-split into ':'-separated statements. `goto` /
    `STORYFUNCTION` match case-SENSITIVELY there; every other command is lowercased
    by DialogueActionHandler.RunActionCode (line 29) — hence the lowercase compare."""
    knots = _ink_knots(story)
    vocab = gd.dialogue_commands()
    for line in _ink_strings(story.get("root")):
        if not line.startswith("^") or ">>>" not in line:
            continue
        code = line[1:]
        gt = code.find(">")
        if gt > 0:
            code = code[gt:]
        code = code.replace(">>>>", "").replace(">>>", "").replace("\n", "")
        for stmt in code.split(";"):
            if not stmt.strip():
                continue
            parts = stmt.split(":")
            cmd = parts[0]
            if cmd == "goto":
                target = parts[1] if len(parts) > 1 else ""
                if target not in knots:
                    err("goto_unknown_knot",
                        f"{where}: 'goto:{target}' targets a knot that does not exist "
                        f"(knots: {', '.join(sorted(knots)) or 'none'})")
                continue
            if cmd == "STORYFUNCTION":
                err("storyfunction_reserved",
                    f"{where}: STORYFUNCTION is reserved in events v0.1 "
                    "(EVENT-SPEC §11 #4)")
                continue
            if cmd.lower() in vocab:
                continue
            close = gd.did_you_mean(cmd.lower(), vocab)
            hint = f" (did you mean {', '.join(map(repr, close))}?)" if close else ""
            err("unknown_command",
                f"{where}: dialogue action {cmd!r} not in "
                f"dialogue-action-commands.txt{hint}")


def validate_event(event: dict, idx: int, pack_dir: Path,
                   findings: list[dict]) -> None:
    """EVENT-SPEC.md §4: an opportunity event is a name + compiled Ink story +
    optional level gates / unique flag. Identity is the NAME (Dialogue.name ==
    TextAsset.name == doneEvents key) — collision-checked against BOTH shipped
    namespaces and sibling packs."""
    label = event.get("name") or f"events[{idx}]"
    err = lambda check, msg: findings.append(_finding("ERROR", label, check, msg))   # noqa: E731
    warn = lambda check, msg: findings.append(_finding("WARN", label, check, msg))   # noqa: E731

    for f in REQUIRED_EVENT_FIELDS:
        if f not in event:
            err("missing_field", f"required field {f!r} missing")

    name = event.get("name")
    if isinstance(name, str) and name.strip():
        if gd.event_pool_collision(name):
            err("event_name_collision",
                f"name {name!r} collides (case-insensitive) with a shipped "
                "Dialogue asset or dialogue TextAsset name")
    elif "name" in event:
        err("shape", "name must be a non-empty string")

    for fld in ("minLevel", "maxLevel"):
        if fld in event and (not isinstance(event[fld], int) or event[fld] < 0):
            err("bad_levels", f"{fld} must be a non-negative integer")
    min_level = event.get("minLevel", 0)
    max_level = event.get("maxLevel", 0)
    if (isinstance(min_level, int) and isinstance(max_level, int)
            and max_level != 0 and max_level < min_level):
        err("bad_levels", f"maxLevel {max_level} < minLevel {min_level} "
                          "(maxLevel 0 = uncapped)")
    if "unique" in event and not isinstance(event["unique"], bool):
        err("shape", "unique must be a boolean")

    story_rel = event.get("storyFile")
    if not (isinstance(story_rel, str) and story_rel):
        if "storyFile" in event:
            err("shape", "storyFile must be a pack-relative path string")
        return
    if not story_rel.endswith(".json"):
        warn("story_extension", f"storyFile {story_rel!r} does not end in .json — "
                                "the loader reads compiled Ink JSON, not .ink source")
    story_path = pack_dir / story_rel
    if not story_path.is_file():
        err("story_missing", f"storyFile {story_rel} not found")
        return
    try:
        story = json.loads(story_path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as e:
        err("story_invalid", f"storyFile {story_rel} is not valid JSON: {e}")
        return
    ink_version = story.get("inkVersion")
    if not isinstance(ink_version, int):
        err("bad_ink_version", f"storyFile {story_rel} has no integer inkVersion — "
                               "not compiled Ink JSON (compile with inklecate v1.0.0)")
        return
    if not (18 <= ink_version <= 20):
        err("bad_ink_version",
            f"inkVersion {ink_version} outside the supported range 18-20 — the game "
            "runtime is pinned at 20 (EVENT-SPEC §1); compile with inklecate v1.0.0 "
            "(v1.1+ emits 21, which the game rejects)")
        return
    _validate_story_actions(story, f"storyFile {story_rel}", err)
```

3b. Inside `validate_pack`, replace the content-arrays block

```python
    cards = manifest.get("cards")
    weapons = manifest.get("weapons")
    powers = manifest.get("weaponPowers")
    starting_cards = manifest.get("startingCards")
```
…through the `no_cards` early-return, with:

```python
    cards = manifest.get("cards")
    weapons = manifest.get("weapons")
    powers = manifest.get("weaponPowers")
    starting_cards = manifest.get("startingCards")
    events = manifest.get("events")
    for field, val in (("cards", cards), ("weapons", weapons),
                       ("weaponPowers", powers), ("startingCards", starting_cards),
                       ("events", events)):
        if val is not None and not isinstance(val, list):
            perr("shape", f"{field} must be a list")
    cards = cards if isinstance(cards, list) else []
    weapons = weapons if isinstance(weapons, list) else []
    powers = powers if isinstance(powers, list) else []
    starting_cards = starting_cards if isinstance(starting_cards, list) else []
    events = events if isinstance(events, list) else []
    if not cards and not weapons and not powers and not starting_cards and not events:
        perr("no_cards", "manifest has no cards, weapons, weaponPowers, "
                         "startingCards, or events")
        return findings

    # events presence requires the v2 manifest handshake (EVENT-SPEC §4):
    # a v1 loader must refuse the whole pack, not silently drop the events.
    if events and manifest.get("schemaVersion") != 2:
        perr("schema_version",
             'manifest ships events but does not declare "schemaVersion": 2 — '
             "older loaders would silently drop them (EVENT-SPEC §4 / SchemaGate)")
```

3c. Make `idBlock` optional for events-only packs. Replace the existing idBlock check:

```python
    id_block = manifest.get("idBlock")
    if (not isinstance(id_block, list) or len(id_block) != 2
            or not all(isinstance(x, int) for x in id_block) or id_block[0] > id_block[1]):
        perr("bad_id_block", "idBlock must be [low, high] integers")
        id_block = None
```
with:

```python
    has_card_space_content = any(
        isinstance(manifest.get(f), list) and manifest.get(f)
        for f in ("cards", "weapons", "weaponPowers", "startingCards"))
    id_block = manifest.get("idBlock")
    if id_block is None and not has_card_space_content:
        pass  # events-only pack: events are name-keyed, no IDs (EVENT-SPEC §3)
    elif (not isinstance(id_block, list) or len(id_block) != 2
            or not all(isinstance(x, int) for x in id_block) or id_block[0] > id_block[1]):
        perr("bad_id_block", "idBlock must be [low, high] integers")
        id_block = None
```
(Note: this moves the idBlock check BELOW the content-arrays block from 3b, since it needs `has_card_space_content` — reorder accordingly; nothing between them depends on `id_block`.)

3d. Sibling-pack event-name collisions — extend the sibling scan loop (where `sibling_talent_names` is filled) with:

```python
        for ev in sib.get("events") or []:
            if isinstance(ev, dict) and isinstance(ev.get("name"), str):
                sibling_event_names.setdefault(ev["name"].lower(), sib_name)
```
initialising `sibling_event_names: dict[str, str] = {}` beside the other sibling dicts, and add the events loop after the weapon-powers loop:

```python
    # ---- events (EVENT-SPEC §4: name-keyed, no IDs)
    seen_event_names: set[str] = set()
    for i, ev in enumerate(events):
        if not isinstance(ev, dict):
            perr("shape", f"events[{i}] is not an object")
            continue
        label = ev.get("name") or f"events[{i}]"
        validate_event(ev, i, pack_dir, findings)
        nm = ev.get("name")
        if isinstance(nm, str):
            if nm.lower() in seen_event_names:
                findings.append(_finding("ERROR", label, "event_name_collision",
                                         "event name duplicated inside the pack"))
            if nm.lower() in sibling_event_names:
                findings.append(_finding("ERROR", label, "event_name_collision",
                                         f"event name collides with pack "
                                         f"{sibling_event_names[nm.lower()]!r}"))
            seen_event_names.add(nm.lower())
```

- [ ] **Step 4: Run tests to verify pass**

Run: `python -m pytest tools/tests -q`
Expected: all PASS (new event tests + zero regressions on the existing suite).

- [ ] **Step 5: Commit**

```bash
git add tools/validate_pack.py tools/tests/test_validate_pack.py
git commit -m "tools: gate-1 validation for pack events (ink pin, dialogue vocabulary, dual-namespace collisions)"
```

---

### Task 3: JSON Schema — `schemaVersion: 2` + `events`

**Files:**
- Modify: `schemas/pack.schema.json`
- Test: `tools/tests/test_schema.py`

**Interfaces:**
- Consumes: nothing new. Produces: schema accepting `events` manifests; consumed by Task 4's example pack and editors.

- [ ] **Step 1: Write the failing tests** — append to `tools/tests/test_schema.py`:

```python
# ------------------------------------------------------------------- events

def events_manifest() -> dict:
    return {
        "$schema": "../../../schemas/pack.schema.json",
        "schemaVersion": 2,
        "pack": "Zz Events",
        "events": [{"name": "Hello Wayfarer",
                    "storyFile": "events/HelloWayfarer.ink.json",
                    "minLevel": 0, "maxLevel": 0, "unique": False}],
    }


def test_events_manifest_passes(validator):
    assert errors(validator, events_manifest()) == []


def test_events_require_schema_version_2(validator):
    m = events_manifest()
    del m["schemaVersion"]
    assert errors(validator, m)
    m["schemaVersion"] = 1
    assert errors(validator, m)


def test_events_only_pack_needs_no_idblock(validator):
    m = events_manifest()
    assert "idBlock" not in m and errors(validator, m) == []


def test_cards_still_require_idblock(validator, schema):
    m = example_manifest()
    del m["idBlock"]
    assert errors(validator, m)


def test_event_entry_shape(validator):
    m = events_manifest()
    del m["events"][0]["storyFile"]
    assert errors(validator, m)
    m = events_manifest()
    m["events"][0]["bogusField"] = 1
    assert errors(validator, m)
    m = events_manifest()
    m["events"][0]["minLevel"] = -1
    assert errors(validator, m)
```

- [ ] **Step 2: Run to verify failure**

Run: `python -m pytest tools/tests/test_schema.py -q`
Expected: new tests FAIL (schema refuses `events`, requires top-level `idBlock`).

- [ ] **Step 3: Implement** — in `schemas/pack.schema.json`:

3a. Top-level `"required"` becomes `["pack"]` (drop `"idBlock"`).

3b. `schemaVersion` property: `"enum": [1, 2]` and extend its description with: `"Version 2 adds the events array (EVENT-SPEC.md §4)."`

3c. Add an events branch to the existing top-level `"anyOf"`:

```json
    {
      "required": [
        "events"
      ],
      "properties": {
        "events": {
          "minItems": 1
        }
      }
    }
```

3d. Add a top-level `"allOf"` (sibling of `"anyOf"`) carrying the two conditionals:

```json
  "allOf": [
    {
      "if": {
        "anyOf": [
          { "required": ["cards"] },
          { "required": ["weapons"] },
          { "required": ["weaponPowers"] },
          { "required": ["startingCards"] }
        ]
      },
      "then": { "required": ["idBlock"] }
    },
    {
      "if": { "required": ["events"] },
      "then": {
        "required": ["schemaVersion"],
        "properties": { "schemaVersion": { "const": 2 } }
      }
    }
  ],
```

3e. Add the top-level `events` property:

```json
    "events": {
      "type": "array",
      "description": "Opportunity events (EVENT-SPEC.md §4) — Ink-scripted map encounters. Name-keyed (no numeric IDs; no idBlock needed for an events-only pack). Requires schemaVersion 2.",
      "items": {
        "$ref": "#/definitions/event"
      }
    },
```

3f. Add the definition beside `startingCard`:

```json
    "event": {
      "type": "object",
      "additionalProperties": false,
      "required": ["name", "storyFile"],
      "properties": {
        "name": {
          "type": "string",
          "minLength": 1,
          "description": "Event identity: Dialogue.name, TextAsset.name AND the persistent doneEvents key. Must be unique (case-insensitive) vs shipped Dialogue assets, shipped dialogue TextAssets, and every other installed pack."
        },
        "storyFile": {
          "type": "string",
          "minLength": 1,
          "description": "Pack-relative path to the COMPILED Ink JSON (inkVersion 18-20; inklecate v1.0.0 emits 20). Commit the .ink source beside it; the loader reads only this file."
        },
        "minLevel": {
          "type": "integer",
          "minimum": 0,
          "default": 0,
          "description": "Dialogue.minimumLevel — lowest areaLevel that can roll the event."
        },
        "maxLevel": {
          "type": "integer",
          "minimum": 0,
          "default": 0,
          "description": "Dialogue.maxLevel — highest areaLevel that can roll the event; 0 = uncapped."
        },
        "unique": {
          "type": "boolean",
          "default": false,
          "description": "Once picked, never offered again in that save (persisted into PlayerData.doneEvents by the event NAME)."
        }
      }
    },
```

- [ ] **Step 4: Run tests to verify pass**

Run: `python -m pytest tools/tests -q`
Expected: all PASS, including `test_real_manifests_pass` (existing manifests unaffected by the relaxed `required`).

- [ ] **Step 5: Commit**

```bash
git add schemas/pack.schema.json tools/tests/test_schema.py
git commit -m "tools: pack.schema.json v2 — events array, conditional idBlock, schemaVersion gate"
```

---

### Task 4: `ExampleEventPack` (spec §7 dataset)

**Files:**
- Create: `DC.DawnKit/examples/ExampleEventPack/pack.json`
- Create: `DC.DawnKit/examples/ExampleEventPack/events/HelloWayfarer.ink`
- Create: `DC.DawnKit/examples/ExampleEventPack/events/HelloWayfarer.ink.json`
- Modify: `tools/tests/test_schema.py` (REAL_MANIFESTS)

**Interfaces:**
- Consumes: Tasks 2–3 (must validate green). Produces: the pack Task 10's in-game QA loads.

The compiled JSON is hand-authored, modeled byte-for-byte on shipped inkVersion-20 encodings verified in `tools/out/data/TextAsset/`: choice encoding from `Ambush.txt` (`"ev","str","^label","/str","/ev",{"*":"0.c-0","flg":4}` + trailing named-content dict), tag encoding `{"#":"speaker"}` from `A_Familiar_Face.txt`, `-> END` → `"end"` from `A_Call_for_Help.txt`, action-line survival as a plain `^`-string from `Mimic.txt`. **If in-game QA (Task 10) shows any story misbehavior, compile the committed `.ink` with inklecate v1.0.0 and replace the `.json` — do not debug the hand encoding.**

- [ ] **Step 1: Write `pack.json`**

```json
{
  "$schema": "../../../schemas/pack.schema.json",
  "schemaVersion": 2,
  "pack": "Example Event Pack",
  "events": [
    {
      "name": "Hello Wayfarer",
      "storyFile": "events/HelloWayfarer.ink.json",
      "minLevel": 0,
      "maxLevel": 0,
      "unique": false
    }
  ]
}
```

- [ ] **Step 2: Write the Ink source** `events/HelloWayfarer.ink` (authoring artifact, committed per spec §4):

```ink
// Hello Wayfarer — the smallest opportunity event: text, a speaker tag,
// two choices, one dialogue action, END. Compile with inklecate v1.0.0
// (emits inkVersion 20; ink >= 1.1 emits 21, which the game refuses).
A weary wayfarer waves at you from across the road. #A Wayfarer
* [Wave back]
    The wayfarer smiles and tosses you a small pouch of coins. #A Wayfarer
    >>>>gold:50
    -> END
* [Walk on]
    You keep to your side of the road and walk on. #A Wayfarer
    -> END
```

- [ ] **Step 3: Write the compiled story** `events/HelloWayfarer.ink.json` (single line, UTF-8, no BOM):

```json
{"inkVersion":20,"root":[["^A weary wayfarer waves at you from across the road.",{"#":"A Wayfarer"},"\n","ev","str","^Wave back","/str","/ev",{"*":"0.c-0","flg":4},"ev","str","^Walk on","/str","/ev",{"*":"0.c-1","flg":4},{"c-0":["^The wayfarer smiles and tosses you a small pouch of coins.",{"#":"A Wayfarer"},"\n","^>>>>gold:50","\n","end",{"#f":5}],"c-1":["^You keep to your side of the road and walk on.",{"#":"A Wayfarer"},"\n","end",{"#f":5}]}],"done",{"#f":1}],"listDefs":{}}
```

- [ ] **Step 4: Register the manifest in the schema tests** — in `tools/tests/test_schema.py`, extend `REAL_MANIFESTS`:

```python
REAL_MANIFESTS = sorted(gd.REPO_DIR.glob("DC.*/pack.json")) + [
    gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExamplePack" / "pack.json",
    gd.REPO_DIR / "DC.DawnKit" / "examples" / "ExampleEventPack" / "pack.json",
]
```

- [ ] **Step 5: Verify all gates**

Run: `python -m pytest tools/tests -q` — Expected: PASS.
Run: `python tools/dmk.py validate DC.DawnKit/examples/ExampleEventPack/pack.json` — Expected: `0 error(s), 0 warning(s)`, exit 0.

- [ ] **Step 6: Commit**

```bash
git add DC.DawnKit/examples/ExampleEventPack tools/tests/test_schema.py
git commit -m "feat: ExampleEventPack — Hello Wayfarer opportunity event (EVENT-SPEC §7)"
```

---

### Task 5: Engine — csproj references + dialogue vocabulary

**Files:**
- Modify: `DC.DawnKit/src/DawnKit/DawnKit.csproj`
- Modify: `DC.DawnKit/src/DawnKit/Content/CommandVocabulary.cs`

**Interfaces:**
- Produces: `CommandVocabulary.DialogueCommands` (`HashSet<string>` with `StringComparer.OrdinalIgnoreCase`, null = validation unavailable/fail-open) — consumed by Task 6's `InkStoryLint`. Newtonsoft.Json + Ink-Libraries references — consumed by Tasks 6 and 8.

- [ ] **Step 1: csproj** — in `DawnKit.csproj`, add to the references ItemGroup:

```xml
    <Reference Include="Newtonsoft.Json">
      <HintPath>$(GameDir)\Dawncaster_Data\Managed\Newtonsoft.Json.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Ink-Libraries">
      <HintPath>$(GameDir)\Dawncaster_Data\Managed\Ink-Libraries.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

and to the EmbeddedResource ItemGroup:

```xml
    <EmbeddedResource Include="..\..\..\docs\research\reference\dialogue-action-commands.txt" LogicalName="DawnKit.dialogue-action-commands.txt" />
```

- [ ] **Step 2: CommandVocabulary** — add a property and load it in `Initialize()`:

```csharp
        /// <summary>Dialogue-action commands (EVENT-SPEC §1): the 99
        /// DialogueActionHandler.RunActionCode switch labels. The game lowercases
        /// before dispatch (DialogueActionHandler.cs:29) — case-INSENSITIVE set,
        /// unlike the ordinal effect/talent DSL.</summary>
        internal static HashSet<string> DialogueCommands { get; private set; }
```

In `Initialize()`, after the talent-union block:

```csharp
            HashSet<string> dialogue = LoadCommandFile("dialogue-action-commands.txt");
            DialogueCommands = dialogue != null
                ? new HashSet<string>(dialogue, StringComparer.OrdinalIgnoreCase)
                : null;
```

and extend the log line:

```csharp
            DawnKitPlugin.Log.LogInfo($"[DawnKit] Command vocabulary: {(EffectCommands != null ? EffectCommands.Count.ToString() : "unavailable")} effect / {(TalentCommands != null ? TalentCommands.Count.ToString() : "unavailable")} talent-union / {(DialogueCommands != null ? DialogueCommands.Count.ToString() : "unavailable")} dialogue.");
```

- [ ] **Step 3: Build**

Run: `dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DC.DawnKit/src/DawnKit/DawnKit.csproj DC.DawnKit/src/DawnKit/Content/CommandVocabulary.cs
git commit -m "feat: engine dialogue-action vocabulary + Newtonsoft/Ink references (events groundwork)"
```

---

### Task 6: Engine — event registration path (drafts, lint, validator, registry, public API)

**Files:**
- Modify: `DC.DawnKit/src/DawnKit/Core/Drafts.cs`
- Create: `DC.DawnKit/src/DawnKit/Core/InkStoryLint.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/Validator.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/RegistrationLedger.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/Registry.cs`
- Modify: `DC.DawnKit/src/DawnKit/Api/RegisterResult.cs` (doc comment only)
- Create: `DC.DawnKit/src/DawnKit/Api/Events.cs`

**Interfaces:**
- Consumes: `CommandVocabulary.DialogueCommands` (Task 5), existing `ManifestError`, `DidYouMean.Suggest(string, IEnumerable<string>) -> string`, `OwnerResolver.ResolveCallingOwner()`, `RegistrationLedger.Record/RecordConflict`, `RegisterResult.Success/Failed`.
- Produces (consumed by Tasks 7–9):
  - `EventDraft { Owner, Name, StoryJson, StoryFilePath, MinLevel, MaxLevel, Unique }`
  - `ParsedEvent { Owner, Name, StoryJson, MinLevel, MaxLevel, Unique }`
  - `EventRegistration { ParsedEvent Spec; Dialogue Event; TextAsset Text; }` (in Registry.cs)
  - `Registry.Events : List<EventRegistration>`, `Registry.RegisterEvent(EventDraft) -> RegisterResult`
  - `RegistrationLedger.FindEventSpaceConflict(string name) -> RegistrationInfo`
  - public `DawnKit.Events.Build(string) -> EventBuilder` with `.Owner/.StoryJson/.StoryFile/.Levels/.Unique/.Register`, `DawnKit.Events.All`
  - `InkStoryLint.Check(string storyJson)` (throws `ManifestError`)

- [ ] **Step 1: Drafts** — append to `Drafts.cs` (namespace `DawnKit.Core.Lifecycle`):

```csharp
    /// <summary>Raw builder inputs for an opportunity event (EVENT-SPEC §3).</summary>
    internal sealed class EventDraft
    {
        internal string Owner;
        internal string Name;
        internal string StoryJson;
        internal string StoryFilePath;
        internal int MinLevel;
        internal int MaxLevel;
        internal bool Unique;
    }

    /// <summary>Validated event spec — everything EventFactory needs.</summary>
    internal sealed class ParsedEvent
    {
        internal string Owner;
        internal string Name;
        internal string StoryJson;
        internal int MinLevel;
        internal int MaxLevel;
        internal bool Unique;
    }
```

- [ ] **Step 2: InkStoryLint** — create `Core/InkStoryLint.cs`:

```csharp
using System;
using System.Collections.Generic;
using DawnKit.Content.Vocabulary;
using Newtonsoft.Json.Linq;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>
    /// Register()-time lint of a compiled Ink story (EVENT-SPEC §3): JSON parses,
    /// inkVersion inside the engine pin [18, 20], and every `&gt;&gt;&gt;` action line uses
    /// the closed dialogue-action vocabulary. `goto:&lt;knot&gt;` must target a real
    /// knot; STORYFUNCTION is reserved in v0.1 (EVENT-SPEC §11 #4). Throws
    /// ManifestError on the first failure, mirroring Validator. Null vocabulary =
    /// command validation unavailable (fail-open, like codeLines).
    /// </summary>
    internal static class InkStoryLint
    {
        internal const int MinInkVersion = 18;
        internal const int MaxInkVersion = 20;

        internal static void Check(string storyJson)
        {
            JObject story;
            try
            {
                story = JObject.Parse(storyJson);
            }
            catch (Exception ex)
            {
                throw new ManifestError("story is not valid JSON: " + ex.Message);
            }

            JToken version = story["inkVersion"];
            if (version == null || version.Type != JTokenType.Integer)
            {
                throw new ManifestError("story has no integer inkVersion — not compiled Ink JSON (compile the .ink with inklecate v1.0.0)");
            }
            int inkVersion = (int)version;
            if (inkVersion < MinInkVersion || inkVersion > MaxInkVersion)
            {
                throw new ManifestError(
                    $"story inkVersion {inkVersion} outside the supported range {MinInkVersion}-{MaxInkVersion} — " +
                    "the game runtime is pinned at 20; compile with inklecate v1.0.0 (v1.1+ emits 21, which the game rejects)");
            }

            HashSet<string> knots = Knots(story);
            foreach (string line in Strings(story["root"]))
            {
                if (line.StartsWith("^", StringComparison.Ordinal) && line.Contains(">>>"))
                {
                    CheckActionLine(line, knots);
                }
            }
        }

        /// <summary>Top-level knot names: keys of the root container's trailing dict.</summary>
        private static HashSet<string> Knots(JObject story)
        {
            var knots = new HashSet<string>(StringComparer.Ordinal);
            if (story["root"] is JArray root && root.Count > 0 && root[root.Count - 1] is JObject named)
            {
                foreach (JProperty prop in named.Properties())
                {
                    if (prop.Name != "#f")
                    {
                        knots.Add(prop.Name);
                    }
                }
            }
            return knots;
        }

        private static IEnumerable<string> Strings(JToken node)
        {
            if (node is JValue value && value.Type == JTokenType.String)
            {
                yield return (string)value.Value;
            }
            else if (node is JContainer container)
            {
                foreach (JToken child in container.Children())
                {
                    foreach (string s in Strings(child))
                    {
                        yield return s;
                    }
                }
            }
        }

        // Mirrors DialogueManagerINK.RunDialogueAction (DialogueManagerINK.cs:
        // 1264-1303): strip to the first '>', remove >>>>/>>> and newlines,
        // ';'-split into ':'-separated statements. goto/STORYFUNCTION match
        // case-SENSITIVELY there; everything else is lowercased by
        // DialogueActionHandler.RunActionCode — hence the case-insensitive set.
        private static void CheckActionLine(string line, HashSet<string> knots)
        {
            string code = line.Substring(1);
            int gt = code.IndexOf('>');
            if (gt > 0)
            {
                code = code.Remove(0, gt);
            }
            code = code.Replace(">>>>", "").Replace(">>>", "").Replace("\n", "");
            foreach (string stmt in code.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(stmt))
                {
                    continue;
                }
                string[] parts = stmt.Split(':');
                string cmd = parts[0];
                if (cmd == "goto")
                {
                    string target = parts.Length > 1 ? parts[1] : "";
                    if (!knots.Contains(target))
                    {
                        throw new ManifestError($"story action 'goto:{target}' targets a knot that does not exist");
                    }
                    continue;
                }
                if (cmd == "STORYFUNCTION")
                {
                    throw new ManifestError("story uses STORYFUNCTION — reserved in events v0.1 (EVENT-SPEC §11 #4)");
                }
                if (CommandVocabulary.DialogueCommands == null || CommandVocabulary.DialogueCommands.Contains(cmd))
                {
                    continue;
                }
                string hint = DidYouMean.Suggest(cmd.ToLowerInvariant(), CommandVocabulary.DialogueCommands);
                throw new ManifestError(
                    $"story action command '{cmd}' is not a dialogue-action command" +
                    (hint != null ? $" — did you mean {hint}?" : ""));
            }
        }
    }
}
```
(Adjust the `DidYouMean.Suggest` call to its exact signature in `Core/DidYouMean.cs:21` — it returns a suggestion string or null.)

- [ ] **Step 3: Validator.ParseEvent** — append to the `Validator` class in `Validator.cs`:

```csharp
        /// <summary>EVENT-SPEC §3: name + compiled Ink story + level gates.</summary>
        internal static ParsedEvent ParseEvent(EventDraft d)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
            {
                throw new ManifestError("event has no name");
            }
            string json = d.StoryJson;
            if (json == null && !string.IsNullOrEmpty(d.StoryFilePath))
            {
                try
                {
                    json = System.IO.File.ReadAllText(d.StoryFilePath);
                }
                catch (Exception ex)
                {
                    throw new ManifestError($"cannot read story file '{d.StoryFilePath}': {ex.Message}");
                }
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ManifestError("event has no story — call .StoryJson(...) or .StoryFile(...)");
            }
            if (d.MinLevel < 0 || d.MaxLevel < 0)
            {
                throw new ManifestError("minLevel/maxLevel must be non-negative");
            }
            if (d.MaxLevel != 0 && d.MaxLevel < d.MinLevel)
            {
                throw new ManifestError($"maxLevel {d.MaxLevel} < minLevel {d.MinLevel} (maxLevel 0 = uncapped)");
            }
            InkStoryLint.Check(json);
            return new ParsedEvent
            {
                Owner = d.Owner,
                Name = d.Name.Trim(),
                StoryJson = json,
                MinLevel = d.MinLevel,
                MaxLevel = d.MaxLevel,
                Unique = d.Unique,
            };
        }
```

- [ ] **Step 4: Ledger** — add to `RegistrationLedger` (beside `FindTalentSpaceConflict`):

```csharp
        /// <summary>Same idea for the event space — NAME-only (events have no
        /// numeric IDs; EVENT-SPEC §3: name-keyed ledger kind "event").</summary>
        internal static RegistrationInfo FindEventSpaceConflict(string name)
        {
            foreach (RegistrationInfo e in entries)
            {
                if (e.Ok && e.Kind == "event" &&
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }
            return null;
        }
```

- [ ] **Step 5: Registry** — in `Registry.cs`, add beside `TalentRegistration`:

```csharp
    /// <summary>A durable opportunity-event registration and its live Dialogue
    /// instance + story TextAsset (EVENT-SPEC §3). Both are null until injected,
    /// or after being pruned by an asset wipe.</summary>
    internal sealed class EventRegistration
    {
        internal ParsedEvent Spec;
        internal Dialogue Event;
        internal TextAsset Text;
    }
```
(`TextAsset` needs `using UnityEngine;` at the top of Registry.cs.)

Add the list and registration method to `Registry`:

```csharp
        internal static readonly List<EventRegistration> Events = new List<EventRegistration>();

        internal static RegisterResult RegisterEvent(EventDraft draft)
        {
            const string kind = "event";
            if (string.IsNullOrEmpty(draft.Owner))
            {
                draft.Owner = OwnerResolver.ResolveCallingOwner();
            }
            string owner = draft.Owner;
            ParsedEvent spec;
            try
            {
                spec = Validator.ParseEvent(draft);
            }
            catch (ManifestError me)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{draft.Name ?? "?"}: {me.Message} — event skipped.");
                RegistrationLedger.Record(new RegistrationInfo(owner, kind, 0, draft.Name, false, me.Message));
                return RegisterResult.Failed(kind, owner, draft.Name, me.Message);
            }

            string conflict = FindEventConflict(spec.Owner, spec.Name);
            if (conflict != null)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — event refused.");
                RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, 0, spec.Name, false, conflict));
                RegistrationLedger.RecordConflict(spec.Owner, conflict);
                return RegisterResult.Failed(kind, spec.Owner, spec.Name, conflict);
            }

            Events.Add(new EventRegistration { Spec = spec });
            NoteOwner(spec.Owner);
            RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, 0, spec.Name, true, null));
            return RegisterResult.Success(kind, spec.Owner, spec.Name);
        }

        /// <summary>
        /// Event collision namespace (EVENT-SPEC §3): other mods' event names +
        /// shipped Dialogue asset names + shipped dialogue TextAsset names — all
        /// case-insensitive. Pools are only consulted once loaded (world assets);
        /// injection re-checks either way.
        /// </summary>
        private static string FindEventConflict(string owner, string name)
        {
            RegistrationInfo other = RegistrationLedger.FindEventSpaceConflict(name);
            if (other != null)
            {
                return $"{owner}/{name}: event name already owned by {other.Owner}";
            }
            try
            {
                if (AssetManager.allEvents != null && AssetManager.allEvents.Count > 0)
                {
                    Dialogue shipped = AssetManager.allEvents.FirstOrDefault(e => e != null &&
                        (string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase) ||
                         (e.textFile != null && string.Equals(e.textFile.name, name, StringComparison.OrdinalIgnoreCase))));
                    if (shipped != null && !Events.Any(r => r.Event == shipped))
                    {
                        return $"{owner}/{name}: event name already owned by the shipped event pool (event \"{shipped.name}\")";
                    }
                }
            }
            catch
            {
                // Pool probing must never break Register(); injection re-checks.
            }
            return null;
        }
```

- [ ] **Step 6: RegisterResult doc** — in `Api/RegisterResult.cs`, update the `Kind` doc comment to `"card" / "weapon" / "startingCard" / "weaponPower" / "event" / "set"`.

- [ ] **Step 7: Public API** — create `Api/Events.cs`:

```csharp
using DawnKit.Core.Lifecycle;

namespace DawnKit
{
    /// <summary>
    /// Opportunity events (EVENT-SPEC.md): Ink-scripted map encounters that join
    /// the same global fill pool as vanilla roadside events. Registration is
    /// declarative and durable — the engine constructs the Dialogue + story
    /// TextAsset at the WORLD-asset load phase, and a StartDialogue patch serves
    /// the story when the node is picked. Events are name-keyed: the event name
    /// becomes Dialogue.name, TextAsset.name and the persistent doneEvents key —
    /// unique (case-insensitive) vs shipped events and every other mod.
    /// </summary>
    public static class Events
    {
        public static EventBuilder Build(string name) => new EventBuilder(name);

        /// <summary>Every event registration attempt (including failed ones).</summary>
        public static System.Collections.Generic.IReadOnlyList<RegistrationInfo> All =>
            Core.Ownership.RegistrationLedger.OfKind("event");
    }

    public sealed class EventBuilder
    {
        internal readonly EventDraft Draft = new EventDraft();

        internal EventBuilder(string name)
        {
            Draft.Name = name;
        }

        /// <summary>Owning mod/pack display name — log lines and the ownership registry.</summary>
        public EventBuilder Owner(string owner) { Draft.Owner = owner; return this; }

        /// <summary>Compiled Ink JSON (inkVersion 18-20; inklecate v1.0.0 emits 20).</summary>
        public EventBuilder StoryJson(string compiledInkJson) { Draft.StoryJson = compiledInkJson; return this; }

        /// <summary>Absolute path to a compiled Ink JSON file — read at Register().</summary>
        public EventBuilder StoryFile(string absoluteJsonPath) { Draft.StoryFilePath = absoluteJsonPath; return this; }

        /// <summary>Area-level gate: Dialogue.minimumLevel / maxLevel (max 0 = uncapped).</summary>
        public EventBuilder Levels(int min, int max) { Draft.MinLevel = min; Draft.MaxLevel = max; return this; }

        /// <summary>Never re-offered after being picked once (persisted in the save's doneEvents).</summary>
        public EventBuilder Unique(bool unique = true) { Draft.Unique = unique; return this; }

        /// <summary>Validate and register. The engine injects at the next world-asset load phase.</summary>
        public RegisterResult Register() => Core.Lifecycle.Registry.RegisterEvent(Draft);
    }
}
```
(Note: `Registry` must be referenced as `Core.Lifecycle.Registry` here or via `using DawnKit.Core.Lifecycle;` — the file already has that using; match the `Builders.cs` idiom, which calls `Registry.RegisterCard` with the using in place.)

- [ ] **Step 8: Build**

Run: `dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add DC.DawnKit/src/DawnKit/Core/Drafts.cs DC.DawnKit/src/DawnKit/Core/InkStoryLint.cs DC.DawnKit/src/DawnKit/Core/Validator.cs DC.DawnKit/src/DawnKit/Core/RegistrationLedger.cs DC.DawnKit/src/DawnKit/Core/Registry.cs DC.DawnKit/src/DawnKit/Api/RegisterResult.cs DC.DawnKit/src/DawnKit/Api/Events.cs
git commit -m "feat: DawnKit.Events builder + registry — name-keyed event registration with ink lint"
```

---

### Task 7: Engine — EventFactory + world-phase injection + boot report

**Files:**
- Modify: `DC.DawnKit/src/DawnKit/Content/Factories.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/InjectionEngine.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/BootReport.cs`

**Interfaces:**
- Consumes: `Registry.Events`, `EventRegistration`, `ParsedEvent` (Task 6); `ReferenceResolver.Resolve(string, bool)` (existing — its final pass already calls `BootReport.Emit`, ReferenceResolver.cs:115, so event counts reach the report with no extra call).
- Produces: `EventFactory.Build(EventRegistration)`; `InjectionEngine.OnWorldAssetsLoaded(string source)` (Task 8 adds the availability gate inside `InjectEvents`).

- [ ] **Step 1: EventFactory** — append to `Factories.cs` (namespace `DawnKit.Content.Factories`; file already has `using System.Collections.Generic;`, `using DawnKit.Core.Lifecycle;`, `using UnityEngine;`):

```csharp
    /// <summary>
    /// Safe Dialogue construction from a parsed event spec (EVENT-SPEC §3):
    /// HideAndDontSave, non-null eventConditions (the P5 landmine class), and the
    /// naming rule Dialogue.name == TextAsset.name == event name — the area deck
    /// keys entries by textFile.name (AreaHandler.cs:508) while eventLookupCache
    /// keys by Dialogue.name (AssetManager.cs:317); equality makes every lookup
    /// path agree and keeps doneEvents entries human-readable.
    /// </summary>
    internal static class EventFactory
    {
        /// <summary>Construct the Dialogue + story TextAsset (nothing is added to AssetManager here).</summary>
        internal static void Build(EventRegistration reg)
        {
            ParsedEvent m = reg.Spec;

            var text = new TextAsset(m.StoryJson)
            {
                name = m.Name,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Dialogue ev = ScriptableObject.CreateInstance<Dialogue>();
            ev.name = m.Name;
            ev.hideFlags = HideFlags.HideAndDontSave;
            ev.eventType = AreaHandler.EventTypes.opportunity;
            ev.nameOverwrite = "";
            ev.description = "";
            ev.eventConditions = new List<AreaCondition>();
            ev.textFile = text;
            ev.minimumLevel = m.MinLevel;
            ev.maxLevel = m.MaxLevel;
            ev.unique = m.Unique;
            // rarity stays default (Common=0, like shipped Mimic) — the rarity
            // filter only applies to shrines; opportunity map nodes render the
            // generic label + defaultOpportunityImage (EventDisplay.cs:783-789).

            reg.Text = text;
            reg.Event = ev;
        }
    }
```

- [ ] **Step 2: World-phase hooks** — in `InjectionEngine.cs`, change the two world postfixes in `AssetLoadHooks`:

```csharp
        internal static void SetWorldAssetsLoaded_Postfix() => InjectionEngine.OnWorldAssetsLoaded("SetWorldAssetsLoaded");

        internal static void LoadWorldAssets_Postfix() => InjectionEngine.OnWorldAssetsLoaded("LoadWorldAssets");
```

and add to `InjectionEngine`:

```csharp
        /// <summary>
        /// Phase 2 (world assets): inject registered events, refresh the event
        /// cache, THEN run the authoritative reference-resolution pass (which
        /// emits the boot report — event counts included). EVENT-SPEC §6.
        /// </summary>
        internal static void OnWorldAssetsLoaded(string source)
        {
            try
            {
                InjectEvents(source);
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Event injection failed (hook: {source}): {ex}");
            }
            ReferenceResolver.Resolve(source, finalPass: true);
        }

        private static void InjectEvents(string source)
        {
            if (Registry.Events.Count == 0)
            {
                return;
            }

            // Prune instances wiped by ForceReloadAssets()/ClearAllCollections()
            // (membership check, never object identity — same idiom as cards).
            foreach (EventRegistration r in Registry.Events)
            {
                if (r.Event != null && !AssetManager.allEvents.Contains(r.Event))
                {
                    r.Event = null;
                    r.Text = null;
                }
            }

            int injectedTotal = 0;
            foreach (string owner in Registry.OwnerOrder)
            {
                int injected = 0, skipped = 0;
                foreach (EventRegistration r in Registry.Events)
                {
                    if (r.Spec.Owner != owner)
                    {
                        continue;
                    }
                    try
                    {
                        if (r.Event != null)
                        {
                            continue; // already injected this process and still present
                        }
                        string conflict = FindEventPoolConflict(r.Spec);
                        if (conflict != null)
                        {
                            DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — event skipped.");
                            RegistrationLedger.RecordConflict(r.Spec.Owner, conflict);
                            skipped++;
                            continue;
                        }
                        EventFactory.Build(r);
                        AssetManager.allEvents.Add(r.Event);
                        injected++;
                    }
                    catch (Exception ex)
                    {
                        DawnKitPlugin.Log.LogError($"[DawnKit] {r.Spec.Owner}/{r.Spec.Name}: unexpected error, event skipped: {ex}");
                        skipped++;
                    }
                }
                if (injected + skipped > 0)
                {
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] {owner}: {injected} events injected, {skipped} skipped (hook: {source})");
                }
                injectedTotal += injected;
            }

            if (injectedTotal > 0)
            {
                // Rebuilds eventLookupCache — worldAssetsLoaded is already true in
                // this postfix, so the event cache branch runs (AssetManager.cs:310-320).
                AssetManager.RefreshCaches();
            }
        }

        /// <summary>Live-pool backstop: shipped + earlier-injected events, BOTH
        /// name namespaces (Dialogue.name and textFile.name).</summary>
        private static string FindEventPoolConflict(ParsedEvent spec)
        {
            Dialogue existing = AssetManager.allEvents.FirstOrDefault(e => e != null &&
                (string.Equals(e.name, spec.Name, StringComparison.OrdinalIgnoreCase) ||
                 (e.textFile != null && string.Equals(e.textFile.name, spec.Name, StringComparison.OrdinalIgnoreCase))));
            if (existing == null)
            {
                return null;
            }
            EventRegistration ours = Registry.Events.FirstOrDefault(r => r.Event == existing);
            string claimant = ours != null ? ours.Spec.Owner : "the shipped event pool";
            return $"{spec.Owner}/{spec.Name}: event name already owned by {claimant} (event \"{existing.name}\")";
        }
```

- [ ] **Step 3: BootReport** — in `BootReport.cs`:

`AppliedCount`:

```csharp
        private static int AppliedCount(string owner)
        {
            return Registry.Cards.Count(r => r.Spec.Owner == owner && r.Card != null) +
                   Registry.Talents.Count(r => r.Spec.Owner == owner && r.Talent != null) +
                   Registry.Events.Count(r => r.Spec.Owner == owner && r.Event != null);
        }
```

`TotalApplied`:

```csharp
        internal static int TotalApplied()
        {
            return Registry.Cards.Count(r => r.Card != null) +
                   Registry.Talents.Count(r => r.Talent != null) +
                   Registry.Events.Count(r => r.Event != null);
        }
```

`IsApplied` — insert before the final `return`:

```csharp
            if (e.Kind == "event")
            {
                return Registry.Events.Any(r => r.Spec.Owner == e.Owner &&
                    string.Equals(r.Spec.Name, e.Name, StringComparison.OrdinalIgnoreCase) && r.Event != null);
            }
```
(add `using System;` if not present — the file already has it.)

`DescribeItem` — insert before the `CardRegistration c = ...` lookup:

```csharp
            if (e.Kind == "event")
            {
                EventRegistration ev = Registry.Events.FirstOrDefault(r => r.Spec.Owner == e.Owner &&
                    string.Equals(r.Spec.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                if (ev == null)
                {
                    return "";
                }
                string levels = ev.Spec.MaxLevel == 0 ? $"{ev.Spec.MinLevel}+" : $"{ev.Spec.MinLevel}-{ev.Spec.MaxLevel}";
                return $" levels={levels}{(ev.Spec.Unique ? " unique" : "")}";
            }
```

- [ ] **Step 4: Build**

Run: `dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add DC.DawnKit/src/DawnKit/Content/Factories.cs DC.DawnKit/src/DawnKit/Core/InjectionEngine.cs DC.DawnKit/src/DawnKit/Core/BootReport.cs
git commit -m "feat: world-phase event injection — Dialogue factory, prune/rebuild, boot-report counts"
```

---

### Task 8: Engine — StartDialogue prefix, tracked members, `[Events] Enabled` knob

**Files:**
- Create: `DC.DawnKit/src/DawnKit/Integration/DialogueIntegration.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/PatchManager.cs`
- Modify: `DC.DawnKit/src/DawnKit/Core/InjectionEngine.cs` (availability gate)
- Modify: `DC.DawnKit/src/DawnKit/DawnKitPlugin.cs`

**Interfaces:**
- Consumes: `Registry.Events` (Task 6). Verified game surface (decompiled): `DialogueManagerINK.StartDialogue(string dialogueFile)` public instance (DialogueManagerINK.cs:260); private fields `dialogueTemp`, `story` (`Ink.Runtime.Story`), `dialogueName`; public field `areaUI` (`AreaUI`); private `HidePortrait(float, float)`; public static `SetDialogueRunning(bool)`; private `FadeUIIn()`; public `EnableVisualDialogueUI()`; public `ProceedDialogue(int choice = -1)` (async void — invoke with explicit `-1`); `AreaUI.SetConversationUI(bool)` / `AreaUI.HideAreaUI(float)` public.
- Produces: `DialogueIntegration.Available` (knob && prefix applied && all members found) — gates `InjectEvents`; `DialogueIntegration.StartDialogue_Prefix`.

- [ ] **Step 1: DialogueIntegration** — create `Integration/DialogueIntegration.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using DawnKit.Core.Lifecycle;
using Ink.Runtime;
using UnityEngine;

namespace DawnKit.Integration.Dialogues
{
    /// <summary>
    /// Story serving for mod events (EVENT-SPEC §3/§6). Vanilla
    /// DialogueManagerINK.StartDialogue ignores Dialogue.textFile and loads by
    /// NAME from Addressables/Resources (DialogueManagerINK.cs:260-344) — a
    /// runtime TextAsset exists in neither store, so this prefix replicates the
    /// vanilla success wiring against the registered story and skips the
    /// original. Vanilla names pass through untouched. Fail-safe: if the patch
    /// target or ANY tracked member is missing, <see cref="Available"/> is false
    /// and events are never injected — a node whose story can't be served must
    /// never reach the map.
    /// </summary>
    internal static class DialogueIntegration
    {
        /// <summary>[Events] Enabled config knob (set by DawnKitPlugin.Awake).</summary>
        internal static bool Enabled = true;

        /// <summary>Set by PatchManager when the StartDialogue prefix applied cleanly.</summary>
        internal static bool PatchApplied;

        // Tracked members (PatchManager resolves + logs found/missing for each).
        internal static FieldInfo DialogueTempField;
        internal static FieldInfo StoryField;
        internal static FieldInfo DialogueNameField;
        internal static FieldInfo AreaUIField;
        internal static MethodInfo HidePortraitMethod;
        internal static MethodInfo SetDialogueRunningMethod;
        internal static MethodInfo FadeUIInMethod;
        internal static MethodInfo EnableVisualDialogueUIMethod;
        internal static MethodInfo ProceedDialogueMethod;

        internal static bool MembersResolved =>
            DialogueTempField != null && StoryField != null && DialogueNameField != null &&
            AreaUIField != null && HidePortraitMethod != null && SetDialogueRunningMethod != null &&
            FadeUIInMethod != null && EnableVisualDialogueUIMethod != null && ProceedDialogueMethod != null;

        internal static bool Available => Enabled && PatchApplied && MembersResolved;

        /// <summary>
        /// Replicates, in order, the vanilla wiring for a served story
        /// (StartDialogue preamble + the OnAssetLoaded success branch,
        /// DialogueManagerINK.cs:260-307), then skips the original.
        /// </summary>
        internal static bool StartDialogue_Prefix(DialogueManagerINK __instance, string dialogueFile)
        {
            try
            {
                if (!Available || string.IsNullOrEmpty(dialogueFile))
                {
                    return true;
                }
                EventRegistration reg = Registry.Events.FirstOrDefault(r =>
                    r.Event != null && r.Spec != null &&
                    string.Equals(r.Spec.Name, dialogueFile, StringComparison.OrdinalIgnoreCase));
                if (reg == null)
                {
                    return true; // vanilla event — vanilla loading
                }

                DawnKitPlugin.Log.LogInfo($"[DawnKit] Serving mod event story \"{reg.Spec.Name}\" ({reg.Spec.Owner}).");
                DialogueTempField.SetValue(__instance, dialogueFile);
                HidePortraitMethod.Invoke(__instance, new object[] { 0f, 0f });
                SetDialogueRunningMethod.Invoke(null, new object[] { true });
                StoryField.SetValue(__instance, new Story(reg.Spec.StoryJson));
                DialogueNameField.SetValue(__instance, reg.Spec.Name);
                FadeUIInMethod.Invoke(__instance, null);
                EnableVisualDialogueUIMethod.Invoke(__instance, null);
                var areaUI = (AreaUI)AreaUIField.GetValue(__instance);
                areaUI.SetConversationUI(true);
                areaUI.HideAreaUI(0.25f);
                CanvasGroup cg = __instance.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.blocksRaycasts = true;
                }
                ProceedDialogueMethod.Invoke(__instance, new object[] { -1 });
                return false;
            }
            catch (Exception ex)
            {
                // Fall through to vanilla: its Addressables/Resources misses end in
                // HandleDialogueFailure (SetDialogueRunning(false) + CloseDialogue,
                // DialogueManagerINK.cs:316-343) — a clean abort, not a hang.
                DawnKitPlugin.Log.LogError($"[DawnKit] StartDialogue prefix failed for '{dialogueFile}' — falling back to vanilla loading: {ex}");
                return true;
            }
        }
    }
}
```

- [ ] **Step 2: PatchManager** — in `PatchManager.cs`:

2a. `using DawnKit.Integration.Dialogues;` at the top.

2b. Give `PatchDef` an applied callback:

```csharp
        private sealed class PatchDef
        {
            internal string Label;
            internal Func<MethodInfo> Target;
            internal MethodInfo Prefix;
            internal MethodInfo Postfix;
            internal Action OnApplied;
        }
```
and in the apply loop, after `FoundCount++;` add `def.OnApplied?.Invoke();`.

2c. Add the def to the list (new `// ---- Events (Integration.Dialogues) ----` section):

```csharp
                new PatchDef
                {
                    Label = "DialogueManagerINK.StartDialogue(string)",
                    Target = () => AccessTools.Method(typeof(DialogueManagerINK), "StartDialogue", new[] { typeof(string) }),
                    Prefix = AccessTools.Method(typeof(DialogueIntegration), nameof(DialogueIntegration.StartDialogue_Prefix)),
                    OnApplied = () => DialogueIntegration.PatchApplied = true,
                },
```

2d. Member resolution — append to the non-patch members block at the end of `ApplyAll`:

```csharp
            DialogueIntegration.DialogueTempField = ResolveMember("DialogueManagerINK.dialogueTemp",
                () => AccessTools.Field(typeof(DialogueManagerINK), "dialogueTemp"));
            DialogueIntegration.StoryField = ResolveMember("DialogueManagerINK.story",
                () => AccessTools.Field(typeof(DialogueManagerINK), "story"));
            DialogueIntegration.DialogueNameField = ResolveMember("DialogueManagerINK.dialogueName",
                () => AccessTools.Field(typeof(DialogueManagerINK), "dialogueName"));
            DialogueIntegration.AreaUIField = ResolveMember("DialogueManagerINK.areaUI",
                () => AccessTools.Field(typeof(DialogueManagerINK), "areaUI"));
            DialogueIntegration.HidePortraitMethod = ResolveMember("DialogueManagerINK.HidePortrait(float, float)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "HidePortrait", new[] { typeof(float), typeof(float) }));
            DialogueIntegration.SetDialogueRunningMethod = ResolveMember("DialogueManagerINK.SetDialogueRunning(bool)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "SetDialogueRunning", new[] { typeof(bool) }));
            DialogueIntegration.FadeUIInMethod = ResolveMember("DialogueManagerINK.FadeUIIn",
                () => AccessTools.Method(typeof(DialogueManagerINK), "FadeUIIn"));
            DialogueIntegration.EnableVisualDialogueUIMethod = ResolveMember("DialogueManagerINK.EnableVisualDialogueUI",
                () => AccessTools.Method(typeof(DialogueManagerINK), "EnableVisualDialogueUI"));
            DialogueIntegration.ProceedDialogueMethod = ResolveMember("DialogueManagerINK.ProceedDialogue(int)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "ProceedDialogue", new[] { typeof(int) }));

            if (DialogueIntegration.Enabled && !(DialogueIntegration.PatchApplied && DialogueIntegration.MembersResolved))
            {
                DawnKitPlugin.Log.LogError("[DawnKit] Events integration disabled — StartDialogue target or a tracked member is missing; mod events will NOT be injected (fail-safe: an unservable story must never reach the map).");
            }
```

- [ ] **Step 3: Gate injection** — in `InjectionEngine.InjectEvents` (Task 7), insert right after the `Registry.Events.Count == 0` early-return:

```csharp
            if (!Integration.Dialogues.DialogueIntegration.Available)
            {
                return; // knob off or serving path broken — EVENT-SPEC §5/§6 fail-safe
            }
```

- [ ] **Step 4: Knob + version** — in `DawnKitPlugin.cs`:

Add field `private ConfigEntry<bool> eventsEnabled;` and in `Awake()` after the `diagnosticsDump` binding:

```csharp
            eventsEnabled = Config.Bind("Events", "Enabled", true,
                "Master switch for modded opportunity events (fail-safe rule): false = no event " +
                "injection and no story-serving patch behavior — vanilla events untouched.");
            Integration.Dialogues.DialogueIntegration.Enabled = eventsEnabled.Value;
```
(Place it BEFORE the `PatchManager.ApplyAll(harmony)` call so the disabled-warning logic in Step 2d sees the real knob value.)

- [ ] **Step 5: Build**

Run: `dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add DC.DawnKit/src/DawnKit/Integration/DialogueIntegration.cs DC.DawnKit/src/DawnKit/Core/PatchManager.cs DC.DawnKit/src/DawnKit/Core/InjectionEngine.cs DC.DawnKit/src/DawnKit/DawnKitPlugin.cs
git commit -m "feat: StartDialogue prefix serves registered event stories ([Events] Enabled knob, tracked members, fail-safe)"
```

---

### Task 9: DawnKit.Packs — manifest v2, scanner, versions, docs

**Files:**
- Modify: `DC.DawnKit/src/DawnKit.Packs/SchemaGate.cs`
- Modify: `DC.DawnKit/src/DawnKit.Packs/PackManifest.cs`
- Modify: `DC.DawnKit/src/DawnKit.Packs/PackScanner.cs`
- Modify: `DC.DawnKit/src/DawnKit.Packs/PacksPlugin.cs`
- Modify: `DC.DawnKit/src/DawnKit/DawnKitPlugin.cs`
- Modify: `DC.DawnKit/RELEASE-NOTES.md`, `DC.DawnKit/EVENT-SPEC.md`, `AGENTS.md`

**Interfaces:**
- Consumes: `DawnKit.Events.Build(...)` public API (Task 6) — `DawnKit.Packs` stays a pure public-API consumer (no new references).
- Produces: `EventManifest { name, storyFile, minLevel, maxLevel, unique }`; `SchemaGate.SupportedSchemaVersion == 2`.

- [ ] **Step 1: SchemaGate** — `SupportedSchemaVersion = 2`, and the self-check table becomes:

```csharp
            var cases = new (int? declared, int effective, bool supported)[]
            {
                (null, 1, true),                      // absent → 1 → loads
                (0, 0, true),                         // lower than supported → loads
                (1, 1, true),                         // v1 (cards/weapons/powers/startingCards) → loads
                (2, 2, true),                         // v2 (adds events, EVENT-SPEC §4) → loads
                (3, 3, false),                        // newer → refused entirely
                (int.MaxValue, int.MaxValue, false),  // absurdly newer → refused
            };
```

- [ ] **Step 2: PackManifest** — add `public List<EventManifest> events;` to `PackManifest` (comment: `// v2 (EVENT-SPEC.md §4) — optional; requires schemaVersion 2.`) and the DTO:

```csharp
    /// <summary>
    /// EVENT-SPEC.md §4 — an opportunity event: name (its whole identity — no
    /// numeric ID) + pack-relative compiled Ink JSON + selection filters.
    /// </summary>
    public class EventManifest
    {
        public string name;
        public string storyFile;
        public int minLevel;
        public int maxLevel;
        public bool unique;
    }
```

- [ ] **Step 3: PackScanner** — in `RegisterPack`:

3a. Content check:

```csharp
            bool hasEvents = pm.events != null && pm.events.Count > 0;
            if (!hasCards && !hasWeapons && !hasPowers && !hasStartingCards && !hasEvents)
            {
                PacksPlugin.Log.LogError($"[DawnKit.Packs] {manifestFile}: no cards/weapons/weaponPowers/startingCards/events in manifest — pack skipped.");
                return;
            }
```

3b. Gate the no-idBlock warning on card-space content — wrap the existing `pm.idBlock == null` warning branch in `if (hasCards || hasWeapons || hasPowers || hasStartingCards)` (an events-only pack needs no block; only emit the synthetic-set warning when card-space content exists).

3c. Advisory for missing handshake (validator enforces it offline; the loader stays lenient — this loader DOES understand events):

```csharp
            if (hasEvents && SchemaGate.Effective(pm.schemaVersion) < 2)
            {
                PacksPlugin.Log.LogWarning($"[DawnKit.Packs] {packName}: ships events without \"schemaVersion\": 2 — older DawnKit.Packs releases will silently drop them (EVENT-SPEC §4). Declare schemaVersion 2.");
            }
```

3d. Events loop, after the startingCards loop (`int events = 0;` beside the other counters — rename to `int eventsRegistered = 0;` to avoid colliding with the `DawnKit.Events` type name):

```csharp
            foreach (EventManifest em in pm.events ?? new List<EventManifest>())
            {
                if (em == null)
                {
                    PacksPlugin.Log.LogError($"[DawnKit.Packs] {packName}: null event entry — skipped.");
                    failed++;
                    continue;
                }
                EventBuilder b = Events.Build(em.name).Owner(packName)
                    .Levels(em.minLevel, em.maxLevel)
                    .Unique(em.unique);
                if (!string.IsNullOrEmpty(em.storyFile))
                {
                    b.StoryFile(PackPath(packDir, em.storyFile));
                }
                // else: Register() fails with "event has no story" — per-item isolation
                if (b.Register().Ok) eventsRegistered++; else failed++;
            }
```

3e. Extend the summary log line:

```csharp
            PacksPlugin.Log.LogInfo($"[DawnKit.Packs] {packName}: registered {cards} cards, {weapons} weapons, {powers} weapon powers, {startingCards} starting cards, {eventsRegistered} events{failNote} (applied at asset load).");
```

- [ ] **Step 4: Versions + docs**

- `PacksPlugin.Version` → `"0.9.0"`; `DawnKitPlugin.Version` → `"0.9.0"`.
- `DC.DawnKit/RELEASE-NOTES.md`: retitle to `# DawnKit v0.9.0 — release notes` and add a "What's new in 0.9.0 — opportunity events (manifest v2)" section above the 0.8.0 one, covering: `events` manifest array + schemaVersion 2 handshake, `DawnKit.Events.Build(...).StoryFile(...).Levels(...).Unique().Register()`, world-phase injection, StartDialogue serving, `[Events] Enabled` knob, the honest-degradation note (mid-run uninstall: a dealt node aborts cleanly at pickup — EVENT-SPEC §9), and the inklecate v1.0.0 / inkVersion 20 authoring pin.
- `DC.DawnKit/EVENT-SPEC.md`: header `# Event Spec v0.1 (DRAFT) — opportunity events (Ink)` → `# Event Spec v1.0 — opportunity events (Ink)`; drop the DRAFT wording where present (the spec is now the shipped E1 contract).
- `AGENTS.md`: in the task-routing row for designing/drafting content, extend the contract list to `(DC.DawnKit/CARD-PACK-SPEC.md / DC.DawnKit/WEAPON-SPEC.md / DC.DawnKit/EVENT-SPEC.md)`.

- [ ] **Step 5: Build + full test suite**

Run: `dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"` — Expected: 0 errors.
Run: `python -m pytest tools/tests -q` — Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add DC.DawnKit/src/DawnKit.Packs DC.DawnKit/src/DawnKit/DawnKitPlugin.cs DC.DawnKit/RELEASE-NOTES.md DC.DawnKit/EVENT-SPEC.md AGENTS.md
git commit -m "feat: pack.json v2 events loading (SchemaGate 2) — DawnKit 0.9.0"
```

---

### Task 10: Full gates + in-game QA (EVENT-SPEC §8)

**Files:** none (verification only; fixes loop back to the owning task).

- [ ] **Step 1: Offline gates**

```bash
python -m pytest tools/tests -q
python tools/dmk.py validate --all
python tools/dmk.py validate DC.DawnKit/examples/ExampleEventPack/pack.json
dotnet build -c Release "DC.DawnKit\src\DawnKit.slnx"
```
Expected: all green / exit 0. (`validate --all` covers only repo-root `DC.*` packs — the example pack is validated by the explicit second call.)

- [ ] **Step 2: Deliberate-mutation spot checks** (offline, throwaway edits — revert after):
  - `gold:50` → `glod:50` in `HelloWayfarer.ink.json`: `dmk validate` ERROR `unknown_command` with did-you-mean `'gold'`.
  - `"inkVersion":20` → `21`: ERROR `bad_ink_version`.
  - `"name": "Hello Wayfarer"` → `"Mimic"`: ERROR `event_name_collision`.

- [ ] **Step 3: In-game QA (human, <15 min, per EVENT-SPEC §8)** — with the example pack in the configured PacksPath:
  1. Boot. `BepInEx\LogOutput.log` shows `Target found: DialogueManagerINK.StartDialogue(string)` + all 9 `DialogueManagerINK.*` members, `Command vocabulary: ... 99 dialogue`, `Example Event Pack: registered ... 1 events`, and after world load `Example Event Pack: 1 events injected`; boot report shows 1 applied, 0 conflicts.
  2. Start a run, explore until an Opportunity node appears (restart the area if RNG withholds it); pick it: dialogue opens, speaker "A Wayfarer", both choices render; "Wave back" → gold +50 in the HUD → dialogue closes → node consumed; save/continue round-trips.
  3. Edge cases: other choice (no action); set `"unique": true`, pick once, verify no re-offer later in the run and after continue; remove the pack mid-run — vanilla events unaffected, a dealt mod node aborts cleanly at pickup; `[Events] Enabled = false` → boot report 0 events, map never offers it.
  4. **If the story misrenders/misbehaves:** compile `HelloWayfarer.ink` with inklecate v1.0.0, replace the `.ink.json`, re-run offline gates, retest (see Task 4 note).

- [ ] **Step 4: Final commit (QA notes / any fixes)**

```bash
git add -A
git commit -m "docs: EVENT-SPEC E1 QA pass notes"
```

---

## Self-Review (done at plan time)

- **Spec coverage:** §1 pins → InkStoryLint/validator + authoring docs (T2/T4/T6); §2 behavior → T7/T8; §3 architecture (registry, naming rule, phase-2 injection, story serving, pack data flow, C# API) → T6/T7/T8/T9; §4 formats + validator → T2/T3/T4/T9; §5 knobs → T8; §6 patch targets + fail-safe → T8; §7 dataset → T4; §8 testing → T10; §9 save considerations → behavior emerges from name-keying (no code); §10 E1 scope only — E2/E3 items (dmk ink, `dmk new` scaffolding, TUTORIAL, C# example twin) deliberately excluded; §11 open questions left open (STORYFUNCTION rejected, no frequency knobs, no sim modeling).
- **Deviations from spec text (intentional, functionally equivalent):** (1) the "extractor grows a dialogue-textfile-index" is implemented as `gamedata.py` loaders over the EXISTING extraction (`textFile.m_PathID` → data-index TextAsset entries) — no extractor change, no game-dir re-extraction needed; (2) the example story is hand-compiled from verified shipped encodings with an explicit inklecate fallback in QA.
- **Type consistency:** `EventRegistration.Spec/.Event/.Text`, `ParsedEvent.{Owner,Name,StoryJson,MinLevel,MaxLevel,Unique}`, kind string `"event"`, `DialogueIntegration.Available` are used identically across T6–T9.
