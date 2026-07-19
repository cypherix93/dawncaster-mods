using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DawnKit.Packs
{
    /// <summary>
    /// The bundled data-driven client (SPEC.md §3.1): scans pack folders
    /// (&lt;PacksPath&gt;/&lt;Pack&gt;/pack.json), parses the manifests and registers all
    /// content through the DawnKit PUBLIC API — this assembly references no engine
    /// internals, no game assembly and no Harmony, and runs zero patches. The
    /// engine applies the registrations at the load phases.
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInDependency(DawnKitPlugin.Guid)]
    public sealed class PacksPlugin : BaseUnityPlugin
    {
        public const string Guid = "dcmods.dawnkit.packs";
        public const string PluginName = "DawnKit.Packs";
        public const string Version = "0.9.0";

        /// <summary>Legacy pre-split monolith config, migrated on first run (SPEC.md §5).</summary>
        private const string LegacyConfigFile = "com.dawncastermods.sandbox.cfg";

        internal static ManualLogSource Log;

        private ConfigEntry<string> packsPath;
        private ConfigEntry<string> expansionOverride;
        private ConfigEntry<bool> autoDiscoverModCards;

        private void Awake()
        {
            Log = Logger;

            bool firstRun = !File.Exists(Config.ConfigFilePath);

            packsPath = Config.Bind("Packs", "PacksPath",
                Path.Combine(Paths.PluginPath, "DawncasterPacks"),
                "Directory scanned for <Pack>/pack.json card-pack manifests (CARD-PACK-SPEC.md schema). " +
                "Dev setup: point it at the repo root so the checked-in DC.<Pack> packages are the live source.");
            expansionOverride = Config.Bind("Packs", "ExpansionOverride",
                "",
                "Emergency override: when non-empty, every loaded card's expansion is forced to this " +
                "AssetManager.CardExpansions member and per-pack synthetic card sets are disabled " +
                "(no set rows in run settings). Default empty = each pack becomes its own card set, " +
                "toggleable in the run-settings 'card sets' screen like the official sets.");
            autoDiscoverModCards = Config.Bind("Packs", "AutoDiscoverModCards",
                true,
                "Mark all loaded mod cards as discovered in the Codex (in-memory injection into the " +
                "codex card list) so they render face-up instead of as undiscovered silhouettes.");

            if (firstRun)
            {
                MigrateLegacyConfig();
            }

            string overrideCanonical = null;
            if (!string.IsNullOrEmpty(expansionOverride.Value))
            {
                if (Sets.TryParseNativeExpansion(expansionOverride.Value, out string canonical))
                {
                    overrideCanonical = canonical;
                }
                else
                {
                    Logger.LogWarning($"[DawnKit.Packs] ExpansionOverride '{expansionOverride.Value}' is not a CardExpansions member — manifest expansions will be used as-is.");
                }
            }

            Logger.LogInfo($"[DawnKit.Packs] Configured. PacksPath={packsPath.Value}, ExpansionOverride={(overrideCanonical ?? "(none — per-pack synthetic sets)")}, AutoDiscoverModCards={autoDiscoverModCards.Value}.");

            // schemaVersion gate determinism check (the M1b AutoId self-check
            // pattern): pins the decision table in the boot log.
            if (SchemaGate.SelfCheck(out int passed, out int total))
            {
                Logger.LogInfo($"[DawnKit.Packs] Schema gate self-check: {passed}/{total} reference cases OK (supported schemaVersion {SchemaGate.SupportedSchemaVersion}).");
            }
            else
            {
                Logger.LogError($"[DawnKit.Packs] Schema gate self-check FAILED: {passed}/{total} reference cases — schemaVersion refusals may be wrong.");
            }

            PackScanner.RegisterAll(packsPath.Value, overrideCanonical, autoDiscoverModCards.Value);
        }

        /// <summary>
        /// First-run migration (SPEC.md §5): the pre-split monolith kept its [Packs]
        /// keys in com.dawncastermods.sandbox.cfg. Honor them as the initial values
        /// of this plugin's own config so the game keeps working without user
        /// edits; the [Sandbox] key stays with the sandbox plugin.
        /// </summary>
        private void MigrateLegacyConfig()
        {
            try
            {
                string legacyPath = Path.Combine(Paths.ConfigPath, LegacyConfigFile);
                if (!File.Exists(legacyPath))
                {
                    return;
                }
                Dictionary<string, string> legacy = ParseCfg(legacyPath);
                bool migrated = false;
                if (legacy.TryGetValue("Packs.PacksPath", out string legacyPacksPath) && !string.IsNullOrEmpty(legacyPacksPath))
                {
                    packsPath.Value = legacyPacksPath;
                    migrated = true;
                }
                if (legacy.TryGetValue("Packs.ExpansionOverride", out string legacyOverride))
                {
                    expansionOverride.Value = legacyOverride;
                    migrated = true;
                }
                if (legacy.TryGetValue("Packs.AutoDiscoverModCards", out string legacyDiscover) &&
                    bool.TryParse(legacyDiscover, out bool discover))
                {
                    autoDiscoverModCards.Value = discover;
                    migrated = true;
                }
                if (migrated)
                {
                    Config.Save();
                    Logger.LogWarning($"[DawnKit.Packs] Migrated [Packs] settings from legacy {LegacyConfigFile} (PacksPath={packsPath.Value}). " +
                                      $"Future changes go in {System.IO.Path.GetFileName(Config.ConfigFilePath)} — the legacy [Packs] keys are no longer read.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DawnKit.Packs] Legacy config migration failed ({ex.Message}) — using defaults.");
            }
        }

        /// <summary>Minimal BepInEx cfg reader: "[Section]" headers + "Key = Value" lines → "Section.Key" map.</summary>
        private static Dictionary<string, string> ParseCfg(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    result[$"{section}.{key}"] = value;
                }
            }
            return result;
        }
    }
}
