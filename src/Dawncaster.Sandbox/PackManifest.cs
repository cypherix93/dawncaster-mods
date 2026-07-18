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
