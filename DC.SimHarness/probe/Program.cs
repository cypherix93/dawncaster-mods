using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

// M0 feasibility probe. Each check is isolated; we print PASS/THROW so we can map
// exactly which construction/native paths survive in a bare net472 process
// (no Unity player hosting the Mono internal calls).
internal static class Program
{
    private const string ManagedDir =
        @"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed";

    private static int Main()
    {
        // Resolve every game dependency (Assembly-CSharp + the Unity module web) from
        // the game's Managed folder. MUST be installed before any game type is touched.
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(ManagedDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return RunProbe();
    }

    private static void Check(string name, Action act)
    {
        try { act(); Console.WriteLine($"[PASS ] {name}"); }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
            Console.WriteLine($"[THROW] {name}: {inner.GetType().Name}: {inner.Message}");
        }
    }

    // NB: touches game types -> only JITted after the resolver is installed.
    private static int RunProbe()
    {
        Console.WriteLine("=== M0 shadow-Unity feasibility probe (net472, no Unity player) ===");

        Assembly asmCsharp = null;
        Check("load Assembly-CSharp + reflect Card", () =>
        {
            asmCsharp = typeof(Card).Assembly;
            Console.WriteLine($"        -> {asmCsharp.GetName().Name} @ {asmCsharp.Location}");
            if (typeof(Card).GetField("cardID") == null) throw new Exception("cardID field not found");
        });
        if (asmCsharp == null) { Console.WriteLine("cannot proceed; Assembly-CSharp failed to load"); return 1; }

        // Managed construction WITHOUT running ctors or Unity native alloc.
        Check("FormatterServices.GetUninitializedObject(Card) + set field", () =>
        {
            var card = (Card)FormatterServices.GetUninitializedObject(typeof(Card));
            card.cardID = 123456;
            if (card.cardID != 123456) throw new Exception("field set/read mismatch");
        });

        // Strategy-1 native path: ScriptableObject.CreateInstance<Card>().
        Check("ScriptableObject.CreateInstance<Card>()", () =>
        {
            var card = UnityEngine.ScriptableObject.CreateInstance<Card>();
            if (card == null) throw new Exception("null instance");
        });

        // Strategy-1 native path: Debug.Log — SpellEffects calls this constantly.
        Check("UnityEngine.Debug.Log(...)", () => UnityEngine.Debug.Log("probe"));

        // The real question: can we call a pure-math game method headless?
        Check("DamageCalculations.CalculateDamage (uninit CardContainer)", () =>
        {
            var ccType = asmCsharp.GetType("CardContainer");
            if (ccType == null) throw new Exception("CardContainer type not found");
            var cc = FormatterServices.GetUninitializedObject(ccType);
            var m = asmCsharp.GetType("DamageCalculations").GetMethod("CalculateDamage",
                BindingFlags.Public | BindingFlags.Static);
            if (m == null) throw new Exception("CalculateDamage not found");
            var result = m.Invoke(null, new object[] { 6, cc, false, false, false });
            Console.WriteLine($"        -> CalculateDamage(6, <uninit cc>) = {result}");
        });

        // Does merely touching SpellEffects' static ctor detonate (Addressables/singletons)?
        Check("RunClassConstructor(SpellEffects) (static init)", () =>
        {
            var t = asmCsharp.GetType("SpellEffects");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        });

        Console.WriteLine("=== probe complete ===");
        return 0;
    }
}
