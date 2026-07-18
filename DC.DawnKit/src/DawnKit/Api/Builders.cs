using System;
using System.Collections.Generic;
using DawnKit.Core.Lifecycle;

namespace DawnKit
{
    /// <summary>
    /// Public card registry (SPEC.md §4.1): DawnKit.Cards.Build(name)... .Register().
    /// Registration is declarative and durable — the engine constructs and injects
    /// at the player-asset load phase, resolves name-declared references in the
    /// world-asset phase, refreshes caches/run lists, and re-applies automatically
    /// after ForceReloadAssets. Clients never observe phases, hooks, or reloads.
    /// </summary>
    public static class Cards
    {
        public static CardBuilder Build(string name) => new CardBuilder(name);

        /// <summary>Every card registration attempt (including failed ones), with ownership metadata.</summary>
        public static IReadOnlyList<RegistrationInfo> All => Core.Ownership.RegistrationLedger.OfKind("card");
    }

    /// <summary>
    /// Starting weapons (WEAPON-SPEC.md): a weapon IS a card with category
    /// BasicAttack, forced excludeFromRewards, registered in allCards only, and
    /// offered at character creation via Profession.weapons for its classes.
    /// </summary>
    public static class Weapons
    {
        public static WeaponBuilder Build(string name) => new WeaponBuilder(name);

        public static IReadOnlyList<RegistrationInfo> All => Core.Ownership.RegistrationLedger.OfKind("weapon");
    }

    /// <summary>
    /// Weapon powers (WEAPON-SPEC.md): tier-0 Talents with an ActivateWeapon
    /// payload, offered via Profession.talents. Structural conventions (tier 0,
    /// unique, empty requiredTalents/requiredProfessions) are enforced by the
    /// engine factory.
    /// </summary>
    public static class WeaponPowers
    {
        public static WeaponPowerBuilder Build(string name) => new WeaponPowerBuilder(name);

        public static IReadOnlyList<RegistrationInfo> All => Core.Ownership.RegistrationLedger.OfKind("weaponPower");
    }

    /// <summary>
    /// Shared card/weapon builder surface. Setters mirror the pack.json fields
    /// (CARD-PACK-SPEC.md §2); string overloads take the game's exact enum
    /// spellings (for 1:1 data-pack traceability), typed overloads take the
    /// clean-spelled mirror enums. All validation runs at Register() — enum
    /// membership (exact-then-case-insensitive with a warning), codeLine command
    /// vocabulary, cost-key and flag whitelists, missing name/ID. One bad item is
    /// skipped with a rich error; the load never crashes.
    /// </summary>
    public abstract class CardBuilderBase<TSelf> where TSelf : CardBuilderBase<TSelf>
    {
        internal readonly CardDraft Draft = new CardDraft();

        internal CardBuilderBase(string name)
        {
            Draft.Name = name;
        }

        private TSelf Self => (TSelf)this;

        /// <summary>Owning mod/pack display name — used for log lines, set grouping and (M1b) the ownership registry.</summary>
        public TSelf Owner(string owner) { Draft.Owner = owner; return Self; }

        /// <summary>Numeric cardID from your registered ID block (docs/ID-REGISTRY.md). M1b adds .AutoId().</summary>
        public TSelf Id(int cardId) { Draft.CardId = cardId; return Self; }

        /// <summary>Place the card in a registered mod set (its synthetic expansion).</summary>
        public TSelf InSet(SetHandle set) { Draft.Set = set; return Self; }

        /// <summary>Native expansion by game enum name (AssetManager.CardExpansions member).</summary>
        public TSelf Expansion(string gameName) { Draft.ExpansionRaw = gameName; Draft.ExpansionSet = true; return Self; }

        public TSelf Expansion(Expansion value) => Expansion(CleanEnums.GameName(value));

        public TSelf Type(string gameName) { Draft.TypeRaw = gameName; Draft.TypeSet = true; return Self; }

        public TSelf Type(CardType value) => Type(CleanEnums.GameName(value));

        public TSelf Category(string gameName) { Draft.CategoryRaw = gameName; Draft.CategorySet = true; return Self; }

        public TSelf Category(CardCategory value) => Category(CleanEnums.GameName(value));

        /// <summary>Card suffix. Unset defaults to None; an explicit null/empty string is a validation error (manifest parity).</summary>
        public TSelf Suffix(string gameName) { Draft.SuffixRaw = gameName; Draft.SuffixSet = true; return Self; }

        public TSelf Suffix(Suffix value) => Suffix(CleanEnums.GameName(value));

        public TSelf Rarity(string gameName) { Draft.RarityRaw = gameName; Draft.RaritySet = true; return Self; }

        public TSelf Rarity(Rarity value) => Rarity(CleanEnums.GameName(value));

        /// <summary>Add one cost component. Keys: DEX, INT, STR, HOLY, Neutral, DEXINT, DEXSTR, INTSTR, Life.</summary>
        public TSelf Cost(string key, int value)
        {
            if (Draft.Costs == null) Draft.Costs = new Dictionary<string, int>();
            Draft.Costs[key] = value;
            return Self;
        }

        public TSelf Costs(IDictionary<string, int> costs)
        {
            if (costs != null)
            {
                foreach (KeyValuePair<string, int> kv in costs) Cost(kv.Key, kv.Value);
            }
            return Self;
        }

        public TSelf Description(string text) { Draft.Description = text; return Self; }

        public TSelf UtilityNumber(string value) { Draft.UtilityNumber = value; return Self; }

        public TSelf Charges(int charges) { Draft.Charges = charges; return Self; }

        /// <summary>Card.CardProperties keyword by game enum name.</summary>
        public TSelf Keyword(string gameName)
        {
            if (Draft.Keywords == null) Draft.Keywords = new List<string>();
            Draft.Keywords.Add(gameName);
            return Self;
        }

        public TSelf Keywords(IEnumerable<string> gameNames)
        {
            if (gameNames != null)
            {
                foreach (string k in gameNames) Keyword(k);
            }
            return Self;
        }

        /// <summary>Free-text keyword strings (Card.cardKeywords).</summary>
        public TSelf CardKeywords(IEnumerable<string> keywords)
        {
            Draft.CardKeywords = keywords != null ? new List<string>(keywords) : new List<string>();
            return Self;
        }

        /// <summary>Boolean card flag by manifest name (whitelisted; e.g. uniqueInHand, excludeFromRewards).</summary>
        public TSelf Flag(string flag)
        {
            if (Draft.Flags == null) Draft.Flags = new List<string>();
            Draft.Flags.Add(flag);
            return Self;
        }

        public TSelf Flags(IEnumerable<string> flags)
        {
            if (flags != null)
            {
                foreach (string f in flags) Flag(f);
            }
            return Self;
        }

        public TSelf PlayCondition(ConditionSpec condition)
        {
            if (Draft.PlayConditions == null) Draft.PlayConditions = new List<ConditionSpec>();
            Draft.PlayConditions.Add(condition);
            return Self;
        }

        public TSelf PlayConditions(IEnumerable<ConditionSpec> conditions)
        {
            if (conditions != null)
            {
                foreach (ConditionSpec c in conditions) PlayCondition(c);
            }
            return Self;
        }

        public TSelf Effect(EffectSpec effect)
        {
            if (Draft.Effects == null) Draft.Effects = new List<EffectSpec>();
            Draft.Effects.Add(effect);
            return Self;
        }

        public TSelf Effect(Trigger trigger, string codeLine, Action<EffectSpec> configure = null)
        {
            var spec = new EffectSpec(trigger, codeLine);
            configure?.Invoke(spec);
            return Effect(spec);
        }

        public TSelf Effects(IEnumerable<EffectSpec> effects)
        {
            if (effects != null)
            {
                foreach (EffectSpec e in effects) Effect(e);
            }
            return Self;
        }

        public TSelf Enchantment(EnchantmentSpec enchantment) { Draft.Enchantment = enchantment; return Self; }

        /// <summary>Absolute path to a 512×512 PNG. Missing/unloadable art falls back to the generated cost-color placeholder.</summary>
        public TSelf Art(string absolutePngPath) { Draft.ArtPath = absolutePngPath; return Self; }

        /// <summary>Mark the card discovered in the Codex (in-memory; default true).</summary>
        public TSelf CodexDiscovery(bool enabled) { Draft.CodexDiscovered = enabled; return Self; }
    }

    public sealed class CardBuilder : CardBuilderBase<CardBuilder>
    {
        internal CardBuilder(string name) : base(name) { }

        /// <summary>Validate and register. The engine applies the card at the next asset-load phase.</summary>
        public RegisterResult Register() => Registry.RegisterCard(Draft, isWeapon: false);
    }

    public sealed class WeaponBuilder : CardBuilderBase<WeaponBuilder>
    {
        internal WeaponBuilder(string name) : base(name) { }

        /// <summary>Profession asset names offered this weapon at character creation ("all" = every class).</summary>
        public WeaponBuilder ForClasses(params string[] classes)
        {
            Draft.Classes = classes != null ? new List<string>(classes) : null;
            return this;
        }

        public WeaponBuilder ForClasses(IEnumerable<string> classes)
        {
            Draft.Classes = classes != null ? new List<string>(classes) : null;
            return this;
        }

        /// <summary>Validate and register. Category must be BasicAttack; excludeFromRewards is forced true.</summary>
        public RegisterResult Register() => Registry.RegisterCard(Draft, isWeapon: true);
    }

    public sealed class WeaponPowerBuilder
    {
        internal readonly TalentDraft Draft = new TalentDraft();

        internal WeaponPowerBuilder(string name)
        {
            Draft.Name = name;
        }

        public WeaponPowerBuilder Owner(string owner) { Draft.Owner = owner; return this; }

        /// <summary>Numeric talentID (its own ID namespace — WEAPON-SPEC.md §3).</summary>
        public WeaponPowerBuilder Id(int talentId) { Draft.TalentId = talentId; return this; }

        public WeaponPowerBuilder InSet(SetHandle set) { Draft.Set = set; return this; }

        /// <summary>Native expansion by game enum name. Unset defaults to Core (or the set's expansion via InSet).</summary>
        public WeaponPowerBuilder Expansion(string gameName) { Draft.ExpansionRaw = gameName; Draft.ExpansionSet = true; return this; }

        public WeaponPowerBuilder Expansion(Expansion value) => Expansion(CleanEnums.GameName(value));

        public WeaponPowerBuilder Description(string text) { Draft.Description = text; return this; }

        public WeaponPowerBuilder Flavortext(string text) { Draft.Flavortext = text; return this; }

        public WeaponPowerBuilder Cooldown(int turns) { Draft.Cooldown = turns; return this; }

        public WeaponPowerBuilder Keywords(IEnumerable<string> keywords)
        {
            Draft.Keywords = keywords != null ? new List<string>(keywords) : null;
            return this;
        }

        /// <summary>Attribute requirements shown at character creation (display gating only).</summary>
        public WeaponPowerBuilder Requirements(int rDEX = 0, int rINT = 0, int rSTR = 0)
        {
            Draft.RDex = rDEX;
            Draft.RInt = rINT;
            Draft.RStr = rSTR;
            return this;
        }

        public WeaponPowerBuilder Effect(EffectSpec effect)
        {
            if (Draft.Effects == null) Draft.Effects = new List<EffectSpec>();
            Draft.Effects.Add(effect);
            return this;
        }

        public WeaponPowerBuilder Effect(Trigger trigger, string codeLine, Action<EffectSpec> configure = null)
        {
            var spec = new EffectSpec(trigger, codeLine);
            configure?.Invoke(spec);
            return Effect(spec);
        }

        public WeaponPowerBuilder Effects(IEnumerable<EffectSpec> effects)
        {
            if (effects != null)
            {
                foreach (EffectSpec e in effects) Effect(e);
            }
            return this;
        }

        /// <summary>Profession asset names offered this power at character creation ("all" = every class).</summary>
        public WeaponPowerBuilder ForClasses(params string[] classes)
        {
            Draft.Classes = classes != null ? new List<string>(classes) : null;
            return this;
        }

        public WeaponPowerBuilder ForClasses(IEnumerable<string> classes)
        {
            Draft.Classes = classes != null ? new List<string>(classes) : null;
            return this;
        }

        /// <summary>Absolute path to a 512×512 PNG powerImage; falls back to the white two-band placeholder.</summary>
        public WeaponPowerBuilder Art(string absolutePngPath) { Draft.ArtPath = absolutePngPath; return this; }

        /// <summary>Validate and register. Tier is forced 0, unique true, requirement lists empty (engine conventions).</summary>
        public RegisterResult Register() => Registry.RegisterTalent(Draft);
    }
}
