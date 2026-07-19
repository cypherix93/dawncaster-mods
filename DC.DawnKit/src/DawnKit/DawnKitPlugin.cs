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
    /// through the public API (DawnKit.Cards/Sets/Weapons/WeaponPowers/
    /// StartingCards) in Awake(); they never patch the game for content injection.
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    public sealed class DawnKitPlugin : BaseUnityPlugin
    {
        public const string Guid = "dcmods.dawnkit";
        public const string PluginName = "DawnKit";
        public const string Version = "0.9.0";

        internal static ManualLogSource Log;
        internal static bool Verbose;
        internal static bool DiagnosticsDump;

        private ConfigEntry<bool> engineEnabled;
        private ConfigEntry<bool> verboseLogging;
        private ConfigEntry<bool> diagnosticsDump;
        private ConfigEntry<bool> eventsEnabled;

        private void Awake()
        {
            Log = Logger;

            engineEnabled = Config.Bind("Engine", "Enabled", true,
                "Master switch (fail-safe rule): false = no Harmony patches are applied and no content " +
                "is injected — completely vanilla behavior, regardless of installed content mods.");
            verboseLogging = Config.Bind("Engine", "VerboseLogging", false,
                "Decision-level Debug logs (per-item routing, reference-resolution detail, class counts).");
            diagnosticsDump = Config.Bind("Engine", "DiagnosticsDump", false,
                "Write BepInEx/DawnKit-diagnostics.txt at boot completion: per-mod registered content " +
                "with IDs/names/sets/classes, the ownership table, the conflict report and unresolved " +
                "references. Overwritten each boot. The bug-report channel — attach it to issue reports.");
            eventsEnabled = Config.Bind("Events", "Enabled", true,
                "Master switch for modded opportunity events (fail-safe rule): false = no event " +
                "injection and no story-serving patch behavior — vanilla events untouched.");
            Verbose = verboseLogging.Value;
            DiagnosticsDump = diagnosticsDump.Value;
            Integration.Dialogues.DialogueIntegration.Enabled = eventsEnabled.Value;

            if (!engineEnabled.Value)
            {
                Logger.LogWarning("[DawnKit] Engine disabled by config ([Engine] Enabled=false) — no patches applied, vanilla behavior.");
                return;
            }

            CommandVocabulary.Initialize();

            // AutoId determinism gate (SPEC.md §4.3/§8): the C# formula must match
            // the Python twin (tools/gamedata.py) on the shared reference vectors.
            if (Core.Ownership.AutoIdAllocator.SelfCheck())
            {
                Logger.LogInfo("[DawnKit] AutoId self-check: 5/5 reference vectors OK.");
            }

            var harmony = new Harmony(Guid);
            PatchManager.ApplyAll(harmony);
            Logger.LogInfo($"[DawnKit] {PluginName} {Version} loaded — {PatchManager.FoundCount}/{PatchManager.TargetCount} patch targets applied.");
        }
    }
}
