using System;
using System.Collections.Generic;
using DawnKit.Content.Vocabulary;
using Newtonsoft.Json.Linq;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>
    /// Register()-time lint of a compiled Ink story (EVENT-SPEC §3): JSON parses,
    /// inkVersion inside the engine pin [18, 20], and every `&gt;&gt;&gt;` action line uses
    /// the closed dialogue-action vocabulary. `goto:&lt;knot&gt;` must target a real
    /// knot; STORYFUNCTION is reserved in v0.1 (EVENT-SPEC §11 #4). Throws
    /// ManifestError on the first failure, mirroring Validator. Null vocabulary =
    /// command validation unavailable (fail-open with the codeLine idiom).
    /// </summary>
    internal static class InkStoryLint
    {
        internal const int MinInkVersion = 18;
        internal const int MaxInkVersion = 20;

        internal static void Check(string storyJson)
        {
            JObject story;
            try
            {
                story = JObject.Parse(storyJson);
            }
            catch (Exception ex)
            {
                throw new ManifestError("story is not valid JSON: " + ex.Message);
            }

            JToken version = story["inkVersion"];
            if (version == null || version.Type != JTokenType.Integer)
            {
                throw new ManifestError("story has no integer inkVersion — not compiled Ink JSON (compile the .ink with inklecate v1.0.0)");
            }
            int inkVersion = (int)version;
            if (inkVersion < MinInkVersion || inkVersion > MaxInkVersion)
            {
                throw new ManifestError(
                    $"story inkVersion {inkVersion} outside the supported range {MinInkVersion}-{MaxInkVersion} — " +
                    "the game runtime is pinned at 20; compile with inklecate v1.0.0 (v1.1+ emits 21, which the game rejects)");
            }

            HashSet<string> knots = Knots(story);
            foreach (string line in Strings(story["root"]))
            {
                if (line.StartsWith("^", StringComparison.Ordinal) && line.Contains(">>>"))
                {
                    CheckActionLine(line, knots);
                }
            }
        }

        /// <summary>Top-level knot names: keys of the root container's trailing dict.</summary>
        private static HashSet<string> Knots(JObject story)
        {
            var knots = new HashSet<string>(StringComparer.Ordinal);
            if (story["root"] is JArray root && root.Count > 0 && root[root.Count - 1] is JObject named)
            {
                foreach (JProperty prop in named.Properties())
                {
                    if (prop.Name != "#f")
                    {
                        knots.Add(prop.Name);
                    }
                }
            }
            return knots;
        }

        private static IEnumerable<string> Strings(JToken node)
        {
            if (node is JValue value && value.Type == JTokenType.String)
            {
                yield return (string)value.Value;
            }
            else if (node is JContainer container)
            {
                foreach (JToken child in container.Children())
                {
                    foreach (string s in Strings(child))
                    {
                        yield return s;
                    }
                }
            }
        }

        // Mirrors DialogueManagerINK.RunDialogueAction (DialogueManagerINK.cs:
        // 1264-1303): strip to the first '>', remove >>>>/>>> and newlines,
        // ';'-split into ':'-separated statements. goto/STORYFUNCTION match
        // case-SENSITIVELY there; everything else is lowercased by
        // DialogueActionHandler.RunActionCode — hence the case-insensitive set.
        private static void CheckActionLine(string line, HashSet<string> knots)
        {
            string code = line.Substring(1);
            int gt = code.IndexOf('>');
            if (gt > 0)
            {
                code = code.Remove(0, gt);
            }
            code = code.Replace(">>>>", "").Replace(">>>", "").Replace("\n", "");
            foreach (string stmt in code.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(stmt))
                {
                    continue;
                }
                string[] parts = stmt.Split(':');
                string cmd = parts[0];
                if (cmd == "goto")
                {
                    string target = parts.Length > 1 ? parts[1] : "";
                    if (!knots.Contains(target))
                    {
                        throw new ManifestError($"story action 'goto:{target}' targets a knot that does not exist");
                    }
                    continue;
                }
                if (cmd == "STORYFUNCTION")
                {
                    throw new ManifestError("story uses STORYFUNCTION — reserved in events v0.1 (EVENT-SPEC §11 #4)");
                }
                if (CommandVocabulary.DialogueCommands == null || CommandVocabulary.DialogueCommands.Contains(cmd))
                {
                    continue;
                }
                string hint = DidYouMean.Suggest(cmd.ToLowerInvariant(), CommandVocabulary.DialogueCommands);
                throw new ManifestError($"story action command '{cmd}' is not a dialogue-action command{hint}");
            }
        }
    }
}
