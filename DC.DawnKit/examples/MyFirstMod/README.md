# MyFirstMod — the 20-line DawnKit example mod

A complete, compiling BepInEx mod that adds **one card set** and **one card**
("Practice Strike") to Dawncaster through the DawnKit public API. This is the
whole mod — no Harmony, no game-assembly reference, no lifecycle code. The same
content expressed as pure data lives in [`../ExamplePack/`](../ExamplePack/README.md)
(pick whichever consumption mode you prefer). Full API reference:
[`../../API.md`](../../API.md).

## Build

```powershell
# 1. Build the engine once (produces src\DawnKit\bin\Release\DawnKit.dll):
dotnet build "D:\src\mods\dawncaster-mods\DC.DawnKit\src\DawnKit.slnx" -c Release

# 2. Build this example against it:
dotnet build "D:\src\mods\dawncaster-mods\DC.DawnKit\examples\MyFirstMod\MyFirstMod.csproj" -c Release
```

Output: `bin\Release\MyFirstMod.dll` — the only file your mod ships. Every
reference in the csproj is `Private=false` (`DawnKit.dll` via `HintPath`,
`BepInEx.dll`, the two Unity modules), so nothing else is copied. For a
standalone mod outside this repo, point `<DawnKitDll>` at your game's
`BepInEx\plugins\DawnKit.dll` instead of the repo build output.

## Install

Copy `MyFirstMod.dll` into `BepInEx\plugins\` next to `DawnKit.dll` (the engine
must be installed — see [`../../src/README.md`](../../src/README.md)). On boot,
`BepInEx\LogOutput.log` shows the card injected and the DawnKit boot report
counts your mod (e.g. `5 mods · 64 items registered`); in-game, "My First Mod"
appears as its own toggleable row on the run-settings card-sets screen and
Practice Strike enters the run pool with the generated placeholder art.

> Don't install this alongside `../ExamplePack/` — they register the same set
> and card, so the second one is refused with both claimants named (which is
> itself a nice live demo of the conflict report).

## The code, line by line

```csharp
using BepInEx;
using DawnKit;
```

`BepInEx` for the plugin base class; `DawnKit` for everything else — registries
(`Sets`, `Cards`), the builder types, the clean-spelled enums, `RegisterResult`.
You never `using HarmonyLib` and never reference `Assembly-CSharp.dll`: the
engine owns all patches and all game-type handling.

```csharp
[BepInPlugin("com.example.myfirstmod", "My First Mod", "1.0.0")]
```

Standard BepInEx identity: GUID, display name, version. If you never call
`.Owner(...)`, DawnKit attributes every registration to your plugin
automatically (resolved from the call stack — here the assembly name,
`MyFirstMod`) in the boot report, conflict messages and diagnostics dump; call
`.Owner("...")` for a prettier display name.

```csharp
[BepInDependency(DawnKitPlugin.Guid)] // load after the DawnKit engine
```

Hard dependency on `dcmods.dawnkit`: BepInEx loads DawnKit first (so its
registries exist when your `Awake` runs) and skips your plugin with a clear log
message if the engine is missing.

```csharp
private void Awake()
```

**Register in `Awake()`** — that is the whole lifecycle contract from your
side. Registration is declarative and durable: the engine constructs and
injects the card when the game loads player assets (phase 1), resolves any
name-declared references when world assets load (phase 2), refreshes caches
and run lists, and automatically re-applies everything after the game's
version-change asset wipe (`ForceReloadAssets`). You never observe any of it.

```csharp
SetHandle set = Sets.Register("My First Mod", author: "dcmods.example");
```

One line = one card set: a named, colored, toggleable row with eye-icon
preview in run settings and Sunforge, exactly like the official sets. The
`(author, name)` pair deterministically derives your mod's 100-ID block —
`700,000,000 + (FNV1a32("dcmods.example/my first mod") % 1,000,000) × 100 =
737,673,400` — and from it the set's expansion value (`377734`). Same pair,
same block, every machine, forever (saves depend on it), which is why the
author string matters: **change both names when you fork this example.** If
the block is already owned by a different mod, `Register` returns `null` and
logs both owners (hard refusal, no silent drift — the fix is an explicit
block; see API.md §AutoId).

```csharp
RegisterResult result = Cards.Build("Practice Strike")
```

Builders start from the name because **names are identity** in Dawncaster
(case-insensitive, checked against the shipped pool and every other mod).

```csharp
    .InSet(set)                     // our own toggleable card set row
    .AutoId()                       // deterministic ID from the set's block
```

`.InSet(set)` gives the card the set's synthetic expansion (that's what makes
the toggle row work); `.AutoId()` allocates the next free ID from the set's
block in registration order — cards bottom-up (this one gets 737,673,400),
weapons top-down from the block end. No manual ID bookkeeping. (Have an ID
block registered in `docs/ID-REGISTRY.md`? Use `.Id(...)` instead.)

```csharp
    .Type(CardType.Melee)
    .Category(CardCategory.Action)
    .Rarity(Rarity.Common)
```

Typed overloads use DawnKit's clean-spelled enum mirrors — you write
`Rarity.Common` and `Suffix.Physical`; the engine maps to the game's canonical
typos (`CardRariry`, `Phyisical`) internally. Every setter also has a string
overload taking the game's exact spelling, for data-driven callers. Suffix is
optional and defaults to `None`.

```csharp
    .Cost("STR", 1)
    .Description("Deal 6 damage.")
    .Effect(Trigger.PlayAction, "damage:6")
```

One strength, the card text, and one effect: when played (`PlayAction`), run
the effect-DSL codeLine `damage:6`. Every command in a codeLine is validated
at `Register()` against the game's extracted 565-command vocabulary — typos
fail fast with did-you-mean suggestions, not silently at draw time. No
`.Art(...)` call means the card gets the generated cost-colored placeholder;
pass an absolute path to a 512×512 PNG when you have real art.

```csharp
    .Register();

if (!result.Ok)
    Logger.LogError($"[MyFirstMod] Practice Strike refused: {result.Error}");
```

`Register()` runs all validation now and returns a `RegisterResult` — **always
check it**. `Ok` means the card is recorded and will be injected at asset
load; failure means this one item was skipped (never the game, never your
whole mod) and `result.Error` carries the full named reason ("unknown effect
command 'damge' … did you mean 'damage'?", "cardID … already owned by …").
The same error also lands in the engine's boot report, the in-game status row
and the `DiagnosticsDump` file, so even without this log line nothing fails
silently.

## Verify it worked

`BepInEx\LogOutput.log` after boot:

```
[Info   :   BepInEx] Loading [My First Mod 1.0.0]
[Info   :   DawnKit] [DawnKit] MyFirstMod: 1 cards injected, 0 skipped (hook: SetPlayerAssetsLoaded)
[Info   :   DawnKit] [DawnKit] Synthetic card sets: ... My First Mod=(CardExpansions)377734 [1 cards]
[Info   :   DawnKit] [DawnKit] ── Boot report ──...
[Info   :   DawnKit] [DawnKit] 5 mods · 64 items registered, 64 applied, 0 failed · 0 conflicts
[Info   :   DawnKit] [DawnKit]   MyFirstMod: 1 registered, 1 applied, 0 failed
```

(Those are real lines from this example's verification boot.)

(Counts assume the four repo packs are also installed; alone it reads
`1 mods · 1 items`.) In game: new run → card sets screen → the "My First Mod"
row with eye-icon preview showing Practice Strike.

## Next steps

- `Weapons.Build(...)` / `WeaponPowers.Build(...)` — starting weapons and
  their activated powers, offered per class (`.ForClasses("Warrior", ...)`).
- Referenced statuses/cards by name (`EffectSpec.ReferenceStatus` /
  `ReferenceCards`) — the engine resolves them at the correct load phase.
- The full builder surface, validation rules, knobs and log vocabulary:
  [`../../API.md`](../../API.md).
