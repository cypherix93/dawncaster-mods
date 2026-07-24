using System.Runtime.CompilerServices;
using Dc.SimHarness.Engine;
internal static class HostModuleInit
{
    [ModuleInitializer]
    internal static void Init() => GameBootstrap.EnsureInitialized();
}
