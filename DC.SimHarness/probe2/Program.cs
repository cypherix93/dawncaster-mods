using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

// Revised M0 gate (net8): bind Assembly-CSharp against the SHIMMED UnityEngine.
// The calls that threw SecurityException on net472 (Debug.Log, CreateInstance) must
// now no-op; Mathf math must be REAL; a game logic method must run headless.
internal static class Program
{
    private const string Managed =
        @"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed";
    private static readonly string Shims =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shims"));

    private static int Main()
    {
        // Serve UnityEngine*/Unity.*/tail from the shims dir; everything else (Assembly-CSharp,
        // Newtonsoft, ...) from the game's Managed dir. Installed before touching game types.
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            var shim = Path.Combine(Shims, name + ".dll");
            if (File.Exists(shim)) return Assembly.LoadFrom(shim);
            var real = Path.Combine(Managed, name + ".dll");
            return File.Exists(real) ? Assembly.LoadFrom(real) : null;
        };
        return RunProbe();
    }

    private static void Check(string name, Action act)
    {
        try { act(); Console.WriteLine($"[PASS ] {name}"); }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
            Console.WriteLine($"[THROW] {name}: {inner.GetType().Name}: {inner.Message}");
        }
    }

    private static int RunProbe()
    {
        Console.WriteLine("=== Revised M0 gate (net8 + shimmed UnityEngine) ===");
        Console.WriteLine($"shims: {Shims}");

        Assembly asm = null;
        Check("load Assembly-CSharp (net8) + reflect Card", () =>
        {
            asm = typeof(Card).Assembly;
            Console.WriteLine($"        -> {asm.GetName().Name}");
        });
        if (asm == null) return 1;

        Check("GetUninitializedObject(Card) + set/read field", () =>
        {
            var c = (Card)FormatterServices.GetUninitializedObject(typeof(Card));
            c.cardID = 777;
            if (c.cardID != 777) throw new Exception("mismatch");
        });

        Check("UnityEngine.Debug.Log (was SecurityException) -> should no-op", () =>
            UnityEngine.Debug.Log("probe2"));

        Check("ScriptableObject.CreateInstance<Card>() -> no-op/null tolerated", () =>
        {
            var _ = UnityEngine.ScriptableObject.CreateInstance<Card>();  // may return null now
        });

        Check("Mathf REAL math: RoundToInt(2.6)=3, Clamp(9,0,5)=5", () =>
        {
            if (UnityEngine.Mathf.RoundToInt(2.6f) != 3) throw new Exception("RoundToInt wrong");
            if (UnityEngine.Mathf.Clamp(9, 0, 5) != 5) throw new Exception("Clamp wrong");
            if (UnityEngine.Mathf.Min(3, 7) != 3) throw new Exception("Min wrong");
        });

        Check("DamageCalculations.CalculateDamage(6, <uninit cc>, false)", () =>
        {
            var ccType = asm.GetType("CardContainer");
            var cc = FormatterServices.GetUninitializedObject(ccType);
            var m = asm.GetType("DamageCalculations").GetMethod("CalculateDamage",
                BindingFlags.Public | BindingFlags.Static);
            var r = m.Invoke(null, new object[] { 6, cc, false, false, false });
            Console.WriteLine($"        -> CalculateDamage(6, <uninit cc>) = {r}");
        });

        Check("RunClassConstructor(SpellEffects)", () =>
        {
            var t = asm.GetType("SpellEffects");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        });

        Console.WriteLine("=== gate probe complete ===");
        return 0;
    }
}
