using System.Runtime.CompilerServices;
using Dc.SimHarness.Engine;

namespace Engine.Tests;

internal static class ModuleInit
{
    // Runs at assembly load, before any test method JITs — so the shim/Assembly-CSharp
    // resolver is installed before any game type is touched.
    [ModuleInitializer]
    internal static void Init() => GameBootstrap.EnsureInitialized();
}
