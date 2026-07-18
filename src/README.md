# Dawncaster.Sandbox — hello-card BepInEx plugin

Proof-of-concept content-injection plugin for Dawncaster (Steam, Unity 2022.3.62f2, Mono).
Injects one custom card, **SandboxStrike** (id 900001, Melee Action, 1 STR, `damage:6`),
into `AssetManager.allCards` / `playercards` at asset-load time. No game files are modified;
everything is runtime injection via Harmony.

## Prerequisites

- BepInEx **5.4.23.2** (win_x64) extracted into the game root
  `E:\Games\Steam\steamapps\common\Dawncaster` (adds `BepInEx\`, `winhttp.dll`,
  `doorstop_config.ini` next to `Dawncaster.exe`). Zip archived at
  `D:\temp\claude\dawncaster-mods\tools\bin\BepInEx_win_x64_5.4.23.2.zip`.
- .NET SDK (any recent; builds `net472` via `Microsoft.NETFramework.ReferenceAssemblies`).

## Build

```powershell
dotnet build "D:\temp\claude\dawncaster-mods\src\Dawncaster.Sandbox\Dawncaster.Sandbox.csproj" -c Release
```

Output: `src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll` (references to
BepInEx/Harmony/Assembly-CSharp/UnityEngine are `Private=false`, so only this one DLL ships).

## Install

```powershell
Copy-Item "D:\temp\claude\dawncaster-mods\src\Dawncaster.Sandbox\bin\Release\Dawncaster.Sandbox.dll" `
          "E:\Games\Steam\steamapps\common\Dawncaster\BepInEx\plugins\" -Force
```

Launch the game normally (Steam must be running).

## How it works

- `[BepInPlugin("com.dawncastermods.sandbox", "Dawncaster Sandbox", "0.1.0")]`, BepInEx 5 style.
- Harmony **postfix on `AssetManager.SetPlayerAssetsLoaded()`** — the game's async boot path
  (`GameLoader.LoadGameAsync` → `CreateCardCollectionsAsync` → `SetPlayerAssetsLoaded()`,
  see `decompiled/GameLoader.cs:219-223`) calls it after all cards are loaded.
- Safety-net postfix on `AssetManager.LoadPlayerAssets()` too, because the synchronous path
  sets the `playerAssetsLoaded` flag directly without calling `SetPlayerAssetsLoaded()`.
  Injection is idempotent (checks `allCards` for the card name first, plus a cardID
  collision scan), so both hooks firing is harmless. This also re-injects after
  `ForceReloadAssets()` wipes the lists on a version change.
- The card is `ScriptableObject.CreateInstance<Card>()` with **all list fields initialized
  non-null** (only `keywords` is initialized inline in `Card.cs`) and a non-null
  `CardEnchantments` payload to avoid NREs in Codex display code. After appending to
  `allCards`/`playercards` it calls `AssetManager.RefreshCaches()` and, if a player is
  loaded, `AssetManager.CreateRunLists()`.

## Verification results (2026-07-17)

Game launched with the plugin installed; `BepInEx\LogOutput.log` (mirrored into `Player.log`):

```
[Message:   BepInEx] BepInEx 5.4.23.2 - Dawncaster
[Info   :   BepInEx] Running under Unity v2022.3.62.7762112
[Info   :   BepInEx] Loading [Dawncaster Sandbox 0.1.0]
[Info   :Dawncaster Sandbox] [Sandbox] Dawncaster Sandbox 0.1.0 loaded, hooks installed.
[Message:   BepInEx] Chainloader startup complete
[Info   :Dawncaster Sandbox] [Sandbox] Injected SandboxStrike (id 900001), allCards=2478 (hook: SetPlayerAssetsLoaded)
```

- Injection fired exactly once, via the `SetPlayerAssetsLoaded` postfix (the async boot path;
  the `LoadPlayerAssets` safety net never needed to fire).
- `allCards` count after injection: **2478** (2477 baseline + 1 injected).
- No `[Sandbox] ... failed` lines, no NREs in `Player.log`.
