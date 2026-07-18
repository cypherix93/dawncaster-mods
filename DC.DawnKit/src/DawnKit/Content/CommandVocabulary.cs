using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DawnKit.Content.Vocabulary
{
    /// <summary>
    /// Effect-DSL command vocabularies, embedded into the engine DLL at build time
    /// (docs/research/reference/*.txt). EffectCommands gates card codeLines;
    /// TalentCommands is the union with the TalentHandler.RunTalentEffect extras
    /// (the switch falls through to SpellEffects) and gates weapon-power
    /// codeLines. Null vocabulary = validation unavailable (fail-open with a
    /// warning, never a crash).
    /// </summary>
    internal static class CommandVocabulary
    {
        internal static HashSet<string> EffectCommands { get; private set; }
        internal static HashSet<string> TalentCommands { get; private set; }

        internal static void Initialize()
        {
            EffectCommands = LoadCommandFile("effect-commands.txt");
            HashSet<string> talentOnly = LoadCommandFile("talent-commands.txt");
            if (EffectCommands != null && talentOnly != null)
            {
                TalentCommands = new HashSet<string>(EffectCommands, StringComparer.Ordinal);
                TalentCommands.UnionWith(talentOnly);
            }
            else
            {
                TalentCommands = null; // half a vocabulary would misreport — disable instead
            }
            DawnKitPlugin.Log.LogInfo($"[DawnKit] Command vocabulary: {(EffectCommands != null ? EffectCommands.Count.ToString() : "unavailable")} effect / {(TalentCommands != null ? TalentCommands.Count.ToString() : "unavailable")} talent-union.");
        }

        private static HashSet<string> LoadCommandFile(string fileName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("DawnKit." + fileName))
                {
                    if (stream == null)
                    {
                        DawnKitPlugin.Log.LogWarning($"[DawnKit] Embedded {fileName} not found; codeLine command validation disabled.");
                        return null;
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        var set = new HashSet<string>(StringComparer.Ordinal);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length > 0)
                            {
                                set.Add(line);
                            }
                        }
                        return set;
                    }
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogWarning($"[DawnKit] Failed to load command vocabulary {fileName}: {ex.Message}; codeLine command validation disabled.");
                return null;
            }
        }
    }
}
