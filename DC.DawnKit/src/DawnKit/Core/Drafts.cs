using System.Collections.Generic;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>Raw builder inputs for a card/weapon, validated by Validator at Register().</summary>
    internal sealed class CardDraft
    {
        internal string Owner;
        internal string Name;
        internal int CardId;
        internal bool AutoIdRequested;
        internal SetHandle Set;
        internal string ExpansionRaw;
        internal bool ExpansionSet;
        internal string TypeRaw;
        internal bool TypeSet;
        internal string CategoryRaw;
        internal bool CategorySet;
        internal string SuffixRaw;
        internal bool SuffixSet;
        internal string RarityRaw;
        internal bool RaritySet;
        internal Dictionary<string, int> Costs;
        internal string Description;
        internal string UtilityNumber;
        internal int Charges;
        internal List<string> Keywords;
        internal List<string> CardKeywords;
        internal List<string> Flags;
        internal List<ConditionSpec> PlayConditions;
        internal List<EffectSpec> Effects;
        internal EnchantmentSpec Enchantment;
        internal string ArtPath;
        internal bool CodexDiscovered = true;
        internal List<string> Classes; // weapons only
    }

    /// <summary>Raw builder inputs for a weapon power (tier-0 Talent).</summary>
    internal sealed class TalentDraft
    {
        internal string Owner;
        internal string Name;
        internal int TalentId;
        internal bool AutoIdRequested;
        internal SetHandle Set;
        internal string ExpansionRaw;
        internal bool ExpansionSet;
        internal string Description;
        internal string Flavortext;
        internal int Cooldown;
        internal List<string> Keywords;
        internal int RDex;
        internal int RInt;
        internal int RStr;
        internal List<EffectSpec> Effects;
        internal List<string> Classes;
        internal string ArtPath;
    }

    // ------------------------------------------------------------------
    // Parsed (validated) specs — everything the factories need, with all
    // enum members resolved against the game's enums. Built once at
    // Register(); reused by every (re-)injection pass.
    // ------------------------------------------------------------------

    internal sealed class ParsedCondition
    {
        internal ConditionChecker.ConditionValue Value;
        internal ConditionChecker.ConditionOperator Op;
        internal string Target;
        internal bool IgnoreForDisplay;
    }

    internal sealed class ParsedEffect
    {
        internal EventHandler.GameTriggers Trigger;
        internal string CodeLine;
        internal bool Forecast;
        internal string ReferenceStatus;   // resolved by name in phase 2
        internal List<string> ReferenceCards; // resolved by name, index-aligned
        internal bool HideReferenceCards;
        internal List<ParsedCondition> Conditions;
    }

    internal sealed class ParsedEnchantment
    {
        internal LastingEffect.EffectType Type;
        internal string Text;
        internal bool Combat;
        internal bool ShowStacks;
        internal List<ParsedEffect> Effects;
    }

    internal sealed class ParsedCard
    {
        internal string Owner;
        internal string Name;
        internal int CardId;
        internal AssetManager.CardExpansions Expansion;
        internal Card.CardType Type;
        internal Card.CardCategory Category;
        internal Card.Suffix Suffix;
        internal Card.CardRariry Rarity;
        internal Dictionary<string, int> Costs;
        internal string Description;
        internal string UtilityNumber;
        internal int Charges;
        internal List<Card.CardProperties> Keywords;
        internal List<string> CardKeywords;
        internal List<string> Flags;
        internal List<ParsedCondition> PlayConditions;
        internal List<ParsedEffect> Effects;
        internal ParsedEnchantment Enchantment; // null => empty non-null default payload
        internal string ArtPath;
        internal bool CodexDiscovered;
        internal bool IsWeapon;
        internal List<string> Classes;
    }

    internal sealed class ParsedTalent
    {
        internal string Owner;
        internal string Name;
        internal int TalentId;
        internal AssetManager.CardExpansions Expansion;
        internal string Description;
        internal string Flavortext;
        internal int Cooldown;
        internal List<string> Keywords;
        internal int RDex;
        internal int RInt;
        internal int RStr;
        internal List<ParsedEffect> Effects;
        internal List<string> Classes;
        internal string ArtPath;
    }
}
