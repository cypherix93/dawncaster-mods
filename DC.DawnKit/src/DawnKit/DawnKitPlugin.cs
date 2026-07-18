using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DawnKit.Content.Vocabulary;
using DawnKit.Core;
using HarmonyLib;

namespace DawnKit
{
    /// <summary>
    /// The DawnKit engine (SPEC.md): owns ALL Harmony patches, the load-phase
    /// lifecycle, two-phase reference resolution, safe SO factories,
    /// set-screen/Codex/class integration and re-injection after asset wipes.
    /// Ships zero content and zero tuning numbers (docs/CONVENTIONS.md). Client
    /// mods declare a BepInDependency on <see cref="Guid"/> and register content
    /// through the public API (DawnKit.Cards/Sets/Weapons/WeaponPowers) in
    /// Awake(); they never patch the game for content injection.
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    public sealed class DawnKitPlugin : BaseUnityPlugin
    {
        public const string Guid = "dcmods.dawnkit";
        public const string PluginName = "DawnKit";
        public const string Version = "0.5.0";

        internal static ManualLogSource Log;
        internal static bool Verbose;

        private ConfigEntry<bool> engineEnabled;
        private ConfigEntry<bool> verboseLogging;

        private void Awake()
        {
            Log = Logger;

            engineEnabled = Config.Bind("Engine", "Enabled", true,
                "Master switch (fail-safe rule): false = no Harmony patches are applied and no content " +
                "is injected — completely vanilla behavior, regardless of installed content mods.");
            verboseLogging = Config.Bind("Engine", "VerboseLogging", false,
                "Decision-level Debug logs (per-item routing, reference-resolution detail, class counts).");
            Verbose = verboseLogging.Value;

            if (!engineEnabled.Value)
            {
                Logger.LogWarning("[DawnKit] Engine disabled by config ([Engine] Enabled=false) — no patches applied, vanilla behavior.");
                return;
            }

            CommandVocabulary.Initialize();

            var harmony = new Harmony(Guid);
            PatchManager.ApplyAll(harmony);
            Logger.LogInfo($"[DawnKit] {PluginName} {Version} loaded — {PatchManager.FoundCount}/{PatchManager.TargetCount} patch targets applied.");
        }
    }
}
