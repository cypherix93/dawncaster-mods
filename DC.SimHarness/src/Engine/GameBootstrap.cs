using System.Reflection;
using System.Runtime.Serialization;

namespace Dc.SimHarness.Engine;

/// One-time process setup for running the real game logic headless: install the
/// assembly resolver that serves the shimmed UnityEngine DLLs (and real Assembly-CSharp),
/// and locate the extracted-data root. All game-coupling lives behind this class.
public static class GameBootstrap
{
    private static bool _init;
    private static readonly object Gate = new();

    /// Directory holding the shimmed UnityEngine/dependency DLLs. Auto-detected; override before EnsureInitialized.
    public static string? ShimsDir { get; set; }

    /// The game's real Managed dir (Assembly-CSharp, Newtonsoft). Read-only.
    public static string GameManaged { get; set; } =
        @"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed";

    /// Extracted-data root (…/tools/out/data) and its sibling data-index.json. Auto-detected; override before use.
    public static string? DataRoot { get; set; }
    public static string? DataIndex { get; set; }

    public static void EnsureInitialized()
    {
        if (_init) return;
        lock (Gate)
        {
            if (_init) return;
            ShimsDir ??= LocateShims();
            (DataRoot, DataIndex) = LocateData();
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            _init = true;
        }
    }

    /// Ensure PlayerHandler.thePlayerData exists with the collections that content/combat
    /// setup reads (excludedsets, bannedCards). Idempotent. Built reflectively so we don't
    /// hard-depend on PlayerData's exact field types.
    public static void EnsurePlayerData()
    {
        var phd = typeof(PlayerHandler).GetField("thePlayerData",
            BindingFlags.Public | BindingFlags.Static);
        if (phd is null) throw new MissingFieldException("PlayerHandler.thePlayerData not found");
        if (phd.GetValue(null) != null) return;

        // thePlayerData is a PlayerHandler (MonoBehaviour singleton: `thePlayerData = this`).
        var pd = FormatterServices.GetUninitializedObject(typeof(PlayerHandler));
        foreach (var name in new[] { "excludedsets", "bannedCards" })
        {
            var f = typeof(PlayerHandler).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            // Instantiate whatever collection type the field is (List<>, HashSet<>, …).
            if (f != null && f.GetValue(pd) == null && f.FieldType.IsGenericType
                && f.FieldType.GetConstructor(Type.EmptyTypes) != null)
                f.SetValue(pd, Activator.CreateInstance(f.FieldType));
        }
        phd.SetValue(null, pd);
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        if (name is null) return null;
        // Shimmed Unity surface first, then the real game Managed dir.
        var shim = ShimsDir is null ? null : Path.Combine(ShimsDir, name + ".dll");
        if (shim != null && File.Exists(shim)) return Assembly.LoadFrom(shim);
        var real = Path.Combine(GameManaged, name + ".dll");
        return File.Exists(real) ? Assembly.LoadFrom(real) : null;
    }

    // Walk up from the running assembly to find the DC.SimHarness/shims directory.
    private static string LocateShims()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "shims");
            if (dir.Name == "DC.SimHarness" && Directory.Exists(candidate)) return candidate;
            var nested = Path.Combine(dir.FullName, "DC.SimHarness", "shims");
            if (Directory.Exists(nested)) return nested;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate DC.SimHarness/shims. Run ShimGen first, or set GameBootstrap.ShimsDir.");
    }

    // Locate tools/out/data + data-index.json. Extracted data is gitignored and lives in
    // the MAIN checkout, so from a worktree we follow the .git pointer to the common dir.
    private static (string root, string index) LocateData()
    {
        foreach (var start in EnumerateRoots())
        {
            var index = Path.Combine(start, "tools", "out", "data-index.json");
            var root = Path.Combine(start, "tools", "out", "data");
            if (File.Exists(index) && Directory.Exists(root)) return (root, index);
        }
        throw new DirectoryNotFoundException(
            "Could not locate tools/out/data + data-index.json. Set GameBootstrap.DataRoot/DataIndex, " +
            "or run tools/extract_data.py in the main checkout.");
    }

    private static IEnumerable<string> EnumerateRoots()
    {
        // Ancestors of the running assembly (covers non-worktree runs).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) { yield return dir.FullName; dir = dir.Parent; }

        // Worktree case: read the ".git" file's "gitdir:" pointer -> common dir -> main root.
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitFile = Path.Combine(dir.FullName, ".git");
            if (File.Exists(gitFile))
            {
                var line = File.ReadAllText(gitFile).Trim();
                const string tag = "gitdir:";
                if (line.StartsWith(tag))
                {
                    var gitdir = line[tag.Length..].Trim();
                    var full = Path.GetFullPath(gitdir, dir.FullName);
                    // .../.git/worktrees/<name> -> main root is three levels up from here.
                    var main = new DirectoryInfo(full);
                    for (int i = 0; i < 3 && main?.Parent != null; i++) main = main.Parent;
                    if (main != null) yield return main.FullName;
                }
                break;
            }
            dir = dir.Parent;
        }
    }
}
