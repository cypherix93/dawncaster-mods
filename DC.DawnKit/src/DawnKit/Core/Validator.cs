using System;
using System.Collections.Generic;
using DawnKit.Content.Vocabulary;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>A problem that invalidates a single registration (item is skipped, load continues).</summary>
    internal sealed class ManifestError : Exception
    {
        public ManifestError(string message) : base(message) { }
    }

    /// <summary>
    /// Register()-time validation (SPEC.md §4.2): every check the pre-split loader
    /// ran, moved to registration time. Enum parsing is exact-first, then
    /// case-insensitive with a warning (warn-and-accept for the data path; the
    /// typed C# overloads always produce exact game spellings). Throws
    /// ManifestError on the first failure — one error per item, item skipped.
    /// </summary>
    internal static class Validator
    {
        internal static ParsedCard ParseCard(CardDraft d, CardKind kind)
        {
            if (string.IsNullOrEmpty(d.Name))
            {
                throw new ManifestError(KindLabel(kind) + " has no name");
            }
            if (d.CardId == 0)
            {
                throw new ManifestError("cardID is missing or 0");
            }

            var p = new ParsedCard
            {
                Owner = d.Owner ?? "(unknown)",
                Name = d.Name,
                CardId = d.CardId,
                Kind = kind,
                Classes = d.Classes,
                CodexDiscovered = d.CodexDiscovered,
                ArtPath = d.ArtPath,
                Description = d.Description ?? "",
                UtilityNumber = d.UtilityNumber ?? "",
                Charges = d.Charges,
                CardKeywords = d.CardKeywords != null ? new List<string>(d.CardKeywords) : new List<string>()
            };

            // Precedence mirrors the pre-split loader: set membership (synthetic
            // expansion) wins over a declared native expansion; a card with
            // neither is an error.
            if (d.Set != null)
            {
                p.Expansion = d.Set.Expansion;
            }
            else if (d.ExpansionSet || !string.IsNullOrEmpty(d.ExpansionRaw))
            {
                p.Expansion = ParseEnum<AssetManager.CardExpansions>(d.ExpansionRaw, "expansion", d.Name);
            }
            else
            {
                throw new ManifestError("missing expansion");
            }

            p.Type = ParseEnum<Card.CardType>(RequireRaw(d.TypeRaw, d.TypeSet, "type"), "type", d.Name);
            p.Category = ParseEnum<Card.CardCategory>(RequireRaw(d.CategoryRaw, d.CategorySet, "category"), "category", d.Name);
            p.Rarity = ParseEnum<Card.CardRariry>(RequireRaw(d.RarityRaw, d.RaritySet, "rarity"), "rarity", d.Name);
            // Suffix: unset (typed path convenience) defaults to None; an explicit
            // null/empty (data-path manifest omission) is an error, like the old loader.
            p.Suffix = d.SuffixSet
                ? ParseEnum<Card.Suffix>(d.SuffixRaw, "suffix", d.Name)
                : Card.Suffix.None;

            if (kind == CardKind.Weapon && p.Category != Card.CardCategory.BasicAttack)
            {
                throw new ManifestError($"weapon category is {p.Category}, must be BasicAttack");
            }
            // Starting cards deliberately carry NO category restriction: the
            // shipped 63-card corpus spans Action/Enchantment/Equipment
            // (WEAPON-SPEC §1) — any legal card shape may be a starting card.

            p.Costs = ValidateCosts(d.Costs);

            p.Keywords = new List<Card.CardProperties>();
            if (d.Keywords != null)
            {
                foreach (string kw in d.Keywords)
                {
                    p.Keywords.Add(ParseEnum<Card.CardProperties>(kw, "keyword", d.Name));
                }
            }

            p.Flags = new List<string>();
            if (d.Flags != null)
            {
                foreach (string flag in d.Flags)
                {
                    p.Flags.Add(ValidateFlag(flag));
                }
            }

            p.PlayConditions = ParseConditions(d.PlayConditions, d.Name);

            p.Effects = new List<ParsedEffect>();
            if (d.Effects != null)
            {
                foreach (EffectSpec fx in d.Effects)
                {
                    p.Effects.Add(ParseEffect(fx, d.Name, CommandVocabulary.EffectCommands));
                }
            }

            if (d.Enchantment != null)
            {
                var ench = new ParsedEnchantment
                {
                    Type = ParseEnum<LastingEffect.EffectType>(d.Enchantment.Type, "enchantment type", d.Name),
                    Text = d.Enchantment.Text ?? "",
                    Combat = d.Enchantment.Combat,
                    ShowStacks = d.Enchantment.ShowStacks,
                    Effects = new List<ParsedEffect>()
                };
                if (d.Enchantment.Effects != null)
                {
                    foreach (EffectSpec fx in d.Enchantment.Effects)
                    {
                        ench.Effects.Add(ParseEffect(fx, d.Name, CommandVocabulary.EffectCommands));
                    }
                }
                p.Enchantment = ench;
            }

            return p;
        }

        internal static ParsedTalent ParseTalent(TalentDraft d)
        {
            if (string.IsNullOrEmpty(d.Name))
            {
                throw new ManifestError("weapon power has no name");
            }
            if (d.TalentId == 0)
            {
                throw new ManifestError("talentID is missing or 0");
            }

            var p = new ParsedTalent
            {
                Owner = d.Owner ?? "(unknown)",
                Name = d.Name,
                TalentId = d.TalentId,
                Description = d.Description ?? "",
                Flavortext = d.Flavortext ?? "",
                Cooldown = d.Cooldown,
                Keywords = d.Keywords != null ? new List<string>(d.Keywords) : new List<string>(),
                RDex = d.RDex,
                RInt = d.RInt,
                RStr = d.RStr,
                Classes = d.Classes,
                ArtPath = d.ArtPath
            };

            // expansion: set > explicit native > Core (the pre-split default).
            if (d.Set != null)
            {
                p.Expansion = d.Set.Expansion;
            }
            else if (d.ExpansionSet || !string.IsNullOrEmpty(d.ExpansionRaw))
            {
                p.Expansion = ParseEnum<AssetManager.CardExpansions>(d.ExpansionRaw, "expansion", d.Name);
            }
            else
            {
                p.Expansion = AssetManager.CardExpansions.Core;
            }

            p.Effects = new List<ParsedEffect>();
            if (d.Effects != null)
            {
                foreach (EffectSpec fx in d.Effects)
                {
                    // Talent codeLines may use the full SpellEffects DSL plus the
                    // TalentHandler.RunTalentEffect extras — validate against the
                    // union vocabulary.
                    p.Effects.Add(ParseEffect(fx, d.Name, CommandVocabulary.TalentCommands));
                }
            }

            return p;
        }

        internal static string KindLabel(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.Weapon: return "weapon";
                case CardKind.StartingCard: return "starting card";
                default: return "card";
            }
        }

        private static string RequireRaw(string raw, bool wasSet, string field)
        {
            if (!wasSet && string.IsNullOrEmpty(raw))
            {
                throw new ManifestError($"missing {field}");
            }
            return raw;
        }

        private static ParsedEffect ParseEffect(EffectSpec m, string ownerName, HashSet<string> vocabulary)
        {
            if (m == null)
            {
                throw new ManifestError("null effect entry");
            }
            ValidateCodeLine(m.CodeLine, vocabulary);
            return new ParsedEffect
            {
                Trigger = ParseEnum<EventHandler.GameTriggers>(m.Trigger, "trigger", ownerName),
                CodeLine = m.CodeLine ?? "",
                Forecast = m.Forecast,
                ReferenceStatus = m.ReferenceStatus,
                ReferenceCards = m.ReferenceCards != null ? new List<string>(m.ReferenceCards) : null,
                HideReferenceCards = m.HideReferenceCards,
                Conditions = ParseConditions(m.Conditions, ownerName)
            };
        }

        private static List<ParsedCondition> ParseConditions(List<ConditionSpec> list, string cardName)
        {
            var result = new List<ParsedCondition>();
            if (list == null)
            {
                return result;
            }
            foreach (ConditionSpec c in list)
            {
                if (c == null)
                {
                    throw new ManifestError("null condition entry");
                }
                result.Add(new ParsedCondition
                {
                    Value = ParseEnum<ConditionChecker.ConditionValue>(c.Value, "condition value", cardName),
                    Op = ParseEnum<ConditionChecker.ConditionOperator>(c.Op, "condition op", cardName),
                    Target = c.Target ?? "",
                    IgnoreForDisplay = c.IgnoreForDisplay
                });
            }
            return result;
        }

        private static void ValidateCodeLine(string codeLine, HashSet<string> vocabulary)
        {
            if (vocabulary == null || string.IsNullOrEmpty(codeLine))
            {
                return;
            }
            foreach (string statement in codeLine.Split(';'))
            {
                string s = statement.Trim();
                if (s.Length == 0)
                {
                    continue;
                }
                string command = s.Split(':')[0].Trim();
                if (command.Length > 0 && !vocabulary.Contains(command))
                {
                    throw new ManifestError($"unknown effect command '{command}' in codeLine '{codeLine}'{DidYouMean.Suggest(command, vocabulary)}");
                }
            }
        }

        private static Dictionary<string, int> ValidateCosts(Dictionary<string, int> cost)
        {
            var result = new Dictionary<string, int>();
            if (cost == null)
            {
                return result;
            }
            foreach (KeyValuePair<string, int> kv in cost)
            {
                switch (kv.Key)
                {
                    case "DEX":
                    case "INT":
                    case "STR":
                    case "HOLY":
                    case "Neutral":
                    case "DEXINT":
                    case "DEXSTR":
                    case "INTSTR":
                    case "Life":
                        result[kv.Key] = kv.Value;
                        break;
                    default:
                        throw new ManifestError($"unknown cost key '{kv.Key}'");
                }
            }
            return result;
        }

        private static string ValidateFlag(string flag)
        {
            switch (flag)
            {
                case "uniqueInHand":
                case "canBeAcquired":
                case "hideConditionGlow":
                case "resetTempValues":
                case "cullLastWordFromName":
                case "overwriteUpgradable":
                case "cantbeupgraded":
                case "requireAllConditions":
                case "pauseQueue":
                case "excludeFromConjurations":
                case "excludeFromSunforge":
                case "excludeFromRewards":
                case "excludeFromCodex":
                    return flag;
                default:
                    throw new ManifestError($"unknown flag '{flag}'");
            }
        }

        internal static T ParseEnum<T>(string raw, string field, string cardName) where T : struct
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ManifestError($"missing {field}");
            }
            if (Enum.TryParse<T>(raw, ignoreCase: false, out T value) && Enum.IsDefined(typeof(T), value))
            {
                return value;
            }
            if (Enum.TryParse<T>(raw, ignoreCase: true, out value) && Enum.IsDefined(typeof(T), value))
            {
                DawnKitPlugin.Log.LogWarning($"[DawnKit] {cardName}: {field} '{raw}' only matched {typeof(T).Name}.{value} case-insensitively — fix the manifest spelling.");
                return value;
            }
            throw new ManifestError($"unknown {typeof(T).Name} member '{raw}' for {field}{DidYouMean.Suggest(raw, Enum.GetNames(typeof(T)))}");
        }
    }
}
