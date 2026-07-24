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

            // Carve-outs: members that combat READS BACK, so a no-op stub is wrong.
            // UnityEngine.Object.name must round-trip (AssetManager lookups key on it).
            if (name == "UnityEngine.CoreModule")
            {
                AddNameBackingField(asm.MainModule);
                WireStartCoroutine(asm.MainModule);
            }

            var outPath = Path.Combine(outDir, name + ".dll");
            asm.Write(outPath);
            Console.WriteLine($"  shimmed {name,-42} native methods neutralized: {n}");
            shimmed++; totalNeutralized += n;
        }
        // Ship the coroutine-pump assembly alongside the shims (CoreModule now references it).
        var pumpSrc = typeof(Dc.SimHarness.Runtime.SimCoroutine).Assembly.Location;
        var pumpDst = Path.Combine(outDir, Path.GetFileName(pumpSrc));
        File.Copy(pumpSrc, pumpDst, overwrite: true);
        Console.WriteLine($"copied pump assembly -> {Path.GetFileName(pumpDst)}");

        Console.WriteLine($"\nDone. {shimmed} assemblies shimmed, {totalNeutralized} native methods neutralized, {missing} missing. -> {outDir}");
        return 0;
    }

    // Give UnityEngine.Object a real managed string backing field for `name` and wire
    // get_name/set_name to it, so names round-trip headless. Overrides the no-op stub.
    private static void AddNameBackingField(ModuleDefinition module)
    {
        var obj = module.GetType("UnityEngine.Object");
        if (obj == null) { Console.WriteLine("    (!) UnityEngine.Object not found; carve-out skipped"); return; }
        var strType = module.TypeSystem.String;
        var field = new FieldDefinition("__simName", FieldAttributes.Private, strType);
        obj.Fields.Add(field);

        // Unity overloads ==/!= so an Object with a null NATIVE pointer reads as == null.
        // Our headless objects have no native pointer, so without this every `obj != null`
        // check in game code would be false. Rewrite to plain reference semantics.
        var refEquals = module.ImportReference(
            typeof(object).GetMethod("ReferenceEquals", new[] { typeof(object), typeof(object) }));
        foreach (var m in obj.Methods)
        {
            if (m.Name == "op_Equality" && m.Parameters.Count == 2)
            {
                var b = new MethodBody(m); var il = b.GetILProcessor();
                m.IsInternalCall = false; m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, refEquals); il.Emit(OpCodes.Ret);
                m.Body = b;
            }
            else if (m.Name == "op_Inequality" && m.Parameters.Count == 2)
            {
                var b = new MethodBody(m); var il = b.GetILProcessor();
                m.IsInternalCall = false; m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, refEquals);
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
                m.Body = b;
            }
        }

        foreach (var m in obj.Methods)
        {
            if (m.Name == "get_name" && m.Parameters.Count == 0 && m.ReturnType.MetadataType == MetadataType.String)
            {
                var body = new MethodBody(m); var il = body.GetILProcessor();
                m.IsInternalCall = false; m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
                m.Body = body;
            }
            else if (m.Name == "set_name" && m.Parameters.Count == 1)
            {
                var body = new MethodBody(m); var il = body.GetILProcessor();
                m.IsInternalCall = false; m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
                m.Body = body;
            }
        }
    }

    // Make MonoBehaviour.StartCoroutine run the coroutine SYNCHRONOUSLY to completion via
    // our injected pump, so every coroutine the game starts internally (damage routines,
    // ability sequences) resolves immediately headless. StopCoroutine*/string overloads no-op.
    private static void WireStartCoroutine(ModuleDefinition module)
    {
        var mb = module.GetType("UnityEngine.MonoBehaviour");
        if (mb == null) { Console.WriteLine("    (!) MonoBehaviour not found; coroutine wiring skipped"); return; }
        var run = module.ImportReference(
            typeof(Dc.SimHarness.Runtime.SimCoroutine).GetMethod("Run"));

        foreach (var m in mb.Methods)
        {
            bool isStart = m.Name == "StartCoroutine";
            bool isStop = m.Name is "StopCoroutine" or "StopAllCoroutines";
            if (!isStart && !isStop) continue;
            m.IsInternalCall = false; m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
            var body = new MethodBody(m); var il = body.GetILProcessor();

            // StartCoroutine(IEnumerator): drain it now, return null Coroutine.
            if (isStart && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.Collections.IEnumerator")
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, run);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
            else if (m.ReturnType.MetadataType == MetadataType.Void)
            {
                il.Emit(OpCodes.Ret);                       // StopCoroutine / StopAllCoroutines
            }
            else
            {
                il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);  // StartCoroutine(string) etc.
            }
            m.Body = body;
        }

        // Ship the pump assembly next to the shims so the resolver can bind it.
        var src = typeof(Dc.SimHarness.Runtime.SimCoroutine).Assembly.Location;
        Console.WriteLine($"    (coroutine pump wired; SimRuntime at {Path.GetFileName(src)})");
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
