using Mono.Cecil;
using Mono.Cecil.Cil;

// Sizes the shadow-UnityEngine shim: walks Assembly-CSharp IL and collects every
// member reference into a NON-Assembly-CSharp, non-BCL assembly (UnityEngine*,
// UniTask, DOTween, TextMeshPro, ...), grouped by the assembly that must be shimmed.
// Reports both the direct combat-class surface and the transitive-closure surface.

const string managed = @"E:\Games\Steam\steamapps\common\Dawncaster\Dawncaster_Data\Managed";
var asmPath = Path.Combine(managed, "Assembly-CSharp.dll");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { AssemblyResolver = resolver });
var main = asm.MainModule;

// Combat + card-math class set (the path v1 needs).
string[] seeds = {
    "SpellEffects", "CombatHandler", "MonsterHandler", "DamageCalculations",
    "ConditionChecker", "CardHandler", "PlayerHandler", "PlayerData", "DeckHandler",
    "EventHandler", "SpellManager", "Card", "CardContainer", "StatusEffect", "Status",
    "Battle", "CardEffect", "Condition", "NumberParser", "Enchantment",
    "LastingEffect", "LastingEffectContainer", "GameTextTranslator",
};

// BCL / self scopes we do NOT need to shim.
static bool IsBcl(string a) =>
    a is "mscorlib" or "netstandard" or "System" || a.StartsWith("System.")
    || a.StartsWith("Mono.") || a is "Newtonsoft.Json";
static bool IsSelf(string a) => a is "Assembly-CSharp";

static string ScopeAsm(TypeReference tr)
{
    var s = tr.Scope;
    while (s is TypeReference nested) s = nested.Scope; // defensive
    return s switch
    {
        AssemblyNameReference an => an.Name,
        ModuleDefinition md => md.Assembly.Name.Name,
        ModuleReference => tr.Module.Assembly.Name.Name,
        _ => s?.Name ?? "?"
    };
}

TypeDefinition? Find(string n) => main.Types.FirstOrDefault(t => t.Name == n);

var seedMethods = new List<MethodDefinition>();
var missing = new List<string>();
foreach (var n in seeds)
{
    var t = Find(n);
    if (t == null) { missing.Add(n); continue; }
    seedMethods.AddRange(t.Methods.Where(m => m.HasBody));
}
if (missing.Count > 0) Console.WriteLine($"(seed types not found: {string.Join(", ", missing)})\n");

// external assembly -> distinct types, distinct members
var directTypes = new Dictionary<string, HashSet<string>>();
var directMembers = new Dictionary<string, HashSet<string>>();
var transTypes = new Dictionary<string, HashSet<string>>();
var transMembers = new Dictionary<string, HashSet<string>>();

void Record(Dictionary<string, HashSet<string>> types, Dictionary<string, HashSet<string>> members, MethodDefinition m)
{
    foreach (var ins in m.Body.Instructions)
    {
        TypeReference? dt = (ins.Operand as MethodReference)?.DeclaringType
                         ?? (ins.Operand as FieldReference)?.DeclaringType
                         ?? ins.Operand as TypeReference;
        if (dt == null) continue;
        var a = ScopeAsm(dt);
        if (IsBcl(a) || IsSelf(a)) continue;
        (types.TryGetValue(a, out var ts) ? ts : types[a] = new()).Add(dt.FullName);
        var memName = ins.Operand is MemberReference mr ? $"{dt.FullName}::{mr.Name}" : dt.FullName;
        (members.TryGetValue(a, out var ms) ? ms : members[a] = new()).Add(memName);
    }
}

// Direct pass.
foreach (var m in seedMethods) Record(directTypes, directMembers, m);

// Transitive closure over Assembly-CSharp methods reachable from the seed set.
var visited = new HashSet<string>();
var queue = new Queue<MethodDefinition>(seedMethods);
foreach (var m in seedMethods) visited.Add(m.FullName);
int cap = 60000;
while (queue.Count > 0 && visited.Count < cap)
{
    var m = queue.Dequeue();
    Record(transTypes, transMembers, m);
    foreach (var ins in m.Body.Instructions)
    {
        if (ins.Operand is MethodReference callee && ScopeAsm(callee.DeclaringType) == "Assembly-CSharp")
        {
            var def = callee.Resolve();
            if (def is { HasBody: true } && visited.Add(def.FullName)) queue.Enqueue(def);
        }
    }
}

void Report(string title, Dictionary<string, HashSet<string>> types, Dictionary<string, HashSet<string>> members)
{
    Console.WriteLine($"\n=== {title} ===");
    Console.WriteLine($"{"assembly",-40}{"types",8}{"members",10}");
    foreach (var kv in members.OrderByDescending(k => k.Value.Count))
        Console.WriteLine($"{kv.Key,-40}{types[kv.Key].Count,8}{kv.Value.Count,10}");
}

Console.WriteLine($"Seed methods with bodies: {seedMethods.Count}");
Report("DIRECT (combat classes only)", directTypes, directMembers);
Report($"TRANSITIVE (closure, {visited.Count} methods visited{(visited.Count >= cap ? ", CAPPED" : "")})", transTypes, transMembers);

// Flag the tricky non-CoreModule native libs on the combat path.
string[] tricky = { "UniTask", "DOTween", "Unity.TextMeshPro", "TextMeshPro", "UnityEngine.UIModule", "UnityEngine.UI", "LeanTween" };
Console.WriteLine("\n=== tricky native libs on combat path? ===");
foreach (var t in tricky)
{
    bool d = directMembers.Keys.Any(k => k.Contains(t, StringComparison.OrdinalIgnoreCase));
    bool tr = transMembers.Keys.Any(k => k.Contains(t, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"  {t,-28} direct={(d ? "YES" : "no")}  transitive={(tr ? "YES" : "no")}");
}

// The CoreModule surface is the primary shim target — list its distinct members count clearly.
Console.WriteLine();
foreach (var core in new[] { "UnityEngine.CoreModule", "UnityEngine" })
    if (transMembers.TryGetValue(core, out var ms))
        Console.WriteLine($"** {core}: {transTypes[core].Count} types, {ms.Count} distinct members to stub (transitive) **");
