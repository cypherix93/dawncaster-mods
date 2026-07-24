using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dc.SimHarness.ShimGen;

// Rewrites the game's real Unity dependency DLLs into headless-safe "shims":
// every native method (InternalCall / PInvoke / no managed body) gets a
// `return default` body and its native flags cleared, so the code links and runs
// under a plain CLR with no Unity player. Managed bodies (e.g. Mathf math) are
// left untouched. Metadata (types, generics, enums, structs, inheritance) is
// preserved exactly because we edit the real assembly rather than regenerate it.
internal static class Program
{
    // Assemblies to shim. Start with the combat direct-surface + CoreModule; the
    // resolver in the host reports any further ones needed, which get appended here.
    private static readonly string[] DefaultTargets =
    {
        "UnityEngine.CoreModule",
        "UnityEngine",
        "UnityEngine.UI",
        "UnityEngine.UIModule",
        "UnityEngine.TextRenderingModule",
        "UnityEngine.TextCoreTextEngineModule",
        "UnityEngine.TextCoreFontEngineModule",
        "UnityEngine.AudioModule",
        "UnityEngine.ParticleSystemModule",
        "UnityEngine.AnimationModule",
        "UnityEngine.PhysicsModule",
        "UnityEngine.Physics2DModule",
        "UnityEngine.InputModule",
        "UnityEngine.InputLegacyModule",
        "UnityEngine.IMGUIModule",
        "UnityEngine.JSONSerializeModule",
        "UnityEngine.AssetBundleModule",
        "UnityEngine.ImageConversionModule",
        "Unity.TextMeshPro",
        "Unity.Localization",
        "Unity.ResourceManager",
        "Unity.Addressables",
        "Unity.InputSystem",
        "Unity.Postprocessing.Runtime",
        "UIEffect",
        "UniTask",
        "com.rlabrecque.steamworks.net",
        "Gilzoide.SqliteNet",
    };

    private static int Main(string[] args)
    {
        var managed = args.Length > 0 ? args[0]
            : @"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed";
        var outDir = args.Length > 1 ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shims");
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);

        var targets = args.Length > 2 ? args[2..] : DefaultTargets;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);

        int shimmed = 0, totalNeutralized = 0, missing = 0;
        foreach (var name in targets)
        {
            var inPath = Path.Combine(managed, name + ".dll");
            if (!File.Exists(inPath)) { Console.WriteLine($"  (skip, not found) {name}"); missing++; continue; }

            var asm = AssemblyDefinition.ReadAssembly(inPath, new ReaderParameters { AssemblyResolver = resolver });
            int n = 0;
            foreach (var type in AllTypes(asm.MainModule))
                foreach (var m in type.Methods)
                    if (Neutralize(m, asm.MainModule)) n++;

            var outPath = Path.Combine(outDir, name + ".dll");
            asm.Write(outPath);
            Console.WriteLine($"  shimmed {name,-42} native methods neutralized: {n}");
            shimmed++; totalNeutralized += n;
        }
        Console.WriteLine($"\nDone. {shimmed} assemblies shimmed, {totalNeutralized} native methods neutralized, {missing} missing. -> {outDir}");
        return 0;
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition m)
    {
        foreach (var t in m.Types)
        {
            yield return t;
            foreach (var nt in Nested(t)) yield return nt;
        }
    }
    private static IEnumerable<TypeDefinition> Nested(TypeDefinition t)
    {
        foreach (var nt in t.NestedTypes)
        {
            yield return nt;
            foreach (var d in Nested(nt)) yield return d;
        }
    }

    // Returns true if the method was a native/bodyless method we gave a stub body to.
    private static bool Neutralize(MethodDefinition m, ModuleDefinition module)
    {
        // Leave alone: abstract (interface/abstract decls), runtime-provided (delegate
        // Invoke/BeginInvoke), and anything that already has real managed IL.
        if (m.IsAbstract || m.IsRuntime) return false;
        bool native = m.IsInternalCall || m.IsPInvokeImpl || (!m.HasBody && !m.IsAbstract);
        if (!native) return false;

        // Clear native flags so the CLR expects managed IL.
        m.IsInternalCall = false;
        if (m.IsPInvokeImpl) { m.IsPInvokeImpl = false; m.PInvokeInfo = null; }
        m.ImplAttributes &= ~MethodImplAttributes.InternalCall;

        var body = new MethodBody(m);
        var il = body.GetILProcessor();
        var rt = m.ReturnType;
        if (rt.MetadataType == MetadataType.Void)
        {
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Universal default: local of return type, initobj, load, return.
            // initobj works for reference types (=> null), value types, and generic params.
            var v = new VariableDefinition(rt);
            body.Variables.Add(v);
            body.InitLocals = true;
            il.Emit(OpCodes.Ldloca_S, v);
            il.Emit(OpCodes.Initobj, rt);
            il.Emit(OpCodes.Ldloc, v);
            il.Emit(OpCodes.Ret);
        }
        m.Body = body;
        return true;
    }
}
