using System.Collections.Generic;

namespace Dawncaster.Sandbox
{
    // DTOs mirroring CARD-PACK-SPEC.md §2 (packs/<Pack>/pack.json).
    // Field names match the JSON keys 1:1; Newtonsoft binds public fields by name
    // and ignores unknown members (so "meta" and "idBlock" extras are dropped).

    public class PackManifest
    {
        public string pack;
        public List<long> idBlock;
        public List<CardManifest> cards;
        // v1.1 (WEAPON-SPEC.md §2) — both optional.
        public List<WeaponManifest> weapons;
        public List<WeaponPowerManifest> weaponPowers;
    }

    public class CardManifest
    {
        public string name;
        public int cardID;
        public string expansion;
        public string type;
        public string category;
        public string suffix;
        public string rarity;
        public Dictionary<string, int> cost;
        public string description;
        public List<string> keywords;
        public List<string> cardKeywords;
        public string utilityNumber;
        public int charges;
        public List<string> flags;
        public List<ConditionManifest> playConditions;
        public List<EffectManifest> effects;
        public EnchantmentManifest enchantment;
        public string art;
        // "meta" is intentionally not declared — it is review-gate data,
        // stripped before load per spec.
    }

    /// <summary>
    /// WEAPON-SPEC.md §2 — a starting weapon IS a card (full card schema) plus
    /// the Professions that offer it at character creation.
    /// </summary>
    public class WeaponManifest : CardManifest
    {
        public List<string> classes; // exact Profession asset names; "all" allowed
    }

    /// <summary>
    /// WEAPON-SPEC.md §2 — a weapon power is a tier-0 Talent.
    /// "meta" is intentionally not declared (review-gate data, stripped on load).
    /// </summary>
    public class WeaponPowerManifest
    {
        public string name;
        public int talentID;
        public string description;
        public string flavortext;
        public int cooldown;
        public List<string> keywords;
        public List<EffectManifest> effects;
        public RequirementsManifest requirements;
        public List<string> classes;
        public string art; // powerImage PNG, 512x512
    }

    public class RequirementsManifest
    {
        public int rDEX;
        public int rINT;
        public int rSTR;
    }

    public class EffectManifest
    {
        public string trigger;
        public string codeLine;
        public bool forecast = true;
        public string referenceStatus;
        public List<string> referenceCards;
        public bool hideReferenceCards;
        public List<ConditionManifest> conditions;
    }

    public class ConditionManifest
    {
        public string value;
        public string op;
        public string target;
        public bool ignoreForDisplay;
    }

    public class EnchantmentManifest
    {
        public string text;
        public string type;
        public bool combat;
        public bool showstacks = true;
        public List<EffectManifest> effects;
    }
}
