#!/usr/bin/env python3
"""Assemble a DawnKit release zip (M2, MOD-TOOLKIT §4).

Steps:
  1. Read the release version from DawnKitPlugin.cs (single source of truth;
     the three plugins version in lockstep — src/README.md VERSIONING).
  2. dotnet build -c Release the DawnKit solution.
  3. Assemble dist/DawnKit-v<version>/ with the two shippable DLLs
     (DawnKit.dll + DawnKit.Packs.dll — the sandbox is dev-only and never
     ships), a generated INSTALL.md and a LICENSES.md placeholder.
  4. Zip it DETERMINISTICALLY (sorted entries, fixed timestamps, fixed file
     modes) to dist/DawnKit-v<version>.zip and print its SHA256. The zip hash
     is reproducible for identical input bytes; the DLLs themselves embed a
     per-build MVID, so "same sources, different build" still differs.

dist/ is gitignored. This script builds and packages only — it never touches
the game installation and never publishes anything.
"""

from __future__ import annotations

import hashlib
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "DC.DawnKit" / "src"
SOLUTION = SRC / "DawnKit.slnx"
PLUGIN_CS = SRC / "DawnKit" / "DawnKitPlugin.cs"
DIST = REPO / "dist"

# Fixed timestamp for every zip entry (deterministic archives). Arbitrary but
# stable; never derived from the clock.
ZIP_DATE_TIME = (2026, 1, 1, 0, 0, 0)

SHIPPED_DLLS = [
    SRC / "DawnKit" / "bin" / "Release" / "DawnKit.dll",
    SRC / "DawnKit.Packs" / "bin" / "Release" / "DawnKit.Packs.dll",
]

INSTALL_MD = """# DawnKit {version} — install guide

DawnKit is a content-injection engine for Dawncaster (Steam, Windows x64).
It ships two BepInEx plugins:

- `DawnKit.dll` — the engine (lifecycle, injection, set-screen/Codex/class
  integration, validation, boot report). Ships zero content.
- `DawnKit.Packs.dll` — the data-driven pack loader (reads `pack.json`
  content packs; no code needed to make a mod).

## 1. Prerequisite: BepInEx 5.4.23 (x64)

DawnKit requires BepInEx 5.4.23 for Windows x64 (verified against 5.4.23.2):

> https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2
> (download `BepInEx_win_x64_5.4.23.2.zip`)

Extract it into the game root (the folder containing `Dawncaster.exe`,
typically `...\\Steam\\steamapps\\common\\Dawncaster`) so that `BepInEx\\`,
`winhttp.dll` and `doorstop_config.ini` sit next to `Dawncaster.exe`.
Launch the game once so BepInEx creates its folder structure, then quit.

## 2. Install the DLLs

Copy `DawnKit.dll` and `DawnKit.Packs.dll` from this folder into:

    <game root>\\BepInEx\\plugins\\

## 3. Install content packs

Packs are folders with a `pack.json` manifest. They go under:

    <game root>\\BepInEx\\plugins\\DawncasterPacks\\<PackName>\\pack.json

(plus the pack's `art\\*.png` files, if any). The folder is created on first
run if missing; you can point somewhere else via `Packs.PacksPath` below.
On boot, each pack appears as its own toggleable card set in the
run-settings screen, and the set screen shows a status row
(`DawnKit: N mods, M items loaded`) — plus an error count if anything was
refused (details in `BepInEx\\LogOutput.log`).

Packs may declare `"schemaVersion": 1` in `pack.json` (absent means 1).
A pack declaring a NEWER schema version than this DawnKit.Packs supports is
refused entirely with a clear log error — the fix is updating DawnKit.

## 4. Configuration reference

`BepInEx\\config\\dcmods.dawnkit.cfg` (engine — created on first run):

| Key | Default | Meaning |
|---|---|---|
| `Engine.Enabled` | `true` | Master switch. `false` = no patches, no injection — completely vanilla. |
| `Engine.VerboseLogging` | `false` | Decision-level debug logs. |
| `Engine.DiagnosticsDump` | `false` | Write `BepInEx\\DawnKit-diagnostics.txt` each boot (per-mod content, conflicts, unresolved references) — attach it to bug reports. |

`BepInEx\\config\\dcmods.dawnkit.packs.cfg` (pack loader):

| Key | Default | Meaning |
|---|---|---|
| `Packs.PacksPath` | `<plugins>\\DawncasterPacks` | Directory whose subfolders are scanned for `pack.json`. |
| `Packs.ExpansionOverride` | *(empty)* | Emergency: force every pack card into a native card set; disables per-pack set rows. |
| `Packs.AutoDiscoverModCards` | `true` | Render mod cards face-up in the Codex (in-memory only; the Codex save file is never written). |

## 5. Uninstall

Delete the two DLLs (and any pack folders) from `BepInEx\\plugins\\`.
Removal degrades safely — these behaviors are verified in the game's own
code, not hoped for:

- A saved run using a mod **weapon** falls back to the Longsword with a
  logged error; the run continues.
- A missing mod **weapon power** fails activation gracefully (no crash).
- Stale mod **set values** in saves/PlayerPrefs are harmless — the game
  round-trips them as plain integers.
- **Codex** entries for mod cards are inert residue; DawnKit only marks
  discovery in memory and never writes the Codex save file.
- "Run it back" with a mod **loadout** just fails the name lookup — the
  last-character config stores names, which simply no longer resolve.

DawnKit itself never writes save data. To disable without uninstalling,
set `Engine.Enabled = false`.

## 6. Licensing

See `LICENSES.md`. DawnKit is an unaffiliated fan project and distributes
no game assets.
"""

LICENSES_MD = """# Licenses — DawnKit {version}

**TBD.** The DawnKit license is not yet decided (MOD-TOOLKIT.md open
question #5: engine license, docs license, and guidance for pack authors'
own content are all pending). Until a license is chosen and this file is
replaced, all rights are reserved by the authors and this build is intended
for private testing only — do not redistribute.

Notes for the eventual decision:

- DawnKit is an unaffiliated fan project for Dawncaster (Wanderlost).
  It distributes **no game assets**; it links against the game's own
  assemblies at runtime only.
- BepInEx is a separate project with its own license (LGPL-2.1) and is NOT
  bundled in this archive — users install it from the official release.
"""


def read_version() -> str:
    text = PLUGIN_CS.read_text(encoding="utf-8")
    m = re.search(r'public const string Version = "([^"]+)";', text)
    if not m:
        sys.exit(f"ERROR: could not find Version const in {PLUGIN_CS}")
    return m.group(1)


def build() -> None:
    print(f"[package_release] dotnet build {SOLUTION} -c Release")
    result = subprocess.run(
        ["dotnet", "build", str(SOLUTION), "-c", "Release", "--nologo", "-v", "q"],
        cwd=str(REPO),
    )
    if result.returncode != 0:
        sys.exit("ERROR: dotnet build failed — release not assembled.")


def assemble(version: str) -> Path:
    stage = DIST / f"DawnKit-v{version}"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)

    for dll in SHIPPED_DLLS:
        if not dll.is_file():
            sys.exit(f"ERROR: expected build output missing: {dll}")
        shutil.copy2(dll, stage / dll.name)

    (stage / "INSTALL.md").write_text(INSTALL_MD.format(version=version), encoding="utf-8", newline="\n")
    (stage / "LICENSES.md").write_text(LICENSES_MD.format(version=version), encoding="utf-8", newline="\n")
    return stage


def zip_deterministic(stage: Path, version: str) -> Path:
    zip_path = DIST / f"DawnKit-v{version}.zip"
    if zip_path.exists():
        zip_path.unlink()

    entries = sorted(p for p in stage.rglob("*") if p.is_file())
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for path in entries:
            arcname = f"DawnKit-v{version}/" + path.relative_to(stage).as_posix()
            info = zipfile.ZipInfo(arcname, date_time=ZIP_DATE_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16  # rw-r--r--, no OS-dependent bits
            info.create_system = 3  # fixed (unix) regardless of build host
            zf.writestr(info, path.read_bytes(), compresslevel=9)
    return zip_path


def main() -> None:
    version = read_version()
    print(f"[package_release] DawnKit version {version} (from {PLUGIN_CS.name})")
    build()
    stage = assemble(version)
    zip_path = zip_deterministic(stage, version)
    sha = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    print(f"[package_release] Assembled: {stage}")
    for p in sorted(stage.iterdir()):
        print(f"[package_release]   {p.name} ({p.stat().st_size:,} bytes)")
    print(f"[package_release] Zip:    {zip_path} ({zip_path.stat().st_size:,} bytes)")
    print(f"[package_release] SHA256: {sha}")


if __name__ == "__main__":
    main()
