# Headless Combat Sim Harness — Implementation Plan (M0 + M1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the shadow-UnityEngine bet (M0) and, if it holds, deliver a working headless single-combat simulator that runs one real deck vs. one real `Battle` to a win/lose result through the game's own `SpellEffects`/`CombatHandler` logic (M1).

**Architecture:** A net472 C# host loads the game's real `Assembly-CSharp.dll`. We bypass Addressables by reconstructing real `Card`/`Battle`/`StatusEffect` instances from the already-extracted `tools/out/data/*.json` and populating `AssetManager`'s static lists. UI/animation is neutralized; coroutines run through a synchronous pump so combat resolves at CPU speed with no delays.

**Tech Stack:** C# / .NET Framework 4.7.2 (matches the existing DawnKit plugin build), `dotnet build`/`dotnet test`, xUnit for tests (align with DC.DawnKit's test project if it already defines a framework), Newtonsoft.Json (shipped by the game). Reference/decompiled truth in `decompiled/`.

## Global Constraints

- **The game directory is READ-ONLY** (`E:\Games\Steam\steamapps\common\Dawncaster`). Copy DLLs out; never write there.
- **Never commit game DLLs or extracted/derived assets** (binding rule 7). `DC.SimHarness/lib/` (copied game DLLs) and any `tools/out/` data are gitignored.
- **Closed vocabularies / canonical typos are API surface** — the real enum members are `CardRariry`, `Phyisical`, `conditonOperator`, etc. Use them exactly as the game declares them; never "correct" spellings when touching game types.
- **Fail-safe:** the harness is a read-only consumer of game logic. It never mutates game files and never runs against a live game process.
- **Determinism:** all randomness flows through a single seeded RNG; identical seed ⇒ identical fight transcript.
- **Target framework net472**, matching `DC.DawnKit/src` (README: "Plugin build: `dotnet build -c Release` (net472)").

---

## File Structure

```
DC.SimHarness/
  DC.SimHarness.sln
  .gitignore                        # ignores lib/ and bin/obj
  scripts/
    copy-game-dlls.ps1              # copies Assembly-CSharp + UnityEngine.*Module + Newtonsoft to lib/
  lib/                              # (gitignored) local copies of game DLLs referenced by HintPath
  src/
    Engine/
      Engine.csproj                 # net472 class lib; references lib/ DLLs
      CoroutinePump.cs              # synchronous IEnumerator driver
      SeededRng.cs                  # deterministic RNG facade
      GameBootstrap.cs              # loads Assembly-CSharp, neutralizes UI, inits statics
      ContentLoader.cs              # tools/out/data JSON -> real Card/Battle/StatusEffect -> AssetManager
      StatusIndex.cs                # m_PathID -> StatusEffect resolution
      CombatDriver.cs               # StartCombat -> pump phases -> FightResult
      AutoplayPolicy.cs             # IPlayPolicy + GreedyPolicy
      FightResult.cs                # outcome record
    Host/
      Host.csproj                   # net472 console entry
      Program.cs                    # CLI: run one fight, print/emit result
  tests/
    Engine.Tests/
      Engine.Tests.csproj           # xUnit
      CoroutinePumpTests.cs
      SeededRngTests.cs
      ContentLoaderTests.cs
      SpikeSmokeTests.cs            # the M0 gate
      SingleCombatTests.cs          # M1
```

---

## Milestone 0 — Feasibility spike (GO/NO-GO GATE)

> M0 decides whether approach B is viable. It is partly **exploratory**: the exact
> set of Unity members that must be neutralized is unknown until `Assembly-CSharp`
> is actually loaded and combat is exercised. Tasks 0.1–0.2 are deterministic and
> fully TDD-able. Task 0.3 is a bounded spike with a concrete success assertion and
> an explicit fallback. **Do not proceed to M1 until 0.3's gate test is green.**

### Task 0.1: Scaffold solution + DLL copy script

**Files:**
- Create: `DC.SimHarness/DC.SimHarness.sln`, `DC.SimHarness/.gitignore`
- Create: `DC.SimHarness/src/Engine/Engine.csproj`, `DC.SimHarness/src/Host/Host.csproj`, `DC.SimHarness/tests/Engine.Tests/Engine.Tests.csproj`
- Create: `DC.SimHarness/scripts/copy-game-dlls.ps1`
- Create: `DC.SimHarness/src/Host/Program.cs`

**Interfaces:**
- Produces: a buildable solution; `lib/` populated with `Assembly-CSharp.dll`, `Newtonsoft.Json.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll` (+ any transitively required UnityEngine.*Module.dll).

- [ ] **Step 1: Write `.gitignore`**

```gitignore
lib/
**/bin/
**/obj/
```

- [ ] **Step 2: Write the DLL copy script** (`scripts/copy-game-dlls.ps1`)

```powershell
$src = "E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed"
$dst = Join-Path $PSScriptRoot "..\lib"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
$names = @("Assembly-CSharp.dll","Newtonsoft.Json.dll","UnityEngine.dll",
           "UnityEngine.CoreModule.dll","UnityEngine.JSONSerializeModule.dll")
foreach ($n in $names) { Copy-Item (Join-Path $src $n) $dst -Force }
Write-Host "Copied $($names.Count) DLLs to $dst"
```

- [ ] **Step 3: Run it**

Run: `pwsh DC.SimHarness/scripts/copy-game-dlls.ps1`
Expected: `Copied 5 DLLs to ...\lib`. (If a UnityEngine module is missing at build/run time, add its name here and re-run — the copy list grows as the spike reveals dependencies.)

- [ ] **Step 4: Write `Engine.csproj`** (net472, references the copied DLLs by HintPath)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>Dc.SimHarness.Engine</AssemblyName>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp"><HintPath>..\..\lib\Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Newtonsoft.Json"><HintPath>..\..\lib\Newtonsoft.Json.dll</HintPath></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>..\..\lib\UnityEngine.CoreModule.dll</HintPath></Reference>
    <Reference Include="UnityEngine"><HintPath>..\..\lib\UnityEngine.dll</HintPath></Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Write `Host.csproj`** (console, references Engine)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>dmk-sim</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Engine\Engine.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Write `Program.cs`** (placeholder entry — real CLI arrives in M1 Task 1.6)

```csharp
namespace Dc.SimHarness.Host;
internal static class Program
{
    private static int Main(string[] args)
    {
        System.Console.WriteLine("dmk-sim host alive");
        return 0;
    }
}
```

- [ ] **Step 7: Write `Engine.Tests.csproj`** (xUnit, references Engine)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Engine\Engine.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Create the solution and add projects**

Run:
```bash
cd DC.SimHarness
dotnet new sln -n DC.SimHarness
dotnet sln add src/Engine/Engine.csproj src/Host/Host.csproj tests/Engine.Tests/Engine.Tests.csproj
dotnet build
```
Expected: build succeeds (host prints nothing yet; just compiles).

- [ ] **Step 9: Commit**

```bash
git add DC.SimHarness
git commit -m "feat(sim): scaffold DC.SimHarness solution + game-DLL copy script"
```

### Task 0.2: Coroutine pump + seeded RNG (deterministic, no game DLL needed)

**Files:**
- Create: `src/Engine/CoroutinePump.cs`, `src/Engine/SeededRng.cs`
- Test: `tests/Engine.Tests/CoroutinePumpTests.cs`, `tests/Engine.Tests/SeededRngTests.cs`

**Interfaces:**
- Produces: `CoroutinePump.RunToCompletion(IEnumerator root)` — drives a coroutine (and nested `IEnumerator` yields) to completion, treating every non-enumerator yield (e.g. `WaitForSeconds`, `null`, `WaitForEndOfFrame`) as "advance immediately".
- Produces: `SeededRng` with `SeededRng.Reset(int seed)`, `int Range(int minInclusive, int maxExclusive)`, `double NextDouble()`. Later tasks route all randomness through this.

- [ ] **Step 1: Write the failing pump test**

```csharp
using System.Collections;
using Dc.SimHarness.Engine;
using Xunit;

public class CoroutinePumpTests
{
    private static IEnumerator Nested(System.Collections.Generic.List<int> log)
    {
        log.Add(1);
        yield return new object();          // stand-in for WaitForSeconds
        log.Add(2);
    }

    [Fact]
    public void RunsNestedCoroutineToCompletionIgnoringWaits()
    {
        var log = new System.Collections.Generic.List<int>();
        IEnumerator Root()
        {
            log.Add(0);
            yield return Nested(log);        // nested enumerator must be fully drained
            log.Add(3);
        }
        CoroutinePump.RunToCompletion(Root());
        Assert.Equal(new[] { 0, 1, 2, 3 }, log);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CoroutinePumpTests`
Expected: FAIL — `CoroutinePump` does not exist.

- [ ] **Step 3: Implement `CoroutinePump`**

```csharp
using System.Collections;
namespace Dc.SimHarness.Engine;

public static class CoroutinePump
{
    /// Drives an IEnumerator to completion. Nested IEnumerator yields are
    /// recursed into and fully drained; every other yield (WaitForSeconds,
    /// null, custom yield instructions) is treated as "advance immediately".
    public static void RunToCompletion(IEnumerator root)
    {
        var stack = new System.Collections.Generic.Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (top.MoveNext())
            {
                if (top.Current is IEnumerator child) stack.Push(child);
                // else: a wait/instruction — ignore, loop advances next MoveNext
            }
            else stack.Pop();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter CoroutinePumpTests`
Expected: PASS.

- [ ] **Step 5: Write the failing RNG test**

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class SeededRngTests
{
    [Fact]
    public void SameSeedProducesSameSequence()
    {
        SeededRng.Reset(1234);
        var a = new[] { SeededRng.Range(0, 100), SeededRng.Range(0, 100), SeededRng.Range(0, 100) };
        SeededRng.Reset(1234);
        var b = new[] { SeededRng.Range(0, 100), SeededRng.Range(0, 100), SeededRng.Range(0, 100) };
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test --filter SeededRngTests`
Expected: FAIL — `SeededRng` does not exist.

- [ ] **Step 7: Implement `SeededRng`**

```csharp
namespace Dc.SimHarness.Engine;

/// Single deterministic randomness source for the whole harness.
/// M0 uses this internally; wiring the game's RNG namespace to draw from it
/// is a follow-up (see spec "RNG parity" — distribution-correct is enough for v1).
public static class SeededRng
{
    private static System.Random _rng = new System.Random(0);
    public static void Reset(int seed) => _rng = new System.Random(seed);
    public static int Range(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
    public static double NextDouble() => _rng.NextDouble();
}
```

- [ ] **Step 8: Run to verify it passes**

Run: `dotnet test --filter SeededRngTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add DC.SimHarness
git commit -m "feat(sim): synchronous coroutine pump + seeded RNG with tests"
```

### Task 0.3: THE SPIKE — resolve one real effect headless (GO/NO-GO)

> **This is the gate.** Objective: load the real `Assembly-CSharp.dll`, create a
> real `StatusEffect`/`Card`/`Battle` and a real `CardContainer`, and run a single
> `damage:` effect through the real `SpellEffects` so that a monster's health drops
> by the amount `DamageCalculations.CalculateDamage` computes — all headless, no
> Unity player.
>
> **This step is exploratory by nature.** You will iterate: run, hit a Unity member
> that touches the native player or an uninitialized singleton, neutralize just that
> touchpoint, re-run. Neutralization order of preference: (a) avoid calling the
> path; (b) pre-seed the static/singleton the code dereferences; (c) if the real
> `UnityEngine.CoreModule` type genuinely cannot be constructed headless, introduce
> a replacement stub assembly (Strategy 2 below) for that type only.
>
> **Bound the spike:** if after focused effort the touchpoints prove pervasive
> (e.g. combat type-load chains into Addressables/Steamworks/localization at static
> init and cannot be cheaply pre-seeded), STOP and record findings — that is the
> signal to fall back to approach C (hybrid) from the spec. Capture the outcome in
> `DC.SimHarness/SPIKE-FINDINGS.md` either way.

**Files:**
- Create: `src/Engine/GameBootstrap.cs` (whatever minimal static-init/neutralization the spike proves necessary)
- Create: `tests/Engine.Tests/SpikeSmokeTests.cs`
- Create: `DC.SimHarness/SPIKE-FINDINGS.md`

**Interfaces:**
- Produces: `GameBootstrap.EnsureInitialized()` — idempotent; makes the process safe to touch combat types (neutralizes UI singletons, pre-seeds required statics). Exact body is discovered during the spike.

**Strategy note (decide empirically, cheap path first):**
- **Strategy 1 (try first):** reference the *real* `UnityEngine.CoreModule.dll`. Pure-managed core types (`ScriptableObject`, `MonoBehaviour` as data holders, plain structs) often construct fine via `ScriptableObject.CreateInstance<T>()` / `FormatterServices.GetUninitializedObject`. `DamageCalculations.CalculateDamage` is `public static int` with no coroutines (`decompiled/DamageCalculations.cs:20`) — the most likely-to-succeed first target.
- **Strategy 2 (only if 1 blocks):** compile a replacement `UnityEngine.CoreModule.dll` exposing the referenced type shapes with no-op bodies, and bind `Assembly-CSharp` against it. Heavier; adopt per-type, not wholesale.

- [ ] **Step 1: Write the gate test (expected to fail/red for a while)**

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class SpikeSmokeTests
{
    // GO/NO-GO: a single damage effect must reduce a monster's health headless.
    [Fact]
    public void OneDamageEffectReducesMonsterHealthHeadless()
    {
        GameBootstrap.EnsureInitialized();
        // Arrange a minimal monster with known health and a 6-damage player card,
        // then run the effect through the real SpellEffects. The concrete API calls
        // (CardHandler.CreateCardContainer, SpellEffects.RunStaticEffect / RunEffect,
        // reading MonsterHandler health) are filled in during the spike against
        // decompiled signatures.
        int before = SpikeHarness.MonsterHealth();
        SpikeHarness.RunPlayerEffect("damage:6");
        int after = SpikeHarness.MonsterHealth();
        Assert.True(after < before, $"expected damage; before={before} after={after}");
    }
}
```

- [ ] **Step 2: Run — confirm it fails (no bootstrap yet)**

Run: `dotnet test --filter SpikeSmokeTests`
Expected: FAIL (compile error / missing `GameBootstrap`/`SpikeHarness`).

- [ ] **Step 3: Spike loop — get the real DLL to load and a status/card/battle to construct**

Iterate against `decompiled/` truth:
- `ScriptableObject.CreateInstance<Card>()` / `<StatusEffect>()` / `<Battle>()`; set public fields directly (they are all public per `decompiled/Card.cs`, `StatusEffect.cs`, `Battle.cs`).
- Pre-seed singletons the path dereferences: `SpellEffects.theSpellEffects`, `MonsterHandler.theMonsterHandler`, `PlayerHandler.thePlayerData` (`decompiled/CombatHandler.cs:82,126`, `MonsterHandler.cs`). Assign fresh instances via `CreateInstance`/`new`/`GetUninitializedObject` as their types require.
- Neutralize UI: any `FindObjectOfType<CombatUIHandler>()`/`PlayerUIHandler` result is null — guard by pre-assigning no-op-safe instances or by not entering UI paths. Record each touchpoint in `SPIKE-FINDINGS.md`.
Record in `SPIKE-FINDINGS.md`: which strategy worked, the exact neutralization list, and any dead-ends.

- [ ] **Step 4: Implement the minimal `SpikeHarness` + `GameBootstrap` that makes the gate test green**

Write only what the spike proved necessary. Keep `GameBootstrap.EnsureInitialized()` idempotent.

- [ ] **Step 5: Run the gate test to verify it passes**

Run: `dotnet test --filter SpikeSmokeTests`
Expected: PASS — monster health dropped after a headless `damage:6`.

- [ ] **Step 6: Write `SPIKE-FINDINGS.md` conclusion (GO or NO-GO)**

State plainly: GO (Strategy 1 or 2, with the neutralization list) or NO-GO (blocker + recommendation to fall back to approach C). **If NO-GO, stop here and report — do not start M1.**

- [ ] **Step 7: Commit**

```bash
git add DC.SimHarness
git commit -m "feat(sim): M0 spike — headless single-effect resolution + findings"
```

---

## Milestone 1 — Full single combat

> Only start M1 if M0's gate is green. M1 turns the spike into a real single-combat
> simulator: load all content, build a deck + a battle, drive the real phase machine
> with an autoplay policy, and return a structured result.

### Task 1.1: `StatusIndex` — resolve `referenceStatus` PPtrs

**Files:**
- Create: `src/Engine/StatusIndex.cs`
- Test: `tests/Engine.Tests/ContentLoaderTests.cs` (shared file; add status-index tests here)

**Interfaces:**
- Consumes: extracted `StatusEffect` JSONs under `tools/out/data/StatusEffect/*.json`, each with `m_Name` and the PPtr `m_PathID` used by `Card.CardEffectList[].referenceStatus`.
- Produces: `StatusIndex.Build(string dataRoot)` returning `IReadOnlyDictionary<long, string>` (pathID → status asset name); mirrors `gd.status_pathid_map()` in the Python toolchain.

- [ ] **Step 1: Write the failing test** (uses the real extracted data on disk)

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class StatusIndexTests
{
    private static string DataRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..","..","..","..","..","..","tools","out","data"));

    [Fact]
    public void MapsPathIdToStatusName()
    {
        var map = StatusIndex.Build(DataRoot);
        Assert.NotEmpty(map);                          // 49 statuses extracted
        Assert.Contains(map.Values, v => v == "Poison");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter StatusIndexTests`
Expected: FAIL — `StatusIndex` missing.

- [ ] **Step 3: Implement `StatusIndex`**

```csharp
using Newtonsoft.Json.Linq;
namespace Dc.SimHarness.Engine;

public static class StatusIndex
{
    // Each StatusEffect JSON carries its own m_PathID under m_Script? No —
    // the PPtr in a Card points at the StatusEffect asset's path_id. The
    // extractor records each asset's path_id in the file; read it back here.
    public static System.Collections.Generic.IReadOnlyDictionary<long, string> Build(string dataRoot)
    {
        var dir = System.IO.Path.Combine(dataRoot, "StatusEffect");
        var map = new System.Collections.Generic.Dictionary<long, string>();
        foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*.json"))
        {
            var o = JObject.Parse(System.IO.File.ReadAllText(f));
            var name = (string?)o["m_Name"];
            var pathId = (long?)o["m_PathID"] ?? (long?)o["path_id"];
            if (name != null && pathId is long id) map[id] = name;
        }
        return map;
    }
}
```

> Verify during implementation how the extractor records each asset's own path_id
> (check `tools/extract_data.py` and `tools/sim/gamedata.py`'s `status_pathid_map`).
> If the id lives in a sidecar index rather than each file, read that index instead —
> match the Python source of truth exactly rather than guessing.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter StatusIndexTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): StatusIndex resolves referenceStatus PPtrs from extracted data"
```

### Task 1.2: `ContentLoader` — extracted JSON → real `Card`/`Battle`/`StatusEffect` → `AssetManager`

**Files:**
- Create: `src/Engine/ContentLoader.cs`
- Test: `tests/Engine.Tests/ContentLoaderTests.cs`

**Interfaces:**
- Consumes: `StatusIndex.Build`, `GameBootstrap.EnsureInitialized`, extracted JSON under `tools/out/data/{Card,Battle,StatusEffect}/`.
- Produces:
  - `ContentLoader.LoadAll(string dataRoot)` — reconstructs every `StatusEffect`, then every `Card`, then every `Battle`, populates `AssetManager.allStatusEffects`/`allCards`/`allBattles`, then calls `AssetManager.RefreshCaches()` and `AssetManager.CreateRunLists()`.
  - `ContentLoader.LoadPack(string packJsonPath)` — reconstructs a modded pack's cards (CARD-PACK-SPEC shape) and appends them the same way.

- [ ] **Step 1: Write the failing test**

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class ContentLoaderTests
{
    private static string DataRoot => StatusIndexTests_Shared.DataRoot;

    [Fact]
    public void LoadsFullPoolIntoAssetManager()
    {
        GameBootstrap.EnsureInitialized();
        ContentLoader.LoadAll(DataRoot);
        Assert.True(AssetManager.allCards.Count > 2000);           // ~2525 extracted
        Assert.NotNull(AssetManager.GetCard("Strike"));            // a known core card
        Assert.NotEmpty(AssetManager.allBattles);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ContentLoaderTests`
Expected: FAIL — `ContentLoader` missing.

- [ ] **Step 3: Implement `ContentLoader`** (deserialize onto real game fields)

The extracted field names match the real `Card` public fields (`cardID`, `cardType`, `cardCategory`, `cardRarity`, `costDEX…costLife`, `keywords`, `CardEffectList[].codeLine`, …). Deserialize with Newtonsoft directly onto `ScriptableObject.CreateInstance<Card>()`, then fix up references the JSON stores as PPtrs:
- `m_Name` → `card.name` (the SO name; drives lookups).
- `artwork`/`audioClip` PPtrs → leave null (no art needed headless).
- each `CardEffect.referenceStatus` PPtr `m_PathID` → resolve via `StatusIndex` to the already-loaded `StatusEffect` instance.
- int enum fields deserialize straight onto the game's enums (`CardType`, `CardCategory`, `CardRariry`, `CardProperties`).

```csharp
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Dc.SimHarness.Engine;

public static class ContentLoader
{
    public static void LoadAll(string dataRoot)
    {
        var statusByName = LoadStatuses(dataRoot);   // name -> StatusEffect (populates allStatusEffects)
        var pathToStatus = StatusIndex.Build(dataRoot);
        LoadCards(dataRoot, pathToStatus, statusByName);
        LoadBattles(dataRoot);
        AssetManager.RefreshCaches();
        AssetManager.CreateRunLists();
    }

    private static System.Collections.Generic.Dictionary<string, StatusEffect> LoadStatuses(string dataRoot)
    {
        var byName = new System.Collections.Generic.Dictionary<string, StatusEffect>();
        foreach (var f in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(dataRoot, "StatusEffect"), "*.json"))
        {
            var se = ScriptableObject.CreateInstance<StatusEffect>();
            JsonConvert.PopulateObject(System.IO.File.ReadAllText(f), se, RefSafe);
            var name = (string)JObject.Parse(System.IO.File.ReadAllText(f))["m_Name"];
            se.name = name;
            AssetManager.allStatusEffects.Add(se);
            byName[name] = se;
        }
        return byName;
    }

    private static void LoadCards(string dataRoot,
        System.Collections.Generic.IReadOnlyDictionary<long, string> pathToStatus,
        System.Collections.Generic.Dictionary<string, StatusEffect> statusByName)
    {
        foreach (var f in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(dataRoot, "Card"), "*.json"))
        {
            var raw = JObject.Parse(System.IO.File.ReadAllText(f));
            var card = ScriptableObject.CreateInstance<Card>();
            JsonConvert.PopulateObject(raw.ToString(), card, RefSafe);
            card.name = (string)raw["m_Name"];
            // resolve referenceStatus PPtrs onto the reconstructed CardEffects
            var effects = raw["CardEffectList"] as JArray;
            if (effects != null && card.CardEffectList != null)
                for (int i = 0; i < effects.Count && i < card.CardEffectList.Count; i++)
                {
                    var pid = (long?)effects[i]?["referenceStatus"]?["m_PathID"] ?? 0;
                    if (pid != 0 && pathToStatus.TryGetValue(pid, out var sName)
                        && statusByName.TryGetValue(sName, out var se))
                        card.CardEffectList[i].referenceStatus = se;
                }
            AssetManager.allCards.Add(card);
            if (card.cardexpansion != AssetManager.CardExpansions.None
                && card.cardexpansion != AssetManager.CardExpansions.Metaprogress)
                AssetManager.playercards.Add(card);
        }
    }

    private static void LoadBattles(string dataRoot)
    {
        foreach (var f in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(dataRoot, "Battle"), "*.json"))
        {
            var b = ScriptableObject.CreateInstance<Battle>();
            var raw = JObject.Parse(System.IO.File.ReadAllText(f));
            JsonConvert.PopulateObject(raw.ToString(), b, RefSafe);
            b.name = (string)raw["m_Name"];
            AssetManager.allBattles.Add(b);
        }
    }

    // Ignore PPtr sub-objects (artwork/audio/script) that don't map to a field cleanly.
    private static readonly JsonSerializerSettings RefSafe = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
    };

    public static void LoadPack(string packJsonPath) { /* CARD-PACK-SPEC shape; implemented in Task 1.7 */ }
}
```

> If `PopulateObject` chokes on a PPtr object (e.g. `artwork`), strip those keys from
> the `JObject` before populating, or add a `JsonConverter` that maps PPtr→null.
> Confirm the enum int→member mapping matches `decompiled/Card.cs` exactly, including
> canonical typos (`CardRariry`, `Phyisical`).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ContentLoaderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): ContentLoader reconstructs real Card/Battle/Status into AssetManager"
```

### Task 1.3: `FightResult` + `IPlayPolicy`/`GreedyPolicy`

**Files:**
- Create: `src/Engine/FightResult.cs`, `src/Engine/AutoplayPolicy.cs`
- Test: `tests/Engine.Tests/SingleCombatTests.cs` (policy unit test here)

**Interfaces:**
- Produces: `record FightResult(bool Won, int Turns, int PlayerHpRemaining, int MonsterHpRemaining, bool DegenerateFlag, string? DegenerateReason)`.
- Produces: `interface IPlayPolicy { IReadOnlyList<CardContainer> ChoosePlays(IReadOnlyList<CardContainer> hand, int energy); }` and `GreedyPolicy : IPlayPolicy` — plays affordable cards, highest raw cost first, until energy is exhausted (a fixed, documented baseline).

- [ ] **Step 1: Write the failing policy test**

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class GreedyPolicyTests
{
    [Fact]
    public void PlaysAffordableCardsWithinEnergy()
    {
        // Build two 1-cost containers + one 5-cost; energy 2 -> picks the two 1-costs.
        var hand = TestHands.Costs(1, 1, 5);   // helper builds CardContainers via CardHandler
        var chosen = new GreedyPolicy().ChoosePlays(hand, energy: 2);
        Assert.Equal(2, chosen.Count);
        Assert.All(chosen, c => Assert.True(TestHands.TotalCost(c) <= 5));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter GreedyPolicyTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement `FightResult` and `GreedyPolicy`**

```csharp
namespace Dc.SimHarness.Engine;

public sealed record FightResult(
    bool Won, int Turns, int PlayerHpRemaining, int MonsterHpRemaining,
    bool DegenerateFlag, string? DegenerateReason);

public interface IPlayPolicy
{
    System.Collections.Generic.IReadOnlyList<CardContainer> ChoosePlays(
        System.Collections.Generic.IReadOnlyList<CardContainer> hand, int energy);
}

/// Fixed baseline: play affordable cards, most-expensive-first, until energy runs out.
public sealed class GreedyPolicy : IPlayPolicy
{
    public System.Collections.Generic.IReadOnlyList<CardContainer> ChoosePlays(
        System.Collections.Generic.IReadOnlyList<CardContainer> hand, int energy)
    {
        var picks = new System.Collections.Generic.List<CardContainer>();
        int left = energy;
        foreach (var c in System.Linq.Enumerable.OrderByDescending(hand, TotalCost))
        {
            int cost = TotalCost(c);
            if (cost <= left) { picks.Add(c); left -= cost; }
        }
        return picks;
    }

    // Uses the container's cost array (GetCostArray order per decompiled/Card.cs).
    private static int TotalCost(CardContainer c) => System.Linq.Enumerable.Sum(c.GetCostArray());
}
```

> Confirm `CardContainer.GetCostArray()` exists and its element order against
> `decompiled/CardHandler.cs`/`Card.cs`; adjust `TotalCost` if the container exposes
> cost differently.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter GreedyPolicyTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): FightResult record + GreedyPolicy autoplay baseline"
```

### Task 1.4: `CombatDriver` — run one fight to a result

**Files:**
- Create: `src/Engine/CombatDriver.cs`
- Test: `tests/Engine.Tests/SingleCombatTests.cs`

**Interfaces:**
- Consumes: `GameBootstrap`, `ContentLoader`, `CoroutinePump`, `SeededRng`, `IPlayPolicy`, and the real `CombatHandler`/`MonsterHandler`/`PlayerHandler`/`SpellEffects` statics.
- Produces: `CombatDriver.RunFight(FightSetup setup, IPlayPolicy policy, int seed) : FightResult`, where `FightSetup` names a `Battle` (by name), a player deck (list of card names + counts), starting HP, energy/turn, and a turn cap.

- [ ] **Step 1: Write the failing integration test**

```csharp
using Dc.SimHarness.Engine;
using Xunit;

public class SingleCombatTests
{
    [Fact]
    public void StrikeDeckBeatsAWeakBattle()
    {
        GameBootstrap.EnsureInitialized();
        ContentLoader.LoadAll(StatusIndexTests_Shared.DataRoot);
        var setup = new FightSetup(
            BattleName: TestBattles.WeakestBattleName(),   // helper picks a low-HP Battle from allBattles
            Deck: new() { ["Strike"] = 8, ["Defend"] = 2 },
            PlayerMaxHp: 60, EnergyPerTurn: 3, TurnCap: 30);
        var result = CombatDriver.RunFight(setup, new GreedyPolicy(), seed: 7);
        Assert.True(result.Won);
        Assert.True(result.Turns <= 30);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SingleCombatTests`
Expected: FAIL — `CombatDriver`/`FightSetup` missing.

- [ ] **Step 3: Implement `CombatDriver`**

Drive the real phase machine via the pump. The concrete sequence (verified against `decompiled/CombatHandler.cs`): construct a `CombatHandler`, build the player (`PlayerHandler.thePlayerData` with deck/HP/energy/class basics), call `StartCombat(battle, sec)`, then loop phases — on each player `PlayPhase`, ask the `IPlayPolicy` for plays and execute each via `SpellEffects.ExecuteEffect`/`RunEffect`, then advance `GoToPhase(...)` through Discard→EndTurn→Enemy→Start until `SpellEffects.CombatStat.victory` or `.defeat`, or the turn cap trips. Every coroutine (`SetStartingEffects`, `MonsterHandler.PlayAbilities`) runs through `CoroutinePump.RunToCompletion`. Read the result off `SpellEffects.CombatStat` + `PlayerHandler`/`MonsterHandler` health.

```csharp
namespace Dc.SimHarness.Engine;

public sealed record FightSetup(
    string BattleName,
    System.Collections.Generic.Dictionary<string, int> Deck,
    int PlayerMaxHp, int EnergyPerTurn, int TurnCap);

public static class CombatDriver
{
    public static FightResult RunFight(FightSetup setup, IPlayPolicy policy, int seed)
    {
        SeededRng.Reset(seed);
        GameBootstrap.EnsureInitialized();
        GameBootstrap.PreparePlayer(setup);                     // deck -> combat deck, HP, energy, basics
        var battle = AssetManager.GetBattle(setup.BattleName);
        var handler = GameBootstrap.CombatHandlerInstance();

        CoroutinePump.RunToCompletion(WrapVoid(() => handler.StartCombat(battle, GameBootstrap.EmptyEvent())));

        int turns = 0;
        while (!SpellEffects.CombatStat.victory && !SpellEffects.CombatStat.defeat && turns < setup.TurnCap)
        {
            // player play phase
            var hand = GameBootstrap.PlayerHand();
            foreach (var pick in policy.ChoosePlays(hand, GameBootstrap.CurrentEnergy()))
                CoroutinePump.RunToCompletion(GameBootstrap.PlayCard(pick));
            // advance the real phase machine to the next player turn (or terminal state)
            GameBootstrap.AdvanceToNextPlayerTurn(handler);      // pumps Discard/EndTurn/Enemy/Start coroutines
            turns++;
        }

        bool degenerate = turns >= setup.TurnCap && !SpellEffects.CombatStat.victory && !SpellEffects.CombatStat.defeat;
        return new FightResult(
            Won: SpellEffects.CombatStat.victory,
            Turns: turns,
            PlayerHpRemaining: GameBootstrap.PlayerHp(),
            MonsterHpRemaining: GameBootstrap.MonsterHp(),
            DegenerateFlag: degenerate,
            DegenerateReason: degenerate ? "turn cap reached without resolution" : null);
    }

    private static System.Collections.IEnumerator WrapVoid(System.Action a) { a(); yield break; }
}
```

> `GameBootstrap` gains the small helpers named above (`PreparePlayer`,
> `CombatHandlerInstance`, `PlayerHand`, `CurrentEnergy`, `PlayCard`,
> `AdvanceToNextPlayerTurn`, `PlayerHp`, `MonsterHp`, `EmptyEvent`) — each a thin
> wrapper over the real statics discovered in M0. Keep them in `GameBootstrap` so
> all game-coupling lives in one file.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SingleCombatTests`
Expected: PASS — the Strike deck wins within the turn cap.

- [ ] **Step 5: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): CombatDriver runs one real fight headless to a FightResult"
```

### Task 1.5: Determinism test

**Files:**
- Test: `tests/Engine.Tests/SingleCombatTests.cs` (add a determinism fact)

- [ ] **Step 1: Write the test**

```csharp
[Fact]
public void SameSeedSameResult()
{
    GameBootstrap.EnsureInitialized();
    ContentLoader.LoadAll(StatusIndexTests_Shared.DataRoot);
    var setup = new FightSetup(TestBattles.WeakestBattleName(),
        new() { ["Strike"] = 8, ["Defend"] = 2 }, 60, 3, 30);
    var a = CombatDriver.RunFight(setup, new GreedyPolicy(), seed: 42);
    var b = CombatDriver.RunFight(setup, new GreedyPolicy(), seed: 42);
    Assert.Equal(a, b);   // record equality: same outcome, turns, HP
}
```

- [ ] **Step 2: Run to verify it passes** (should pass if all randomness routes through `SeededRng`)

Run: `dotnet test --filter SameSeedSameResult`
Expected: PASS. If it fails, find the unseeded randomness source (likely the game's `RNG` namespace) and route it through `SeededRng` inside `GameBootstrap.EnsureInitialized()`.

- [ ] **Step 3: Commit**

```bash
git add DC.SimHarness && git commit -m "test(sim): fight determinism under fixed seed"
```

### Task 1.6: Host CLI — run one fight from the command line

**Files:**
- Modify: `src/Host/Program.cs`

**Interfaces:**
- Produces: `dmk-sim --battle <name> --deck <name:count,...> --hp 60 --energy 3 --seed 7 [--data <dataRoot>]` → prints a one-line JSON `FightResult`.

- [ ] **Step 1: Write `Program.cs`**

```csharp
using Dc.SimHarness.Engine;
namespace Dc.SimHarness.Host;

internal static class Program
{
    private static int Main(string[] args)
    {
        var a = Args.Parse(args);   // tiny local parser; --key value
        GameBootstrap.EnsureInitialized();
        ContentLoader.LoadAll(a.DataRoot);
        var setup = new FightSetup(a.Battle, a.Deck, a.Hp, a.Energy, a.TurnCap);
        var r = CombatDriver.RunFight(setup, new GreedyPolicy(), a.Seed);
        System.Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(r));
        return r.DegenerateFlag ? 2 : (r.Won ? 0 : 1);
    }
}
```

- [ ] **Step 2: Build and run a smoke fight**

Run:
```bash
cd DC.SimHarness
dotnet build
dotnet run --project src/Host -- --battle "<a real battle name>" --deck "Strike:8,Defend:2" --hp 60 --energy 3 --seed 7
```
Expected: a JSON line like `{"Won":true,"Turns":6,...}` and exit code 0/1/2.

- [ ] **Step 3: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): host CLI runs a single headless fight and emits JSON"
```

### Task 1.7: Load a modded pack's cards

**Files:**
- Modify: `src/Engine/ContentLoader.cs` (`LoadPack`)
- Test: `tests/Engine.Tests/ContentLoaderTests.cs`

**Interfaces:**
- Consumes: a `pack.json` in CARD-PACK-SPEC shape (fields per `DC.DawnKit/CARD-PACK-SPEC.md`; the Python `card_from_pack_json` in `tools/sim/dsl.py` is the reference for field names — `name`, `cardID`, `rarity`, `category`, `type`, `cost{}`, `keywords[]`, `effects[].codeLine`, `effects[].referenceStatus`, `playConditions[]`).
- Produces: `ContentLoader.LoadPack(string packJsonPath)` appends the pack's cards to `AssetManager.allCards`/`playercards` and refreshes caches.

- [ ] **Step 1: Write the failing test** (use an existing shipped pack)

```csharp
[Fact]
public void LoadsModdedPackCards()
{
    GameBootstrap.EnsureInitialized();
    ContentLoader.LoadAll(StatusIndexTests_Shared.DataRoot);
    int before = AssetManager.allCards.Count;
    ContentLoader.LoadPack(TestPaths.Repo("DC.VenomousLegacy/pack.json"));
    Assert.True(AssetManager.allCards.Count > before);
    Assert.NotNull(AssetManager.GetCard(TestPacks.FirstCardName("DC.VenomousLegacy/pack.json")));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter LoadsModdedPackCards`
Expected: FAIL — `LoadPack` is a stub.

- [ ] **Step 3: Implement `LoadPack`** (map CARD-PACK-SPEC → real `Card` fields)

```csharp
public static void LoadPack(string packJsonPath)
{
    var pack = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(packJsonPath));
    foreach (var c in pack["cards"] ?? new Newtonsoft.Json.Linq.JArray())
    {
        var card = ScriptableObject.CreateInstance<Card>();
        card.name = (string)c["name"];
        card.cardID = (int?)c["cardID"] ?? 0;
        card.cardRarity = System.Enum.Parse<Card.CardRariry>((string?)c["rarity"] ?? "Common");
        card.cardCategory = System.Enum.Parse<Card.CardCategory>((string?)c["category"] ?? "Action");
        card.cardType = System.Enum.Parse<Card.CardType>((string?)c["type"] ?? "Melee");
        PackMapper.ApplyCosts(card, c["cost"]);
        PackMapper.ApplyEffects(card, c["effects"]);          // codeLine + referenceStatus by status name
        PackMapper.ApplyKeywords(card, c["keywords"]);
        AssetManager.allCards.Add(card);
        AssetManager.playercards.Add(card);
    }
    AssetManager.RefreshCaches();
    AssetManager.CreateRunLists();
}
```

> `PackMapper` holds the field-by-field mapping (a small helper in `ContentLoader.cs`).
> Mirror `card_from_pack_json` in `tools/sim/dsl.py` exactly for field names and the
> cost/keyword handling. Resolve `effects[].referenceStatus` (a status *name* in the
> pack shape) against `AssetManager.GetStatus(name)`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter LoadsModdedPackCards`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DC.SimHarness && git commit -m "feat(sim): load modded pack.json cards into the headless engine"
```

---

## Deferred to a follow-up plan (M2–M4)

Not planned in concrete steps here **on purpose**: their exact tasks depend on the
shim surface that M0 discovers and the driver shape M1 lands. Planning them with
"complete code" now would be fabrication. Once M0+M1 are green, write
`docs/superpowers/plans/<date>-headless-combat-sim-harness-m2-m4.md` covering:

- **M2 — Content coverage + cross-check:** run the full pool; validate against
  `tools/sim`'s 91-card simulable subset (power ordering should broadly agree).
- **M3 — Batch runner + metrics + combo detection + `dmk` integration:** N fights
  over seeds × decks × enemy sets; win-rate/survival/damage curves; degeneracy
  detection (non-termination, iteration-cap trips, unbounded stack growth,
  late-window scaling); `BALANCE-REPORT`-style output; new `dmk` subcommand.
- **M4 — Golden-fight validation:** capture a few real in-game outcomes and assert
  a distributional match (exact-transcript only where RNG parity is established).

---

## Self-Review

- **Spec coverage (M0+M1 slice):** shadow-Unity bet → Task 0.3 (gate); coroutine
  pump / no delays → 0.2; asset bypass via `tools/out/data` → 1.1–1.2; real combat
  loop → 1.4; autoplay policy → 1.3; determinism → 1.5; modded-pack loading → 1.7;
  CLI → 1.6. Win-rate/survival aggregation, combo detection, and `dmk` wiring are
  explicitly deferred to the M2–M4 plan (documented above), consistent with the
  spec's milestone breakdown.
- **Placeholder scan:** the only intentionally-open task is 0.3 (a spike — its body
  is discovered, not fabricated); every deterministic task carries real code. The
  `LoadPack` stub in Task 1.2 is filled in Task 1.7. No "TBD"/"add error handling"
  placeholders remain.
- **Type consistency:** `FightResult`, `FightSetup`, `IPlayPolicy`/`GreedyPolicy`,
  `GameBootstrap.*`, `ContentLoader.LoadAll`/`LoadPack`, `StatusIndex.Build`,
  `CoroutinePump.RunToCompletion`, `SeededRng.*` are used consistently across tasks.
- **Grounding caveat:** every game-API call (`SpellEffects.ExecuteEffect`,
  `CardContainer.GetCostArray`, `MonsterHandler` health accessors, the extractor's
  path_id storage) carries a verify-against-`decompiled/` note where the exact
  signature was not confirmed in this pass — implementers confirm before relying.
```
