using System;
using System.Collections.Generic;

namespace DawnKit
{
    /// <summary>
    /// One card/talent effect, mirroring the pack.json effect object
    /// (CARD-PACK-SPEC.md §2). String fields use the game's exact enum spellings
    /// (validated at Register()); references are declared by asset NAME and
    /// resolved by the engine at the correct load phase (two-phase resolution —
    /// clients never call AssetManager.GetStatus/GetCard themselves).
    /// </summary>
    public sealed class EffectSpec
    {
        /// <summary>EventHandler.GameTriggers member name (game spelling). Required.</summary>
        public string Trigger;
        /// <summary>Effect DSL codeLine; every command is validated against the embedded vocabulary.</summary>
        public string CodeLine;
        public bool Forecast = true;
        /// <summary>StatusEffect asset name, resolved by the engine in the world-asset phase.</summary>
        public string ReferenceStatus;
        /// <summary>Card asset names, index-aligned with the effect's referenceCard slots.</summary>
        public List<string> ReferenceCards;
        public bool HideReferenceCards;
        public List<ConditionSpec> Conditions;

        public EffectSpec() { }

        public EffectSpec(Trigger trigger, string codeLine)
        {
            Trigger = CleanEnums.GameName(trigger);
            CodeLine = codeLine;
        }
    }

    /// <summary>A play/effect condition (ConditionChecker semantics; game enum spellings).</summary>
    public sealed class ConditionSpec
    {
        /// <summary>ConditionChecker.ConditionValue member name.</summary>
        public string Value;
        /// <summary>ConditionChecker.ConditionOperator member name.</summary>
        public string Op;
        public string Target;
        public bool IgnoreForDisplay;
    }

    /// <summary>Card enchantment payload (LastingEffect semantics).</summary>
    public sealed class EnchantmentSpec
    {
        /// <summary>LastingEffect.EffectType member name.</summary>
        public string Type;
        public string Text;
        public bool Combat;
        public bool ShowStacks = true;
        public List<EffectSpec> Effects;
    }
}
